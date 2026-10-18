using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitleDownloader.Models;

namespace Jellyfin.Plugin.SubtitleDownloader.Providers;

/// <summary>
/// Common interface for all subtitle providers.
/// </summary>
public interface ISubtitleSourceProvider
{
    /// <summary>
    /// Gets the provider display name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the provider is currently enabled in configuration.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Searches for subtitles matching the query.
    /// </summary>
    /// <param name="query">Search criteria.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching subtitles.</returns>
    Task<List<SubtitleSearchResult>> SearchAsync(SubtitleSearchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads raw subtitle bytes.
    /// </summary>
    /// <param name="request">Download request details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Downloaded raw subtitle bytes.</returns>
    Task<byte[]> DownloadAsync(DownloadSubtitleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Tests provider connectivity and credentials.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Test result status.</returns>
    Task<ProviderTestResult> TestConnectionAsync(CancellationToken cancellationToken);
}
