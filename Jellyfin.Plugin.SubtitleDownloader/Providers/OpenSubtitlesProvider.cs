using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitleDownloader.Configuration;
using Jellyfin.Plugin.SubtitleDownloader.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleDownloader.Providers;

/// <summary>
/// Subtitle provider for OpenSubtitles.com REST API (v2).
/// </summary>
public class OpenSubtitlesProvider : ISubtitleSourceProvider
{
    private const string BaseUrl = "https://api.opensubtitles.com/api/v1";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenSubtitlesProvider> _logger;
    private string? _cachedJwtToken;
    private DateTime _jwtExpiresAt = DateTime.MinValue;

    public OpenSubtitlesProvider(IHttpClientFactory httpClientFactory, ILogger<OpenSubtitlesProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "OpenSubtitles";

    public bool IsEnabled => Plugin.Instance?.Configuration.OpenSubtitlesEnabled ?? true;

    private string ApiKey
    {
        get
        {
            var key = Plugin.Instance?.Configuration.OpenSubtitlesApiKey;
            return string.IsNullOrWhiteSpace(key) ? "gUCLWGoAg2PmyseoTM0INFFVPcDCeDlT" : key.Trim();
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("OpenSubtitles");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Api-Key", ApiKey);
        client.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Server/12.0.0");
        return client;
    }

    private async Task<string?> GetAuthTokenAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || string.IsNullOrWhiteSpace(config.OpenSubtitlesUsername) || string.IsNullOrWhiteSpace(config.OpenSubtitlesPassword))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(_cachedJwtToken) && DateTime.UtcNow < _jwtExpiresAt)
        {
            return _cachedJwtToken;
        }

        try
        {
            using var client = CreateClient();
            var payload = new
            {
                username = config.OpenSubtitlesUsername.Trim(),
                password = config.OpenSubtitlesPassword.Trim()
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{BaseUrl}/login", content, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var doc = JsonNode.Parse(json);
                _cachedJwtToken = doc?["token"]?.GetValue<string>();
                _jwtExpiresAt = DateTime.UtcNow.AddHours(12);
                return _cachedJwtToken;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to authenticate with OpenSubtitles.com.");
        }

        return null;
    }

    public async Task<List<SubtitleSearchResult>> SearchAsync(SubtitleSearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SubtitleSearchResult>();
        if (!IsEnabled)
        {
            return results;
        }

        using var client = CreateClient();

        // 1. If we have a MovieHash, query by hash first for guaranteed 100% sync match!
        if (!string.IsNullOrEmpty(query.MovieHash))
        {
            try
            {
                var hashUrl = $"{BaseUrl}/subtitles?moviehash={query.MovieHash}";
                if (!string.IsNullOrWhiteSpace(query.Language) && !query.Language.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    hashUrl += $"&languages={query.Language}";
                }

                var hashResults = await ExecuteSearchUrlAsync(client, hashUrl, isHashMatch: true, query, cancellationToken).ConfigureAwait(false);
                results.AddRange(hashResults);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenSubtitles hash search failed for hash {Hash}", query.MovieHash);
            }
        }

        // 2. Query by IMDb ID or TMDB ID or Title
        try
        {
            var searchParams = new List<string>();

            if (!string.IsNullOrEmpty(query.ImdbId))
            {
                string imdb = query.ImdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? query.ImdbId.Substring(2) : query.ImdbId;
                searchParams.Add($"imdb_id={imdb}");
            }
            else if (!string.IsNullOrEmpty(query.TmdbId))
            {
                searchParams.Add($"tmdb_id={query.TmdbId}");
            }
            else if (!string.IsNullOrEmpty(query.CleanTitle))
            {
                searchParams.Add($"query={Uri.EscapeDataString(query.CleanTitle)}");
            }

            if (!string.IsNullOrWhiteSpace(query.Language) && !query.Language.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                searchParams.Add($"languages={query.Language}");
            }

            if (query.IsSeries)
            {
                if (query.SeasonNumber.HasValue)
                {
                    searchParams.Add($"season_number={query.SeasonNumber.Value}");
                }
                if (query.EpisodeNumber.HasValue)
                {
                    searchParams.Add($"episode_number={query.EpisodeNumber.Value}");
                }
            }
            else if (query.ProductionYear.HasValue)
            {
                searchParams.Add($"year={query.ProductionYear.Value}");
            }

            if (searchParams.Count > 0)
            {
                var searchUrl = $"{BaseUrl}/subtitles?" + string.Join("&", searchParams);
                var metaResults = await ExecuteSearchUrlAsync(client, searchUrl, isHashMatch: false, query, cancellationToken).ConfigureAwait(false);

                // Deduplicate with existing hash results
                foreach (var r in metaResults)
                {
                    if (!results.Exists(x => x.Id == r.Id))
                    {
                        results.Add(r);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSubtitles metadata search failed for title {Title}", query.CleanTitle);
        }

        return results;
    }

    private async Task<List<SubtitleSearchResult>> ExecuteSearchUrlAsync(
        HttpClient client,
        string url,
        bool isHashMatch,
        SubtitleSearchQuery query,
        CancellationToken cancellationToken)
    {
        var list = new List<SubtitleSearchResult>();
        var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return list;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var doc = JsonNode.Parse(json);
        var dataArray = doc?["data"]?.AsArray();
        if (dataArray == null)
        {
            return list;
        }

        foreach (var item in dataArray)
        {
            try
            {
                var attributes = item?["attributes"];
                if (attributes == null) continue;

                var subId = attributes["subtitle_id"]?.ToString() ?? item?["id"]?.ToString() ?? string.Empty;
                var lang = attributes["language"]?.ToString() ?? "en";
                var release = attributes["release"]?.ToString() ?? string.Empty;
                var files = attributes["files"]?.AsArray();
                string? fileId = null;
                string format = "srt";

                if (files != null && files.Count > 0)
                {
                    fileId = files[0]?["file_id"]?.ToString();
                    var fn = files[0]?["file_name"]?.ToString();
                    if (!string.IsNullOrEmpty(fn) && fn.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
                    {
                        format = "vtt";
                    }
                }

                if (string.IsNullOrEmpty(fileId))
                {
                    fileId = subId;
                }

                bool hearingImpaired = attributes["hearing_impaired"]?.GetValue<bool>() ?? false;
                bool forced = attributes["foreign_parts_only"]?.GetValue<bool>() ?? false;
                float? rating = attributes["ratings"]?.GetValue<float>();
                int? downloads = attributes["download_count"]?.GetValue<int>();
                var comments = attributes["comments"]?.ToString();

                // Compute release match
                bool isReleaseMatch = false;
                if (!string.IsNullOrEmpty(query.ReleaseName) && !string.IsNullOrEmpty(release))
                {
                    var cleanRelease = query.ReleaseName.ToLowerInvariant();
                    var subRelease = release.ToLowerInvariant();
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
                if (isReleaseMatch) score += 40;
                if (downloads.HasValue && downloads.Value > 1000) score += 10;
                if (rating.HasValue && rating.Value > 4) score += 10;

                list.Add(new SubtitleSearchResult
                {
                    Id = $"os:{fileId}",
                    FileId = fileId,
                    Provider = Name,
                    Language = lang,
                    LanguageName = GetLanguageDisplayName(lang),
                    ReleaseName = string.IsNullOrWhiteSpace(release) ? (query.CleanTitle + " Subtitle") : release,
                    Format = format,
                    IsHashMatch = isHashMatch,
                    IsReleaseMatch = isReleaseMatch,
                    HearingImpaired = hearingImpaired,
                    Forced = forced,
                    Rating = rating,
                    DownloadCount = downloads,
                    Comment = comments,
                    SyncScore = score
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse OpenSubtitles item.");
            }
        }

        return list;
    }

    public async Task<byte[]> DownloadAsync(DownloadSubtitleRequest request, CancellationToken cancellationToken)
    {
        string fileId = !string.IsNullOrEmpty(request.FileId)
            ? request.FileId
            : request.SubtitleId.StartsWith("os:", StringComparison.OrdinalIgnoreCase)
                ? request.SubtitleId.Substring(3)
                : request.SubtitleId;

        using var client = CreateClient();

        // Check if user has token
        string? token = await GetAuthTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var downloadPayload = new { file_id = int.TryParse(fileId, out int fid) ? fid : 0 };
        var jsonContent = new StringContent(JsonSerializer.Serialize(downloadPayload), Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{BaseUrl}/download", jsonContent, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenSubtitles download failed with status {response.StatusCode}.");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var doc = JsonNode.Parse(responseJson);
        var downloadLink = doc?["link"]?.GetValue<string>();

        if (string.IsNullOrEmpty(downloadLink))
        {
            throw new InvalidOperationException("OpenSubtitles did not return a valid download link.");
        }

        // Fetch actual subtitle bytes from downloadLink
        using var fileClient = new HttpClient();
        fileClient.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Server/12.0.0");
        return await fileClient.GetByteArrayAsync(downloadLink, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync($"{BaseUrl}/subtitles?query=Inception&languages=en", cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                string? token = await GetAuthTokenAsync(cancellationToken).ConfigureAwait(false);
                string authStatus = !string.IsNullOrEmpty(token) ? " (Authenticated as registered user)" : " (Free / Public mode)";
                return new ProviderTestResult
                {
                    Provider = Name,
                    Success = true,
                    Message = $"Connected successfully to OpenSubtitles.com REST API!{authStatus}"
                };
            }

            return new ProviderTestResult
            {
                Provider = Name,
                Success = false,
                Message = $"OpenSubtitles responded with HTTP {response.StatusCode}."
            };
        }
        catch (Exception ex)
        {
            return new ProviderTestResult
            {
                Provider = Name,
                Success = false,
                Message = $"Failed to connect: {ex.Message}"
            };
        }
    }

    private static string GetLanguageDisplayName(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "en" => "English",
            "fa" or "per" or "fas" => "Persian / فارسی",
            "es" or "spa" => "Spanish / Español",
            "fr" or "fre" or "fra" => "French / Français",
            "de" or "ger" or "deu" => "German / Deutsch",
            "it" or "ita" => "Italian / Italiano",
            "ar" or "ara" => "Arabic / العربية",
            "pt" or "por" => "Portuguese / Português",
            "ru" or "rus" => "Russian / Русский",
            "tr" or "tur" => "Turkish / Türkçe",
            "ja" or "jpn" => "Japanese / 日本語",
            "ko" or "kor" => "Korean / 한국어",
            "zh" or "zho" or "chi" => "Chinese / 中文",
            _ => code.ToUpperInvariant()
        };
    }
}
