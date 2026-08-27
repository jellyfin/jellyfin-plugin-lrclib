using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LrcLib.Configuration;

/// <summary>
/// Configuration for LrcLib.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// The default base URL for accessing the API.
    /// </summary>
    public const string DefaultBaseUrl = "https://lrclib.net";

    /// <summary>
    /// Gets or sets a value indicating whether to use strict search.
    /// </summary>
    public bool UseStrictSearch { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to exclude artist name.
    /// </summary>
    public bool ExcludeArtistName { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether to exclude album name.
    /// </summary>
    public bool ExcludeAlbumName { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether synced lyrics are preferred over plain lyrics.
    /// </summary>
    public bool PreferSyncedLyrics { get; set; } = true;

    /// <summary>
    /// Gets or sets the base URL used for accessing the API.
    /// </summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>
    /// Gets or sets a value indicating whether rate limiting is disabled.
    /// </summary>
    public bool DisableRateLimit { get; set; } = false;
}
