// Platypus Remote - Audio Streaming Player
// Handles HTML5 audio playback for Remote Stream mode

window.PlatypusAudioPlayer = {
    _audio: null,
    _queue: [],
    _currentIndex: -1,
    _dotNetRef: null,

    // Initialize the audio player
    initialize: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
        this._audio = new Audio();
        this._audio.preload = 'auto';

        // Event handlers
        this._audio.addEventListener('timeupdate', () => {
            if (this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnTimeUpdate', 
                    this._audio.currentTime, 
                    this._audio.duration || 0,
                    !this._audio.paused);
            }
        });

        this._audio.addEventListener('ended', () => {
            console.log('Track ended, playing next');
            this.next();
        });

        this._audio.addEventListener('error', (e) => {
            console.error('Audio error:', e);
            if (this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnAudioError', 'Audio playback error');
            }
        });

        this._audio.addEventListener('loadedmetadata', () => {
            if (this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnMetadataLoaded', this._audio.duration);
            }
        });

        this._audio.addEventListener('play', () => {
            if (this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnPlayStateChanged', true);
            }
        });

        this._audio.addEventListener('pause', () => {
            if (this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnPlayStateChanged', false);
            }
        });

        console.log('PlatypusAudioPlayer initialized');
        return true;
    },

    // Play a file by streaming URL
    playUrl: function (url, title, artist) {
        if (!this._audio) return false;
        
        console.log('Playing URL:', url);
        this._audio.src = url;
        this._audio.play().catch(e => console.error('Play failed:', e));
        
        // Update media session if available
        if ('mediaSession' in navigator) {
            navigator.mediaSession.metadata = new MediaMetadata({
                title: title || 'Unknown Track',
                artist: artist || 'Unknown Artist',
                album: 'Platypus Remote'
            });
            
            navigator.mediaSession.setActionHandler('play', () => this.play());
            navigator.mediaSession.setActionHandler('pause', () => this.pause());
            navigator.mediaSession.setActionHandler('previoustrack', () => this.previous());
            navigator.mediaSession.setActionHandler('nexttrack', () => this.next());
        }
        
        return true;
    },

    // Play current track
    play: function () {
        if (!this._audio || !this._audio.src) return false;
        this._audio.play().catch(e => console.error('Play failed:', e));
        return true;
    },

    // Pause playback
    pause: function () {
        if (!this._audio) return false;
        this._audio.pause();
        return true;
    },

    // Stop playback
    stop: function () {
        if (!this._audio) return false;
        this._audio.pause();
        this._audio.currentTime = 0;
        this._audio.src = '';
        return true;
    },

    // Seek to position (0-1)
    seek: function (position) {
        if (!this._audio || !this._audio.duration) return false;
        this._audio.currentTime = this._audio.duration * position;
        return true;
    },

    // Set volume (0-1)
    setVolume: function (volume) {
        if (!this._audio) return false;
        this._audio.volume = Math.max(0, Math.min(1, volume));
        return true;
    },

    // Get current volume
    getVolume: function () {
        return this._audio ? this._audio.volume : 0.5;
    },

    // Queue management
    clearQueue: function () {
        this._queue = [];
        this._currentIndex = -1;
        return true;
    },

    addToQueue: function (item) {
        // item: { path, title, artist, streamUrl }
        this._queue.push(item);
        return this._queue.length;
    },

    setQueue: function (items) {
        this._queue = items || [];
        this._currentIndex = this._queue.length > 0 ? 0 : -1;
        return true;
    },

    getQueue: function () {
        return this._queue;
    },

    getQueueLength: function () {
        return this._queue.length;
    },

    getCurrentIndex: function () {
        return this._currentIndex;
    },

    // Play item at index
    playAtIndex: function (index) {
        if (index < 0 || index >= this._queue.length) return false;
        
        this._currentIndex = index;
        const item = this._queue[index];
        return this.playUrl(item.streamUrl, item.title, item.artist);
    },

    // Play next track
    next: function () {
        if (this._queue.length === 0) return false;
        
        let nextIndex = this._currentIndex + 1;
        if (nextIndex >= this._queue.length) {
            nextIndex = 0; // Loop to start
        }
        
        return this.playAtIndex(nextIndex);
    },

    // Play previous track
    previous: function () {
        if (this._queue.length === 0) return false;
        
        // If more than 3 seconds into track, restart; otherwise go to previous
        if (this._audio && this._audio.currentTime > 3) {
            this._audio.currentTime = 0;
            return true;
        }
        
        let prevIndex = this._currentIndex - 1;
        if (prevIndex < 0) {
            prevIndex = this._queue.length - 1;
        }
        
        return this.playAtIndex(prevIndex);
    },

    // Remove from queue
    removeFromQueue: function (index) {
        if (index < 0 || index >= this._queue.length) return false;
        
        this._queue.splice(index, 1);
        
        if (index < this._currentIndex) {
            this._currentIndex--;
        } else if (index === this._currentIndex) {
            // Currently playing track removed
            if (this._currentIndex >= this._queue.length) {
                this._currentIndex = this._queue.length - 1;
            }
            if (this._currentIndex >= 0) {
                this.playAtIndex(this._currentIndex);
            } else {
                this.stop();
            }
        }
        
        return true;
    },

    // Get current state
    getState: function () {
        if (!this._audio) {
            return {
                isPlaying: false,
                currentTime: 0,
                duration: 0,
                volume: 0.5,
                queueIndex: -1,
                queueLength: 0
            };
        }
        
        return {
            isPlaying: !this._audio.paused,
            currentTime: this._audio.currentTime,
            duration: this._audio.duration || 0,
            volume: this._audio.volume,
            queueIndex: this._currentIndex,
            queueLength: this._queue.length,
            currentTrack: this._currentIndex >= 0 ? this._queue[this._currentIndex] : null
        };
    },

    // Dispose
    dispose: function () {
        if (this._audio) {
            this._audio.pause();
            this._audio.src = '';
            this._audio = null;
        }
        this._queue = [];
        this._currentIndex = -1;
        this._dotNetRef = null;
    }
};
