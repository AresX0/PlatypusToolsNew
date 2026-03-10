// QR Scanner interop for Blazor
// Uses html5-qrcode library for cross-browser QR scanning

window.QrScanner = {
    _scanner: null,
    _dotNetRef: null,

    init: async function (elementId, dotNetRef) {
        this._dotNetRef = dotNetRef;

        // Dynamically load html5-qrcode if not already loaded
        if (typeof Html5Qrcode === 'undefined') {
            await this._loadScript('https://unpkg.com/html5-qrcode@2.3.8/html5-qrcode.min.js');
        }

        try {
            this._scanner = new Html5Qrcode(elementId);
            return true;
        } catch (e) {
            console.error('QR Scanner init failed:', e);
            return false;
        }
    },

    start: async function () {
        if (!this._scanner) return false;

        try {
            await this._scanner.start(
                { facingMode: "environment" },
                {
                    fps: 10,
                    qrbox: { width: 250, height: 250 },
                    aspectRatio: 1.0
                },
                (decodedText) => {
                    if (this._dotNetRef) {
                        this._dotNetRef.invokeMethodAsync('OnQrCodeScanned', decodedText);
                    }
                },
                (errorMessage) => {
                    // Scan error - ignored (happens on every frame without a QR code)
                }
            );
            return true;
        } catch (e) {
            console.error('QR Scanner start failed:', e);
            if (this._dotNetRef) {
                this._dotNetRef.invokeMethodAsync('OnQrScanError', e.message || 'Camera access denied');
            }
            return false;
        }
    },

    stop: async function () {
        if (this._scanner) {
            try {
                const state = this._scanner.getState();
                if (state === 2) { // SCANNING state
                    await this._scanner.stop();
                }
            } catch (e) {
                console.warn('QR Scanner stop warning:', e);
            }
        }
    },

    dispose: async function () {
        await this.stop();
        this._scanner = null;
        this._dotNetRef = null;
    },

    _loadScript: function (src) {
        return new Promise((resolve, reject) => {
            // Check if already loaded
            if (document.querySelector(`script[src="${src}"]`)) {
                resolve();
                return;
            }
            const script = document.createElement('script');
            script.src = src;
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }
};

// Clipboard helper for copying vault data
window.VaultClipboard = {
    copyText: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            // Fallback for older browsers
            const textarea = document.createElement('textarea');
            textarea.value = text;
            textarea.style.position = 'fixed';
            textarea.style.opacity = '0';
            document.body.appendChild(textarea);
            textarea.select();
            try {
                document.execCommand('copy');
                return true;
            } catch {
                return false;
            } finally {
                document.body.removeChild(textarea);
            }
        }
    }
};
