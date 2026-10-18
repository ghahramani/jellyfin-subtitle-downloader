using Jellyfin.Plugin.SubtitleDownloader.Providers;
using Jellyfin.Plugin.SubtitleDownloader.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubtitleDownloader;

/// <summary>
/// Registers services and providers into Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // HTTP Client
        serviceCollection.AddHttpClient();

        // Middleware for index.html client script injection
        serviceCollection.AddSingleton<IStartupFilter, ScriptInjectionStartupFilter>();

        // Subtitle Providers
        serviceCollection.AddSingleton<ISubtitleSourceProvider, OpenSubtitlesProvider>();
        serviceCollection.AddSingleton<ISubtitleSourceProvider, SubdlProvider>();
        serviceCollection.AddSingleton<ISubtitleSourceProvider, JellyfinInternalProvider>();

        // Core Aggregator Service
        serviceCollection.AddSingleton<SubtitleAggregatorService>();
    }
}
