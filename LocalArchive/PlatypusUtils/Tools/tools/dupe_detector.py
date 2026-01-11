#!/usr/bin/env python3
"""
Basic open-source duplicate detector for audio and video files using
Chromaprint (fpcalc) for audio fingerprints and OpenCV for video perceptual
hashing (average hash over sampled frames).

Requirements:
- Python 3.8+
- opencv-python (pip install opencv-python)
- fpcalc (Chromaprint CLI) available on PATH
- ffmpeg optional (only for your own workflows, not required here)

This script scans a folder, optionally recursing, computes fingerprints/hashes,
and reports groups of duplicate content.
"""

import argparse
import dataclasses
import json
import os
import shutil
import subprocess
import sys
from collections import defaultdict
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

try:
    import cv2  # type: ignore
    import numpy as np  # type: ignore
except ImportError as exc:  # pragma: no cover - handled at runtime
    cv2 = None
    np = None
    _import_error = exc
else:
    _import_error = None

AUDIO_EXTS = {".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma"}
VIDEO_EXTS = {".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v"}


@dataclasses.dataclass
class FingerprintResult:
    kind: str  # "audio" or "video" or "fallback"
    key: str   # stable key for grouping duplicates
    detail: str  # human-readable detail (hash summary)


def find_files(root: Path, recurse: bool, kinds: Sequence[str], custom_exts: Sequence[str]) -> List[Path]:
    allowed_exts = set()
    if "audio" in kinds:
        allowed_exts.update(AUDIO_EXTS)
    if "video" in kinds:
        allowed_exts.update(VIDEO_EXTS)
    allowed_exts.update(ext.lower() if ext.startswith(".") else f".{ext.lower()}" for ext in custom_exts)

    def _iter_files() -> Iterable[Path]:
        if recurse:
            yield from root.rglob("*")
        else:
            yield from root.iterdir()

    files = [p for p in _iter_files() if p.is_file() and (not allowed_exts or p.suffix.lower() in allowed_exts)]
    return files


def run_fpcalc(path: Path, length: int = 120) -> Optional[str]:
    """Return Chromaprint fingerprint string from fpcalc, or None if unavailable."""
    fpcalc = shutil.which("fpcalc")
    if not fpcalc:
        return None
    try:
        proc = subprocess.run(
            [fpcalc, "-length", str(length), str(path)],
            check=True,
            capture_output=True,
            text=True,
        )
    except subprocess.CalledProcessError:
        return None
    for line in proc.stdout.splitlines():
        if line.startswith("FINGERPRINT="):
            return line.split("=", 1)[1].strip()
    return None


def sha256_file(path: Path) -> str:
    import hashlib

    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def frame_ahash(frame: "np.ndarray", hash_size: int = 8) -> int:
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    resized = cv2.resize(gray, (hash_size + 1, hash_size), interpolation=cv2.INTER_AREA)
    diff = resized[:, 1:] > resized[:, :-1]
    bits = diff.flatten()
    value = 0
    for bit in bits:
        value = (value << 1) | int(bool(bit))
    return value


def combine_hashes_hash_majority(hashes: List[int], hash_bits: int = 64) -> int:
    if not hashes:
        return 0
    counts = [0] * hash_bits
    for h in hashes:
        for i in range(hash_bits):
            if h & (1 << (hash_bits - 1 - i)):
                counts[i] += 1
    threshold = len(hashes) / 2
    combined = 0
    for i, c in enumerate(counts):
        combined = (combined << 1) | int(c >= threshold)
    return combined


def video_perceptual_hash(path: Path, sample_frames: int = 12) -> Optional[int]:
    if cv2 is None or np is None:
        return None
    cap = cv2.VideoCapture(str(path))
    if not cap.isOpened():
        return None
    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
    indices = []
    if frame_count > 0 and sample_frames > 0:
        step = max(1, frame_count // sample_frames)
        indices = [i * step for i in range(sample_frames)]
    hashes: List[int] = []
    for idx in indices:
        cap.set(cv2.CAP_PROP_POS_FRAMES, float(idx))
        ok, frame = cap.read()
        if not ok or frame is None:
            continue
        hashes.append(frame_ahash(frame))
    cap.release()
    if not hashes:
        return None
    return combine_hashes_hash_majority(hashes)


def fingerprint_file(path: Path, kinds: Sequence[str], sample_frames: int = 12) -> FingerprintResult:
    ext = path.suffix.lower()
    is_audio = ext in AUDIO_EXTS and "audio" in kinds
    is_video = ext in VIDEO_EXTS and "video" in kinds

    if is_audio:
        fp = run_fpcalc(path)
        if fp:
            return FingerprintResult("audio", fp, f"fpcalc:{len(fp)}chars")
    if is_video:
        vhash = video_perceptual_hash(path, sample_frames=sample_frames)
        if vhash is not None:
            return FingerprintResult("video", f"vhash:{vhash:016x}", f"vhash:{vhash:016x}")
    # Fallback to sha256
    fallback = sha256_file(path)
    return FingerprintResult("fallback", fallback, f"sha256:{fallback[:12]}...")


def find_duplicates(files: Sequence[Path], kinds: Sequence[str], sample_frames: int = 12) -> Dict[str, List[Tuple[Path, FingerprintResult]]]:
    groups: Dict[str, List[Tuple[Path, FingerprintResult]]] = defaultdict(list)
    for p in files:
        fp = fingerprint_file(p, kinds=kinds, sample_frames=sample_frames)
        groups[fp.key].append((p, fp))
    dupes = {k: v for k, v in groups.items() if len(v) > 1}
    return dupes


def format_report(dupes: Dict[str, List[Tuple[Path, FingerprintResult]]]) -> str:
    lines: List[str] = []
    for key, items in sorted(dupes.items(), key=lambda kv: kv[0]):
        lines.append(f"hash={key} (x{len(items)})")
        for path, fp in sorted(items, key=lambda x: str(x[0])):
            lines.append(f"  - {path} [{fp.kind} {fp.detail}]")
    return "\n".join(lines)


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Basic duplicate detector using Chromaprint and OpenCV perceptual hashing")
    parser.add_argument("root", type=Path, help="Root folder to scan")
    parser.add_argument("--no-recurse", action="store_true", help="Do not scan subfolders (default: recurse)")
    parser.add_argument("--kinds", default="audio,video", help="Comma list: audio,video")
    parser.add_argument("--custom-exts", default="", help="Comma-separated extensions to include additionally")
    parser.add_argument("--sample-frames", type=int, default=12, help="Frames to sample per video for hashing")
    parser.add_argument("--json", action="store_true", help="Emit JSON report")
    args = parser.parse_args(argv)

    if _import_error:
        parser.error(f"opencv-python/numpy not available: {_import_error}")

    kinds = [k.strip().lower() for k in args.kinds.split(',') if k.strip()]
    custom_exts = [e.strip() for e in args.custom_exts.split(',') if e.strip()]
    files = find_files(args.root, recurse=not args.no_recurse, kinds=kinds, custom_exts=custom_exts)
    if not files:
        print("No matching files found.")
        return 0
    dupes = find_duplicates(files, kinds=kinds, sample_frames=args.sample_frames)

    if args.json:
        payload = {
            "root": str(args.root),
            "recurse": not args.no_recurse,
            "duplicates": [
                {
                    "hash": key,
                    "items": [
                        {
                            "path": str(path),
                            "kind": fp.kind,
                            "detail": fp.detail,
                        }
                        for path, fp in items
                    ],
                }
                for key, items in dupes.items()
            ],
        }
        print(json.dumps(payload, indent=2))
    else:
        if dupes:
            print(format_report(dupes))
        else:
            print("No duplicates detected.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
