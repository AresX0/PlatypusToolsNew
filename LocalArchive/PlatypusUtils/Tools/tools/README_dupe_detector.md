# Duplicate Detector (Chromaprint + OpenCV)

Basic open-source duplicate detector for audio and video files. It uses:
- Chromaprint `fpcalc` for audio fingerprints
- OpenCV average-hash over sampled video frames for video perceptual hashes
- SHA-256 fallback when fingerprinting is unavailable

## Requirements
- Python 3.8+
- `opencv-python` and `numpy` (`pip install opencv-python numpy`)
- Chromaprint `fpcalc` on PATH (https://acoustid.org/chromaprint)

## Usage
```bash
# Install deps
pip install opencv-python numpy

# Run (recurses by default)
python tools/dupe_detector.py C:\media --kinds audio,video --sample-frames 12

# No recursion
python tools/dupe_detector.py C:\media --no-recurse

# JSON output
python tools/dupe_detector.py C:\media --json
```

## Notes
- Audio duplicates are grouped by Chromaprint fingerprint when `fpcalc` is available.
- Video duplicates are grouped by a combined perceptual hash derived from sampled frames (default 12 frames across the timeline).
- If fingerprinting fails, SHA-256 is used as a strict fallback (exact file match only).
- You can broaden matching by adding more sampled frames or by post-processing hashes with a Hamming-distance threshold.
