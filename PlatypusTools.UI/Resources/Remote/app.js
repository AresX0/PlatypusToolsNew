// Platypus Remote - Web Controller
// Real-time audio control via SignalR

class PlatypusRemote {
    constructor() {
        this.connection = null;
        this.isConnected = false;
        this.nowPlaying = null;
        this.queue = [];
        this.library = [];
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 10;
        this.currentTab = 'nowPlayingTab';
        this.isStreaming = false;
        this.deferredInstallPrompt = null;
        this.mode = 'control'; // 'control' = PC plays, 'stream' = phone plays
        this.streamTrack = null; // Current track metadata for stream mode
        this.streamLibraryIndex = -1; // Index in library for next/prev in stream mode
        this.streamQueue = []; // Mobile-only queue for stream mode
        this.streamQueueIndex = -1; // Current position in stream queue
        this.sleepTimer = null; // Sleep timer interval
        this.sleepTimerEnd = null; // When sleep timer expires
        this.systemInfoLoaded = false; // Track if system info was loaded
        this.touchStartX = 0; // Swipe gesture tracking
        this.touchStartY = 0;
        this.videoLibrary = []; // Video library cache
        this.videoLibraryLoaded = false; // Track if video library was loaded
        this.vaultItems = []; // Cached vault items
        this.vaultFolders = []; // Cached vault folders
        this.vaultUnlocked = false;
        this.vaultAuthRefreshInterval = null; // TOTP refresh timer
        this.generatedPassword = '';
        this.qrStream = null; // Camera stream for QR scanner
        this.playlists = []; // Cached playlists
        this.activePlaylist = 'all'; // Currently selected playlist filter
        this.organizedContent = null; // Cached TV series/movies
        this.videoViewMode = 'grid'; // 'grid' | 'series' | 'movies'
        this.seriesDrillPath = []; // Navigation stack for series → season → episode
        this.currentVideoFilePath = null; // Currently playing video file path
        this.historyUpdateTimer = null; // Timer for periodic position updates
        
        this.init();
    }

    async init() {
        this.bindElements();
        
        // Check Cloudflare Zero Trust auth before proceeding
        if (!await this.checkAuth()) return;
        
        this.bindEvents();
        this.bindStreamAudioEvents();
        this.bindKeyboardShortcuts();
        this.bindSwipeGestures();
        this.bindSleepTimer();
        this.setupInstallPrompt();
        await this.connect();
    }

    // Centralized fetch wrapper that handles 401 responses from CF Zero Trust
    async apiFetch(url, options = {}) {
        const response = await fetch(url, options);
        if (response.status === 401) {
            const data = await response.json().catch(() => ({}));
            this.showAuthRequired(data.message || 'Authentication required');
            throw new Error('Unauthorized');
        }
        return response;
    }

    // Check Cloudflare Zero Trust authentication status
    async checkAuth() {
        try {
            const res = await fetch('/api/auth/status');
            if (res.status === 401) {
                // No valid CF Access token — show login message
                const data = await res.json().catch(() => ({}));
                this.showAuthRequired(data.message || 'Cloudflare Zero Trust authentication required');
                return false;
            }
            if (res.ok) {
                const auth = await res.json();
                if (auth.zeroTrustEnabled && !auth.authenticated) {
                    this.showAuthRequired('Please authenticate through Cloudflare Access to continue.');
                    return false;
                }
                if (auth.authenticated && auth.email) {
                    this.authEmail = auth.email;
                }
            }
            return true;
        } catch (err) {
            // Network error or server down — let it fall through to normal connect
            console.warn('Auth check failed (server may be down):', err.message);
            return true;
        }
    }

    // Display authentication-required screen
    showAuthRequired(message) {
        const loadingView = this.elements?.loadingView || document.getElementById('loadingView');
        const mainView = this.elements?.mainView || document.getElementById('mainView');
        const bottomNav = this.elements?.bottomNav || document.getElementById('bottomNav');
        
        if (mainView) mainView.style.display = 'none';
        if (bottomNav) bottomNav.style.display = 'none';
        if (loadingView) {
            loadingView.style.display = 'flex';
            loadingView.innerHTML = `
                <div style="text-align:center; padding:40px 20px; max-width:400px;">
                    <div style="font-size:64px; margin-bottom:20px;">🛡️</div>
                    <h2 style="margin:0 0 12px; color:#fff;">Authentication Required</h2>
                    <p style="color:#aaa; margin:0 0 20px; line-height:1.5;">${message}</p>
                    <p style="color:#888; font-size:13px;">This server is protected by Cloudflare Zero Trust.<br>
                    You will be redirected to the login page automatically.</p>
                    <button onclick="location.reload()" 
                            style="margin-top:20px; padding:12px 32px; background:#2196F3; color:#fff; border:none; border-radius:8px; font-size:15px; cursor:pointer;">
                        🔄 Retry
                    </button>
                </div>`;
        }
    }

    bindElements() {
        this.elements = {
            statusDot: document.getElementById('statusDot'),
            statusText: document.getElementById('statusText'),
            errorContainer: document.getElementById('errorContainer'),
            loadingView: document.getElementById('loadingView'),
            mainView: document.getElementById('mainView'),
            albumArt: document.getElementById('albumArt'),
            trackTitle: document.getElementById('trackTitle'),
            trackArtist: document.getElementById('trackArtist'),
            trackAlbum: document.getElementById('trackAlbum'),
            progressBar: document.getElementById('progressBar'),
            progressFill: document.getElementById('progressFill'),
            currentTime: document.getElementById('currentTime'),
            totalTime: document.getElementById('totalTime'),
            playPauseBtn: document.getElementById('playPauseBtn'),
            prevBtn: document.getElementById('prevBtn'),
            nextBtn: document.getElementById('nextBtn'),
            shuffleBtn: document.getElementById('shuffleBtn'),
            repeatBtn: document.getElementById('repeatBtn'),
            volumeSlider: document.getElementById('volumeSlider'),
            volumeValue: document.getElementById('volumeValue'),
            queueList: document.getElementById('queueList'),
            queueCount: document.getElementById('queueCount'),
            // New elements
            bottomNav: document.getElementById('bottomNav'),
            installBanner: document.getElementById('installBanner'),
            installBtn: document.getElementById('installBtn'),
            closeBannerBtn: document.getElementById('closeBannerBtn'),
            librarySearch: document.getElementById('librarySearch'),
            searchBtn: document.getElementById('searchBtn'),
            libraryList: document.getElementById('libraryList'),
            streamToggle: document.getElementById('streamToggle'),
            streamAudio: document.getElementById('streamAudio'),
            // Mode buttons
            controlModeBtn: document.getElementById('controlModeBtn'),
            streamModeBtn: document.getElementById('streamModeBtn'),
            // Sleep timer
            sleepTimerSelect: document.getElementById('sleepTimerSelect'),
            sleepTimerLabel: document.getElementById('sleepTimerLabel'),
            sleepTimerCountdown: document.getElementById('sleepTimerCountdown'),
            // System info
            systemInfo: document.getElementById('systemInfo'),
            // Video
            videoSearch: document.getElementById('videoSearch'),
            videoSearchBtn: document.getElementById('videoSearchBtn'),
            videoGrid: document.getElementById('videoGrid'),
            videoFolderCount: document.getElementById('videoFolderCount'),
            videoFileCount: document.getElementById('videoFileCount'),
            videoFolderList: document.getElementById('videoFolderList'),
            videoRescanBtn: document.getElementById('videoRescanBtn'),
            videoPlayerModal: document.getElementById('videoPlayerModal'),
            videoPlayer: document.getElementById('videoPlayer'),
            videoPlayerTitle: document.getElementById('videoPlayerTitle'),
            videoPlayerClose: document.getElementById('videoPlayerClose'),
            // Plex-like features
            continueWatching: document.getElementById('continueWatching'),
            continueWatchingList: document.getElementById('continueWatchingList'),
            playlistList: document.getElementById('playlistList'),
            playlistSection: document.getElementById('playlistSection'),
            videoViewTabs: document.getElementById('videoViewTabs'),
            seriesList: document.getElementById('seriesList'),
            subtitleBar: document.getElementById('subtitleBar'),
            subtitleSelect: document.getElementById('subtitleSelect')
        };
    }

    bindEvents() {
        // Playback controls
        this.elements.playPauseBtn.addEventListener('click', () => this.playPause());
        this.elements.prevBtn.addEventListener('click', () => this.previous());
        this.elements.nextBtn.addEventListener('click', () => this.next());
        this.elements.shuffleBtn.addEventListener('click', () => this.toggleShuffle());
        this.elements.repeatBtn.addEventListener('click', () => this.toggleRepeat());

        // Volume
        this.elements.volumeSlider.addEventListener('input', (e) => {
            this.setVolume(e.target.value / 100);
            this.elements.volumeValue.textContent = `${e.target.value}%`;
        });

        // Progress bar click to seek
        this.elements.progressBar.addEventListener('click', (e) => {
            const rect = this.elements.progressBar.getBoundingClientRect();
            const percent = (e.clientX - rect.left) / rect.width;
            if (this.mode === 'stream') {
                const audio = this.elements.streamAudio;
                if (audio && audio.duration) {
                    audio.currentTime = percent * audio.duration;
                }
                return;
            }
            if (!this.nowPlaying) return;
            const seekTime = percent * this.nowPlaying.durationSeconds;
            this.seek(seekTime);
        });

        // Bottom navigation
        document.querySelectorAll('.nav-item').forEach(item => {
            item.addEventListener('click', () => this.switchTab(item.dataset.tab));
        });

        // Install banner
        if (this.elements.installBtn) {
            this.elements.installBtn.addEventListener('click', () => this.installApp());
        }
        if (this.elements.closeBannerBtn) {
            this.elements.closeBannerBtn.addEventListener('click', () => this.hideInstallBanner());
        }

        // Library search
        if (this.elements.searchBtn) {
            this.elements.searchBtn.addEventListener('click', () => this.searchLibrary());
        }
        if (this.elements.librarySearch) {
            this.elements.librarySearch.addEventListener('keypress', (e) => {
                if (e.key === 'Enter') this.searchLibrary();
            });
        }

        // Library tabs
        document.querySelectorAll('.library-tab').forEach(tab => {
            tab.addEventListener('click', () => this.filterLibrary(tab.dataset.filter));
        });

        // Stream toggle
        if (this.elements.streamToggle) {
            this.elements.streamToggle.addEventListener('click', () => this.toggleStreaming());
        }

        // Mode buttons
        if (this.elements.controlModeBtn) {
            this.elements.controlModeBtn.addEventListener('click', () => this.setMode('control'));
        }
        if (this.elements.streamModeBtn) {
            this.elements.streamModeBtn.addEventListener('click', () => this.setMode('stream'));
        }

        // Video search
        if (this.elements.videoSearchBtn) {
            this.elements.videoSearchBtn.addEventListener('click', () => this.searchVideoLibrary());
        }
        if (this.elements.videoSearch) {
            this.elements.videoSearch.addEventListener('keypress', (e) => {
                if (e.key === 'Enter') this.searchVideoLibrary();
            });
        }

        // Video rescan
        if (this.elements.videoRescanBtn) {
            this.elements.videoRescanBtn.addEventListener('click', () => this.rescanVideoLibrary());
        }

        // Video player close
        if (this.elements.videoPlayerClose) {
            this.elements.videoPlayerClose.addEventListener('click', () => this.closeVideoPlayer());
        }

        // Vault unlock form
        const vaultForm = document.getElementById('vaultUnlockForm');
        if (vaultForm) {
            vaultForm.addEventListener('submit', (e) => this.unlockVault(e));
        }

        // Vault MFA form
        const mfaForm = document.getElementById('vaultMfaForm');
        if (mfaForm) {
            mfaForm.addEventListener('submit', (e) => this.verifyMfa(e));
        }
    }

    bindStreamAudioEvents() {
        const audio = this.elements.streamAudio;
        if (!audio) return;

        audio.addEventListener('timeupdate', () => {
            if (this.mode !== 'stream' || !this.isStreaming) return;
            this.updateProgress(audio.currentTime, audio.duration || 0);
        });

        audio.addEventListener('loadedmetadata', () => {
            if (this.mode !== 'stream') return;
            this.updateProgress(audio.currentTime, audio.duration || 0);
        });

        audio.addEventListener('play', () => {
            if (this.mode !== 'stream') return;
            this.elements.playPauseBtn.textContent = '⏸️';
        });

        audio.addEventListener('pause', () => {
            if (this.mode !== 'stream') return;
            this.elements.playPauseBtn.textContent = '▶️';
        });

        audio.addEventListener('ended', () => {
            if (this.mode !== 'stream') return;
            // Auto-advance to next track in library
            this.streamNext();
        });

        audio.addEventListener('error', (e) => {
            if (this.mode !== 'stream') return;
            console.error('Stream audio error:', e);
        });
    }

    setMode(mode) {
        this.mode = mode;
        
        // Update button styles
        if (this.elements.controlModeBtn) {
            this.elements.controlModeBtn.classList.toggle('active', mode === 'control');
        }
        if (this.elements.streamModeBtn) {
            this.elements.streamModeBtn.classList.toggle('active', mode === 'stream');
        }

        if (mode === 'stream') {
            // Restore stream track UI if we have one, otherwise show prompt
            if (this.streamTrack) {
                this.showStreamTrackUI();
            } else {
                this.elements.trackTitle.textContent = 'Pick a track from Library';
                this.elements.trackArtist.textContent = 'Stream Mode';
                this.elements.trackAlbum.textContent = '';
                this.elements.playPauseBtn.textContent = '▶️';
                this.updateProgress(0, 0);
            }
            // Show stream queue if on queue tab
            if (this.currentTab === 'queueTab') this.renderQueue();
        } else {
            // Switching to control - stop local audio, restore remote state
            this.stopStreaming();
            if (this.nowPlaying) {
                this.applyNowPlayingToUI(this.nowPlaying);
            }
            // Restore PC queue if on queue tab
            if (this.currentTab === 'queueTab') this.renderQueue();
        }

        // Update status text
        this.elements.statusText.textContent = mode === 'stream' 
            ? 'Streaming Mode' 
            : 'Connected';
    }

    async connect() {
        try {
            // Load SignalR from CDN
            await this.loadSignalR();
            
            // Connect to the hub
            const hubUrl = window.location.origin + '/hub/platypus';
            this.connection = new signalR.HubConnectionBuilder()
                .withUrl(hubUrl)
                .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
                .configureLogging(signalR.LogLevel.Warning)
                .build();

            // Handle connection events
            this.connection.onreconnecting(() => {
                this.setConnectionStatus(false, 'Reconnecting...');
            });

            this.connection.onreconnected(() => {
                this.setConnectionStatus(true, 'Connected');
                this.connection.invoke('GetNowPlaying');
                this.connection.invoke('GetQueue');
            });

            this.connection.onclose(() => {
                this.setConnectionStatus(false, 'Disconnected');
                this.showLoading();
                this.scheduleReconnect();
            });

            // Handle server messages
            this.connection.on('nowPlaying', (data) => this.handleNowPlaying(data));
            this.connection.on('position', (position) => this.handlePosition(position));
            this.connection.on('queue', (data) => this.handleQueue(data));

            // Start connection
            await this.connection.start();
            this.setConnectionStatus(true, 'Connected');
            this.showMain();
            this.reconnectAttempts = 0;

            // Request initial state
            await this.connection.invoke('GetNowPlaying');
            await this.connection.invoke('GetQueue');

        } catch (error) {
            console.error('Connection failed:', error);
            this.showError('Failed to connect to PlatypusTools');
            this.scheduleReconnect();
        }
    }

    async loadSignalR() {
        if (window.signalR) return;
        
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js';
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    scheduleReconnect() {
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            this.showError('Unable to connect. Please check that PlatypusTools is running.');
            return;
        }
        
        this.reconnectAttempts++;
        const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 30000);
        console.log(`Reconnecting in ${delay}ms (attempt ${this.reconnectAttempts})`);
        
        setTimeout(() => this.connect(), delay);
    }

    setConnectionStatus(connected, text) {
        this.isConnected = connected;
        this.elements.statusDot.classList.toggle('connected', connected);
        this.elements.statusText.textContent = text;
    }

    showLoading() {
        this.elements.loadingView.style.display = 'flex';
        this.elements.mainView.style.display = 'none';
        if (this.elements.bottomNav) this.elements.bottomNav.style.display = 'none';
    }

    showMain() {
        this.elements.loadingView.style.display = 'none';
        this.elements.mainView.style.display = 'flex';
        if (this.elements.bottomNav) this.elements.bottomNav.style.display = 'flex';
        // Load Continue Watching on first show
        this.loadContinueWatching();
    }

    showError(message) {
        this.elements.errorContainer.innerHTML = `<div class="error-message">${message}</div>`;
    }

    clearError() {
        this.elements.errorContainer.innerHTML = '';
    }

    // Apply now-playing data to UI elements (shared by control + stream modes)
    applyNowPlayingToUI(data) {
        this.elements.trackTitle.textContent = data.title || 'No Track Playing';
        this.elements.trackArtist.textContent = data.artist || '-';
        this.elements.trackAlbum.textContent = data.album || '-';

        if (data.albumArtData) {
            this.elements.albumArt.innerHTML = `<img src="data:image/jpeg;base64,${data.albumArtData}" alt="Album Art">`;
        } else {
            this.elements.albumArt.innerHTML = '<span>🎵</span>';
        }

        this.elements.playPauseBtn.textContent = data.isPlaying ? '⏸️' : '▶️';
        this.updateProgress(data.positionSeconds || 0, data.durationSeconds || 0);

        const volumePercent = Math.round((data.volume || 0) * 100);
        this.elements.volumeSlider.value = volumePercent;
        this.elements.volumeValue.textContent = `${volumePercent}%`;

        if (data.isShuffle !== undefined) {
            this.elements.shuffleBtn.classList.toggle('active', data.isShuffle);
        }
        if (data.repeatMode !== undefined) {
            this.elements.repeatBtn.classList.toggle('active', data.repeatMode > 0);
            this.elements.repeatBtn.textContent = data.repeatMode === 2 ? '🔂' : '🔁';
        }
    }

    // Show the current stream track info in the UI
    showStreamTrackUI() {
        if (!this.streamTrack) return;
        this.elements.trackTitle.textContent = this.streamTrack.title || 'Unknown';
        this.elements.trackArtist.textContent = this.streamTrack.artist || 'Streaming to device';
        this.elements.trackAlbum.textContent = this.streamTrack.album || '';
        this.elements.albumArt.innerHTML = '<span>🎵</span>';
        
        const audio = this.elements.streamAudio;
        if (audio && audio.duration) {
            this.updateProgress(audio.currentTime, audio.duration);
        }
        this.elements.playPauseBtn.textContent = (audio && !audio.paused) ? '⏸️' : '▶️';
    }

    // Server message handlers
    handleNowPlaying(data) {
        this.nowPlaying = data;
        this.clearError();

        // In stream mode, don't let remote state overwrite our local playback UI
        if (this.mode === 'stream') return;

        this.applyNowPlayingToUI(data);
    }

    handlePosition(positionSeconds) {
        if (this.nowPlaying) {
            this.nowPlaying.positionSeconds = positionSeconds;
        }
        // In stream mode, local audio timeupdate drives the progress bar
        if (this.mode === 'stream') return;
        if (this.nowPlaying) {
            this.updateProgress(positionSeconds, this.nowPlaying.durationSeconds);
        }
    }

    handleQueue(data) {
        this.queue = data || [];
        // In stream mode, don't overwrite the stream queue display
        if (this.mode === 'stream') return;
        this.renderQueue();
    }

    updateProgress(position, duration) {
        const percent = duration > 0 ? (position / duration) * 100 : 0;
        this.elements.progressFill.style.width = `${percent}%`;
        this.elements.currentTime.textContent = this.formatTime(position);
        this.elements.totalTime.textContent = this.formatTime(duration);
    }

    renderQueue() {
        // In stream mode, show the mobile stream queue
        if (this.mode === 'stream') {
            this.renderStreamQueue();
            return;
        }

        if (!this.queue.length) {
            this.elements.queueList.innerHTML = '<div class="no-track"><p>No tracks in queue</p></div>';
            this.elements.queueCount.textContent = '0 tracks';
            return;
        }

        this.elements.queueCount.textContent = `${this.queue.length} tracks`;
        
        this.elements.queueList.innerHTML = this.queue.map(item => `
            <div class="queue-item ${item.isCurrentTrack ? 'current' : ''}" data-index="${item.index}">
                <span class="index">${item.index + 1}</span>
                <div class="track-details">
                    <div class="track-title">${this.escapeHtml(item.title)}</div>
                    <div class="track-artist">${this.escapeHtml(item.artist)}</div>
                </div>
                <span class="track-duration">${this.formatTime(item.durationSeconds)}</span>
            </div>
        `).join('');

        // Bind click events to queue items
        this.elements.queueList.querySelectorAll('.queue-item').forEach(item => {
            item.addEventListener('click', () => {
                const index = parseInt(item.dataset.index);
                this.playQueueItem(index);
            });
        });
    }

    renderStreamQueue() {
        if (!this.streamQueue.length) {
            this.elements.queueList.innerHTML = '<div class="no-track"><p>No tracks in phone queue</p><p style="font-size:0.85rem;margin-top:8px;">Tap a track in Library to add it</p></div>';
            this.elements.queueCount.textContent = '0 tracks (phone)';
            return;
        }

        this.elements.queueCount.textContent = `${this.streamQueue.length} tracks (phone)`;

        this.elements.queueList.innerHTML = this.streamQueue.map((item, index) => `
            <div class="queue-item ${index === this.streamQueueIndex ? 'current' : ''}" data-index="${index}">
                <span class="index">${index + 1}</span>
                <div class="track-details">
                    <div class="track-title">${this.escapeHtml(item.title || item.fileName || 'Unknown')}</div>
                    <div class="track-artist">${this.escapeHtml(item.artist || 'Unknown Artist')}</div>
                </div>
                <button class="action" data-remove="${index}" style="background:#F44336;color:white;border:none;border-radius:4px;padding:4px 8px;font-size:0.8rem;">✕</button>
            </div>
        `).join('');

        // Bind click to play from stream queue
        this.elements.queueList.querySelectorAll('.queue-item').forEach(item => {
            item.addEventListener('click', (e) => {
                if (e.target.dataset.remove !== undefined) return; // Let remove handle it
                const index = parseInt(item.dataset.index);
                this.playQueueItem(index);
            });
        });

        // Bind remove buttons
        this.elements.queueList.querySelectorAll('[data-remove]').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const idx = parseInt(btn.dataset.remove);
                this.streamQueue.splice(idx, 1);
                // Adjust current index if needed
                if (idx < this.streamQueueIndex) this.streamQueueIndex--;
                else if (idx === this.streamQueueIndex) this.streamQueueIndex = -1;
                this.renderStreamQueue();
            });
        });
    }

    formatTime(seconds) {
        if (!seconds || isNaN(seconds)) return '0:00';
        const mins = Math.floor(seconds / 60);
        const secs = Math.floor(seconds % 60);
        return `${mins}:${secs.toString().padStart(2, '0')}`;
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text || '';
        return div.innerHTML;
    }

    escapeJsString(text) {
        // Escape for use inside JS single-quoted string
        return (text || '').replace(/\\/g, '\\\\').replace(/'/g, "\\'");
    }

    // Control methods - mode-aware: route to local audio or remote SignalR
    playPause() {
        if (this.mode === 'stream') {
            const audio = this.elements.streamAudio;
            if (!audio || !this.isStreaming) return;
            // MUST be synchronous for mobile tap-to-play
            if (audio.paused) {
                audio.play().catch(e => console.error('Play failed:', e));
            } else {
                audio.pause();
            }
            return;
        }
        if (!this.isConnected) return;
        this.connection.invoke('PlayPause').catch(e => console.error('PlayPause failed:', e));
    }

    play() {
        if (this.mode === 'stream') {
            const audio = this.elements.streamAudio;
            if (!audio || !this.isStreaming) return;
            audio.play().catch(e => console.error('Play failed:', e));
            return;
        }
        if (!this.isConnected) return;
        this.connection.invoke('Play').catch(e => console.error('Play failed:', e));
    }

    pause() {
        if (this.mode === 'stream') {
            const audio = this.elements.streamAudio;
            if (audio) audio.pause();
            return;
        }
        if (!this.isConnected) return;
        this.connection.invoke('Pause').catch(e => console.error('Pause failed:', e));
    }

    next() {
        if (this.mode === 'stream') {
            this.streamNext();
            return;
        }
        if (!this.isConnected) return;
        this.connection.invoke('Next').catch(e => console.error('Next failed:', e));
    }

    previous() {
        if (this.mode === 'stream') {
            this.streamPrevious();
            return;
        }
        if (!this.isConnected) return;
        this.connection.invoke('Previous').catch(e => console.error('Previous failed:', e));
    }

    seek(positionSeconds) {
        if (this.mode === 'stream') {
            const audio = this.elements.streamAudio;
            if (audio && audio.duration) {
                audio.currentTime = positionSeconds;
            }
            return;
        }
        if (!this.isConnected) return;
        this.connection.invoke('Seek', positionSeconds).catch(e => console.error('Seek failed:', e));
    }

    setVolume(volume) {
        if (this.mode === 'stream') {
            const audio = this.elements.streamAudio;
            if (audio) audio.volume = volume;
            return;
        }
        if (!this.isConnected) return;
        this.connection.invoke('SetVolume', volume).catch(e => console.error('SetVolume failed:', e));
    }

    async toggleShuffle() {
        if (!this.isConnected) return;
        await this.connection.invoke('ToggleShuffle');
    }

    async toggleRepeat() {
        if (!this.isConnected) return;
        await this.connection.invoke('ToggleRepeat');
    }

    playQueueItem(index) {
        if (this.mode === 'stream') {
            // Play from stream queue
            if (index >= 0 && index < this.streamQueue.length) {
                this.streamQueueIndex = index;
                const item = this.streamQueue[index];
                if (item?.filePath) this.playLocalAudio(item.filePath, item);
                this.renderQueue(); // Update current highlight
            }
            return;
        }
        if (!this.isConnected) return;
        this.connection.invoke('PlayQueueItem', index).catch(e => console.error('PlayQueueItem failed:', e));
    }

    // Stream mode: advance to next track (stream queue first, then library)
    streamNext() {
        // Use stream queue if it has items
        if (this.streamQueue.length > 0) {
            this.streamQueueIndex = (this.streamQueueIndex + 1) % this.streamQueue.length;
            const item = this.streamQueue[this.streamQueueIndex];
            if (item?.filePath) {
                this.playLocalAudio(item.filePath, item);
                this.renderQueue();
            }
            return;
        }
        // Fallback to library
        if (!this.library.length) return;
        this.streamLibraryIndex = (this.streamLibraryIndex + 1) % this.library.length;
        const item = this.library[this.streamLibraryIndex];
        if (item?.filePath) this.playLocalAudio(item.filePath, item);
    }

    // Stream mode: go to previous track
    streamPrevious() {
        // If more than 3s in, restart current track
        const audio = this.elements.streamAudio;
        if (audio && audio.currentTime > 3) {
            audio.currentTime = 0;
            return;
        }
        // Use stream queue if it has items
        if (this.streamQueue.length > 0) {
            this.streamQueueIndex = (this.streamQueueIndex - 1 + this.streamQueue.length) % this.streamQueue.length;
            const item = this.streamQueue[this.streamQueueIndex];
            if (item?.filePath) {
                this.playLocalAudio(item.filePath, item);
                this.renderQueue();
            }
            return;
        }
        // Fallback to library
        if (!this.library.length) return;
        this.streamLibraryIndex = (this.streamLibraryIndex - 1 + this.library.length) % this.library.length;
        const item = this.library[this.streamLibraryIndex];
        if (item?.filePath) this.playLocalAudio(item.filePath, item);
    }

    // Tab Navigation
    switchTab(tabId) {
        // Update tab panels
        document.querySelectorAll('.tab-panel').forEach(panel => {
            panel.classList.remove('active');
        });
        document.getElementById(tabId)?.classList.add('active');

        // Update nav items
        document.querySelectorAll('.nav-item').forEach(item => {
            item.classList.toggle('active', item.dataset.tab === tabId);
        });

        this.currentTab = tabId;

        // Load library data when switching to library tab
        if (tabId === 'libraryTab') {
            if (this.library.length === 0) this.loadLibrary();
            this.loadPlaylists();
        }

        // Re-render queue when switching to queue tab (mode-aware)
        if (tabId === 'queueTab') {
            this.renderQueue();
        }

        // Load system info when switching to system tab
        if (tabId === 'systemTab' && !this.systemInfoLoaded) {
            this.loadSystemInfo();
        }

        // Load video library when switching to videos tab
        if (tabId === 'videosTab') {
            if (!this.videoLibraryLoaded) this.loadVideoLibrary();
            if (!this.organizedContent) this.loadOrganizedContent();
        }

        // Load continue watching when on Now Playing
        if (tabId === 'nowPlayingTab') {
            this.loadContinueWatching();
        }

        // Load vault status when switching to vault tab
        if (tabId === 'vaultTab') {
            this.loadVaultStatus();
        }

        // Load photos when switching to photos tab
        if (tabId === 'photosTab' && !this.photosLoaded) {
            this.initPhotos();
        }
    }

    // Install Banner (PWA)
    setupInstallPrompt() {
        window.addEventListener('beforeinstallprompt', (e) => {
            e.preventDefault();
            this.deferredInstallPrompt = e;
            // Show install banner if not already installed
            if (this.elements.installBanner && !this.isStandalone()) {
                this.elements.installBanner.classList.remove('hidden');
            }
        });

        // Check if running as PWA
        window.addEventListener('appinstalled', () => {
            this.hideInstallBanner();
            this.deferredInstallPrompt = null;
        });
    }

    isStandalone() {
        return window.matchMedia('(display-mode: standalone)').matches || 
               window.navigator.standalone === true;
    }

    async installApp() {
        if (!this.deferredInstallPrompt) {
            // For iOS, show instructions
            if (/iPhone|iPad|iPod/i.test(navigator.userAgent)) {
                alert('To install: tap the Share button, then "Add to Home Screen"');
            }
            return;
        }

        this.deferredInstallPrompt.prompt();
        const { outcome } = await this.deferredInstallPrompt.userChoice;
        
        if (outcome === 'accepted') {
            this.hideInstallBanner();
        }
        this.deferredInstallPrompt = null;
    }

    hideInstallBanner() {
        if (this.elements.installBanner) {
            this.elements.installBanner.classList.add('hidden');
        }
    }

    // Library Browsing
    async loadLibrary() {
        if (!this.elements.libraryList) return;
        
        this.elements.libraryList.innerHTML = '<div class="loading"><div class="spinner"></div><p>Loading library...</p></div>';

        try {
            const response = await fetch('/api/library');
            if (!response.ok) throw new Error('Failed to load library');
            
            this.library = await response.json();
            this.renderLibrary(this.library);
        } catch (error) {
            console.error('Failed to load library:', error);
            this.elements.libraryList.innerHTML = '<div class="no-content"><p>📂 Unable to load library</p><p style="font-size:0.85rem;margin-top:8px;">Make sure music folders are configured</p></div>';
        }
    }

    async searchLibrary() {
        const query = this.elements.librarySearch?.value?.trim();
        if (!query) {
            this.loadLibrary();
            return;
        }

        this.elements.libraryList.innerHTML = '<div class="loading"><div class="spinner"></div><p>Searching...</p></div>';

        try {
            const response = await fetch(`/api/library/search?q=${encodeURIComponent(query)}`);
            if (!response.ok) throw new Error('Search failed');
            
            const results = await response.json();
            this.renderLibrary(results);
        } catch (error) {
            console.error('Search failed:', error);
            this.elements.libraryList.innerHTML = '<div class="no-content"><p>Search failed</p></div>';
        }
    }

    filterLibrary(filter) {
        // Update tab styling
        document.querySelectorAll('.library-tab').forEach(tab => {
            tab.classList.toggle('active', tab.dataset.filter === filter);
        });

        // Filter the library based on selection
        if (filter === 'all') {
            this.renderLibrary(this.library);
        } else {
            // For now, show all - server-side filtering can be added later
            this.renderLibrary(this.library);
        }
    }

    renderLibrary(items) {
        if (!this.elements.libraryList) return;

        if (!items || items.length === 0) {
            this.elements.libraryList.innerHTML = '<div class="no-content"><p>🎵 No tracks found</p><p style="font-size:0.85rem;margin-top:8px;">Try a different search or browse your folders</p></div>';
            return;
        }

        this.elements.libraryList.innerHTML = items.map((item, index) => `
            <div class="library-item" data-path="${this.escapeHtml(item.filePath || '')}" data-index="${index}">
                <div class="thumb">🎵</div>
                <div class="details">
                    <div class="title">${this.escapeHtml(item.title || item.fileName || 'Unknown')}</div>
                    <div class="subtitle">${this.escapeHtml(item.artist || 'Unknown Artist')}</div>
                </div>
                <button class="action play-btn" data-path="${this.escapeHtml(item.filePath || '')}">▶️</button>
            </div>
        `).join('');

        // Bind play button click events - SYNC for mobile audio
        this.elements.libraryList.querySelectorAll('.play-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const path = btn.dataset.path;
                if (!path) return;
                
                // For stream mode, play audio SYNCHRONOUSLY (required for mobile)
                if (this.mode === 'stream') {
                    const idx = parseInt(btn.closest('.library-item')?.dataset.index || '0');
                    const libraryItem = items[idx];
                    this.playLocalAudio(path, libraryItem);
                    this.switchTab('nowPlayingTab');
                } else {
                    // Control mode can be async
                    this.playOnPC(path);
                }
            });
        });

        // Bind row click events for queue (mode-aware)
        this.elements.libraryList.querySelectorAll('.library-item').forEach(item => {
            item.addEventListener('click', () => {
                const path = item.dataset.path;
                if (!path) return;
                const idx = parseInt(item.dataset.index || '0');
                const libraryItem = items[idx];
                
                if (this.mode === 'stream') {
                    // Add to mobile stream queue
                    this.addToStreamQueue(path, libraryItem);
                } else {
                    // Add to PC queue
                    this.addToQueue(path);
                }
            });
        });
    }

    async playOnPC(filePath) {
        if (!this.isConnected || !filePath) return;
        try {
            await this.connection.invoke('PlayFile', filePath);
            this.switchTab('nowPlayingTab');
        } catch (error) {
            console.error('Failed to play on PC:', error);
        }
    }

    async playLibraryItem(filePath, libraryItem) {
        if (!filePath) return;
        
        if (this.mode === 'stream') {
            // Stream mode: play directly on phone - MUST be immediate for mobile
            this.playLocalAudio(filePath, libraryItem);
            this.switchTab('nowPlayingTab');
        } else {
            // Control mode: play on PC via SignalR
            if (!this.isConnected) return;
            try {
                await this.connection.invoke('PlayFile', filePath);
                this.switchTab('nowPlayingTab');
            } catch (error) {
                console.error('Failed to play file:', error);
            }
        }
    }

    playLocalAudio(filePath, libraryItem) {
        if (!this.elements.streamAudio) return;
        
        const streamUrl = `/api/stream?path=${encodeURIComponent(filePath)}`;
        const audio = this.elements.streamAudio;
        
        // Set source and play immediately - no async between click and play (mobile requirement)
        audio.src = streamUrl;
        audio.load();
        
        const playPromise = audio.play();
        if (playPromise) {
            playPromise.catch(e => console.error('Play failed:', e));
        }
        
        this.isStreaming = true;
        
        // Store stream track metadata
        const filename = filePath.split(/[/\\]/).pop() || 'Unknown';
        this.streamTrack = {
            title: libraryItem?.title || filename.replace(/\.[^/.]+$/, ''),
            artist: libraryItem?.artist || 'Streaming to device',
            album: libraryItem?.album || '',
            filePath: filePath
        };

        // Track library index for next/prev
        if (libraryItem && this.library.length) {
            const idx = this.library.findIndex(l => l.filePath === filePath);
            if (idx >= 0) this.streamLibraryIndex = idx;
        }

        // Update UI directly (bypass handleNowPlaying which guards against stream mode)
        this.showStreamTrackUI();
    }

    addToStreamQueue(filePath, libraryItem) {
        if (!filePath) return;
        const filename = filePath.split(/[/\\]/).pop() || 'Unknown';
        this.streamQueue.push({
            title: libraryItem?.title || filename.replace(/\.[^/.]+$/, ''),
            artist: libraryItem?.artist || 'Unknown Artist',
            album: libraryItem?.album || '',
            filePath: filePath,
            fileName: libraryItem?.fileName || filename
        });

        // If this is the first item and nothing is playing, start playing it
        if (this.streamQueue.length === 1 && !this.isStreaming) {
            this.streamQueueIndex = 0;
            this.playLocalAudio(filePath, libraryItem);
        }

        // Show feedback
        const items = this.elements.libraryList?.querySelectorAll('.library-item');
        items?.forEach(item => {
            if (item.dataset.path === filePath) {
                item.style.background = 'var(--success)';
                setTimeout(() => item.style.background = '', 500);
            }
        });

        // Re-render queue if it's visible
        if (this.currentTab === 'queueTab') {
            this.renderStreamQueue();
        }
    }

    async addToQueue(filePath) {
        if (!filePath || !this.isConnected) return;
        try {
            await this.connection.invoke('AddToQueue', filePath);
            // Show feedback
            const items = this.elements.libraryList?.querySelectorAll('.library-item');
            items?.forEach(item => {
                if (item.dataset.path === filePath) {
                    item.style.background = 'var(--success)';
                    setTimeout(() => item.style.background = '', 500);
                }
            });
        } catch (error) {
            console.error('Failed to add to queue:', error);
        }
    }

    // Audio Streaming
    toggleStreaming() {
        this.isStreaming = !this.isStreaming;
        this.elements.streamToggle?.classList.toggle('active', this.isStreaming);

        if (!this.isStreaming) {
            this.stopStreaming();
        }
    }

    stopStreaming() {
        const audio = this.elements.streamAudio;
        if (audio) {
            audio.pause();
            audio.removeAttribute('src');
            audio.load(); // Reset the element
        }
        this.isStreaming = false;
    }

    // === KEYBOARD SHORTCUTS ===
    bindKeyboardShortcuts() {
        document.addEventListener('keydown', (e) => {
            // Don't trigger when typing in input fields
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.tagName === 'SELECT') return;

            switch (e.key) {
                case ' ':
                case 'k':
                    e.preventDefault();
                    this.playPause();
                    break;
                case 'ArrowRight':
                    if (e.shiftKey) {
                        this.next();
                    } else {
                        // Seek forward 10s
                        if (this.mode === 'stream') {
                            const audio = this.elements.streamAudio;
                            if (audio) audio.currentTime = Math.min(audio.duration || 0, audio.currentTime + 10);
                        } else if (this.nowPlaying) {
                            this.seek(Math.min(this.nowPlaying.durationSeconds, (this.nowPlaying.positionSeconds || 0) + 10));
                        }
                    }
                    e.preventDefault();
                    break;
                case 'ArrowLeft':
                    if (e.shiftKey) {
                        this.previous();
                    } else {
                        // Seek back 10s
                        if (this.mode === 'stream') {
                            const audio = this.elements.streamAudio;
                            if (audio) audio.currentTime = Math.max(0, audio.currentTime - 10);
                        } else if (this.nowPlaying) {
                            this.seek(Math.max(0, (this.nowPlaying.positionSeconds || 0) - 10));
                        }
                    }
                    e.preventDefault();
                    break;
                case 'ArrowUp':
                    e.preventDefault();
                    this.adjustVolume(5);
                    break;
                case 'ArrowDown':
                    e.preventDefault();
                    this.adjustVolume(-5);
                    break;
                case 'm':
                    e.preventDefault();
                    this.toggleMute();
                    break;
                case 's':
                    e.preventDefault();
                    this.toggleShuffle();
                    break;
                case 'r':
                    e.preventDefault();
                    this.toggleRepeat();
                    break;
                case '1':
                    this.switchTab('nowPlayingTab');
                    break;
                case '2':
                    this.switchTab('libraryTab');
                    break;
                case '3':
                    this.switchTab('queueTab');
                    break;
                case '4':
                    this.switchTab('videosTab');
                    break;
                case '5':
                    this.switchTab('systemTab');
                    break;
                case '6':
                    this.switchTab('vaultTab');
                    break;
                case '7':
                    this.switchTab('photosTab');
                    break;
            }
        });
    }

    adjustVolume(delta) {
        const current = parseInt(this.elements.volumeSlider.value) || 70;
        const newVol = Math.max(0, Math.min(100, current + delta));
        this.elements.volumeSlider.value = newVol;
        this.elements.volumeValue.textContent = `${newVol}%`;
        this.setVolume(newVol / 100);
    }

    toggleMute() {
        const slider = this.elements.volumeSlider;
        if (parseInt(slider.value) > 0) {
            this._savedVolume = parseInt(slider.value);
            slider.value = 0;
            this.elements.volumeValue.textContent = '0%';
            this.setVolume(0);
        } else {
            const restore = this._savedVolume || 70;
            slider.value = restore;
            this.elements.volumeValue.textContent = `${restore}%`;
            this.setVolume(restore / 100);
        }
    }

    // === SWIPE GESTURES ===
    bindSwipeGestures() {
        const nowPlayingTab = document.getElementById('nowPlayingTab');
        if (!nowPlayingTab) return;

        nowPlayingTab.addEventListener('touchstart', (e) => {
            this.touchStartX = e.changedTouches[0].screenX;
            this.touchStartY = e.changedTouches[0].screenY;
        }, { passive: true });

        nowPlayingTab.addEventListener('touchend', (e) => {
            const deltaX = e.changedTouches[0].screenX - this.touchStartX;
            const deltaY = e.changedTouches[0].screenY - this.touchStartY;

            // Only handle horizontal swipes (ignore vertical scrolls)
            if (Math.abs(deltaX) > 80 && Math.abs(deltaX) > Math.abs(deltaY) * 1.5) {
                if (deltaX > 0) {
                    this.previous(); // Swipe right = previous
                } else {
                    this.next(); // Swipe left = next
                }
            }
        }, { passive: true });
    }

    // === SLEEP TIMER ===
    bindSleepTimer() {
        const select = this.elements.sleepTimerSelect;
        if (!select) return;

        select.addEventListener('change', () => {
            const minutes = parseInt(select.value);
            if (minutes > 0) {
                this.startSleepTimer(minutes);
            } else {
                this.cancelSleepTimer();
            }
        });
    }

    startSleepTimer(minutes) {
        this.cancelSleepTimer(); // Clear any existing timer
        this.sleepTimerEnd = Date.now() + minutes * 60 * 1000;

        this.sleepTimer = setInterval(() => {
            const remaining = this.sleepTimerEnd - Date.now();
            if (remaining <= 0) {
                this.cancelSleepTimer();
                this.sleepTimerTriggered();
                return;
            }
            const mins = Math.floor(remaining / 60000);
            const secs = Math.floor((remaining % 60000) / 1000);
            if (this.elements.sleepTimerCountdown) {
                this.elements.sleepTimerCountdown.textContent = `${mins}:${secs.toString().padStart(2, '0')}`;
            }
            if (this.elements.sleepTimerLabel) {
                this.elements.sleepTimerLabel.textContent = 'Sleeping in';
            }
        }, 1000);
    }

    cancelSleepTimer() {
        if (this.sleepTimer) {
            clearInterval(this.sleepTimer);
            this.sleepTimer = null;
        }
        this.sleepTimerEnd = null;
        if (this.elements.sleepTimerCountdown) this.elements.sleepTimerCountdown.textContent = '';
        if (this.elements.sleepTimerLabel) this.elements.sleepTimerLabel.textContent = 'Sleep Timer';
        if (this.elements.sleepTimerSelect) this.elements.sleepTimerSelect.value = '0';
    }

    sleepTimerTriggered() {
        // Pause both local and remote playback
        this.pause();
        if (this.mode === 'stream') {
            this.stopStreaming();
        }
        if (this.elements.sleepTimerLabel) this.elements.sleepTimerLabel.textContent = '💤 Paused by sleep timer';
    }

    // === SYSTEM INFO ===
    async loadSystemInfo() {
        if (!this.elements.systemInfo) return;

        this.elements.systemInfo.innerHTML = '<div class="loading"><div class="spinner"></div><p>Loading system info...</p></div>';

        try {
            // Fetch both health endpoints
            const [healthRes, detailedRes] = await Promise.all([
                fetch('/health'),
                fetch('/api/health/detailed').catch(() => null)
            ]);

            if (!healthRes.ok) throw new Error('Health endpoint unavailable');
            const health = await healthRes.json();
            const detailed = detailedRes?.ok ? await detailedRes.json() : null;

            this.systemInfoLoaded = true;
            this.renderSystemInfo(health, detailed);
        } catch (error) {
            console.error('Failed to load system info:', error);
            this.elements.systemInfo.innerHTML = '<div class="no-content"><p>🖥️ Unable to load system info</p><p style="font-size:0.85rem;margin-top:8px;">Server may not support health endpoint</p></div>';
        }
    }

    renderSystemInfo(health, detailed) {
        if (!this.elements.systemInfo) return;

        let html = '';

        // Server Status card
        html += `<div class="sys-card">
            <h3>🦆 PlatypusTools Server</h3>
            <div class="sys-row"><span class="label">Status</span><span class="value" style="color:var(--success)">● ${health.status}</span></div>
            <div class="sys-row"><span class="label">Version</span><span class="value">${health.version || 'N/A'}</span></div>
            <div class="sys-row"><span class="label">Uptime</span><span class="value">${health.uptime?.display || 'N/A'}</span></div>
            <div class="sys-row"><span class="label">Port</span><span class="value">${health.server?.port || 'N/A'}</span></div>
        </div>`;

        // Now Playing card
        if (health.audio) {
            html += `<div class="sys-card">
                <h3>🎵 Audio Status</h3>
                <div class="sys-row"><span class="label">Playing</span><span class="value">${health.audio.playing ? '▶ Yes' : '⏸ No'}</span></div>
                ${health.audio.title ? `<div class="sys-row"><span class="label">Track</span><span class="value">${this.escapeHtml(health.audio.title)}</span></div>` : ''}
                ${health.audio.artist ? `<div class="sys-row"><span class="label">Artist</span><span class="value">${this.escapeHtml(health.audio.artist)}</span></div>` : ''}
            </div>`;
        }

        // Memory card
        if (health.memory) {
            const memUsed = health.memory.workingSetMB || 0;
            const memPeak = health.memory.peakWorkingSetMB || 0;
            html += `<div class="sys-card">
                <h3>💾 Memory</h3>
                <div class="sys-row"><span class="label">Working Set</span><span class="value">${memUsed} MB</span></div>
                <div class="sys-row"><span class="label">Peak</span><span class="value">${memPeak} MB</span></div>
                <div class="sys-row"><span class="label">GC Memory</span><span class="value">${health.memory.gcTotalMemoryMB || 0} MB</span></div>
            </div>`;
        }

        // System card
        if (health.system) {
            html += `<div class="sys-card">
                <h3>🖥️ System</h3>
                <div class="sys-row"><span class="label">OS</span><span class="value">${health.system.os || 'N/A'}</span></div>
                <div class="sys-row"><span class="label">CPU Cores</span><span class="value">${health.system.processors || 'N/A'}</span></div>
                <div class="sys-row"><span class="label">.NET</span><span class="value">${health.system.dotnetVersion || 'N/A'}</span></div>
                <div class="sys-row"><span class="label">Architecture</span><span class="value">${health.system.is64Bit ? '64-bit' : '32-bit'}</span></div>
            </div>`;
        }

        // Drives card (from detailed endpoint)
        if (detailed?.drives?.length) {
            html += `<div class="sys-card"><h3>💿 Drives</h3>`;
            for (const drive of detailed.drives) {
                const usedGB = (drive.totalGB || 0) - (drive.freeGB || 0);
                const pct = drive.totalGB > 0 ? Math.round((usedGB / drive.totalGB) * 100) : 0;
                const meterClass = pct > 90 ? 'danger' : pct > 75 ? 'warn' : 'ok';
                html += `<div class="sys-row"><span class="label">${drive.name} (${drive.format})</span><span class="value">${drive.freeGB} / ${drive.totalGB} GB free</span></div>
                    <div class="sys-meter"><div class="sys-meter-fill ${meterClass}" style="width:${pct}%"></div></div>`;
            }
            html += `</div>`;
        }

        // Process details (from detailed endpoint)
        if (detailed?.process) {
            html += `<div class="sys-card">
                <h3>⚙️ Process</h3>
                <div class="sys-row"><span class="label">PID</span><span class="value">${detailed.process.id}</span></div>
                <div class="sys-row"><span class="label">Threads</span><span class="value">${detailed.process.threads}</span></div>
                <div class="sys-row"><span class="label">Handles</span><span class="value">${detailed.process.handles}</span></div>
                <div class="sys-row"><span class="label">CPU Time</span><span class="value">${detailed.process.totalProcessorTimeSec}s</span></div>
            </div>`;
        }

        // Tailscale status
        if (health.tailscale) {
            const tsColor = health.tailscale.connected ? 'var(--success)' : 'var(--text-secondary)';
            html += `<div class="sys-card">
                <h3>🔒 Tailscale</h3>
                <div class="sys-row"><span class="label">Installed</span><span class="value">${health.tailscale.installed ? 'Yes' : 'No'}</span></div>
                <div class="sys-row"><span class="label">Connected</span><span class="value" style="color:${tsColor}">${health.tailscale.connected ? '● Connected' : '○ Not Connected'}</span></div>
                ${health.tailscale.ip ? `<div class="sys-row"><span class="label">IP</span><span class="value">${health.tailscale.ip}</span></div>` : ''}
                ${health.tailscale.remoteUrl ? `<div class="sys-row"><span class="label">URL</span><span class="value" style="font-size:0.8rem;word-break:break-all">${health.tailscale.remoteUrl}</span></div>` : ''}
            </div>`;
        }

        // Refresh button
        html += `<div style="text-align:center;padding:16px 0;">
            <button id="refreshSystemBtn" style="padding:10px 24px;background:var(--accent);color:white;border:none;border-radius:8px;cursor:pointer;font-size:0.9rem;">🔄 Refresh</button>
        </div>`;

        this.elements.systemInfo.innerHTML = html;

        // Bind refresh
        document.getElementById('refreshSystemBtn')?.addEventListener('click', () => {
            this.systemInfoLoaded = false;
            this.loadSystemInfo();
        });
    }

    // ===== VIDEO LIBRARY METHODS =====

    async loadVideoLibrary() {
        try {
            this.elements.videoGrid.innerHTML = '<div class="no-content"><div class="spinner"></div><p>Loading video library...</p></div>';

            const [libraryRes, foldersRes] = await Promise.all([
                fetch('/api/video/library'),
                fetch('/api/video/folders')
            ]);

            if (libraryRes.ok) {
                this.videoLibrary = await libraryRes.json();
            }

            let folders = [];
            if (foldersRes.ok) {
                folders = await foldersRes.json();
            }

            this.videoLibraryLoaded = true;
            this.elements.videoFolderCount.textContent = folders.length;
            this.elements.videoFileCount.textContent = this.videoLibrary.length;

            // Render folder list
            if (this.elements.videoFolderList) {
                if (folders.length === 0) {
                    this.elements.videoFolderList.innerHTML = '<div class="folder-item" style="color: var(--text-secondary); font-style: italic;">No folders configured — add folders in Media Library tab</div>';
                } else {
                    this.elements.videoFolderList.innerHTML = folders.map(f =>
                        `<div class="folder-item"><span>📂</span> ${this.escapeHtml(f)}</div>`
                    ).join('');
                }
            }

            this.renderVideoGrid(this.videoLibrary);
        } catch (err) {
            console.error('Failed to load video library:', err);
            this.elements.videoGrid.innerHTML = '<div class="no-content"><p>❌ Failed to load video library</p></div>';
        }
    }

    async searchVideoLibrary() {
        const query = this.elements.videoSearch?.value?.trim();
        if (!query) {
            this.renderVideoGrid(this.videoLibrary);
            return;
        }

        try {
            const res = await fetch(`/api/video/search?q=${encodeURIComponent(query)}`);
            if (res.ok) {
                const results = await res.json();
                this.renderVideoGrid(results);
            }
        } catch (err) {
            console.error('Video search failed:', err);
        }
    }

    async rescanVideoLibrary() {
        try {
            this.elements.videoRescanBtn.textContent = '⏳ Scanning...';
            this.elements.videoRescanBtn.disabled = true;

            await fetch('/api/video/rescan', { method: 'POST' });

            // Reload library after rescan
            this.videoLibraryLoaded = false;
            await this.loadVideoLibrary();
        } catch (err) {
            console.error('Video rescan failed:', err);
        } finally {
            this.elements.videoRescanBtn.textContent = '🔄 Rescan';
            this.elements.videoRescanBtn.disabled = false;
        }
    }

    renderVideoGrid(videos) {
        if (!videos || videos.length === 0) {
            this.elements.videoGrid.innerHTML = `<div class="no-content">
                <p>🎬 No videos found</p>
                <p style="font-size: 0.85rem; margin-top: 8px;">Add folders in the Media Library tab, then click Rescan</p>
            </div>`;
            return;
        }

        let html = '';
        for (const video of videos) {
            const duration = this.formatVideoTime(video.durationSeconds);
            const size = video.fileSizeMB || '';
            const resolution = video.resolution || '';
            const thumbSrc = video.thumbnailBase64
                ? `data:image/jpeg;base64,${video.thumbnailBase64}`
                : null;

            html += `<div class="video-card" data-path="${this.escapeHtml(video.filePath)}" onclick="platypusRemote.playVideo('${this.escapeJs(video.filePath)}', '${this.escapeJs(video.fileName)}')">
                <div class="video-thumb">
                    ${thumbSrc
                        ? `<img src="${thumbSrc}" alt="" loading="lazy">`
                        : '<span style="font-size:2.5rem;opacity:0.4">🎬</span>'
                    }
                    <div class="play-overlay">▶️</div>
                    ${duration ? `<span class="video-duration-badge">${duration}</span>` : ''}
                </div>
                <div class="video-meta">
                    <div class="video-title" title="${this.escapeHtml(video.fileName)}">${this.escapeHtml(video.title || video.fileName)}</div>
                    <div class="video-info">
                        ${resolution ? `<span>${resolution}</span>` : ''}
                        ${size ? `<span>${size}</span>` : ''}
                    </div>
                </div>
            </div>`;
        }
        this.elements.videoGrid.innerHTML = html;

        // Lazy-load thumbnails for items that don't have them
        this.lazyLoadVideoThumbnails(videos);
    }

    async lazyLoadVideoThumbnails(videos) {
        for (const video of videos) {
            if (video.thumbnailBase64) continue; // Already has thumbnail

            try {
                const res = await fetch(`/api/video/thumbnail?path=${encodeURIComponent(video.filePath)}`);
                if (res.ok) {
                    const data = await res.json();
                    if (data.thumbnail) {
                        video.thumbnailBase64 = data.thumbnail;
                        // Update the card's thumbnail
                        const card = this.elements.videoGrid.querySelector(`[data-path="${CSS.escape(video.filePath)}"]`);
                        if (card) {
                            const thumbDiv = card.querySelector('.video-thumb');
                            if (thumbDiv) {
                                const existingImg = thumbDiv.querySelector('img');
                                if (existingImg) {
                                    existingImg.src = `data:image/jpeg;base64,${data.thumbnail}`;
                                } else {
                                    const span = thumbDiv.querySelector('span:not(.video-duration-badge):not(.play-overlay)');
                                    if (span) {
                                        const img = document.createElement('img');
                                        img.src = `data:image/jpeg;base64,${data.thumbnail}`;
                                        img.alt = '';
                                        img.loading = 'lazy';
                                        span.replaceWith(img);
                                    }
                                }
                            }
                        }
                    }
                }
            } catch { /* ignore thumbnail errors */ }
        }
    }

    playVideo(filePath, title, seriesInfo) {
        if (!filePath) return;
        this.currentVideoFilePath = filePath;

        const streamUrl = `/api/stream?path=${encodeURIComponent(filePath)}`;
        const video = this.elements.videoPlayer;
        const modal = this.elements.videoPlayerModal;

        this.elements.videoPlayerTitle.textContent = title || 'Video';
        video.src = streamUrl;
        modal.classList.add('active');

        // Load resume position
        this.loadResumePosition(filePath).then(pos => {
            if (pos > 0) {
                video.currentTime = pos;
            }
            video.play().catch(() => {});
        });

        // Load subtitle tracks
        this.loadSubtitleTracks(filePath);

        // Track playback history periodically
        this.startHistoryTracking(filePath, title, seriesInfo);

        // Handle Escape key to close
        this._videoEscHandler = (e) => {
            if (e.key === 'Escape') this.closeVideoPlayer();
        };
        document.addEventListener('keydown', this._videoEscHandler);
    }

    closeVideoPlayer() {
        const video = this.elements.videoPlayer;
        const modal = this.elements.videoPlayerModal;

        // Save final position before closing
        if (this.currentVideoFilePath && video.currentTime > 0) {
            this.updatePlaybackPosition(this.currentVideoFilePath, video.currentTime);
        }

        this.stopHistoryTracking();
        video.pause();
        video.src = '';

        // Remove subtitle tracks
        video.querySelectorAll('track').forEach(t => t.remove());
        if (this.elements.subtitleBar) this.elements.subtitleBar.style.display = 'none';

        modal.classList.remove('active');
        this.currentVideoFilePath = null;

        if (this._videoEscHandler) {
            document.removeEventListener('keydown', this._videoEscHandler);
            this._videoEscHandler = null;
        }
    }

    formatVideoTime(seconds) {
        if (!seconds || seconds <= 0) return '';
        const h = Math.floor(seconds / 3600);
        const m = Math.floor((seconds % 3600) / 60);
        const s = Math.floor(seconds % 60);
        if (h > 0) return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
        return `${m}:${s.toString().padStart(2, '0')}`;
    }

    escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    escapeJs(str) {
        if (!str) return '';
        return str.replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/"/g, '\\"');
    }

    // ── Vault Methods ──

    async loadVaultStatus() {
        try {
            const res = await fetch('/api/vault/status');
            const status = await res.json();
            this.vaultUnlocked = status.isUnlocked;
            if (status.mfaPending) {
                // Password verified but MFA pending — show MFA form
                document.getElementById('vaultLocked').style.display = 'flex';
                document.getElementById('vaultUnlocked').style.display = 'none';
                document.getElementById('vaultNoVault').style.display = 'none';
                document.getElementById('vaultUnlockForm').style.display = 'none';
                document.getElementById('vaultMfaForm').style.display = 'flex';
            } else if (status.isUnlocked) {
                document.getElementById('vaultLocked').style.display = 'none';
                document.getElementById('vaultUnlocked').style.display = 'flex';
                document.getElementById('vaultMfaForm').style.display = 'none';
                await this.loadVaultItems();
                await this.loadVaultFolders();
            } else {
                document.getElementById('vaultLocked').style.display = 'flex';
                document.getElementById('vaultUnlocked').style.display = 'none';
                document.getElementById('vaultMfaForm').style.display = 'none';
                document.getElementById('vaultNoVault').style.display = status.vaultExists ? 'none' : 'block';
                document.getElementById('vaultUnlockForm').style.display = status.vaultExists ? 'flex' : 'none';
            }
        } catch { }
    }

    async unlockVault(e) {
        if (e) e.preventDefault();
        const pw = document.getElementById('vaultMasterPw').value;
        if (!pw) return;
        const errEl = document.getElementById('vaultError');
        errEl.style.display = 'none';
        try {
            const res = await fetch('/api/vault/unlock', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ masterPassword: pw })
            });
            if (res.ok) {
                const status = await res.json();
                document.getElementById('vaultMasterPw').value = '';
                if (status.mfaPending) {
                    // Password correct, MFA required — show MFA form
                    document.getElementById('vaultUnlockForm').style.display = 'none';
                    document.getElementById('vaultMfaForm').style.display = 'flex';
                    document.getElementById('vaultMfaCode').value = '';
                    document.getElementById('vaultMfaCode').focus();
                } else {
                    this.vaultUnlocked = true;
                    await this.loadVaultStatus();
                }
            } else {
                const err = await res.json();
                errEl.textContent = err.error || 'Unlock failed';
                errEl.style.display = 'block';
            }
        } catch (ex) {
            errEl.textContent = 'Connection error';
            errEl.style.display = 'block';
        }
    }

    async verifyMfa(e) {
        if (e) e.preventDefault();
        const code = document.getElementById('vaultMfaCode').value.trim();
        if (!code) return;
        const errEl = document.getElementById('vaultMfaError');
        errEl.style.display = 'none';
        try {
            const res = await fetch('/api/vault/mfa/verify', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ code })
            });
            if (res.ok) {
                this.vaultUnlocked = true;
                document.getElementById('vaultMfaCode').value = '';
                document.getElementById('vaultMfaForm').style.display = 'none';
                await this.loadVaultStatus();
            } else {
                const err = await res.json();
                errEl.textContent = err.error || 'Invalid code';
                errEl.style.display = 'block';
                document.getElementById('vaultMfaCode').value = '';
                document.getElementById('vaultMfaCode').focus();
            }
        } catch {
            errEl.textContent = 'Connection error';
            errEl.style.display = 'block';
        }
    }

    async cancelMfa() {
        try {
            await fetch('/api/vault/mfa/cancel', { method: 'POST' });
        } catch { }
        document.getElementById('vaultMfaForm').style.display = 'none';
        document.getElementById('vaultMfaCode').value = '';
        document.getElementById('vaultMfaError').style.display = 'none';
        document.getElementById('vaultUnlockForm').style.display = 'flex';
    }

    async lockVault() {
        await fetch('/api/vault/lock', { method: 'POST' });
        this.vaultUnlocked = false;
        this.vaultItems = [];
        this.vaultFolders = [];
        if (this.vaultAuthRefreshInterval) {
            clearInterval(this.vaultAuthRefreshInterval);
            this.vaultAuthRefreshInterval = null;
        }
        this.stopQrScanner();
        this.loadVaultStatus();
    }

    async loadVaultItems() {
        try {
            const search = document.getElementById('vaultSearch')?.value || '';
            const type = document.getElementById('vaultTypeFilter')?.value || '';
            const folderId = document.getElementById('vaultFolderFilter')?.value || '';
            const params = new URLSearchParams();
            if (search) params.set('q', search);
            if (type) params.set('type', type);
            if (folderId) params.set('folderId', folderId);
            const res = await fetch(`/api/vault/items?${params}`);
            if (res.ok) {
                this.vaultItems = await res.json();
                this.renderVaultItems();
            }
        } catch { }
    }

    async loadVaultFolders() {
        try {
            const res = await fetch('/api/vault/folders');
            if (res.ok) {
                this.vaultFolders = await res.json();
                const sel = document.getElementById('vaultFolderFilter');
                // Preserve current selection
                const cur = sel.value;
                // Remove old folder options (keep first 2: All, No Folder)
                while (sel.options.length > 2) sel.remove(2);
                this.vaultFolders.forEach(f => {
                    const opt = document.createElement('option');
                    opt.value = f.id;
                    opt.textContent = `${f.name} (${f.itemCount})`;
                    sel.appendChild(opt);
                });
                sel.value = cur;
            }
        } catch { }
    }

    filterVaultItems() {
        this.loadVaultItems();
    }

    renderVaultItems() {
        const list = document.getElementById('vaultItemsList');
        if (!list) return;
        if (this.vaultItems.length === 0) {
            list.innerHTML = '<div class="no-content"><p>No items found</p></div>';
            return;
        }
        const typeIcons = { 1: '🔑', 2: '📝', 3: '💳', 4: '👤' };
        list.innerHTML = this.vaultItems.map(item => {
            const icon = typeIcons[item.type] || '🔑';
            const fav = item.favorite ? '⭐ ' : '';
            let sub = '';
            if (item.type === 1) sub = item.username || (item.uris && item.uris[0]) || '';
            else if (item.type === 3) sub = item.cardBrand ? `${item.cardBrand} •••• ${item.cardLast4 || ''}` : '';
            else if (item.type === 4) sub = item.identityName || item.identityEmail || '';
            return `<div class="vault-item" onclick="window.platypusRemote.showItemDetail('${item.id}')">
                <span class="vi-icon">${icon}</span>
                <div class="vi-info">
                    <div class="vi-name">${fav}${this.escapeHtml(item.name)}</div>
                    <div class="vi-sub">${this.escapeHtml(sub)}</div>
                </div>
                <div class="vi-actions">
                    ${item.type === 1 && item.username ? `<button onclick="event.stopPropagation(); window.platypusRemote.copyText('${this.escapeJs(item.username)}', 'Username')" title="Copy username">👤</button>` : ''}
                    ${item.hasTotp ? `<button onclick="event.stopPropagation(); window.platypusRemote.copyTotp('${item.id}')" title="Copy TOTP">🔢</button>` : ''}
                </div>
            </div>`;
        }).join('');
    }

    async showItemDetail(id) {
        try {
            const res = await fetch(`/api/vault/items/${id}`);
            if (!res.ok) return;
            const item = await res.json();
            const detail = document.getElementById('vaultItemDetail');
            const typeNames = { 1: 'Login', 2: 'Secure Note', 3: 'Card', 4: 'Identity' };
            let html = `<div style="display:flex; align-items:center; justify-content:space-between; margin-bottom:16px;">
                <h3 style="margin:0;">${this.escapeHtml(item.name)}</h3>
                <button onclick="document.getElementById('vaultItemDetail').style.display='none'" style="padding:8px; border:none; background:transparent; color:white; font-size:1.2rem; cursor:pointer;">✕</button>
            </div>
            <div style="font-size:0.8rem; color:var(--text-secondary); margin-bottom:16px;">${typeNames[item.type] || 'Item'}${item.favorite ? ' ⭐' : ''}</div>`;

            if (item.type === 1) {
                // Login
                if (item.username) html += this.vaultDetailField('Username', item.username);
                if (item.password) html += this.vaultDetailField('Password', item.password, true);
                if (item.uris?.length) html += this.vaultDetailField('URL', item.uris.join(', '));
                if (item.hasTotp) html += `<div class="vault-field"><label>TOTP Code</label><div style="display:flex; gap:8px; align-items:center;">
                    <span id="detailTotp" style="font-family:monospace; font-size:1.4rem; letter-spacing:3px; color:var(--accent);">------</span>
                    <button onclick="window.platypusRemote.copyTotp('${item.id}')" style="padding:4px 8px; border:none; border-radius:4px; background:var(--bg-surface); color:white; cursor:pointer;">📋</button>
                </div></div>`;
                this.refreshDetailTotp(item.id);
            } else if (item.type === 3) {
                // Card
                if (item.cardholderName) html += this.vaultDetailField('Cardholder', item.cardholderName);
                if (item.cardNumber) html += this.vaultDetailField('Number', item.cardNumber, true);
                if (item.cardExpMonth || item.cardExpYear) html += this.vaultDetailField('Expires', `${item.cardExpMonth || '??'}/${item.cardExpYear || '??'}`);
                if (item.cardCode) html += this.vaultDetailField('CVV', item.cardCode, true);
                if (item.cardBrand) html += this.vaultDetailField('Brand', item.cardBrand);
            } else if (item.type === 4) {
                // Identity
                if (item.identityName) html += this.vaultDetailField('Name', item.identityName);
                if (item.identityEmail) html += this.vaultDetailField('Email', item.identityEmail);
            }

            if (item.notes) html += this.vaultDetailField('Notes', item.notes);

            detail.innerHTML = html;
            detail.style.display = 'block';
        } catch { }
    }

    vaultDetailField(label, value, secret = false) {
        const display = secret ? '••••••••' : this.escapeHtml(value);
        const toggleId = `vf_${Math.random().toString(36).substr(2, 6)}`;
        return `<div class="vault-field">
            <label>${label}</label>
            <div style="display:flex; align-items:center; gap:8px; background:var(--bg-secondary); padding:8px 12px; border-radius:6px;">
                <span id="${toggleId}" style="flex:1; font-family:${secret ? 'monospace' : 'inherit'}; word-break:break-all;">${display}</span>
                ${secret ? `<button onclick="const el=document.getElementById('${toggleId}'); el.textContent = el.textContent === '••••••••' ? '${this.escapeJs(value)}' : '••••••••';" style="padding:4px 6px; border:none; border-radius:4px; background:transparent; color:var(--text-secondary); cursor:pointer;">👁</button>` : ''}
                <button onclick="window.platypusRemote.copyText('${this.escapeJs(value)}', '${label}')" style="padding:4px 6px; border:none; border-radius:4px; background:transparent; color:var(--text-secondary); cursor:pointer;">📋</button>
            </div>
        </div>`;
    }

    async refreshDetailTotp(itemId) {
        try {
            const res = await fetch(`/api/vault/totp/${itemId}`);
            if (res.ok) {
                const data = await res.json();
                const el = document.getElementById('detailTotp');
                if (el) el.textContent = data.code;
            }
        } catch { }
    }

    async copyTotp(itemId) {
        try {
            const res = await fetch(`/api/vault/totp/${itemId}`);
            if (res.ok) {
                const data = await res.json();
                await navigator.clipboard.writeText(data.code);
                this.showToast('TOTP code copied');
            }
        } catch { }
    }

    async copyText(text, label) {
        try {
            await navigator.clipboard.writeText(text);
            this.showToast(`${label || 'Text'} copied`);
        } catch { }
    }

    showToast(msg) {
        // Simple toast notification
        let toast = document.getElementById('vaultToast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'vaultToast';
            toast.style.cssText = 'position:fixed; bottom:80px; left:50%; transform:translateX(-50%); background:#333; color:white; padding:10px 20px; border-radius:8px; z-index:1000; font-size:0.85rem; opacity:0; transition:opacity 0.3s;';
            document.body.appendChild(toast);
        }
        toast.textContent = msg;
        toast.style.opacity = '1';
        setTimeout(() => toast.style.opacity = '0', 2000);
    }

    // ── Add Item Form ──

    showAddItemForm() {
        const detail = document.getElementById('vaultItemDetail');
        detail.innerHTML = `<div style="display:flex; align-items:center; justify-content:space-between; margin-bottom:16px;">
            <h3 style="margin:0;">Add Item</h3>
            <button onclick="document.getElementById('vaultItemDetail').style.display='none'" style="padding:8px; border:none; background:transparent; color:white; font-size:1.2rem; cursor:pointer;">✕</button>
        </div>
        <div class="vault-field">
            <label>Type</label>
            <select id="addItemType" onchange="window.platypusRemote.updateAddItemFields()">
                <option value="1">Login</option>
                <option value="2">Secure Note</option>
                <option value="3">Card</option>
                <option value="4">Identity</option>
            </select>
        </div>
        <div class="vault-field">
            <label>Name</label>
            <input type="text" id="addItemName" placeholder="Item name">
        </div>
        <div id="addItemTypeFields"></div>
        <div class="vault-field">
            <label>Folder</label>
            <select id="addItemFolder">
                <option value="">No Folder</option>
                ${this.vaultFolders.map(f => `<option value="${f.id}">${this.escapeHtml(f.name)}</option>`).join('')}
            </select>
        </div>
        <div class="vault-field">
            <label>Notes</label>
            <textarea id="addItemNotes" placeholder="Optional notes"></textarea>
        </div>
        <button onclick="window.platypusRemote.saveNewItem()" style="width:100%; padding:12px; border-radius:8px; border:none; background:var(--accent); color:white; cursor:pointer; font-weight:600; font-size:1rem;">Save</button>`;
        this.updateAddItemFields();
        detail.style.display = 'block';
    }

    updateAddItemFields() {
        const type = document.getElementById('addItemType')?.value;
        const container = document.getElementById('addItemTypeFields');
        if (!container) return;
        if (type === '1') {
            container.innerHTML = `<div class="vault-field"><label>Username</label><input type="text" id="addItemUsername" placeholder="Username or email"></div>
                <div class="vault-field"><label>Password</label><div style="display:flex; gap:6px;"><input type="password" id="addItemPassword" placeholder="Password" style="flex:1;">
                <button onclick="window.platypusRemote.fillGeneratedPw()" style="padding:8px; border:none; border-radius:6px; background:var(--bg-surface); color:white; cursor:pointer;" title="Generate">🎲</button></div></div>
                <div class="vault-field"><label>URL</label><input type="url" id="addItemUri" placeholder="https://example.com"></div>`;
        } else if (type === '3') {
            container.innerHTML = `<div class="vault-field"><label>Cardholder Name</label><input type="text" id="addItemCardName"></div>
                <div class="vault-field"><label>Card Number</label><input type="text" id="addItemCardNumber"></div>
                <div style="display:flex; gap:8px;"><div class="vault-field" style="flex:1;"><label>Exp Month</label><input type="text" id="addItemCardExpM" placeholder="MM"></div>
                <div class="vault-field" style="flex:1;"><label>Exp Year</label><input type="text" id="addItemCardExpY" placeholder="YYYY"></div>
                <div class="vault-field" style="flex:1;"><label>CVV</label><input type="password" id="addItemCardCvv"></div></div>`;
        } else {
            container.innerHTML = '';
        }
    }

    async fillGeneratedPw() {
        try {
            const res = await fetch('/api/vault/generate?length=20');
            if (res.ok) {
                const data = await res.json();
                const el = document.getElementById('addItemPassword');
                if (el) { el.type = 'text'; el.value = data.password; }
            }
        } catch { }
    }

    async saveNewItem() {
        const type = parseInt(document.getElementById('addItemType')?.value || '1');
        const name = document.getElementById('addItemName')?.value?.trim();
        if (!name) { this.showToast('Name is required'); return; }

        const item = { type, name, folderId: document.getElementById('addItemFolder')?.value || null, notes: document.getElementById('addItemNotes')?.value || null };
        if (type === 1) {
            item.username = document.getElementById('addItemUsername')?.value || null;
            item.password = document.getElementById('addItemPassword')?.value || null;
            const uri = document.getElementById('addItemUri')?.value;
            if (uri) item.uris = [uri];
        } else if (type === 3) {
            item.cardholderName = document.getElementById('addItemCardName')?.value || null;
            item.cardNumber = document.getElementById('addItemCardNumber')?.value || null;
            item.cardExpMonth = document.getElementById('addItemCardExpM')?.value || null;
            item.cardExpYear = document.getElementById('addItemCardExpY')?.value || null;
            item.cardCode = document.getElementById('addItemCardCvv')?.value || null;
        }

        try {
            const res = await fetch('/api/vault/items', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(item)
            });
            if (res.ok) {
                document.getElementById('vaultItemDetail').style.display = 'none';
                this.showToast('Item saved');
                await this.loadVaultItems();
            }
        } catch { this.showToast('Save failed'); }
    }

    // ── Authenticator ──

    switchVaultSubtab(subtab) {
        document.querySelectorAll('.vault-subtab').forEach(b => b.classList.toggle('active', b.dataset.subtab === subtab));
        document.getElementById('vaultItemsPanel').style.display = subtab === 'items' ? 'block' : 'none';
        document.getElementById('vaultAuthPanel').style.display = subtab === 'authenticator' ? 'block' : 'none';
        document.getElementById('vaultGenPanel').style.display = subtab === 'generator' ? 'block' : 'none';

        if (subtab === 'authenticator') this.loadAuthenticator();
        if (subtab === 'generator') this.generatePassword();
    }

    async loadAuthenticator() {
        try {
            const res = await fetch('/api/vault/authenticator');
            if (!res.ok) return;
            const entries = await res.json();
            this.renderAuthenticator(entries);
            // Auto-refresh every 1s
            if (this.vaultAuthRefreshInterval) clearInterval(this.vaultAuthRefreshInterval);
            this.vaultAuthRefreshInterval = setInterval(() => this.loadAuthenticator(), 1000);
        } catch { }
    }

    renderAuthenticator(entries) {
        const list = document.getElementById('vaultAuthList');
        if (!list) return;
        if (entries.length === 0) {
            list.innerHTML = '<div class="no-content"><p>No authenticator entries</p><p style="font-size:0.85rem; color:var(--text-secondary);">Tap + Add to scan a QR code or enter a secret</p></div>';
            return;
        }
        list.innerHTML = entries.map(e => {
            const pct = (e.remainingSeconds / e.period) * 100;
            const color = e.remainingSeconds <= 5 ? '#f44336' : 'var(--accent)';
            return `<div class="auth-entry">
                <div class="ae-code" onclick="window.platypusRemote.copyText('${e.code}', 'TOTP code')" style="color:${color};">${e.code.replace(/(.{3})/g, '$1 ').trim()}</div>
                <div class="ae-info">
                    <div class="ae-issuer">${this.escapeHtml(e.issuer)}</div>
                    <div class="ae-account">${this.escapeHtml(e.accountName)}</div>
                </div>
                <div class="ae-timer" style="border-color:${color};">${e.remainingSeconds}</div>
            </div>`;
        }).join('');
    }

    showAddAuthForm() {
        document.getElementById('addAuthForm').style.display = 'block';
    }

    hideAddAuthForm() {
        document.getElementById('addAuthForm').style.display = 'none';
        document.getElementById('authOtpUri').value = '';
        this.stopQrScanner();
    }

    async addAuthFromUri() {
        const uri = document.getElementById('authOtpUri')?.value?.trim();
        if (!uri) { this.showToast('Enter an otpauth:// URI'); return; }
        try {
            const res = await fetch('/api/vault/authenticator', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ otpAuthUri: uri })
            });
            if (res.ok) {
                this.hideAddAuthForm();
                this.showToast('Authenticator entry added');
                this.loadAuthenticator();
            } else {
                const err = await res.json();
                this.showToast(err.error || 'Invalid URI');
            }
        } catch { this.showToast('Failed to add entry'); }
    }

    // ── QR Scanner ──

    async startQrScanner() {
        const container = document.getElementById('qrScannerContainer');
        const video = document.getElementById('qrVideo');
        const canvas = document.getElementById('qrCanvas');
        if (!container || !video || !canvas) return;

        try {
            this.qrStream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: 'environment' }
            });
            video.srcObject = this.qrStream;
            await video.play();
            container.style.display = 'block';

            const ctx = canvas.getContext('2d', { willReadFrequently: true });
            const scan = async () => {
                if (!this.qrStream) return;
                if (video.readyState === video.HAVE_ENOUGH_DATA) {
                    canvas.width = video.videoWidth;
                    canvas.height = video.videoHeight;
                    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
                    const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);

                    // Try native BarcodeDetector API first (Chrome on Android, Safari)
                    if ('BarcodeDetector' in window) {
                        try {
                            const det = new BarcodeDetector({ formats: ['qr_code'] });
                            const barcodes = await det.detect(canvas);
                            if (barcodes.length > 0) {
                                const val = barcodes[0].rawValue;
                                if (val.startsWith('otpauth://')) {
                                    this.handleQrResult(val);
                                    return;
                                }
                            }
                        } catch { }
                    }
                }
                if (this.qrStream) requestAnimationFrame(scan);
            };
            requestAnimationFrame(scan);
        } catch (err) {
            this.showToast('Camera access denied or unavailable');
        }
    }

    stopQrScanner() {
        if (this.qrStream) {
            this.qrStream.getTracks().forEach(t => t.stop());
            this.qrStream = null;
        }
        const container = document.getElementById('qrScannerContainer');
        if (container) container.style.display = 'none';
    }

    async handleQrResult(uri) {
        this.stopQrScanner();
        try {
            const res = await fetch('/api/vault/authenticator', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ otpAuthUri: uri })
            });
            if (res.ok) {
                this.hideAddAuthForm();
                this.showToast('Authenticator entry added from QR code');
                this.loadAuthenticator();
            } else {
                const err = await res.json();
                this.showToast(err.error || 'Invalid QR code');
            }
        } catch { this.showToast('Failed to add from QR'); }
    }

    // ── Password Generator ──

    async generatePassword() {
        const length = document.getElementById('genLength')?.value || 20;
        const upper = document.getElementById('genUpper')?.checked !== false;
        const lower = document.getElementById('genLower')?.checked !== false;
        const numbers = document.getElementById('genNumbers')?.checked !== false;
        const special = document.getElementById('genSpecial')?.checked !== false;
        try {
            const res = await fetch(`/api/vault/generate?length=${length}&upper=${upper}&lower=${lower}&numbers=${numbers}&special=${special}`);
            if (res.ok) {
                const data = await res.json();
                this.generatedPassword = data.password;
                const display = document.getElementById('generatedPwDisplay');
                if (display) display.textContent = data.password;
            }
        } catch { }
    }

    async copyGeneratedPassword() {
        if (this.generatedPassword) {
            await navigator.clipboard.writeText(this.generatedPassword);
            this.showToast('Password copied');
        }
    }

    escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ── Photos Gallery ──

    async initPhotos() {
        this.photosPage = 0;
        this.photosPageSize = 60;
        this.photosImages = [];
        this.photosLightboxIndex = -1;

        // Setup folder dropdown
        try {
            const resp = await fetch('/api/photos/folders');
            if (resp.ok) {
                const folders = await resp.json();
                const select = document.getElementById('photosFolderSelect');
                // Keep "All Folders" option
                select.innerHTML = '<option value="">All Folders</option>';
                folders.forEach(f => {
                    const opt = document.createElement('option');
                    opt.value = f;
                    // Show last folder name for readability
                    opt.textContent = f.split(/[\\/]/).filter(Boolean).pop() || f;
                    opt.title = f;
                    select.appendChild(opt);
                });
            }
        } catch { }

        // Setup event listeners
        const folderSel = document.getElementById('photosFolderSelect');
        folderSel.addEventListener('change', () => { this.photosPage = 0; this.loadPhotos(); });

        const searchInput = document.getElementById('photosSearch');
        let searchTimeout;
        searchInput.addEventListener('input', () => {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(() => { this.photosPage = 0; this.loadPhotos(); }, 400);
        });

        document.getElementById('photosPrevBtn').addEventListener('click', () => {
            if (this.photosPage > 0) { this.photosPage--; this.loadPhotos(); }
        });
        document.getElementById('photosNextBtn').addEventListener('click', () => {
            this.photosPage++; this.loadPhotos();
        });

        // Lightbox controls
        document.getElementById('lbClose').addEventListener('click', () => this.closeLightbox());
        document.getElementById('lbPrev').addEventListener('click', () => this.lightboxNav(-1));
        document.getElementById('lbNext').addEventListener('click', () => this.lightboxNav(1));
        document.getElementById('photoLightbox').addEventListener('click', (e) => {
            if (e.target.id === 'photoLightbox') this.closeLightbox();
        });

        // Keyboard nav for lightbox
        document.addEventListener('keydown', (e) => {
            if (!document.getElementById('photoLightbox').classList.contains('active')) return;
            if (e.key === 'Escape') this.closeLightbox();
            if (e.key === 'ArrowLeft') this.lightboxNav(-1);
            if (e.key === 'ArrowRight') this.lightboxNav(1);
        });

        this.photosLoaded = true;
        await this.loadPhotos();
    }

    async loadPhotos() {
        const grid = document.getElementById('photosGrid');
        const loading = document.getElementById('photosLoading');
        const empty = document.getElementById('photosEmpty');
        const pager = document.getElementById('photosPager');
        const status = document.getElementById('photosStatus');

        grid.style.display = 'none';
        empty.style.display = 'none';
        pager.style.display = 'none';
        loading.style.display = 'block';
        status.textContent = '';

        try {
            const folder = document.getElementById('photosFolderSelect').value;
            const search = document.getElementById('photosSearch').value;
            const params = new URLSearchParams({
                page: this.photosPage,
                pageSize: this.photosPageSize
            });
            if (folder) params.set('folder', folder);
            if (search) params.set('q', search);

            const resp = await fetch(`/api/photos?${params}`);
            if (!resp.ok) throw new Error('Failed to load photos');
            const data = await resp.json();

            this.photosImages = data.images || [];

            loading.style.display = 'none';

            if (this.photosImages.length === 0) {
                empty.style.display = 'block';
                return;
            }

            grid.innerHTML = '';
            grid.style.display = 'grid';

            this.photosImages.forEach((img, idx) => {
                const card = document.createElement('div');
                card.className = 'photo-card';
                card.innerHTML = `<div class="pc-placeholder">🖼️</div><div class="pc-name">${this.escapeHtml(img.fileName)}</div>`;
                card.addEventListener('click', () => this.openLightbox(idx));

                // Lazy load thumbnail
                const observer = new IntersectionObserver((entries) => {
                    entries.forEach(entry => {
                        if (entry.isIntersecting) {
                            observer.disconnect();
                            const imgEl = document.createElement('img');
                            imgEl.loading = 'lazy';
                            imgEl.src = `/api/photos/thumbnail?path=${encodeURIComponent(img.filePath)}&size=200`;
                            imgEl.alt = img.fileName;
                            imgEl.onload = () => {
                                const placeholder = card.querySelector('.pc-placeholder');
                                if (placeholder) placeholder.remove();
                                card.insertBefore(imgEl, card.firstChild);
                            };
                            imgEl.onerror = () => { /* keep placeholder */ };
                        }
                    });
                }, { rootMargin: '200px' });
                observer.observe(card);

                grid.appendChild(card);
            });

            // Update pager
            const totalPages = data.totalPages || 1;
            status.textContent = `${data.totalCount} images`;

            if (totalPages > 1) {
                pager.style.display = 'flex';
                document.getElementById('photosPageInfo').textContent = `Page ${data.page + 1} of ${totalPages}`;
                document.getElementById('photosPrevBtn').disabled = data.page <= 0;
                document.getElementById('photosNextBtn').disabled = data.page >= totalPages - 1;
            }
        } catch (err) {
            loading.style.display = 'none';
            empty.style.display = 'block';
            console.error('Photos load error:', err);
        }
    }

    openLightbox(index) {
        this.photosLightboxIndex = index;
        const img = this.photosImages[index];
        if (!img) return;

        const lb = document.getElementById('photoLightbox');
        const lbImg = document.getElementById('lbImage');
        const lbInfo = document.getElementById('lbInfo');

        lbImg.src = `/api/photos/full?path=${encodeURIComponent(img.filePath)}`;
        lbInfo.textContent = `${img.fileName}  ·  ${img.fileSize}`;
        lb.classList.add('active');

        // Prevent body scroll
        document.body.style.overflow = 'hidden';
    }

    closeLightbox() {
        document.getElementById('photoLightbox').classList.remove('active');
        document.getElementById('lbImage').src = '';
        document.body.style.overflow = '';
    }

    lightboxNav(delta) {
        const newIdx = this.photosLightboxIndex + delta;
        if (newIdx < 0 || newIdx >= this.photosImages.length) return;
        this.openLightbox(newIdx);
    }

    // ═══════════════════════════════════════════════
    //  CONTINUE WATCHING / PLAYBACK HISTORY
    // ═══════════════════════════════════════════════

    async loadContinueWatching() {
        try {
            const res = await fetch('/api/history/in-progress');
            if (!res.ok) return;
            const items = await res.json();
            if (!items || items.length === 0) {
                if (this.elements.continueWatching) this.elements.continueWatching.style.display = 'none';
                return;
            }
            this.renderContinueWatching(items.slice(0, 20));
        } catch { /* ignore */ }
    }

    renderContinueWatching(items) {
        const container = this.elements.continueWatchingList;
        const section = this.elements.continueWatching;
        if (!container || !section) return;

        section.style.display = 'block';
        container.innerHTML = items.map(item => {
            const progress = item.durationSeconds > 0
                ? Math.min((item.lastPositionSeconds / item.durationSeconds) * 100, 100)
                : 0;
            const remaining = item.durationSeconds - item.lastPositionSeconds;
            const remText = remaining > 0 ? this.formatVideoTime(remaining) + ' left' : '';
            const displayTitle = item.episodeNumber
                ? `S${item.seasonNumber || 0}E${item.episodeNumber} · ${item.title}`
                : item.title || 'Unknown';
            return `<div class="cw-card" onclick="platypusRemote.playVideo('${this.escapeJs(item.filePath)}', '${this.escapeJs(item.title || '')}')">
                <div class="cw-thumb">
                    <span style="font-size:1.5rem;opacity:0.4">${item.mediaType === 'Audio' ? '🎵' : '🎬'}</span>
                    <div class="cw-progress"><div class="cw-progress-fill" style="width:${progress}%"></div></div>
                </div>
                <div class="cw-info">
                    <div class="cw-title">${this.escapeHtml(displayTitle)}</div>
                    <div class="cw-sub">${this.escapeHtml(remText)}</div>
                </div>
            </div>`;
        }).join('');
    }

    async loadResumePosition(filePath) {
        try {
            const res = await fetch(`/api/history/resume?path=${encodeURIComponent(filePath)}`);
            if (!res.ok) return 0;
            const data = await res.json();
            return data.resumePositionSeconds || 0;
        } catch { return 0; }
    }

    startHistoryTracking(filePath, title, seriesInfo) {
        this.stopHistoryTracking();
        const video = this.elements.videoPlayer;
        // Record initial playback
        this.recordPlayback(filePath, title, 'Video', video.duration || 0, video.currentTime || 0, seriesInfo);
        // Update position every 10 seconds
        this.historyUpdateTimer = setInterval(() => {
            if (video && !video.paused && this.currentVideoFilePath) {
                this.updatePlaybackPosition(this.currentVideoFilePath, video.currentTime);
            }
        }, 10000);
        // Record on pause
        video._onPause = () => {
            if (this.currentVideoFilePath) {
                this.updatePlaybackPosition(this.currentVideoFilePath, video.currentTime);
            }
        };
        video._onEnded = () => {
            if (this.currentVideoFilePath) {
                this.recordPlayback(this.currentVideoFilePath, title, 'Video',
                    video.duration || 0, video.duration || 0, seriesInfo);
            }
        };
        video.addEventListener('pause', video._onPause);
        video.addEventListener('ended', video._onEnded);
    }

    stopHistoryTracking() {
        if (this.historyUpdateTimer) {
            clearInterval(this.historyUpdateTimer);
            this.historyUpdateTimer = null;
        }
        const video = this.elements.videoPlayer;
        if (video._onPause) { video.removeEventListener('pause', video._onPause); video._onPause = null; }
        if (video._onEnded) { video.removeEventListener('ended', video._onEnded); video._onEnded = null; }
    }

    async recordPlayback(filePath, title, mediaType, duration, position, seriesInfo) {
        try {
            await fetch('/api/history/record', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    filePath, title, mediaType,
                    durationSeconds: duration, positionSeconds: position,
                    seriesName: seriesInfo?.seriesName,
                    seasonNumber: seriesInfo?.seasonNumber,
                    episodeNumber: seriesInfo?.episodeNumber
                })
            });
        } catch { /* ignore */ }
    }

    async updatePlaybackPosition(filePath, position) {
        try {
            await fetch('/api/history/position', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ filePath, positionSeconds: position })
            });
        } catch { /* ignore */ }
    }

    // ═══════════════════════════════════════════════
    //  SUBTITLE SUPPORT
    // ═══════════════════════════════════════════════

    async loadSubtitleTracks(filePath) {
        const bar = this.elements.subtitleBar;
        const select = this.elements.subtitleSelect;
        if (!bar || !select) return;

        // Reset
        select.innerHTML = '<option value="">Off</option>';
        bar.style.display = 'none';

        try {
            const res = await fetch(`/api/subtitles/tracks?path=${encodeURIComponent(filePath)}`);
            if (!res.ok) return;
            const tracks = await res.json();
            if (!tracks || tracks.length === 0) return;

            bar.style.display = 'flex';
            tracks.forEach(track => {
                const opt = document.createElement('option');
                opt.value = track.index;
                const label = track.language || track.title || `Track ${track.index}`;
                const extra = track.isForced ? ' (Forced)' : track.isDefault ? ' (Default)' : '';
                const src = track.isExternal ? ' [External]' : '';
                opt.textContent = `${label}${extra}${src}`;
                select.appendChild(opt);
            });

            // Auto-select default track
            const defaultTrack = tracks.find(t => t.isDefault);
            if (defaultTrack) {
                select.value = defaultTrack.index;
                this.onSubtitleChange(defaultTrack.index);
            }
        } catch { /* ignore */ }
    }

    async onSubtitleChange(trackIndex) {
        const video = this.elements.videoPlayer;
        if (!video || !this.currentVideoFilePath) return;

        // Remove existing tracks
        video.querySelectorAll('track').forEach(t => t.remove());

        if (!trackIndex && trackIndex !== 0) return;

        try {
            const vttUrl = `/api/subtitles/content?path=${encodeURIComponent(this.currentVideoFilePath)}&track=${trackIndex}`;
            const track = document.createElement('track');
            track.kind = 'subtitles';
            track.label = 'Subtitles';
            track.srclang = 'en';
            track.src = vttUrl;
            track.default = true;
            video.appendChild(track);
            // Activate the track
            if (video.textTracks.length > 0) {
                video.textTracks[0].mode = 'showing';
            }
        } catch { /* ignore */ }
    }

    // ═══════════════════════════════════════════════
    //  TV SERIES / MOVIES BROWSE
    // ═══════════════════════════════════════════════

    async loadOrganizedContent() {
        try {
            const res = await fetch('/api/content/organized');
            if (!res.ok) return;
            this.organizedContent = await res.json();
        } catch { /* ignore */ }
    }

    switchVideoView(view) {
        this.videoViewMode = view;
        this.seriesDrillPath = [];

        // Update tab buttons
        document.querySelectorAll('.video-view-tab').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.view === view);
        });

        const grid = this.elements.videoGrid;
        const series = this.elements.seriesList;
        const folderBar = document.querySelector('.video-folder-bar');

        if (view === 'grid') {
            if (grid) grid.style.display = '';
            if (series) series.style.display = 'none';
            if (folderBar) folderBar.style.display = '';
            this.renderVideoGrid(this.videoLibrary);
        } else if (view === 'series') {
            if (grid) grid.style.display = 'none';
            if (series) series.style.display = '';
            if (folderBar) folderBar.style.display = 'none';
            this.renderSeriesList();
        } else if (view === 'movies') {
            if (grid) grid.style.display = 'none';
            if (series) series.style.display = '';
            if (folderBar) folderBar.style.display = 'none';
            this.renderMoviesList();
        }
    }

    renderSeriesList() {
        const container = this.elements.seriesList;
        if (!container) return;

        if (!this.organizedContent || !this.organizedContent.tvSeries || this.organizedContent.tvSeries.length === 0) {
            container.innerHTML = '<div class="no-content"><p>📺 No TV series found</p><p style="font-size:0.85rem;margin-top:8px;">Videos with S01E01 naming patterns will appear here</p></div>';
            return;
        }

        const series = this.organizedContent.tvSeries;
        container.innerHTML = series.map(s => {
            const seasonCount = s.seasons ? s.seasons.length : 0;
            const episodeCount = s.seasons ? s.seasons.reduce((sum, sn) => sum + (sn.episodes?.length || 0), 0) : 0;
            return `<div class="series-card" onclick="platypusRemote.drillIntoSeries('${this.escapeJs(s.name)}')">
                <div class="series-poster">
                    ${s.posterUrl ? `<img src="${s.posterUrl}" alt="" loading="lazy">` : '📺'}
                </div>
                <div class="series-info">
                    <div class="series-name">${this.escapeHtml(s.name)}</div>
                    <div class="series-meta">${seasonCount} season${seasonCount !== 1 ? 's' : ''} · ${episodeCount} episode${episodeCount !== 1 ? 's' : ''}</div>
                </div>
            </div>`;
        }).join('');
    }

    renderMoviesList() {
        const container = this.elements.seriesList;
        if (!container) return;

        if (!this.organizedContent || !this.organizedContent.movies || this.organizedContent.movies.length === 0) {
            container.innerHTML = '<div class="no-content"><p>🎥 No movies found</p><p style="font-size:0.85rem;margin-top:8px;">Standalone video files will appear here</p></div>';
            return;
        }

        const movies = this.organizedContent.movies;
        container.innerHTML = movies.map(m => {
            return `<div class="series-card" onclick="platypusRemote.playVideo('${this.escapeJs(m.filePath)}', '${this.escapeJs(m.title)}')">
                <div class="series-poster">
                    ${m.posterUrl ? `<img src="${m.posterUrl}" alt="" loading="lazy">` : '🎥'}
                </div>
                <div class="series-info">
                    <div class="series-name">${this.escapeHtml(m.title)}</div>
                    <div class="series-meta">${m.year ? m.year + ' · ' : ''}${m.genre || ''}</div>
                    ${m.rating ? `<div class="series-badge">⭐ ${m.rating.toFixed(1)}</div>` : ''}
                </div>
            </div>`;
        }).join('');
    }

    drillIntoSeries(seriesName) {
        const series = this.organizedContent?.tvSeries?.find(s => s.name === seriesName);
        if (!series) return;
        this.seriesDrillPath = [seriesName];
        this.renderSeasonsList(series);
    }

    renderSeasonsList(series) {
        const container = this.elements.seriesList;
        if (!container) return;

        let html = `<div class="drill-header" onclick="platypusRemote.drillBack()">
            <span class="drill-back">←</span>
            <span class="drill-title">${this.escapeHtml(series.name)}</span>
        </div>`;

        if (!series.seasons || series.seasons.length === 0) {
            html += '<div class="no-content"><p>No seasons found</p></div>';
        } else if (series.seasons.length === 1) {
            // Skip season level if only one season
            this.seriesDrillPath.push(series.seasons[0].seasonNumber);
            this.renderEpisodeList(series, series.seasons[0]);
            return;
        } else {
            html += series.seasons.map(sn => {
                const epCount = sn.episodes?.length || 0;
                return `<div class="season-card" onclick="platypusRemote.drillIntoSeason('${this.escapeJs(series.name)}', ${sn.seasonNumber})">
                    <h4>Season ${sn.seasonNumber}</h4>
                    <div class="ep-count">${epCount} episode${epCount !== 1 ? 's' : ''}</div>
                </div>`;
            }).join('');
        }
        container.innerHTML = html;
    }

    drillIntoSeason(seriesName, seasonNumber) {
        const series = this.organizedContent?.tvSeries?.find(s => s.name === seriesName);
        if (!series) return;
        const season = series.seasons?.find(sn => sn.seasonNumber === seasonNumber);
        if (!season) return;
        this.seriesDrillPath = [seriesName, seasonNumber];
        this.renderEpisodeList(series, season);
    }

    renderEpisodeList(series, season) {
        const container = this.elements.seriesList;
        if (!container) return;

        let html = `<div class="drill-header" onclick="platypusRemote.drillBack()">
            <span class="drill-back">←</span>
            <span class="drill-title">${this.escapeHtml(series.name)} · Season ${season.seasonNumber}</span>
        </div>`;

        if (!season.episodes || season.episodes.length === 0) {
            html += '<div class="no-content"><p>No episodes found</p></div>';
        } else {
            html += season.episodes.map(ep => {
                const title = ep.episodeTitle || `Episode ${ep.episodeNumber}`;
                const watched = ep.isWatched ? ' ep-watched' : '';
                const resumeBadge = ep.resumePositionSeconds > 0 && !ep.isWatched
                    ? `<span class="ep-resume-badge">${this.formatVideoTime(ep.resumePositionSeconds)} in</span>`
                    : '';
                const seriesInfo = JSON.stringify({
                    seriesName: series.name,
                    seasonNumber: season.seasonNumber,
                    episodeNumber: ep.episodeNumber
                }).replace(/'/g, "\\'");
                return `<div class="episode-item${watched}" onclick="platypusRemote.playVideo('${this.escapeJs(ep.filePath)}', '${this.escapeJs(title)}', ${this.escapeHtml(seriesInfo)})">
                    <div class="ep-number">${ep.episodeNumber}</div>
                    <div class="ep-info">
                        <div class="ep-title">${this.escapeHtml(title)}</div>
                        <div class="ep-meta">${ep.isWatched ? '✅ Watched' : ''} ${resumeBadge}</div>
                    </div>
                </div>`;
            }).join('');
        }
        container.innerHTML = html;
    }

    drillBack() {
        if (this.seriesDrillPath.length <= 1) {
            // Back to series list
            this.seriesDrillPath = [];
            this.renderSeriesList();
        } else {
            // Back to seasons for this series
            const seriesName = this.seriesDrillPath[0];
            this.seriesDrillPath = [seriesName];
            const series = this.organizedContent?.tvSeries?.find(s => s.name === seriesName);
            if (series) this.renderSeasonsList(series);
        }
    }

    // ═══════════════════════════════════════════════
    //  PLAYLISTS
    // ═══════════════════════════════════════════════

    async loadPlaylists() {
        try {
            const res = await fetch('/api/playlists');
            if (!res.ok) return;
            this.playlists = await res.json();
            this.renderPlaylistChips();
        } catch { /* ignore */ }
    }

    renderPlaylistChips() {
        const container = this.elements.playlistList;
        if (!container) return;

        let html = `<div class="playlist-chip ${this.activePlaylist === 'all' ? 'active' : ''}" 
            data-playlist="all" onclick="platypusRemote.selectPlaylist('all')">All Music</div>`;

        html += this.playlists.map(pl => {
            const active = this.activePlaylist === pl.id ? 'active' : '';
            const icon = pl.isCollection ? '📂' : '🎵';
            return `<div class="playlist-chip ${active}" data-playlist="${pl.id}" 
                onclick="platypusRemote.selectPlaylist('${this.escapeJs(pl.id)}')">
                ${icon} ${this.escapeHtml(pl.name)} <span class="pl-count">(${pl.itemCount})</span>
            </div>`;
        }).join('');

        container.innerHTML = html;
    }

    selectPlaylist(id) {
        this.activePlaylist = id;
        document.querySelectorAll('.playlist-chip').forEach(c => {
            c.classList.toggle('active', c.dataset.playlist === id);
        });

        if (id === 'all') {
            // Show full library
            this.renderLibrary(this.library);
        } else {
            // Load playlist items and filter library
            this.loadPlaylistItems(id);
        }
    }

    async loadPlaylistItems(playlistId) {
        try {
            const res = await fetch(`/api/playlists/${playlistId}`);
            if (!res.ok) return;
            const playlist = await res.json();
            if (playlist.items) {
                const filePaths = new Set(playlist.items.map(i => i.filePath));
                const filtered = this.library.filter(item => filePaths.has(item.filePath));
                this.renderLibrary(filtered);
            }
        } catch { /* ignore */ }
    }

    async createPlaylistPrompt() {
        const name = prompt('Enter playlist name:');
        if (!name || !name.trim()) return;
        try {
            await fetch('/api/playlists', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name: name.trim(), description: '', isCollection: false })
            });
            await this.loadPlaylists();
        } catch { /* ignore */ }
    }
}

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    window.platypusRemote = new PlatypusRemote();
});

// Register service worker for PWA
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('/sw.js').catch(() => {
        // Service worker registration failed - that's ok for local use
    });
}
