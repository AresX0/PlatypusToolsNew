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
        
        this.init();
    }

    async init() {
        this.bindElements();
        this.bindEvents();
        this.bindStreamAudioEvents();
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
            streamModeBtn: document.getElementById('streamModeBtn')
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
