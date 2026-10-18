# Jellyfin Subtitle Downloader Plugin

A modern, high-performance Jellyfin plugin that adds an interactive **"Search & Download Subtitles"** button directly to the video player with **automatic video hash sync matching**, **multi-provider integration**, and **instant in-player playback attaching**.

---

## ✨ Features

- **In-Player Subtitle Downloader**: Adds a dedicated Subtitle Download button (`closed_caption` with download indicator) right in the playback OSD bar.
- **Sync-First Matching**:
  - **OpenSubtitles 64-bit MovieHash**: Calculates the exact video hash in milliseconds from the first and last 64KB chunks to find 100% frame-perfect synchronized subtitles.
  - **Release Group Matching**: Automatically analyzes filename tags (`1080p`, `2160p`, `WEB-DL`, `BluRay`, `RARBG`, `YTS`, `PSA`, `GalaxyRG`, `EVO`, etc.) to match identical releases.
- **Multiple Sources & Free Providers**:
  - **OpenSubtitles.com REST API (v2)**: Out-of-the-box integration with default official key or user API key, plus optional VIP user credentials.
  - **SubDL**: Deep subtitle database with release group synchronization and full Persian/Arabic & international catalog (free API key from [subdl.com](https://subdl.com)).
  - **Jellyfin Native Subtitle Providers**: Seamlessly taps into any subtitle provider plugin installed on Jellyfin Server (`ISubtitleProvider`).
- **Instant In-Player Attachment**: Once downloaded, dynamically injects a `<track>` into the HTML5 video player and switches it to `showing` mode — you never have to leave the player, refresh the page, or restart playback!
- **Automatic Encoding Correction**: Corrects non-UTF8 encodings (such as Persian/Arabic `Windows-1256`, Latin `Windows-1252`, `ISO-8859-1`, `UTF-16`) to clean UTF-8 before serving and saving.
- **Library Persistence**: Saves `.srt` alongside media files (`MovieName.lang.srt` or `Series.S01E01.lang.srt`) and queues a high-priority Jellyfin metadata refresh.
- **Full Admin Configuration Panel**: Available under Jellyfin **Dashboard -> Plugins -> Subtitle Downloader**.

---

## 🛠 Project Structure

```
jellyfin-plugins/
├── Jellyfin.Plugin.SubtitleDownloader/
│   ├── Configuration/
│   │   ├── PluginConfiguration.cs        # Settings model (keys, preferences, providers)
│   │   ├── configPage.html               # Admin settings page for Jellyfin Dashboard
│   │   └── configPage.css                # Admin panel styles
│   ├── Controllers/
│   │   └── SubtitleDownloaderController.cs # REST API (/Search, /Download, /TestProvider)
│   ├── Models/
│   │   ├── SubtitleSearchResult.cs       # Unified subtitle result
│   │   ├── SubtitleSearchQuery.cs        # Search query criteria
│   │   ├── DownloadSubtitleRequest.cs    # Download request model
│   │   ├── DownloadSubtitleResponse.cs   # Download & WebVTT response
│   │   └── ProviderTestResult.cs         # Provider connectivity test result
│   ├── Providers/
│   │   ├── ISubtitleSourceProvider.cs    # Common provider interface
│   │   ├── OpenSubtitlesProvider.cs      # OpenSubtitles.com REST API v2
│   │   ├── SubdlProvider.cs              # SubDL API client & zip extractor
│   │   └── JellyfinInternalProvider.cs   # Jellyfin ISubtitleManager wrapper
│   ├── Services/
│   │   ├── MovieHashCalculator.cs        # 64-bit OpenSubtitles hash algorithm
│   │   ├── SubtitleFormatConverter.cs    # Charset detection & SRT -> WebVTT converter
│   │   ├── SubtitleAggregatorService.cs  # Multi-provider aggregator & ranker
│   │   └── ScriptInjectionStartupFilter.cs # ASP.NET Core IStartupFilter for index.html
│   ├── Web/
│   │   ├── SubtitleDownloader.js         # In-player OSD hook, modal dialog & live track attacher
│   │   └── SubtitleDownloader.css        # Frosted-glass modal UI styling
│   ├── meta.json                         # Jellyfin plugin manifest
│   ├── icon.png                          # Plugin icon (256x256)
│   └── Jellyfin.Plugin.SubtitleDownloader.csproj
├── build.sh                              # Automated build & install script
└── README.md
```

---

## 🚀 Building & Installing

### Automated Build with Docker .NET SDK:
```bash
cd /mnt/programming-ssd/projects/exploring/jellyfin-plugins
./build.sh
```

The script builds the project using `.NET 10 SDK preview` (compatible with Jellyfin Server 12.0 and 10.11+), deploys the plugin `.dll` and `meta.json` to `./config/jellyfin/data/plugins/SubtitleDownloader_1.0.0.0/`, and generates a release ZIP in `dist/`.

### Restart Jellyfin:
```bash
docker restart jellyfin
```

---

## ⚙️ Configuration

1. Log into your Jellyfin web interface as an Administrator.
2. Navigate to **Dashboard -> Plugins -> Subtitle Downloader**.
3. Configure your desired providers:
   - **OpenSubtitles.com**: Enable/disable, set custom API key or account credentials, and click **Test Connection**.
   - **SubDL**: Enter your free API key from [subdl.com](https://subdl.com) and click **Test Connection**.
   - **Jellyfin Native Providers**: Enable or disable integration with internal providers.
   - **General Preferences**: Set preferred languages (e.g., `en,fa`), toggle saving to media folder, and toggle sync match highlighting.
4. Click **Save Configuration**.

---

## 🎬 How to Use in the Player

1. Play any movie or TV series episode in Jellyfin Web.
2. In the bottom player controls, look for the new subtitle download button (`closed_caption` icon with a green down-arrow).
3. Click the button to open the **Download Subtitles** overlay.
4. The modal automatically displays the detected video file, computes its hash, and searches available providers.
5. Badges clearly indicate:
   - `⭐ 100% Synced (Hash Match)`: Exact binary match with the video file.
   - `🎯 Release Match`: Compatible release group match (e.g. `1080p`, `WEB-DL`).
6. Click **Download & Apply**.
7. The subtitle is instantly attached to the video player and begins showing cues on screen immediately!
