namespace Jellyfin.Plugin.SubtitleDownloader.Models;

/// <summary>
/// Represents a parsed subtitle cue / event with start and end timestamp ticks.
/// Matches Jellyfin's native SubtitleTrackEvent structure (1 tick = 100 nanoseconds, 10,000,000 ticks/sec).
/// </summary>
public class SubtitleTrackEventDto
{
    public string Id { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public long StartPositionTicks { get; set; }

    public long EndPositionTicks { get; set; }
}
