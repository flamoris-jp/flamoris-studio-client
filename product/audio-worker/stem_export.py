"""Local stem-WAV analysis sidecar for Mousa. No training, upload or UI mutation.

The existing AudioAnalyzer worker remains the feature authority. pYIN estimates
pitch only; its voicing probability is not calibrated note correctness. Every
note is a pending-review candidate, and unknown frames are never encoded as rests.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import signal
import stat
import subprocess
import sys
import tempfile

VERSION = "stem-analysis-0.1.0"
ROLES = ("vocals", "bass", "drums", "harmony", "mix")
MAX_BYTES, MAX_SECONDS, MAX_EXCERPT = 512 * 1024**2, 600, 30
MAX_JSON = 16 * 1024**2
RATE, HOP = 16000, 160
ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}\Z")
HERE = Path(__file__).resolve().parent


def local_path(value, *, existing=True):
    """Reject links/reparse components, even before resolving a parent directory."""
    path = Path(os.path.abspath(value))
    for item in (path, *path.parents):
        if not item.exists() and not item.is_symlink():
            continue
        meta = item.lstat()
        if stat.S_ISLNK(meta.st_mode) or getattr(meta, "st_file_attributes", 0) & 0x400:
            raise ValueError("linked path is not allowed")
    if existing and not path.is_file():
        raise ValueError("select one existing regular file")
    return path


def number(value, low, high):
    if type(value) not in (int, float) or not math.isfinite(value) or not low <= value <= high:
        raise ValueError("numeric argument outside bounds")
    return value


def sha256(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        while data := stream.read(1024 * 1024):
            h.update(data)
    return h.hexdigest()


def inspect_wav(value):
    import soundfile as sf
    path = local_path(value)
    before = path.stat()
    if path.suffix.lower() != ".wav" or not 0 < before.st_size <= MAX_BYTES:
        raise ValueError("select a WAV within the size limit")
    info = sf.info(str(path))
    if (info.format not in ("WAV", "WAVEX") or info.subtype not in ("PCM_16", "PCM_24", "PCM_32", "FLOAT")
            or not 1 <= info.channels <= 2 or not 8000 <= info.samplerate <= 192000
            or not 0 < info.frames / info.samplerate <= MAX_SECONDS):
        raise ValueError("unsupported WAV profile")
    digest = sha256(path)
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError("source changed while fingerprinting")
    return path, {"sha256": digest, "sizeBytes": before.st_size,
                  "duration": info.frames / info.samplerate, "sampleRate": info.samplerate,
                  "channels": info.channels}


def publish(path, value):
    """Write completely, then publish with no replacement of an existing name."""
    path = local_path(path, existing=False)
    if path.exists() or not path.parent.is_dir():
        raise FileExistsError("choose a new output in an existing directory")
    data = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()
    if len(data) > MAX_JSON:
        raise ValueError("result byte limit")
    fd, temporary = tempfile.mkstemp(prefix=".stem-", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data); stream.flush(); os.fsync(stream.fileno())
        # Same-filesystem hard link is atomic and fails if destination exists.
        os.link(temporary, path)
    finally:
        os.unlink(temporary)


def child(args, timeout):
    """Fixed local programs only; terminate descendants on timeout/cancellation."""
    flags = subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    process = subprocess.Popen(args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                               start_new_session=os.name != "nt", creationflags=flags)
    try:
        if process.wait(timeout=timeout):
            raise ValueError("analysis child failed")
    finally:
        if process.poll() is None:
            if os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15, check=False)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.kill(); process.wait()


def note_candidates(frames, end):
    """Conservative rounded-pitch runs; no gap filling or grid quantization.

    Median smoothing reduces small pitch flicker. This is deliberately NOT an
    onset detector: same-pitch syllables, slides and harmony require review.
    """
    import numpy as np
    labels = []
    for i, frame in enumerate(frames):
        if frame["state"] != "voiced":
            labels.append(None); continue
        nearby = [x["midi"] for x in frames[max(0, i-2):i+3] if x["state"] == "voiced"]
        labels.append(int(math.floor(float(np.median(nearby)) + .5)))
    notes, first = [], 0
    for i in range(1, len(frames) + 1):
        if i < len(frames) and labels[i] == labels[first]:
            continue
        stop = frames[i]["time"] if i < len(frames) else end
        start = frames[first]["time"]
        if labels[first] is not None and stop - start >= .08:
            values = [x["midi"] for x in frames[first:i]]
            notes.append({"id": "n" + str(len(notes) + 1), "start": start, "end": stop,
                          "pitch": labels[first], "medianMidi": float(np.median(values)),
                          "voicingProbabilityMean": float(np.mean([x["voicingProbability"] for x in frames[first:i]])),
                          "reviewState": "pending"})
        first = i
    return notes


def estimate_pitch(source, role, start, end):
    import librosa
    import numpy as np
    import soundfile as sf
    if librosa.__version__ != "0.11.0":
        raise ValueError("the admitted pYIN profile requires librosa 0.11.0")
    source, identity = inspect_wav(source)
    number(start, 0, identity["duration"]); number(end, start, identity["duration"])
    if not 0 < end - start <= MAX_EXCERPT or role not in ("vocals", "bass"):
        raise ValueError("pitch role/interval")
    a, b = round(start * identity["sampleRate"]), round(end * identity["sampleRate"])
    with sf.SoundFile(str(source)) as stream:
        stream.seek(a); samples = stream.read(b - a, dtype="float64", always_2d=True)
    if len(samples) != b - a or not np.isfinite(samples).all() or np.max(np.abs(samples), initial=0) > 1.01:
        raise ValueError("invalid/truncated/non-normalized audio samples")
    y = librosa.resample(samples.mean(axis=1), orig_sr=identity["sampleRate"], target_sr=RATE)
    if not 1 <= len(y) <= RATE * MAX_EXCERPT + 1:
        raise ValueError("decoded sample bound")
    fmin, fmax, window = (65.4, 1108.0, 2048) if role == "vocals" else (32.7, 392.0, 4096)
    f0, voiced, probability = librosa.pyin(y, sr=RATE, fmin=fmin, fmax=fmax,
        frame_length=window, hop_length=HOP, center=True, fill_na=float("nan"), resolution=.2)
    rms = librosa.feature.rms(y=y, frame_length=window, hop_length=HOP, center=True)[0]
    frames = []
    for i, (hz, is_voiced, p, energy) in enumerate(zip(f0, voiced, probability, rms)):
        t = start + i * HOP / RATE
        if t >= end: break
        state = "low_energy" if energy < 1e-4 else ("voiced" if is_voiced and p >= .5 and np.isfinite(hz) and fmin <= hz <= fmax else "unknown")
        frames.append({"time": t, "state": state, "f0Hz": float(hz) if state == "voiced" else None,
                       "midi": float(69 + 12 * math.log2(hz / 440)) if state == "voiced" else None,
                       "voicingProbability": float(p), "rms": float(energy)})
    if not frames or len(frames) > 3000: raise ValueError("pitch frame bound")
    return {"method": "librosa.pyin", "settings": {"sampleRate": RATE, "hopLength": HOP,
             "frameLength": window, "fmin": fmin, "fmax": fmax, "resolution": .2,
             "voicingThreshold": .5, "rmsThreshold": 1e-4, "channelMix": "mean"},
            "frames": frames, "notes": note_candidates(frames, end), "reviewRequired": True}


def analyze(source, output, *, song_id, family_id, stem_id, role, start=0.0, end=None, offset=0.0):
    for value in (song_id, family_id, stem_id):
        if not isinstance(value, str) or not ID.fullmatch(value): raise ValueError("invalid opaque ID")
    if role not in ROLES: raise ValueError("unsupported stem role")
    source, identity = inspect_wav(source)
    end = min(identity["duration"], start + MAX_EXCERPT) if end is None else end
    number(start, 0, identity["duration"]); number(end, 0, identity["duration"])
    number(offset, -600, 86400)
    if not .1 <= end-start <= MAX_EXCERPT: raise ValueError("select a 0.1..30 second excerpt")
    output = local_path(output, existing=False)
    if output.exists() or not output.parent.is_dir(): raise FileExistsError("output already exists/parent missing")
    with tempfile.TemporaryDirectory(prefix="mousa-stem-") as scratch:
        scratch = Path(scratch)
        child([sys.executable, str(HERE / "worker.py"), "analyze", "--input", str(source), "--output", str(scratch)], 180)
        data = scratch / "audio.json"
        if data.stat().st_size > MAX_JSON: raise ValueError("feature byte limit")
        features = json.loads(data.read_text(encoding="utf-8"))
        if (features["source"]["sha256"] != identity["sha256"] or features["source"]["sizeBytes"] != identity["sizeBytes"]
                or abs(features["duration"] - identity["duration"]) > .01):
            raise ValueError("feature source mismatch")
        pitch = {"method": "not_applicable", "settings": None, "frames": [], "notes": [], "reviewRequired": True}
        if role in ("vocals", "bass"):
            pitch_path = scratch / "pitch.json"
            child([sys.executable, str(Path(__file__).resolve()), "_pitch", "--input", str(source),
                   "--output", str(pitch_path), "--role", role, "--start", str(start), "--end", str(end)], 180)
            if pitch_path.stat().st_size > MAX_JSON: raise ValueError("pitch byte limit")
            pitch = json.loads(pitch_path.read_text(encoding="utf-8"))
        _, after = inspect_wav(source)
        if after != identity: raise ValueError("source changed during analysis")
        import importlib.metadata as metadata
        result = {"format": "flamoris.stem-analysis", "schemaVersion": 1,
                  "producer": {"name": "AudioAnalyzer", "version": VERSION,
                      "workerSha256": sha256(HERE / "worker.py"), "stemWorkerSha256": sha256(__file__),
                      "dependencies": {name: metadata.version(name) for name in ("numpy", "soundfile", "librosa")}},
                  "source": identity, "identity": {"songId": song_id, "familyId": family_id, "stemId": stem_id, "role": role},
                  "time": {"unit": "seconds", "start": start, "end": end, "songOffset": offset},
                  "audioFeatures": features, "pitch": pitch,
                  "warnings": ["estimates_not_ground_truth", "voicing_is_not_note_correctness", "unknown_is_not_rest",
                               "same_pitch_onsets_and_polyphony_not_resolved", "stem_beats_are_not_global_song_tempo"]}
        publish(output, result)
        return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("analyze", "_pitch"))
    parser.add_argument("--input", required=True); parser.add_argument("--output", required=True)
    parser.add_argument("--role", choices=ROLES, required=True)
    parser.add_argument("--song-id"); parser.add_argument("--family-id"); parser.add_argument("--stem-id")
    parser.add_argument("--start", type=float, default=0); parser.add_argument("--end", type=float)
    parser.add_argument("--offset", type=float, default=0)
    args = parser.parse_args()
    try:
        if args.command == "_pitch":
            publish(args.output, estimate_pitch(args.input, args.role, args.start, args.end))
        else:
            result = analyze(args.input, args.output, song_id=args.song_id, family_id=args.family_id,
                             stem_id=args.stem_id, role=args.role, start=args.start, end=args.end, offset=args.offset)
            print(json.dumps({"status": "pending_review", "noteCandidates": len(result["pitch"]["notes"])}))
    except (ValueError, OSError, TypeError, KeyError, ImportError, subprocess.SubprocessError):
        print("Stem analysis failed; check WAV profile, dependencies, interval and new output path.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
