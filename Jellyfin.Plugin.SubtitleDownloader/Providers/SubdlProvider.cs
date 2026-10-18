using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitleDownloader.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleDownloader.Providers;

/// <summary>
/// Subtitle provider for SubDL API.
/// </summary>
public class SubdlProvider : ISubtitleSourceProvider
{
    private const string BaseApiUrl = "https://api.subdl.com/api/v1/subtitles";
    private const string BaseDlUrl = "https://dl.subdl.com";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SubdlProvider> _logger;

    public SubdlProvider(IHttpClientFactory httpClientFactory, ILogger<SubdlProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "SubDL";

    public bool IsEnabled => Plugin.Instance?.Configuration.SubdlEnabled ?? true;

    private string? ApiKey
    {
        get
        {
            var key = Plugin.Instance?.Configuration.SubdlApiKey;
            return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }
    }

    public async Task<List<SubtitleSearchResult>> SearchAsync(SubtitleSearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SubtitleSearchResult>();
        if (!IsEnabled)
        {
            return results;
        }

        string? apiKey = ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            // SubDL requires an API key (which is free on subdl.com)
            return results;
        }

        try
        {
            var queryParams = new List<string>
            {
                $"api_key={apiKey}",
                "releases=1",
                "hi=1"
            };

            if (!string.IsNullOrEmpty(query.ImdbId))
            {
                string imdb = query.ImdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? query.ImdbId : $"tt{query.ImdbId}";
                queryParams.Add($"imdb_id={imdb}");
            }
            else if (!string.IsNullOrEmpty(query.TmdbId))
            {
                queryParams.Add($"tmdb_id={query.TmdbId}");
            }
            else if (!string.IsNullOrEmpty(query.CleanTitle))
            {
                queryParams.Add($"film_name={Uri.EscapeDataString(query.CleanTitle)}");
            }

            if (query.IsSeries)
            {
                queryParams.Add("type=tv");
                if (query.SeasonNumber.HasValue) queryParams.Add($"season_number={query.SeasonNumber.Value}");
                if (query.EpisodeNumber.HasValue) queryParams.Add($"episode_number={query.EpisodeNumber.Value}");
            }
            else
            {
                queryParams.Add("type=movie");
                if (query.ProductionYear.HasValue) queryParams.Add($"year={query.ProductionYear.Value}");
            }

            if (!string.IsNullOrWhiteSpace(query.Language) && !query.Language.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                queryParams.Add($"languages={query.Language}");
            }

            var requestUrl = $"{BaseApiUrl}?" + string.Join("&", queryParams);
            using var client = _httpClientFactory.CreateClient("SubDL");
            client.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Server/12.0.0");

            var response = await client.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return results;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var doc = JsonNode.Parse(json);
            if (doc?["status"]?.GetValue<bool>() != true)
            {
                return results;
            }

            var subs = doc["subtitles"]?.AsArray();
            if (subs == null)
            {
                return results;
            }

            foreach (var item in subs)
            {
                try
                {
                    var release = item?["release_name"]?.ToString() ?? string.Empty;
                    var lang = item?["lang"]?.ToString() ?? "en";
                    var url = item?["url"]?.ToString() ?? string.Empty;
                    var author = item?["author"]?.ToString() ?? string.Empty;
                    bool hi = item?["hi"]?.GetValue<bool>() ?? false;

                    if (string.IsNullOrEmpty(url)) continue;

                    string fullDlUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"{BaseDlUrl}{url}";
                    string subId = $"subdl:{url.GetHashCode():x8}";

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

                    int score = isReleaseMatch ? 60 : 20;

                    results.Add(new SubtitleSearchResult
                    {
                        Id = subId,
                        Provider = Name,
                        Language = lang,
                        LanguageName = GetLanguageDisplayName(lang),
                        ReleaseName = string.IsNullOrWhiteSpace(release) ? (query.CleanTitle + " (SubDL)") : release,
                        Format = "srt",
                        IsHashMatch = false,
                        IsReleaseMatch = isReleaseMatch,
                        HearingImpaired = hi,
                        Forced = false,
                        DownloadUrl = fullDlUrl,
                        Comment = string.IsNullOrEmpty(author) ? null : $"Author: {author}",
                        SyncScore = score
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse SubDL entry.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SubDL search failed for {Title}", query.CleanTitle);
        }

        return results;
    }

    public async Task<byte[]> DownloadAsync(DownloadSubtitleRequest request, CancellationToken cancellationToken)
    {
        string? downloadUrl = request.DownloadUrl;
        if (string.IsNullOrEmpty(downloadUrl))
        {
            throw new ArgumentException("Download URL is required for SubDL provider.");
        }

        using var client = _httpClientFactory.CreateClient("SubDL");
        client.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Server/12.0.0");

        var data = await client.GetByteArrayAsync(downloadUrl, cancellationToken).ConfigureAwait(false);

        // SubDL often returns zip files. Extract if zip.
        if (data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04)
        {
            using var ms = new MemoryStream(data);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            ZipArchiveEntry? bestEntry = null;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase))
                {
                    if (bestEntry == null || entry.Length > bestEntry.Length)
                    {
                        bestEntry = entry;
                    }
                }
            }

            if (bestEntry != null)
            {
                using var entryStream = bestEntry.Open();
                using var outMs = new MemoryStream();
                await entryStream.CopyToAsync(outMs, cancellationToken).ConfigureAwait(false);
                return outMs.ToArray();
            }
        }

        return data;
    }

    public async Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        string? apiKey = ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            return new ProviderTestResult
            {
                Provider = Name,
                Success = false,
                Message = "SubDL API key is not configured. Get a free API key at subdl.com."
            };
        }

        try
        {
            using var client = _httpClientFactory.CreateClient("SubDL");
            var response = await client.GetAsync($"{BaseApiUrl}?api_key={apiKey}&film_name=Inception&type=movie", cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new ProviderTestResult
                {
                    Provider = Name,
                    Success = true,
                    Message = "SubDL API key is valid and connected successfully!"
                };
            }

            return new ProviderTestResult
            {
                Provider = Name,
                Success = false,
                Message = $"SubDL returned HTTP {response.StatusCode}."
            };
        }
        catch (Exception ex)
        {
            return new ProviderTestResult
            {
                Provider = Name,
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            };
        }
    }

    private static string GetLanguageDisplayName(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "en" or "english" => "English",
            "fa" or "farsi" or "persian" => "Persian / فارسی",
            "es" or "spanish" => "Spanish / Español",
            "fr" or "french" => "French / Français",
            "de" or "german" => "German / Deutsch",
            "it" or "italian" => "Italian / Italiano",
            "ar" or "arabic" => "Arabic / العربية",
            _ => code.ToUpperInvariant()
        };
    }
}
