using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SubtitleDownloader.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleDownloader;

/// <summary>
/// Subtitle Downloader Plugin for Jellyfin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static readonly Guid PluginGuid = Guid.Parse("3f8c8710-4139-4d69-a1d2-06b3a32f6b89");

    public override string Name => "Subtitle Downloader";

    public override Guid Id => PluginGuid;

    public override string Description => "Search, select, and download synchronized subtitles directly inside the video player across multiple sources (OpenSubtitles, SubDL, Podnapisi, and Jellyfin).";

    public static Plugin? Instance { get; private set; }

    public ILogger<Plugin> Logger { get; }

    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Logger = logger;
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "SubtitleDownloader",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
            EnableInMainMenu = false
        };
    }
}
