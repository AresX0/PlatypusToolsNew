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
        
        this.init();
    }

    async init() {
        this.bindElements();
        this.bindEvents();
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
            streamAudio: document.getElementById('streamAudio')
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
            if (!this.nowPlaying) return;
            const rect = this.elements.progressBar.getBoundingClientRect();
            const percent = (e.clientX - rect.left) / rect.width;
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

    // Server message handlers
    handleNowPlaying(data) {
        this.nowPlaying = data;
        this.clearError();
        
        // Update track info
        this.elements.trackTitle.textContent = data.title || 'No Track Playing';
        this.elements.trackArtist.textContent = data.artist || '-';
        this.elements.trackAlbum.textContent = data.album || '-';

        // Update album art
        if (data.albumArtData) {
            this.elements.albumArt.innerHTML = `<img src="data:image/jpeg;base64,${data.albumArtData}" alt="Album Art">`;
        } else {
            this.elements.albumArt.innerHTML = '<span>🎵</span>';
        }

        // Update play/pause button
        this.elements.playPauseBtn.textContent = data.isPlaying ? '⏸️' : '▶️';

        // Update progress
        this.updateProgress(data.positionSeconds, data.durationSeconds);

        // Update volume
        const volumePercent = Math.round(data.volume * 100);
        this.elements.volumeSlider.value = volumePercent;
        this.elements.volumeValue.textContent = `${volumePercent}%`;

        // Update shuffle/repeat
        this.elements.shuffleBtn.classList.toggle('active', data.isShuffle);
        this.elements.repeatBtn.classList.toggle('active', data.repeatMode > 0);
        
        // Update repeat icon based on mode
        const repeatIcons = ['🔁', '🔁', '🔂']; // None, All, One
        this.elements.repeatBtn.textContent = data.repeatMode === 2 ? '🔂' : '🔁';

        // Update streaming if active
        this.updateStreaming();
    }

    handlePosition(positionSeconds) {
        if (this.nowPlaying) {
            this.nowPlaying.positionSeconds = positionSeconds;
            this.updateProgress(positionSeconds, this.nowPlaying.durationSeconds);
        }
    }

    handleQueue(data) {
        this.queue = data || [];
        this.renderQueue();
    }

    updateProgress(position, duration) {
        const percent = duration > 0 ? (position / duration) * 100 : 0;
        this.elements.progressFill.style.width = `${percent}%`;
        this.elements.currentTime.textContent = this.formatTime(position);
        this.elements.totalTime.textContent = this.formatTime(duration);
    }

    renderQueue() {
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

    // Control methods
    async playPause() {
        if (!this.isConnected) return;
        await this.connection.invoke('PlayPause');
    }

    async play() {
        if (!this.isConnected) return;
        await this.connection.invoke('Play');
    }

    async pause() {
        if (!this.isConnected) return;
        await this.connection.invoke('Pause');
    }

    async next() {
        if (!this.isConnected) return;
        await this.connection.invoke('Next');
    }

    async previous() {
        if (!this.isConnected) return;
        await this.connection.invoke('Previous');
    }

    async seek(positionSeconds) {
        if (!this.isConnected) return;
        await this.connection.invoke('Seek', positionSeconds);
    }

    async setVolume(volume) {
        if (!this.isConnected) return;
        await this.connection.invoke('SetVolume', volume);
    }

    async toggleShuffle() {
        if (!this.isConnected) return;
        await this.connection.invoke('ToggleShuffle');
    }

    async toggleRepeat() {
        if (!this.isConnected) return;
        await this.connection.invoke('ToggleRepeat');
    }

    async playQueueItem(index) {
        if (!this.isConnected) return;
        await this.connection.invoke('PlayQueueItem', index);
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
                <button class="action" onclick="event.stopPropagation(); platypusRemote.playLibraryItem('${this.escapeHtml(item.filePath || '')}')">▶️</button>
            </div>
        `).join('');

        // Bind click events
        this.elements.libraryList.querySelectorAll('.library-item').forEach(item => {
            item.addEventListener('click', () => {
                const path = item.dataset.path;
                if (path) this.addToQueue(path);
            });
        });
    }

    async playLibraryItem(filePath) {
        if (!this.isConnected || !filePath) return;
        try {
            await this.connection.invoke('PlayFile', filePath);
            this.switchTab('nowPlayingTab');
        } catch (error) {
            console.error('Failed to play file:', error);
        }
    }

    async addToQueue(filePath) {
        if (!this.isConnected || !filePath) return;
        try {
            await this.connection.invoke('AddToQueue', filePath);
            // Show feedback
            const items = this.elements.libraryList?.querySelectorAll(`[data-path="${filePath}"]`);
            items?.forEach(item => {
                item.style.background = 'var(--success)';
                setTimeout(() => item.style.background = '', 500);
            });
        } catch (error) {
            console.error('Failed to add to queue:', error);
        }
    }

    // Audio Streaming
    toggleStreaming() {
        this.isStreaming = !this.isStreaming;
        this.elements.streamToggle?.classList.toggle('active', this.isStreaming);

        if (this.isStreaming) {
            this.startStreaming();
        } else {
            this.stopStreaming();
        }
    }

    startStreaming() {
        if (!this.nowPlaying?.filePath || !this.elements.streamAudio) return;

        // Create streaming URL
        const streamUrl = `/api/stream?path=${encodeURIComponent(this.nowPlaying.filePath)}`;
        
        this.elements.streamAudio.src = streamUrl;
        this.elements.streamAudio.currentTime = this.nowPlaying.positionSeconds || 0;
        
        // Sync with desktop playback
        if (this.nowPlaying.isPlaying) {
            this.elements.streamAudio.play().catch(e => console.error('Stream play failed:', e));
        }
    }

    stopStreaming() {
        if (this.elements.streamAudio) {
            this.elements.streamAudio.pause();
            this.elements.streamAudio.src = '';
        }
    }

    // Update streaming when track changes
    updateStreaming() {
        if (this.isStreaming && this.nowPlaying) {
            this.startStreaming();
        }
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
