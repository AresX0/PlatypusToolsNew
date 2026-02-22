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
        
        this.init();
    }

    async init() {
        this.bindElements();
        this.bindEvents();
        this.bindStreamAudioEvents();
        this.bindKeyboardShortcuts();
        this.bindSwipeGestures();
        this.bindSleepTimer();
        this.setupInstallPrompt();
        await this.connect();
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
            videoPlayerClose: document.getElementById('videoPlayerClose')
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
        if (tabId === 'libraryTab' && this.library.length === 0) {
            this.loadLibrary();
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
        if (tabId === 'videosTab' && !this.videoLibraryLoaded) {
            this.loadVideoLibrary();
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

    playVideo(filePath, title) {
        if (!filePath) return;

        const streamUrl = `/api/stream?path=${encodeURIComponent(filePath)}`;
        const video = this.elements.videoPlayer;
        const modal = this.elements.videoPlayerModal;

        this.elements.videoPlayerTitle.textContent = title || 'Video';
        video.src = streamUrl;
        modal.classList.add('active');
        video.play().catch(() => {}); // Autoplay may be blocked

        // Handle Escape key to close
        this._videoEscHandler = (e) => {
            if (e.key === 'Escape') this.closeVideoPlayer();
        };
        document.addEventListener('keydown', this._videoEscHandler);
    }

    closeVideoPlayer() {
        const video = this.elements.videoPlayer;
        const modal = this.elements.videoPlayerModal;

        video.pause();
        video.src = '';
        modal.classList.remove('active');

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
