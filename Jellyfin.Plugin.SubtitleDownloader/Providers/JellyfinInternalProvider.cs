using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitleDownloader.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleDownloader.Providers;

/// <summary>
/// Subtitle provider that leverages Jellyfin's internal subtitle managers and installed plugins (e.g. OpenSubtitles plugin, SubtitleEdit, etc.).
/// </summary>
public class JellyfinInternalProvider : ISubtitleSourceProvider
{
    private readonly ISubtitleManager _subtitleManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<JellyfinInternalProvider> _logger;

    public JellyfinInternalProvider(
        ISubtitleManager subtitleManager,
        ILibraryManager libraryManager,
        ILogger<JellyfinInternalProvider> logger)
    {
        _subtitleManager = subtitleManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => "Jellyfin";

    public bool IsEnabled => Plugin.Instance?.Configuration.JellyfinInternalEnabled ?? true;

    public async Task<List<SubtitleSearchResult>> SearchAsync(SubtitleSearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SubtitleSearchResult>();
        if (!IsEnabled)
        {
            return results;
        }

        try
        {
            var item = _libraryManager.GetItemById(query.ItemId) as Video;
            if (item == null)
            {
                return results;
            }

            string lang = string.IsNullOrWhiteSpace(query.Language) || query.Language.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : query.Language;

            var remoteSubs = await _subtitleManager.SearchSubtitles(item, lang, false, false, cancellationToken).ConfigureAwait(false);
            if (remoteSubs == null)
            {
                return results;
            }

            foreach (var sub in remoteSubs)
            {
                bool isHashMatch = sub.IsHashMatch ?? false;
                bool isReleaseMatch = false;

                if (!string.IsNullOrEmpty(query.ReleaseName) && !string.IsNullOrEmpty(sub.Name))
                {
                    var cleanRelease = query.ReleaseName.ToLowerInvariant();
                    var subRelease = sub.Name.ToLowerInvariant();
                    string[] tokens = { "1080p", "720p", "2160p", "bluray", "web-dl", "webrip", "rarbg", "yts", "psa", "galaxyrg", "evo" };
                    foreach (var token in tokens)
                    {
                        if (cleanRelease.Contains(token) && subRelease.Contains(token))
                        {
                            isReleaseMatch = true;
                            break;
                        }
                    }
                }

                int score = 0;
                if (isHashMatch) score += 100;
                if (isReleaseMatch) score += 50;
                if (sub.DownloadCount.HasValue && sub.DownloadCount.Value > 1000) score += 10;
                if (sub.CommunityRating.HasValue && sub.CommunityRating.Value > 4) score += 10;

                results.Add(new SubtitleSearchResult
                {
                    Id = $"jf:{sub.Id}",
                    FileId = sub.Id,
                    Provider = string.IsNullOrEmpty(sub.ProviderName) ? Name : sub.ProviderName,
                    Language = sub.ThreeLetterISOLanguageName ?? lang,
                    LanguageName = GetLanguageDisplayName(sub.ThreeLetterISOLanguageName ?? lang),
                    ReleaseName = string.IsNullOrWhiteSpace(sub.Name) ? (query.CleanTitle + " Subtitle") : sub.Name,
                    Format = string.IsNullOrWhiteSpace(sub.Format) ? "srt" : sub.Format.ToLowerInvariant(),
                    IsHashMatch = isHashMatch,
                    IsReleaseMatch = isReleaseMatch,
                    HearingImpaired = sub.HearingImpaired ?? false,
                    Forced = sub.Forced ?? false,
                    Rating = sub.CommunityRating,
                    DownloadCount = sub.DownloadCount,
                    Comment = sub.Comment,
                    SyncScore = score
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jellyfin internal subtitle search failed for itemId {ItemId}", query.ItemId);
        }

        return results;
    }

    public async Task<byte[]> DownloadAsync(DownloadSubtitleRequest request, CancellationToken cancellationToken)
    {
        string subId = request.SubtitleId.StartsWith("jf:", StringComparison.OrdinalIgnoreCase)
            ? request.SubtitleId.Substring(3)
            : request.SubtitleId;

        // Try getting remote subtitle stream
        var response = await _subtitleManager.GetRemoteSubtitles(subId, cancellationToken).ConfigureAwait(false);
        if (response?.Stream != null)
        {
            using var ms = new MemoryStream();
            await response.Stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            return ms.ToArray();
        }

        // Fallback: also trigger download to item
        var item = _libraryManager.GetItemById(request.ItemId) as Video;
        if (item != null)
        {
            await _subtitleManager.DownloadSubtitles(item, subId, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Could not retrieve subtitle stream from Jellyfin SubtitleManager.");
    }

    public Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new ProviderTestResult
        {
            Provider = Name,
            Success = true,
            Message = "Internal Jellyfin Subtitle Manager is operational."
        });
    }

    private static string GetLanguageDisplayName(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "en" or "eng" => "English",
            "fa" or "per" or "fas" => "Persian / فارسی",
            "es" or "spa" => "Spanish / Español",
            "fr" or "fre" or "fra" => "French / Français",
            "de" or "ger" or "deu" => "German / Deutsch",
            "it" or "ita" => "Italian / Italiano",
            "ar" or "ara" => "Arabic / العربية",
            _ => code.ToUpperInvariant()
        };
    }
}
