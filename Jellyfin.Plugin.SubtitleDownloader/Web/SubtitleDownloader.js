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

    // Position state (defaults: bottom center, normal scale)
    let posVertical = parseFloat(localStorage.getItem('subdl_sub_vertical') || '88');
    let posHorizontal = parseFloat(localStorage.getItem('subdl_sub_horizontal') || '50');
    let fontScale = parseFloat(localStorage.getItem('subdl_sub_font_scale') || '1.0');

    let isAltDown = false;
    let isDragging = false;
    let activeCueText = '';

    // Observe player buttons bar
    function initObserver() {
        const observer = new MutationObserver(() => {
            attachPlayerControls();
            ensureSubtitleOverlay();
        });
        observer.observe(document.body, { childList: true, subtree: true });

        attachPlayerControls();
        ensureSubtitleOverlay();
        initShortcutListeners();
    }

    function getActiveButtonsBar() {
        return document.querySelector('[data-type="video-osd"]:not(.hide) .buttons') ||
               document.querySelector('.videoOsdBottom-maincontrols .buttons') ||
               document.querySelector('.osdControls .buttons');
    }

    function attachPlayerControls() {
        const bar = getActiveButtonsBar();
        if (!bar) return;

        // 1. Download Subtitles Button
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
                openSubtitleModal('search');
            };

            const subBtn = bar.querySelector('.btnSubtitles') || bar.querySelector('.btnAudio') || bar.querySelector('.btnVideoOsdSettings');
            if (subBtn && subBtn.parentNode === bar) {
                bar.insertBefore(btn, subBtn.nextSibling);
            } else {
                bar.appendChild(btn);
            }
        }

        // 2. Subtitle Position Quick Button
        if (!bar.querySelector('#btnSubtitlePosition')) {
            const posBtn = document.createElement('button');
            posBtn.id = 'btnSubtitlePosition';
            posBtn.type = 'button';
            posBtn.className = 'paper-icon-button-light autoSize btnSubtitlePosition osdIconButton';
            posBtn.setAttribute('title', 'Change Subtitle Position (Or Hold Alt + Drag)');
            posBtn.setAttribute('aria-label', 'Change Subtitle Position');
            posBtn.innerHTML = `
                <span class="material-icons" style="font-size:22px;">open_with</span>
            `;

            posBtn.onclick = (e) => {
                e.stopPropagation();
                openSubtitleModal('position');
            };

            const dlBtn = bar.querySelector('#btnSubtitleDownloader');
            if (dlBtn && dlBtn.parentNode === bar) {
                bar.insertBefore(posBtn, dlBtn.nextSibling);
            } else {
                bar.appendChild(posBtn);
            }
        }
    }

    // =========================================================
    // Subtitle Overlay & Drag-and-Drop Positioning Engine
    // =========================================================
    function ensureSubtitleOverlay() {
        const video = document.querySelector('video') || document.querySelector('.htmlvideoplayer');
        if (!video) return;

        const container = video.parentElement || document.body;
        if (container.querySelector('#subdlSubtitleOverlay')) return;

        const overlay = document.createElement('div');
        overlay.id = 'subdlSubtitleOverlay';
        overlay.className = 'subdl-subtitle-overlay';

        overlay.innerHTML = `
            <div id="subdlSubtitleBox" class="subdl-subtitle-box">
                <div class="subdl-drag-badge">
                    <span class="material-icons" style="font-size: 13px;">open_with</span>
                    <span>Drag to move</span>
                </div>
                <span id="subdlSubtitleText" class="subdl-subtitle-text"></span>
            </div>
        `;

        // Video container should have relative positioning
        const computedPos = window.getComputedStyle(container).position;
        if (computedPos === 'static') {
            container.style.position = 'relative';
        }

        container.appendChild(overlay);

        applySavedPosition();
        setupDragEvents(overlay, container);
        hookVideoTracks(video);
    }

    function applySavedPosition() {
        const box = document.getElementById('subdlSubtitleBox');
        if (!box) return;

        box.style.top = `${posVertical}%`;
        box.style.left = `${posHorizontal}%`;
        box.style.setProperty('--subdl-font-scale', fontScale);
    }

    function setSubtitlePosition(topPct, leftPct) {
        posVertical = Math.max(5, Math.min(95, topPct));
        if (leftPct !== undefined) {
            posHorizontal = Math.max(10, Math.min(90, leftPct));
        }

        localStorage.setItem('subdl_sub_vertical', posVertical.toFixed(1));
        localStorage.setItem('subdl_sub_horizontal', posHorizontal.toFixed(1));

        applySavedPosition();

        // Update slider if modal is open
        const slider = document.getElementById('subdlPositionSlider');
        const sliderVal = document.getElementById('subdlPositionSliderValue');
        if (slider) slider.value = posVertical;
        if (sliderVal) sliderVal.textContent = `${Math.round(posVertical)}%`;
    }

    function setFontScale(newScale) {
        fontScale = Math.max(0.6, Math.min(2.5, newScale));
        localStorage.setItem('subdl_sub_font_scale', fontScale.toFixed(2));
        applySavedPosition();

        const scaleVal = document.getElementById('subdlFontScaleValue');
        if (scaleVal) scaleVal.textContent = `${Math.round(fontScale * 100)}%`;
    }

    function setupDragEvents(overlay, container) {
        const box = overlay.querySelector('#subdlSubtitleBox');

        let startX, startY;

        box.addEventListener('mousedown', (e) => {
            if (!isAltDown && !box.classList.contains('is-positioning')) {
                return;
            }

            e.preventDefault();
            e.stopPropagation();

            isDragging = true;
            box.classList.add('is-dragging');

            const onMouseMove = (moveEvent) => {
                if (!isDragging) return;
                const rect = container.getBoundingClientRect();

                const mouseX = moveEvent.clientX - rect.left;
                const mouseY = moveEvent.clientY - rect.top;

                const pctX = (mouseX / rect.width) * 100;
                const pctY = (mouseY / rect.height) * 100;

                setSubtitlePosition(pctY, pctX);
            };

            const onMouseUp = () => {
                isDragging = false;
                box.classList.remove('is-dragging');
                window.removeEventListener('mousemove', onMouseMove);
                window.removeEventListener('mouseup', onMouseUp);

                showToast(`✓ Subtitle position saved (${Math.round(posVertical)}%)`);
            };

            window.addEventListener('mousemove', onMouseMove);
            window.addEventListener('mouseup', onMouseUp);
        });

        // Click anywhere in video container while holding Alt to teleport subtitle there
        container.addEventListener('click', (e) => {
            if (!isAltDown) return;
            const rect = container.getBoundingClientRect();
            const pctY = ((e.clientY - rect.top) / rect.height) * 100;
            const pctX = ((e.clientX - rect.left) / rect.width) * 100;

            setSubtitlePosition(pctY, pctX);
            showToast(`✓ Subtitle moved to ${Math.round(posVertical)}%`);
        });
    }

    function initShortcutListeners() {
        window.addEventListener('keydown', (e) => {
            if (e.key === 'Alt') {
                isAltDown = true;
                updatePositioningMode();
            }
        });

        window.addEventListener('keyup', (e) => {
            if (e.key === 'Alt') {
                isAltDown = false;
                updatePositioningMode();
            }
        });

        window.addEventListener('blur', () => {
            isAltDown = false;
            updatePositioningMode();
        });
    }

    function updatePositioningMode() {
        const box = document.getElementById('subdlSubtitleBox');
        const textEl = document.getElementById('subdlSubtitleText');
        if (!box || !textEl) return;

        if (isAltDown) {
            box.classList.add('is-positioning');
            if (!activeCueText) {
                textEl.innerHTML = '<span style="opacity:0.8;">[ Subtitle Sample Text — Drag to Position ]</span>';
            }
        } else {
            box.classList.remove('is-positioning');
            if (!activeCueText) {
                textEl.innerHTML = '';
            }
        }
    }

    function hookVideoTracks(video) {
        function checkActiveCues() {
            let foundText = '';
            for (let i = 0; i < video.textTracks.length; i++) {
                const tt = video.textTracks[i];
                if (tt.mode === 'showing' || tt.mode === 'hidden') {
                    if (tt.activeCues && tt.activeCues.length > 0) {
                        for (let j = 0; j < tt.activeCues.length; j++) {
                            foundText += (foundText ? '<br>' : '') + tt.activeCues[j].text;
                        }
                    }
                }
            }

            activeCueText = foundText;
            const textEl = document.getElementById('subdlSubtitleText');
            if (textEl) {
                if (foundText) {
                    textEl.innerHTML = foundText;
                } else if (!isAltDown) {
                    textEl.innerHTML = '';
                }
            }
        }

        video.textTracks.addEventListener('change', checkActiveCues);
        video.addEventListener('timeupdate', checkActiveCues);
    }

    // =========================================================
    // Modal & In-Player UI
    // =========================================================
    async function getCurrentPlayingItem() {
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

    async function openSubtitleModal(initialTab = 'search') {
        ensureModalDom();
        const backdrop = document.getElementById('subdlModalBackdrop');
        backdrop.classList.add('active');

        switchTab(initialTab);

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

        if (initialTab === 'search') {
            performSearch();
        }
    }

    function closeModal() {
        const backdrop = document.getElementById('subdlModalBackdrop');
        if (backdrop) {
            backdrop.classList.remove('active');
        }
    }

    function switchTab(tabName) {
        const searchView = document.getElementById('subdlSearchView');
        const posView = document.getElementById('subdlPositionView');
        const tabSearch = document.getElementById('subdlTabSearch');
        const tabPos = document.getElementById('subdlTabPosition');

        if (tabName === 'position') {
            searchView.style.display = 'none';
            posView.classList.add('active');
            tabSearch.classList.remove('active');
            tabPos.classList.add('active');
        } else {
            posView.classList.remove('active');
            searchView.style.display = 'flex';
            tabPos.classList.remove('active');
            tabSearch.classList.add('active');
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
                    <span>Download & Apply</span>
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

            // Apply subtitle directly into active HTML5 video element
            applySubtitleToPlayer(response, item);

            btnElement.className = 'subdl-download-btn applied';
            btnElement.innerHTML = `
                <span class="material-icons" style="font-size: 18px;">check</span>
                <span>Applied!</span>
            `;

            showToast(`✓ Subtitle applied: ${item.LanguageName || item.Language} (${item.Provider})`);

            setTimeout(() => {
                closeModal();
            }, 1200);

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

    function applySubtitleToPlayer(downloadResponse, item) {
        const video = document.querySelector('video') || document.querySelector('.htmlvideoplayer');
        if (!video) return;

        const vttContent = downloadResponse.VttContent || downloadResponse.SubtitleText;
        if (!vttContent) return;

        const blob = new Blob([vttContent], { type: 'text/vtt;charset=utf-8' });
        const blobUrl = URL.createObjectURL(blob);

        const trackLabel = `${item.LanguageName || item.Language} [Synced - ${item.Provider}]`;

        const existingTracks = video.querySelectorAll('track');
        existingTracks.forEach(t => {
            if (t.label === trackLabel) t.remove();
        });

        const track = document.createElement('track');
        track.kind = 'subtitles';
        track.label = trackLabel;
        track.srclang = item.Language;
        track.src = blobUrl;
        track.default = true;

        video.appendChild(track);

        setTimeout(() => {
            for (let i = 0; i < video.textTracks.length; i++) {
                const tt = video.textTracks[i];
                if (tt.label === trackLabel) {
                    tt.mode = 'showing';
                }
            }
        }, 100);

        console.log(`[SubtitleDownloader] Attached subtitle track "${trackLabel}" directly to active player.`);
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
                        <button type="button" class="subdl-tab-btn active" id="subdlTabSearch">
                            <span class="material-icons" style="font-size:16px;">search</span> Search
                        </button>
                        <button type="button" class="subdl-tab-btn" id="subdlTabPosition">
                            <span class="material-icons" style="font-size:16px;">open_with</span> Position & Style
                        </button>
                        <button type="button" class="subdl-close-btn" id="subdlBtnClose" title="Close">
                            <span class="material-icons">close</span>
                        </button>
                    </div>
                </div>

                <!-- Tab 1: Search View -->
                <div id="subdlSearchView" style="display:flex;flex-direction:column;flex:1;overflow:hidden;">
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

                <!-- Tab 2: Position & Styling View -->
                <div id="subdlPositionView" class="subdl-position-panel">
                    <div class="subdl-pro-tip">
                        <span class="material-icons" style="font-size: 20px;">touch_app</span>
                        <div><strong>Interactive Drag & Drop:</strong> Hold the <strong>Alt</strong> key on your keyboard, then click and drag the subtitle anywhere on the video screen!</div>
                    </div>

                    <div class="subdl-setting-group">
                        <div class="subdl-setting-title">
                            <span class="material-icons" style="font-size: 18px;">vertical_align_bottom</span>
                            <span>Position Presets</span>
                        </div>
                        <div class="subdl-presets-row">
                            <button type="button" class="subdl-preset-btn" id="btnPresetBottom">Bottom (Default 88%)</button>
                            <button type="button" class="subdl-preset-btn" id="btnPresetLowerThird">Lower-Third (78%)</button>
                            <button type="button" class="subdl-preset-btn" id="btnPresetCenter">Center Screen (50%)</button>
                            <button type="button" class="subdl-preset-btn" id="btnPresetTop">Top (10%)</button>
                        </div>
                    </div>

                    <div class="subdl-setting-group">
                        <div class="subdl-setting-title">
                            <span class="material-icons" style="font-size: 18px;">tune</span>
                            <span>Fine-tune Vertical Position</span>
                        </div>
                        <div class="subdl-slider-row">
                            <input type="range" id="subdlPositionSlider" min="5" max="95" value="${posVertical}" />
                            <span id="subdlPositionSliderValue" class="subdl-slider-value">${Math.round(posVertical)}%</span>
                        </div>
                    </div>

                    <div class="subdl-setting-group">
                        <div class="subdl-setting-title">
                            <span class="material-icons" style="font-size: 18px;">format_size</span>
                            <span>Subtitle Font Size</span>
                        </div>
                        <div class="subdl-presets-row" style="align-items:center;">
                            <button type="button" class="subdl-preset-btn" id="btnFontSmaller">A- (Smaller)</button>
                            <span id="subdlFontScaleValue" style="font-family:monospace;font-size:1rem;color:#38bdf8;padding:0 8px;">${Math.round(fontScale * 100)}%</span>
                            <button type="button" class="subdl-preset-btn" id="btnFontLarger">A+ (Larger)</button>
                            <button type="button" class="subdl-preset-btn" id="btnFontReset" style="margin-left:auto;">Reset to 100%</button>
                        </div>
                    </div>

                    <div style="margin-top:12px;display:flex;justify-content:flex-end;">
                        <button type="button" class="subdl-preset-btn" id="btnResetAllPosition" style="color:#ef4444;border-color:#ef4444;">
                            Reset All to Factory Defaults
                        </button>
                    </div>
                </div>
            </div>
        `;

        backdrop.onclick = closeModal;
        backdrop.querySelector('#subdlBtnClose').onclick = closeModal;

        backdrop.querySelector('#subdlTabSearch').onclick = () => switchTab('search');
        backdrop.querySelector('#subdlTabPosition').onclick = () => switchTab('position');

        backdrop.querySelector('#subdlBtnSearch').onclick = performSearch;
        backdrop.querySelector('#subdlQueryInput').onkeydown = (e) => {
            if (e.key === 'Enter') performSearch();
        };

        backdrop.querySelector('#subdlLangSelect').onchange = performSearch;
        backdrop.querySelector('#subdlSyncOnly').onchange = () => {
            const syncOnly = backdrop.querySelector('#subdlSyncOnly').checked;
            renderResults(cachedSearchResults, syncOnly);
        };

        // Preset actions
        backdrop.querySelector('#btnPresetBottom').onclick = () => setSubtitlePosition(88, 50);
        backdrop.querySelector('#btnPresetLowerThird').onclick = () => setSubtitlePosition(78, 50);
        backdrop.querySelector('#btnPresetCenter').onclick = () => setSubtitlePosition(50, 50);
        backdrop.querySelector('#btnPresetTop').onclick = () => setSubtitlePosition(10, 50);

        // Slider action
        const slider = backdrop.querySelector('#subdlPositionSlider');
        slider.oninput = (e) => setSubtitlePosition(parseFloat(e.target.value));

        // Font scale actions
        backdrop.querySelector('#btnFontSmaller').onclick = () => setFontScale(fontScale - 0.1);
        backdrop.querySelector('#btnFontLarger').onclick = () => setFontScale(fontScale + 0.1);
        backdrop.querySelector('#btnFontReset').onclick = () => setFontScale(1.0);

        backdrop.querySelector('#btnResetAllPosition').onclick = () => {
            setSubtitlePosition(88, 50);
            setFontScale(1.0);
            showToast('✓ Subtitle position & styling reset to defaults');
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
