using System;

namespace Jellyfin.Plugin.SubtitleDownloader.Models;

/// <summary>
/// Request payload for downloading a subtitle.
/// </summary>
public class DownloadSubtitleRequest
{
    public Guid ItemId { get; set; }

    public string SubtitleId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Format { get; set; } = "srt";

    public string Language { get; set; } = "en";

    public string? DownloadUrl { get; set; }

    public string? FileId { get; set; }
}
