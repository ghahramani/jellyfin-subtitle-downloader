using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitleDownloader.Models;
using Jellyfin.Plugin.SubtitleDownloader.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleDownloader.Services;

/// <summary>
/// Orchestrates multi-provider search, ranking, downloading, charset conversion, and saving to media folder.
/// </summary>
public class SubtitleAggregatorService
{
    private readonly IEnumerable<ISubtitleSourceProvider> _providers;
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<SubtitleAggregatorService> _logger;

    public SubtitleAggregatorService(
        IEnumerable<ISubtitleSourceProvider> providers,
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        ILogger<SubtitleAggregatorService> logger)
    {
        _providers = providers;
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Searches across all enabled providers and ranks results by synchronization quality.
    /// </summary>
    public async Task<List<SubtitleSearchResult>> SearchAllAsync(SubtitleSearchQuery query, CancellationToken cancellationToken)
    {
        var enabledProviders = _providers.Where(p => p.IsEnabled).ToList();
        var tasks = enabledProviders.Select(async p =>
        {
            try
            {
                return await p.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider {Provider} search failed for {Title}", p.Name, query.CleanTitle);
                return new List<SubtitleSearchResult>();
            }
        });

        var providerResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        var allResults = new List<SubtitleSearchResult>();
        foreach (var sublist in providerResults)
        {
            allResults.AddRange(sublist);
        }

        // Sort: Hash matches first, then Release matches, then by rating/downloads
        var sorted = allResults
            .OrderByDescending(r => r.IsHashMatch)
            .ThenByDescending(r => r.IsReleaseMatch)
            .ThenByDescending(r => r.SyncScore)
            .ThenByDescending(r => r.DownloadCount ?? 0)
            .ThenByDescending(r => r.Rating ?? 0)
            .ToList();

        return sorted;
    }

    /// <summary>
    /// Downloads, converts to UTF-8/WebVTT, saves beside media file, and triggers library refresh.
    /// </summary>
    public async Task<DownloadSubtitleResponse> DownloadAndApplyAsync(DownloadSubtitleRequest request, CancellationToken cancellationToken)
    {
        // 1. Identify provider
        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.Name, request.Provider, StringComparison.OrdinalIgnoreCase) ||
            request.SubtitleId.StartsWith(p.Name.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            // Fallback by prefix
            if (request.SubtitleId.StartsWith("os:", StringComparison.OrdinalIgnoreCase))
            {
                provider = _providers.FirstOrDefault(p => p.Name == "OpenSubtitles");
            }
            else if (request.SubtitleId.StartsWith("subdl:", StringComparison.OrdinalIgnoreCase))
            {
                provider = _providers.FirstOrDefault(p => p.Name == "SubDL");
            }
            else if (request.SubtitleId.StartsWith("jf:", StringComparison.OrdinalIgnoreCase))
            {
                provider = _providers.FirstOrDefault(p => p.Name == "Jellyfin");
            }

            provider ??= _providers.FirstOrDefault(p => p.IsEnabled);
        }

        if (provider == null)
        {
            return new DownloadSubtitleResponse
            {
                Success = false,
                Message = $"No available subtitle provider found for request: {request.Provider}"
            };
        }

        // 2. Download raw bytes
        byte[] rawBytes = await provider.DownloadAsync(request, cancellationToken).ConfigureAwait(false);
        if (rawBytes == null || rawBytes.Length == 0)
        {
            return new DownloadSubtitleResponse
            {
                Success = false,
                Message = "Downloaded subtitle file was empty."
            };
        }

        // 3. Decode to clean UTF-8 string (fixing Persian Windows-1256, ISO-8859, etc.)
        string srtText = SubtitleFormatConverter.DecodeBytesToUtf8(rawBytes, request.Language);
        string vttText = SubtitleFormatConverter.ConvertSrtToVtt(srtText);

        string? savedFilePath = null;
        string fileName = $"{request.Language}.srt";

        // 4. Save to media file folder if configured
        var config = Plugin.Instance?.Configuration;
        if (config?.SaveToMediaDirectory ?? true)
        {
            try
            {
                var item = _libraryManager.GetItemById(request.ItemId) as Video;
                if (item != null && !string.IsNullOrEmpty(item.Path) && File.Exists(item.Path))
                {
                    string dir = Path.GetDirectoryName(item.Path)!;
                    string baseName = Path.GetFileNameWithoutExtension(item.Path);
                    string targetFile = Path.Combine(dir, $"{baseName}.{request.Language}.srt");

                    // If file exists, try appending provider or default
                    if (File.Exists(targetFile))
                    {
                        targetFile = Path.Combine(dir, $"{baseName}.{request.Language}.{provider.Name.ToLowerInvariant()}.srt");
                    }

                    File.WriteAllText(targetFile, srtText, System.Text.Encoding.UTF8);
                    savedFilePath = targetFile;
                    fileName = Path.GetFileName(targetFile);

                    _logger.LogInformation("Saved downloaded subtitle to {Path}", targetFile);

                    // Refresh item metadata
                    _providerManager.QueueRefresh(
                        item.Id,
                        new MetadataRefreshOptions(new DirectoryService(_fileSystem)),
                        RefreshPriority.High);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write subtitle file to media directory.");
            }
        }

        return new DownloadSubtitleResponse
        {
            Success = true,
            Message = "Subtitle downloaded and ready to display in player.",
            SubtitleText = srtText,
            VttContent = vttText,
            FileName = fileName,
            FilePath = savedFilePath,
            Language = request.Language,
            Provider = provider.Name
        };
    }

    /// <summary>
    /// Tests connectivity for all or a specific provider.
    /// </summary>
    public async Task<List<ProviderTestResult>> TestProvidersAsync(string? specificProvider, CancellationToken cancellationToken)
    {
        var list = new List<ProviderTestResult>();
        var targetProviders = string.IsNullOrEmpty(specificProvider)
            ? _providers.ToList()
            : _providers.Where(p => string.Equals(p.Name, specificProvider, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var p in targetProviders)
        {
            try
            {
                var result = await p.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
                list.Add(result);
            }
            catch (Exception ex)
            {
                list.Add(new ProviderTestResult
                {
                    Provider = p.Name,
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        return list;
    }
}
