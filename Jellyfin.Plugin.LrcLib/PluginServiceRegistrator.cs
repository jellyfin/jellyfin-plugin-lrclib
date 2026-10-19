using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LrcLib;

/// <summary>
/// Registers the <see cref="HttpClient"/> every lrclib.net request is sent with.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(applicationHost);

        serviceCollection.AddHttpClient(LrcLibPlugin.HttpClientName, client =>
            {
                // Deliberately omit the Jellyfin server product token: LRCLIB's edge has
                // blocked User-Agents containing "Jellyfin-Server" (see issue #47). The
                // plugin identity and project URL are enough for client identification.
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
                    "jellyfin-plugin-lrclib",
                    typeof(LrcLibPlugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0"));
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
                    "(https://github.com/jellyfin/jellyfin-plugin-lrclib)"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
            })
            // Mirrors the handler Jellyfin configures for its own named clients, which a plugin
            // client does not inherit.
            .ConfigurePrimaryHttpMessageHandler(_ => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8
            });
    }
}
