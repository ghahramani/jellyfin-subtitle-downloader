using System;
using System.IO;
using System.Net.Mime;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitleDownloader.Configuration;
using Jellyfin.Plugin.SubtitleDownloader.Models;
using Jellyfin.Plugin.SubtitleDownloader.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleDownloader.Controllers;

/// <summary>
/// API Controller for in-player subtitle search, downloads, and configuration management.
/// </summary>
[ApiController]
[Route("SubtitleDownloader")]
public class SubtitleDownloaderController : ControllerBase
{
    private readonly SubtitleAggregatorService _aggregatorService;
    private readonly ILibraryManager _libraryManager;
    private readonly IConfigurationManager _configManager;
    private readonly ILogger<SubtitleDownloaderController> _logger;

    public SubtitleDownloaderController(
        SubtitleAggregatorService aggregatorService,
        ILibraryManager libraryManager,
        IConfigurationManager configManager,
        ILogger<SubtitleDownloaderController> logger)
    {
        _aggregatorService = aggregatorService;
        _libraryManager = libraryManager;
        _configManager = configManager;
        _logger = logger;
    }

    /// <summary>
    /// Serves the client-side JavaScript for the in-player subtitle modal and controls.
    /// </summary>
    [HttpGet("ClientScript")]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.SubtitleDownloader.Web.SubtitleDownloader.js");
        if (stream == null)
        {
            return NotFound("Client script resource not found.");
        }

        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();
        return Content(content, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Serves the CSS styling for the in-player subtitle modal.
    /// </summary>
    [HttpGet("ClientStyle")]
    [Produces("text/css")]
    public ActionResult GetClientStyle()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Jellyfin.Plugin.SubtitleDownloader.Web.SubtitleDownloader.css");
        if (stream == null)
        {
            return NotFound("Client style resource not found.");
        }

        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();
        return Content(content, "text/css; charset=utf-8");
    }

    /// <summary>
    /// Gets the current plugin configuration.
    /// </summary>
    [HttpGet("Configuration")]
    [Authorize]
    public ActionResult<PluginConfiguration> GetConfiguration()
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        return Ok(config);
    }

    /// <summary>
    /// Updates the plugin configuration.
    /// </summary>
    [HttpPost("Configuration")]
    [Authorize]
    public ActionResult UpdateConfiguration([FromBody] PluginConfiguration newConfig)
    {
        if (newConfig == null)
        {
            return BadRequest("Configuration cannot be null.");
        }

        Plugin.Instance?.UpdateConfiguration(newConfig);
        return Ok(new { success = true, message = "Configuration saved successfully." });
    }

    /// <summary>
    /// Tests connectivity for one or all providers.
    /// </summary>
    [HttpPost("TestProvider")]
    [Authorize]
    public async Task<ActionResult> TestProvider([FromQuery] string? provider, CancellationToken cancellationToken)
    {
        var results = await _aggregatorService.TestProvidersAsync(provider, cancellationToken).ConfigureAwait(false);
        return Ok(results);
    }

    /// <summary>
    /// Searches for subtitles across all configured providers.
    /// </summary>
    [HttpGet("Search")]
    [Authorize]
    public async Task<ActionResult> Search(
        [FromQuery] Guid itemId,
        [FromQuery] string? query,
        [FromQuery] string? language,
        [FromQuery] int? season,
        [FromQuery] int? episode,
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(itemId) as Video;
        if (item == null)
        {
            return NotFound($"Item with ID {itemId} not found.");
        }

        string cleanTitle = !string.IsNullOrWhiteSpace(query)
            ? query.Trim()
            : item.Name;

        string? mediaPath = item.Path;
        string? movieHash = null;
        string? releaseName = null;

        if (!string.IsNullOrEmpty(mediaPath) && System.IO.File.Exists(mediaPath))
        {
            movieHash = MovieHashCalculator.ComputeHash(mediaPath);
            releaseName = Path.GetFileNameWithoutExtension(mediaPath);
        }

        bool isSeries = item is Episode;
        string? seriesName = null;
        int? seasonNumber = season;
        int? episodeNumber = episode;

        if (item is Episode ep)
        {
            isSeries = true;
            seriesName = ep.SeriesName;
            seasonNumber ??= ep.ParentIndexNumber;
            episodeNumber ??= ep.IndexNumber;
            if (string.IsNullOrWhiteSpace(query) && !string.IsNullOrWhiteSpace(ep.SeriesName))
            {
                cleanTitle = ep.SeriesName;
            }
        }

        item.ProviderIds.TryGetValue("Imdb", out string? imdbId);
        item.ProviderIds.TryGetValue("Tmdb", out string? tmdbId);

        var searchQuery = new SubtitleSearchQuery
        {
            ItemId = itemId,
            MediaFilePath = mediaPath,
            MovieHash = movieHash,
            CleanTitle = cleanTitle,
            OriginalTitle = item.OriginalTitle,
            ProductionYear = year ?? item.ProductionYear,
            IsSeries = isSeries,
            SeriesName = seriesName,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            ImdbId = imdbId,
            TmdbId = tmdbId,
            Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim(),
            ReleaseName = releaseName
        };

        var results = await _aggregatorService.SearchAllAsync(searchQuery, cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            item = new
            {
                id = item.Id,
                name = item.Name,
                seriesName,
                seasonNumber,
                episodeNumber,
                year = item.ProductionYear,
                releaseName,
                movieHash,
                hasHash = !string.IsNullOrEmpty(movieHash)
            },
            results
        });
    }

    /// <summary>
    /// Downloads, converts to UTF-8/WebVTT, saves beside media file, and returns VTT content for instant player display.
    /// </summary>
    [HttpPost("Download")]
    [Authorize]
    public async Task<ActionResult> Download(
        [FromBody] DownloadSubtitleRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrEmpty(request.SubtitleId))
        {
            return BadRequest("Invalid download request.");
        }

        try
        {
            var response = await _aggregatorService.DownloadAndApplyAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download subtitle {Id}", request.SubtitleId);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}
