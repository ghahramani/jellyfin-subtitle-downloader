# Jellyfin Subtitle Downloader Plugin

[![Jellyfin 10.10+](https://img.shields.io/badge/Jellyfin-10.10%2B%20%7C%2010.11%20%7C%2012.0-blue.svg)](https://jellyfin.org)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

A clean, conflict-free in-player **Subtitle Downloader** for Jellyfin.

Allows users to search, sync-match, and download subtitles directly inside the video player. Downloaded subtitles are saved beside your media files and automatically appear in Jellyfin's standard CC subtitle menu without any second subtitle overlay or rendering conflicts.

---

## 🌟 Highlights

- **In-Player Subtitle Downloader**: Adds a dedicated button (`closed_caption` icon with a green down-arrow) in the player control bar.
- **Conflict-Free Native Subtitles**: No custom subtitle overlays or secondary renderers. Uses Jellyfin's 100% native subtitle rendering engine and standard CC menu.
- **Automatic Player Refresh & Resume**: Once downloaded, the player refreshes and seamlessly resumes playback with the new subtitle active.
- **Accurate Sync Detection**:
  - **OpenSubtitles 64-bit MovieHash**: Calculates the binary hash from the first and last 64 KB of the media file in milliseconds for a **100% exact sync match**.
  - **Release Group Matching**: Identifies filename tags (`1080p`, `2160p`, `WEB-DL`, `BluRay`, `RARBG`, `YTS`, `PSA`, `GalaxyRG`, `EVO`, etc.) to find matching releases.
- **Multi-Source & Free Providers**:
  - **OpenSubtitles.com REST API (v2)**: Out-of-the-box integration with default official key or your custom/VIP credentials.
  - **SubDL**: Subtitle database with release group synchronization and Persian/Arabic & international catalog (accepts free API key from [subdl.com](https://subdl.com)).
  - **Jellyfin Native Subtitle Providers**: Seamlessly taps into any subtitle provider plugin already installed on Jellyfin Server.
- **Automatic Encoding Correction**: Converts legacy character sets (such as Persian/Arabic `Windows-1256`, Latin `Windows-1252`, `ISO-8859-1`, `UTF-16`) to clean UTF-8.
- **Library Persistence**: Saves `.srt` alongside your media files (`MovieName.lang.srt` or `Series.S01E01.lang.srt`) and triggers a high-priority Jellyfin metadata refresh.

---

## 📦 Easy Installation from Jellyfin Admin Panel

You can install this plugin directly from your Jellyfin Dashboard:

1. Log in to your Jellyfin Web interface as an **Administrator**.
2. Open the **Dashboard** -> **Plugins** -> **Repositories** tab.
3. Click the **+** (Add Repository) button.
4. Enter the repository details:
   - **Repository Name**: `Subtitle Downloader`
   - **Repository URL**: 
     ```
     https://raw.githubusercontent.com/ghahramani/jellyfin-subtitle-downloader/main/manifest.json
     ```
5. Click **Save**.
6. Switch to the **Catalog** tab.
7. Under **Subtitles**, locate **Subtitle Downloader** and click **Install**.
8. Restart your Jellyfin server.

---

## 🎮 How to Use in the Player

1. Start playing any video in Jellyfin Web.
2. In the bottom control bar, click the **Download Subtitles** button (`closed_caption` with down arrow).
3. The search overlay will automatically detect the media title, calculate the video file hash, and query all enabled providers.
4. Badges highlight:
   - `⭐ 100% Synced (Hash Match)`: Guaranteed binary match with your video file.
   - `🎯 Release Match`: Compatible release tag match (e.g. `1080p`, `WEB-DL`).
5. Click **Download Subtitle**. The subtitle is downloaded, converted to UTF-8, saved beside the video file, and the player refreshes to display it natively in the standard CC menu!

---

## ⚙️ Admin Configuration

Navigate to **Dashboard -> Plugins -> Subtitle Downloader** to configure:

- **OpenSubtitles.com**: Enable/disable, custom API key, optional username/password for VIP accounts, and connection testing.
- **SubDL**: Enable/disable, enter your free API key from [subdl.com](https://subdl.com), and test connectivity.
- **Jellyfin Native Providers**: Toggle integration with internal Jellyfin subtitle providers.
- **General Preferences**: Set preferred languages (e.g., `en,fa`), toggle saving to media folder, and toggle sync match highlighting.

---

## 🛠 Manual Build & Development

Requires Docker or `.NET 10 Preview SDK`:

```bash
git clone https://github.com/ghahramani/jellyfin-subtitle-downloader.git
cd jellyfin-subtitle-downloader
./build.sh
```

The script compiles the project, generates the distribution ZIP in `dist/`, and installs the plugin into your Jellyfin data directory.

---

## 📄 License

MIT License. See [LICENSE](LICENSE) for details.
