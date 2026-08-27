using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LrcLib.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LrcLib;

/// <summary>
/// Process-wide request gate for lrclib.net.
/// </summary>
/// <remarks>
/// lrclib.net asks plugins to space their requests out, to let one finish before starting the
/// next, to honour <c>429</c> and its <c>Retry-After</c> header, and to send a user agent that
/// identifies the plugin rather than the server it runs on. Routing every request through this
/// gate is what keeps all four true.
/// </remarks>
internal static class LrcLibRateLimiter
{
    private const int MaxRetryAttempts = 1;
    private const string OfficialHost = "lrclib.net";
    private const string OfficialHostSuffix = "." + OfficialHost;
    // Identifies the plugin and its version to lrclib.net, in place of the server's own user agent.
    private static readonly ProductInfoHeaderValue _userAgentProduct = new("jellyfin-plugin-lrclib", ResolveVersion());

    private static readonly TimeSpan _minimumInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan _minimumDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan _fallbackRetryAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _maximumRetryAfter = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim _requestGate = new(1, 1);

    private static long _nextRequestTimestamp = Stopwatch.GetTimestamp();

    private static bool IsRateLimitDisabled
    {
        get
        {
            var configuration = LrcLibPlugin.Instance?.Configuration;

            return configuration is not null
                && configuration.DisableRateLimit
                && !IsOfficialServer(configuration.BaseUrl);
        }
    }

    /// <summary>
    /// Sends a request to lrclib.net once the rate limit allows it, stamping the plugin's user
    /// agent and retrying once on <see cref="HttpStatusCode.TooManyRequests"/>.
    /// </summary>
    /// <param name="httpClientFactory">The factory the sending client is created from.</param>
    /// <param name="requestFactory">
    /// Builds a fresh <see cref="HttpRequestMessage"/> per attempt; a message that has already
    /// been sent cannot be sent again.
    /// </param>
    /// <param name="logger">The logger used to report backoffs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response; the caller owns it and is responsible for disposing it.</returns>
    public static async Task<HttpResponseMessage> SendAsync(
        IHttpClientFactory httpClientFactory,
        Func<HttpRequestMessage> requestFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Created here rather than by the caller so that no request can reach lrclib.net without
        // passing through ApplyUserAgent below.
        var httpClient = httpClientFactory.CreateClient(NamedClient.Default);

        if (IsRateLimitDisabled)
        {
            using var unlimitedRequest = requestFactory();
            ApplyUserAgent(unlimitedRequest);

            return await httpClient.SendAsync(unlimitedRequest, cancellationToken).ConfigureAwait(false);
        }

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await WaitForIntervalAsync(cancellationToken).ConfigureAwait(false);

                using var request = requestFactory();
                ApplyUserAgent(request);

                var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    return response;
                }

                var retryAfter = ResolveRetryAfter(response.Headers.RetryAfter);
                DelayNextRequest(retryAfter);

                if (attempt >= MaxRetryAttempts)
                {
                    logger.LogWarning("LrcLib rate limit reached, giving up after {Attempts} attempts", attempt + 1);
                    return response;
                }

                logger.LogWarning("LrcLib rate limit reached, retrying in {RetryAfter}", retryAfter);
                response.Dispose();
            }
        }
        finally
        {
            // Measured from completion rather than from the start of the request, so that a slow
            // response is not immediately followed by the next one.
            DelayNextRequest(_minimumInterval);
            _requestGate.Release();
        }
    }

    private static async Task WaitForIntervalAsync(CancellationToken cancellationToken)
    {
        // Task.Delay may fire fractionally early, so re-check rather than assuming a single delay
        // was enough to clear the interval.
        for (var remaining = GetRemainingInterval(); remaining > TimeSpan.Zero; remaining = GetRemainingInterval())
        {
            await Task.Delay(remaining < _minimumDelay ? _minimumDelay : remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void DelayNextRequest(TimeSpan delay)
    {
        var timestamp = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * delay.TotalSeconds);
        if (timestamp > _nextRequestTimestamp)
        {
            _nextRequestTimestamp = timestamp;
        }
    }

    private static TimeSpan GetRemainingInterval()
        => Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), _nextRequestTimestamp);

    private static void ApplyUserAgent(HttpRequestMessage request)
    {
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(_userAgentProduct);
    }

    private static bool IsOfficialServer(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return true;
        }

        return uri.Host.Equals(OfficialHost, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(OfficialHostSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveVersion()
    {
        var version = LrcLibPlugin.Instance?.Version ?? typeof(LrcLibRateLimiter).Assembly.GetName().Version;
        return version?.ToString() ?? "unknown";
    }

    private static TimeSpan ResolveRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        TimeSpan? requested = null;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta.HasValue)
            {
                requested = retryAfter.Delta.Value;
            }
            else if (retryAfter.Date.HasValue)
            {
                requested = retryAfter.Date.Value.UtcDateTime - DateTime.UtcNow;
            }
        }

        var resolved = requested ?? _fallbackRetryAfter;
        if (resolved < _minimumInterval)
        {
            return _minimumInterval;
        }

        return resolved > _maximumRetryAfter ? _maximumRetryAfter : resolved;
    }
}
