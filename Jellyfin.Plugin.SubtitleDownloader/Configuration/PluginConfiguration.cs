using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubtitleDownloader.Configuration;

/// <summary>
/// Plugin configuration for Subtitle Downloader.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether OpenSubtitles provider is enabled.
    /// </summary>
    public bool OpenSubtitlesEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the OpenSubtitles.com API key.
    /// Defaults to the official Jellyfin client API key.
    /// </summary>
    public string OpenSubtitlesApiKey { get; set; } = "gUCLWGoAg2PmyseoTM0INFFVPcDCeDlT";

    /// <summary>
    /// Gets or sets the OpenSubtitles.com account username.
    /// </summary>
    public string OpenSubtitlesUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OpenSubtitles.com account password.
    /// </summary>
    public string OpenSubtitlesPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether SubDL provider is enabled.
    /// </summary>
    public bool SubdlEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the SubDL API key.
    /// </summary>
    public string SubdlApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether Podnapisi provider is enabled.
    /// </summary>
    public bool PodnapisiEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether internal Jellyfin subtitle providers are enabled.
    /// </summary>
    public bool JellyfinInternalEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets comma-separated preferred languages (e.g. "en,fa,es").
    /// </summary>
    public string PreferredLanguages { get; set; } = "en,fa";

    /// <summary>
    /// Gets or sets a value indicating whether downloaded subtitles should be saved directly next to media files.
    /// </summary>
    public bool SaveToMediaDirectory { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether exact hash/sync matches should be automatically highlighted.
    /// </summary>
    public bool HighlightSyncMatches { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to disable request-time script injection middleware.
    /// </summary>
    public bool DisableScriptInjectionMiddleware { get; set; } = false;
}
