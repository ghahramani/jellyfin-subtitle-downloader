namespace Jellyfin.Plugin.SubtitleDownloader.Models;

/// <summary>
/// Unified subtitle search result model across all providers.
/// </summary>
public class SubtitleSearchResult
{
    public string Id { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string LanguageName { get; set; } = string.Empty;

    public string ReleaseName { get; set; } = string.Empty;

    public string Format { get; set; } = "srt";

    public bool IsHashMatch { get; set; }

    public bool IsReleaseMatch { get; set; }

    public bool HearingImpaired { get; set; }

    public bool Forced { get; set; }

    public float? Rating { get; set; }

    public int? DownloadCount { get; set; }

    public string? DownloadUrl { get; set; }

    public string? FileId { get; set; }

    public string? Comment { get; set; }

    public int SyncScore { get; set; }
}
