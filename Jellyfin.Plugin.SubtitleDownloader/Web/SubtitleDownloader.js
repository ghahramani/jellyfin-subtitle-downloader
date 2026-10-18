(function () {
    'use strict';

    console.log('[SubtitleDownloader] In-player script initializing...');

    // Inject CSS stylesheet into page
    if (!document.getElementById('subdl-injected-style')) {
        const link = document.createElement('link');
        link.id = 'subdl-injected-style';
        link.rel = 'stylesheet';
        link.href = (window.ApiClient ? window.ApiClient.getUrl('SubtitleDownloader/ClientStyle') : '../SubtitleDownloader/ClientStyle');
        document.head.appendChild(link);
    }

    const SUPPORTED_LANGUAGES = [
        { code: 'en', name: 'English' },
        { code: 'fa', name: 'Persian / فارسی' },
        { code: 'es', name: 'Spanish / Español' },
        { code: 'fr', name: 'French / Français' },
        { code: 'de', name: 'German / Deutsch' },
        { code: 'it', name: 'Italian / Italiano' },
        { code: 'ar', name: 'Arabic / العربية' },
        { code: 'tr', name: 'Turkish / Türkçe' },
        { code: 'pt', name: 'Portuguese / Português' },
        { code: 'ru', name: 'Russian / Русский' },
        { code: 'all', name: 'All Languages' }
    ];

    let currentItemData = null;
    let cachedSearchResults = [];

    // =========================================================
    // Jellyfin Web & PlaybackManager Integration
    // =========================================================
    function getPlaybackManager() {
        if (window.playbackManager) {
            return window.playbackManager;
        }

        if (window.webpackChunk) {
            try {
                window.webpackChunk.push([
                    ['subdl_probe_' + Math.random().toString(36).substring(7)],
                    {},
                    function (req) {
                        if (req && req.c) {
                            for (const id in req.c) {
                                const exp = req.c[id]?.exports;
                                if (!exp) continue;
                                if (exp.f && typeof exp.f.subtitleTracks === 'function' && typeof exp.f.getCurrentPlayer === 'function') {
                                    window.playbackManager = exp.f;
                                    break;
                                }
                                if (exp.default && typeof exp.default.subtitleTracks === 'function' && typeof exp.default.getCurrentPlayer === 'function') {
                                    window.playbackManager = exp.default;
                                    break;
                                }
                                if (typeof exp.subtitleTracks === 'function' && typeof exp.getCurrentPlayer === 'function') {
                                    window.playbackManager = exp;
                                    break;
                                }
                            }
                        }
                        if (!window.playbackManager && typeof req === 'function') {
                            try {
                                const m = req(68221);
                                if (m?.f && typeof m.f.subtitleTracks === 'function') {
                                    window.playbackManager = m.f;
                                }
                            } catch (e) {}
                        }
                    }
                ]);
            } catch (e) {
                console.warn('[SubtitleDownloader] Could not resolve playbackManager from webpack:', e);
            }
        }

        return window.playbackManager || null;
    }

    function getActivePlayer() {
        const pm = getPlaybackManager();
        if (pm && typeof pm.getCurrentPlayer === 'function') {
            const p = pm.getCurrentPlayer();
            if (p) return p;
        }
        return window.activeJellyfinPlayer || null;
    }

    function getActiveMediaSource() {
        const pm = getPlaybackManager();
        const player = getActivePlayer();
        if (pm && player && typeof pm.currentMediaSource === 'function') {
            try {
                const ms = pm.currentMediaSource(player);
                if (ms) return ms;
            } catch (e) {}
        }
        if (player?._currentPlayOptions?.mediaSource) {
            return player._currentPlayOptions.mediaSource;
        }
        return null;
    }

    // Check and handle auto-selection / auto-resume after reload or playback changes
    function checkPendingSubtitleSelection() {
        const pendingRaw = sessionStorage.getItem('subdl_pending_selection');
        if (!pendingRaw) return;

        let pending;
        try {
            pending = JSON.parse(pendingRaw);
        } catch (e) {
            sessionStorage.removeItem('subdl_pending_selection');
            return;
        }

        // Expire after 60 seconds
        if (Date.now() - pending.timestamp > 60000) {
            sessionStorage.removeItem('subdl_pending_selection');
            return;
        }

        const pm = getPlaybackManager();
        const player = getActivePlayer();

        // If player is NOT currently active (e.g. user reloaded and landed on home page):
        // Resume playing the exact item from the exact same moment
        if (!player && pm && typeof pm.play === 'function') {
            const serverId = pending.serverId || (window.ApiClient ? window.ApiClient.serverId() : '');
            console.log('[SubtitleDownloader] Auto-resuming video after reload from same moment:', pending.itemId, pending.startPositionTicks);

            sessionStorage.removeItem('subdl_pending_selection');

            const playOptions = {
                ids: [pending.itemId],
                serverId: serverId,
                startPositionTicks: pending.startPositionTicks || 0,
                fullscreen: true
            };
            if (pending.item) {
                playOptions.items = [pending.item];
            }
            if (pending.streamIndex != null && pending.streamIndex >= 0) {
                playOptions.subtitleStreamIndex = pending.streamIndex;
            }

            pm.play(playOptions).then(() => {
                console.log('[SubtitleDownloader] Resume playback initiated successfully.');
            }).catch(err => {
                console.error('[SubtitleDownloader] Error resuming playback:', err);
            });
            return;
        }

        // If player is active, search for the newly added subtitle track and select it
        if (player && pm) {
            const mediaSource = getActiveMediaSource();
            const streams = (mediaSource && Array.isArray(mediaSource.MediaStreams))
                ? mediaSource.MediaStreams
                : (typeof pm.subtitleTracks === 'function' ? pm.subtitleTracks(player) : []);

            if (Array.isArray(streams) && streams.length > 0) {
                const targetLang = (pending.language || '').toLowerCase();
                const targetIndex = pending.streamIndex;
                const targetPath = (pending.savedFilePath || '').toLowerCase();

                // Find matching subtitle stream
                const match = (targetIndex != null && streams.find(s => s.Type === 'Subtitle' && s.Index === targetIndex)) ||
                    streams.find(s =>
                        s.Type === 'Subtitle' &&
                        (
                            (targetPath && s.Path && s.Path.toLowerCase() === targetPath) ||
                            ((s.Language && s.Language.toLowerCase().startsWith(targetLang)) ||
                             (s.LanguageCode && s.LanguageCode.toLowerCase().startsWith(targetLang))) &&
                            (s.IsExternal || (s.Path && s.Path.endsWith('.srt')))
                        )
                    ) ||
                    streams.find(s =>
                        s.Type === 'Subtitle' &&
                        ((s.Language && s.Language.toLowerCase().startsWith(targetLang)) ||
                         (s.LanguageCode && s.LanguageCode.toLowerCase().startsWith(targetLang)))
                    );

                if (match && typeof pm.setSubtitleStreamIndex === 'function') {
                    console.log('[SubtitleDownloader] Auto-selecting new subtitle stream index:', match.Index, match.DisplayTitle);
                    pm.setSubtitleStreamIndex(match.Index, player);
                    sessionStorage.removeItem('subdl_pending_selection');
                    showToast(`✓ Subtitle active: ${match.DisplayTitle || match.Language || pending.language}`);
                }
            }
        }
    }

    // Observe player buttons bar
    function initObserver() {
        if (window.Events) {
            window.Events.on(window, 'playerchange', checkPendingSubtitleSelection);
            window.Events.on(window, 'playbackstart', checkPendingSubtitleSelection);
        }

        const observer = new MutationObserver(() => {
            attachPlayerControls();
            checkPendingSubtitleSelection();
        });
        observer.observe(document.body, { childList: true, subtree: true });

        attachPlayerControls();
        setTimeout(checkPendingSubtitleSelection, 300);
        setTimeout(checkPendingSubtitleSelection, 1000);
        setTimeout(checkPendingSubtitleSelection, 2500);
    }

    function getActiveButtonsBar() {
        return document.querySelector('[data-type="video-osd"]:not(.hide) .buttons') ||
               document.querySelector('.videoOsdBottom-maincontrols .buttons') ||
               document.querySelector('.osdControls .buttons');
    }

    function attachPlayerControls() {
        const bar = getActiveButtonsBar();
        if (!bar) return;

        // Download Subtitles Button
        if (!bar.querySelector('#btnSubtitleDownloader')) {
            const btn = document.createElement('button');
            btn.id = 'btnSubtitleDownloader';
            btn.type = 'button';
            btn.className = 'paper-icon-button-light autoSize btnDownloadSubtitles osdIconButton';
            btn.setAttribute('title', 'Search & Download Subtitles');
            btn.setAttribute('aria-label', 'Search & Download Subtitles');
            btn.innerHTML = `
                <span class="material-icons closed_caption" style="position:relative;display:inline-flex;align-items:center;justify-content:center;">
                    <span style="font-size:24px;">closed_caption</span>
                    <span style="position:absolute;bottom:-3px;right:-3px;font-size:10px;font-weight:900;background:#10b981;color:#fff;border-radius:50%;width:13px;height:13px;display:flex;align-items:center;justify-content:center;box-shadow:0 0 4px rgba(0,0,0,0.6);line-height:1;">↓</span>
                </span>
            `;

            btn.onclick = (e) => {
                e.stopPropagation();
                openSubtitleModal();
            };

            const subBtn = bar.querySelector('.btnSubtitles') || bar.querySelector('.btnAudio') || bar.querySelector('.btnVideoOsdSettings');
            if (subBtn && subBtn.parentNode === bar) {
                bar.insertBefore(btn, subBtn.nextSibling);
            } else {
                bar.appendChild(btn);
            }
        }
    }

    // =========================================================
    // Modal & In-Player Search UI
    // =========================================================
    async function getCurrentPlayingItem() {
        const pm = getPlaybackManager();
        const player = getActivePlayer();
        if (pm && player && typeof pm.currentItem === 'function') {
            try {
                const cur = pm.currentItem(player);
                if (cur && cur.Id) {
                    return cur;
                }
            } catch (e) {}
        }

        let itemId = document.querySelector('.btnUserRating')?.getAttribute('data-id');

        if (!itemId && window.ApiClient) {
            try {
                const sessions = await window.ApiClient.getSessions({ deviceId: window.ApiClient.deviceId() });
                if (sessions && sessions.length > 0 && sessions[0].NowPlayingItem) {
                    itemId = sessions[0].NowPlayingItem.Id;
                }
            } catch (ex) {
                console.warn('[SubtitleDownloader] Failed to fetch session item:', ex);
            }
        }

        if (!itemId) {
            const match = location.hash.match(/[?&]id=([a-f0-9]+)/i);
            if (match) itemId = match[1];
        }

        if (itemId && window.ApiClient) {
            try {
                return await window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), itemId);
            } catch (ex) {
                console.warn('[SubtitleDownloader] Failed to get item details:', ex);
            }
        }

        return null;
    }

    async function openSubtitleModal() {
        ensureModalDom();
        const backdrop = document.getElementById('subdlModalBackdrop');
        backdrop.classList.add('active');

        const titleEl = document.getElementById('subdlTitle');
        const metaEl = document.getElementById('subdlMeta');
        const inputEl = document.getElementById('subdlQueryInput');

        currentItemData = await getCurrentPlayingItem();

        if (currentItemData) {
            let displayTitle = currentItemData.Name;
            let metaText = '';

            if (currentItemData.SeriesName) {
                displayTitle = `${currentItemData.SeriesName} - S${String(currentItemData.ParentIndexNumber || 1).padStart(2, '0')}E${String(currentItemData.IndexNumber || 1).padStart(2, '0')}`;
                metaText = `Episode: "${currentItemData.Name}"`;
            } else if (currentItemData.ProductionYear) {
                metaText = `(${currentItemData.ProductionYear})`;
            }

            titleEl.textContent = displayTitle;
            metaEl.textContent = metaText;
            inputEl.value = currentItemData.SeriesName || currentItemData.Name;
        } else {
            titleEl.textContent = 'Active Video';
            metaEl.textContent = '';
            inputEl.value = '';
        }

        performSearch();
    }

    function closeModal() {
        const backdrop = document.getElementById('subdlModalBackdrop');
        if (backdrop) {
            backdrop.classList.remove('active');
        }
    }

    async function performSearch() {
        if (!currentItemData) {
            document.getElementById('subdlResults').innerHTML = `
                <div class="subdl-empty-state">
                    <span class="material-icons" style="font-size: 40px; color: #ef4444;">error_outline</span>
                    <span>Could not detect currently playing media item.</span>
                </div>
            `;
            return;
        }

        const resultsEl = document.getElementById('subdlResults');
        const langEl = document.getElementById('subdlLangSelect');
        const queryEl = document.getElementById('subdlQueryInput');
        const syncOnlyEl = document.getElementById('subdlSyncOnly');

        const lang = langEl.value;
        const query = queryEl.value.trim();

        localStorage.setItem('subdl_preferred_lang', lang);

        resultsEl.innerHTML = `
            <div class="subdl-loading-state">
                <div class="subdl-spinner"></div>
                <span>Searching OpenSubtitles, SubDL, and subtitle providers...</span>
            </div>
        `;

        try {
            const url = window.ApiClient.getUrl('SubtitleDownloader/Search', {
                itemId: currentItemData.Id,
                language: lang,
                query: query
            });

            const response = await window.ApiClient.ajax({
                type: 'GET',
                url: url,
                dataType: 'json'
            });

            cachedSearchResults = response.results || [];
            renderResults(cachedSearchResults, syncOnlyEl.checked);
        } catch (ex) {
            resultsEl.innerHTML = `
                <div class="subdl-empty-state">
                    <span class="material-icons" style="font-size: 40px; color: #ef4444;">cloud_off</span>
                    <span>Search error: ${ex.message || ex}</span>
                </div>
            `;
        }
    }

    function renderResults(results, syncOnly) {
        const container = document.getElementById('subdlResults');

        let filtered = results;
        if (syncOnly) {
            filtered = results.filter(r => r.IsHashMatch || r.IsReleaseMatch);
        }

        if (!filtered || filtered.length === 0) {
            container.innerHTML = `
                <div class="subdl-empty-state">
                    <span class="material-icons" style="font-size: 40px;">subtitles_off</span>
                    <span>No subtitles found for this selection. Try changing the language or query.</span>
                </div>
            `;
            return;
        }

        container.innerHTML = '';
        filtered.forEach((item, index) => {
            const card = document.createElement('div');
            card.className = `subdl-result-card ${item.IsHashMatch ? 'is-hash-match' : ''} ${item.IsReleaseMatch ? 'is-release-match' : ''}`;

            let providerClass = 'provider-os';
            if (item.Provider.toLowerCase().includes('subdl')) providerClass = 'provider-subdl';
            else if (item.Provider.toLowerCase().includes('jellyfin')) providerClass = 'provider-jf';

            card.innerHTML = `
                <div class="subdl-result-info">
                    <div class="subdl-release-name" title="${escapeHtml(item.ReleaseName)}">
                        ${escapeHtml(item.ReleaseName)}
                    </div>
                    <div class="subdl-badges-row">
                        ${item.IsHashMatch ? '<span class="subdl-tag hash-match">⭐ 100% Synced (Hash Match)</span>' : ''}
                        ${item.IsReleaseMatch && !item.IsHashMatch ? '<span class="subdl-tag release-match">🎯 Release Match</span>' : ''}
                        <span class="subdl-tag ${providerClass}">${escapeHtml(item.Provider)}</span>
                        <span class="subdl-tag lang">${escapeHtml(item.LanguageName || item.Language)}</span>
                        ${item.HearingImpaired ? '<span class="subdl-tag extra">HI</span>' : ''}
                        ${item.Forced ? '<span class="subdl-tag extra">Forced</span>' : ''}
                        ${item.DownloadCount ? `<span class="subdl-tag extra">${item.DownloadCount.toLocaleString()} dl</span>` : ''}
                        ${item.Rating ? `<span class="subdl-tag extra">★ ${item.Rating.toFixed(1)}</span>` : ''}
                    </div>
                </div>
                <button type="button" class="subdl-download-btn" id="btnDl_${index}">
                    <span class="material-icons" style="font-size: 18px;">download</span>
                    <span>Download Subtitle</span>
                </button>
            `;

            const dlBtn = card.querySelector(`#btnDl_${index}`);
            dlBtn.onclick = () => downloadAndApplySubtitle(item, dlBtn);

            container.appendChild(card);
        });
    }

    async function downloadAndApplySubtitle(item, btnElement) {
        btnElement.disabled = true;
        btnElement.innerHTML = `
            <div class="subdl-spinner" style="width:14px;height:14px;border-width:2px;"></div>
            <span>Downloading...</span>
        `;

        try {
            const downloadUrl = window.ApiClient.getUrl('SubtitleDownloader/Download');
            const payload = {
                ItemId: currentItemData.Id,
                SubtitleId: item.Id,
                Provider: item.Provider,
                Format: item.Format || 'srt',
                Language: item.Language || 'en',
                DownloadUrl: item.DownloadUrl,
                FileId: item.FileId
            };

            const response = await window.ApiClient.ajax({
                type: 'POST',
                url: downloadUrl,
                data: JSON.stringify(payload),
                contentType: 'application/json',
                dataType: 'json'
            });

            if (!response.Success) {
                throw new Error(response.Message || 'Download failed');
            }

            btnElement.className = 'subdl-download-btn applied';
            btnElement.innerHTML = `
                <span class="material-icons" style="font-size: 18px;">check</span>
                <span>Downloaded!</span>
            `;

            // Capture current playback tick position and item details
            const video = document.querySelector('video') || document.querySelector('.htmlvideoplayer');
            const currentTicks = (video && video.currentTime)
                ? Math.floor(video.currentTime * 10000000)
                : 0;

            const pm = getPlaybackManager();
            const player = getActivePlayer();
            const serverId = currentItemData.ServerId || (window.ApiClient ? window.ApiClient.serverId() : '');

            // Store in sessionStorage with full details in case user reloads the browser
            try {
                sessionStorage.setItem('subdl_pending_selection', JSON.stringify({
                    itemId: currentItemData.Id,
                    item: currentItemData,
                    serverId: serverId,
                    startPositionTicks: currentTicks,
                    streamIndex: response.StreamIndex,
                    language: item.Language || response.Language || 'en',
                    languageName: item.LanguageName || item.Language,
                    savedFilePath: response.FilePath || '',
                    timestamp: Date.now()
                }));
            } catch (storageErr) {
                console.warn('[SubtitleDownloader] sessionStorage error:', storageErr);
            }

            closeModal();
            showToast(`✓ Subtitle downloaded! Playing from current position...`);

            // Fetch fresh item metadata from server so updated media streams are known
            let targetItem = currentItemData;
            if (window.ApiClient && currentItemData?.Id) {
                try {
                    targetItem = await window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), currentItemData.Id) || currentItemData;
                } catch (e) {}
            }

            // Resume playback of the exact video from the exact same moment without reloading page
            if (pm && typeof pm.play === 'function') {
                console.log('[SubtitleDownloader] In-player resume from moment:', currentTicks, 'with subtitle index:', response.StreamIndex);

                const playOptions = {
                    items: [targetItem],
                    ids: [targetItem.Id],
                    serverId: serverId,
                    startPositionTicks: currentTicks,
                    fullscreen: true
                };

                if (response.StreamIndex != null && response.StreamIndex >= 0) {
                    playOptions.subtitleStreamIndex = response.StreamIndex;
                }

                pm.play(playOptions).then(() => {
                    console.log('[SubtitleDownloader] Play resumed successfully at moment:', currentTicks);
                    // Ensure the downloaded subtitle stream is selected
                    if (response.StreamIndex != null && response.StreamIndex >= 0) {
                        setTimeout(() => {
                            const curPlayer = getActivePlayer();
                            if (curPlayer && typeof pm.setSubtitleStreamIndex === 'function') {
                                pm.setSubtitleStreamIndex(response.StreamIndex, curPlayer);
                            }
                        }, 600);
                    }
                }).catch(err => {
                    console.warn('[SubtitleDownloader] In-player resume error:', err);
                });
            }

        } catch (ex) {
            btnElement.disabled = false;
            btnElement.className = 'subdl-download-btn';
            btnElement.innerHTML = `
                <span class="material-icons" style="font-size: 18px;">download</span>
                <span>Retry Download</span>
            `;
            alert('Failed to download subtitle: ' + (ex.message || ex));
        }
    }

    function showToast(message) {
        let toast = document.getElementById('subdlToast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'subdlToast';
            toast.className = 'subdl-toast';
            document.body.appendChild(toast);
        }

        toast.innerHTML = `<span class="material-icons" style="color: #10b981;">check_circle</span><span>${escapeHtml(message)}</span>`;
        toast.classList.add('show');

        setTimeout(() => {
            toast.classList.remove('show');
        }, 3500);
    }

    function ensureModalDom() {
        if (document.getElementById('subdlModalBackdrop')) {
            return;
        }

        const savedLang = localStorage.getItem('subdl_preferred_lang') || 'en';

        const backdrop = document.createElement('div');
        backdrop.id = 'subdlModalBackdrop';
        backdrop.className = 'subdl-modal-backdrop';

        backdrop.innerHTML = `
            <div class="subdl-modal" onclick="event.stopPropagation();">
                <div class="subdl-modal-header">
                    <div class="subdl-title-box">
                        <div class="subdl-title-row">
                            <span class="material-icons" style="color: #10b981;">subtitles</span>
                            <h3 id="subdlTitle">Download Subtitles</h3>
                        </div>
                        <div id="subdlMeta" class="subdl-subtitle-meta"></div>
                    </div>
                    <div class="subdl-header-actions">
                        <button type="button" class="subdl-close-btn" id="subdlBtnClose" title="Close">
                            <span class="material-icons">close</span>
                        </button>
                    </div>
                </div>

                <div class="subdl-search-bar">
                    <select id="subdlLangSelect" class="subdl-select-lang">
                        ${SUPPORTED_LANGUAGES.map(l => `<option value="${l.code}" ${l.code === savedLang ? 'selected' : ''}>${l.name}</option>`).join('')}
                    </select>

                    <input type="text" id="subdlQueryInput" class="subdl-search-input" placeholder="Movie or Series title..." />

                    <button type="button" id="subdlBtnSearch" class="subdl-search-btn">
                        <span class="material-icons" style="font-size: 18px;">search</span>
                        <span>Search</span>
                    </button>

                    <label class="subdl-sync-filter" title="Only display subtitles with exact file hash or release matching">
                        <input type="checkbox" id="subdlSyncOnly" />
                        <span>Sync Only</span>
                    </label>
                </div>

                <div id="subdlResults" class="subdl-results-container"></div>
            </div>
        `;

        backdrop.onclick = closeModal;
        backdrop.querySelector('#subdlBtnClose').onclick = closeModal;

        backdrop.querySelector('#subdlBtnSearch').onclick = performSearch;
        backdrop.querySelector('#subdlQueryInput').onkeydown = (e) => {
            if (e.key === 'Enter') performSearch();
        };

        backdrop.querySelector('#subdlLangSelect').onchange = performSearch;
        backdrop.querySelector('#subdlSyncOnly').onchange = () => {
            const syncOnly = backdrop.querySelector('#subdlSyncOnly').checked;
            renderResults(cachedSearchResults, syncOnly);
        };

        document.body.appendChild(backdrop);
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // Initialize
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initObserver);
    } else {
        initObserver();
    }
})();
