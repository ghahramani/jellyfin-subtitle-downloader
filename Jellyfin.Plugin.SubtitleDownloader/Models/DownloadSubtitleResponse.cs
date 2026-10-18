namespace Jellyfin.Plugin.SubtitleDownloader.Models;

/// <summary>
/// Response payload after downloading and applying a subtitle.
/// </summary>
public class DownloadSubtitleResponse
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? SubtitleText { get; set; }

    public string? VttContent { get; set; }

    public string? FileName { get; set; }

    public string? FilePath { get; set; }

    public string? Language { get; set; }

    public string? LanguageName { get; set; }

    public string? Provider { get; set; }
}
