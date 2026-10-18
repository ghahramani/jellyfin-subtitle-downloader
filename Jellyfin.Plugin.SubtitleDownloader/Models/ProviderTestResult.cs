namespace Jellyfin.Plugin.SubtitleDownloader.Models;

/// <summary>
/// Result of a provider connection test.
/// </summary>
public class ProviderTestResult
{
    public string Provider { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;
}
