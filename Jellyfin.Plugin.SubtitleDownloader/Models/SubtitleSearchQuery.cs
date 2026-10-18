using System;

namespace Jellyfin.Plugin.SubtitleDownloader.Models;

/// <summary>
/// Search criteria passed to subtitle providers.
/// </summary>
public class SubtitleSearchQuery
{
    public Guid ItemId { get; set; }

    public string? MediaFilePath { get; set; }

    public string? MovieHash { get; set; }

    public string CleanTitle { get; set; } = string.Empty;

    public string? OriginalTitle { get; set; }

    public int? ProductionYear { get; set; }

    public bool IsSeries { get; set; }

    public string? SeriesName { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? ImdbId { get; set; }

    public string? TmdbId { get; set; }

    public string Language { get; set; } = "en";

    public string? ReleaseName { get; set; }
}
