"""Bounded, local audio-only analysis. No lyric, project or provider dependency.

The major/minor chord and key-profile approach follows the supplied legacy
AudioAnalyzer's algorithmic direction; this implementation streams decoded audio
and does not copy its project/semantic orchestration or shared-package sources.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import sys

import numpy as np

VERSION = "stream-audio-1.0.0"
RATE, HOP, FFT = 11025, 512, 2048
MAX_BYTES, MAX_SECONDS = 8 * 1024**3, 7200
PITCHES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
FLAGS = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0


def emit(**value):
    print(json.dumps(value, ensure_ascii=False, allow_nan=False), flush=True)


def run_small(args, timeout=30):
    # ffprobe only returns bounded metadata for a single selected stream.
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                            timeout=timeout, creationflags=FLAGS, check=False)
    if result.returncode or len(result.stdout) > 65536:
        raise ValueError("音源の形式を確認できませんでした。")
    return result.stdout


def probe(source):
    data = json.loads(run_small(["ffprobe", "-v", "error", "-select_streams", "a:0",
        "-show_entries", "format=duration:stream=sample_rate,channels,duration", "-of", "json", str(source)]))
    streams = data.get("streams", [])
    if not streams:
        raise ValueError("音声トラックがありません。")
    duration = float(streams[0].get("duration", data.get("format", {}).get("duration", 0)))
    if not math.isfinite(duration) or not 0 < duration <= MAX_SECONDS:
        raise ValueError("音源は2時間以内で、長さを取得できる形式を選んでください。")
    return duration


def fingerprint(source):
    before = source.stat()
    if not 0 < before.st_size <= MAX_BYTES:
        raise ValueError("音源は8 GiB以下を選んでください。")
    digest = hashlib.sha256()
    with source.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    after = source.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError("読み込み中に音源が変わりました。")
    return {"sha256": digest.hexdigest(), "sizeBytes": before.st_size,
            "mtimeUtcTicks": str(before.st_mtime_ns // 100 + 621355968000000000)}


def templates():
    chords, labels = [], []
    for intervals, suffix in [([0, 4, 7], ""), ([0, 3, 7], "m")]:
        base = np.zeros(12); base[intervals] = 1
        for i, name in enumerate(PITCHES):
            chords.append(np.roll(base, i) / np.linalg.norm(base)); labels.append(name + suffix)
    profiles = [np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]),
                np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])]
    keys = np.array([np.roll(p / np.linalg.norm(p), i) for p in profiles for i in range(12)])
    return np.array(chords), labels, keys


def rhythm(flux, duration):
    if len(flux) < 16 or max(flux, default=0) < 1e-7:
        return None, []
    onset = np.asarray(flux, dtype=np.float64)
    onset = np.maximum(0, onset - np.mean(onset))
    size = 1 << (2 * len(onset) - 1).bit_length()
    spectrum = np.fft.rfft(onset, size)
    correlation = np.fft.irfft(spectrum * spectrum.conj(), size)[:len(onset)]
    lo, hi = int(RATE / HOP * 60 / 200), min(len(onset) - 1, int(RATE / HOP * 60 / 60))
    if hi <= lo or correlation[0] < 1e-12:
        return None, []
    lag = lo + int(np.argmax(correlation[lo:hi + 1]))
    bpm = 60 * RATE / (HOP * lag)
    phase = int(np.argmax([np.sum(onset[i::lag]) for i in range(lag)]))
    beats = []
    for index in range(phase, len(onset), lag):
        left, right = max(0, index - 1), min(len(onset), index + 2)
        at = (left + int(np.argmax(onset[left:right]))) * HOP / RATE
        if at <= duration and (not beats or at > beats[-1]): beats.append(round(at, 6))
    return round(bpm, 3), beats


def decode(source, duration, analyze):
    args = ["ffmpeg", "-nostdin", "-v", "error", "-threads", "1", "-i", str(source),
            "-map", "0:a:0", "-vn", "-t", str(MAX_SECONDS + .5), "-ac", "1", "-ar", str(RATE),
            "-f", "f32le", "pipe:1"]
    process = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, creationflags=FLAGS)
    pending = np.empty(0, dtype=np.float32)
    window = np.hanning(FFT)
    freqs = np.fft.rfftfreq(FFT, 1 / RATE)
    selected = (freqs >= 50) & (freqs <= 2500)
    pitch = np.rint(69 + 12 * np.log2(freqs[selected] / 440)).astype(int) % 12
    wave_bins, chroma_bins, flux = {}, {}, []
    sample, prior, last_progress = 0, None, -1
    try:
        while chunk := process.stdout.read(65536):
            if len(chunk) % 4: raise ValueError("音声デコードのフレームが不正です。")
            block = np.frombuffer(chunk, dtype="<f4")
            if not np.isfinite(block).all(): raise ValueError("音源に不正な数値があります。")
            pending = np.concatenate((pending, block))
            while len(pending) >= FFT:
                frame = np.clip(pending[:FFT].astype(np.float64), -1, 1)
                at = sample / RATE
                if at > MAX_SECONDS: raise ValueError("デコード音声が2時間を超えています。")
                bin_id = int(at * 10)
                values = wave_bins.setdefault(bin_id, [1., -1., 0., 0])
                values[0] = min(values[0], float(np.min(frame)))
                values[1] = max(values[1], float(np.max(frame)))
                values[2] += float(np.mean(frame * frame)); values[3] += 1
                if analyze:
                    magnitude = np.abs(np.fft.rfft(frame * window))
                    flux.append(0. if prior is None else float(np.maximum(magnitude - prior, 0).sum()))
                    prior = magnitude
                    chroma = np.bincount(pitch, weights=magnitude[selected], minlength=12)
                    chroma_bins[bin_id] = chroma_bins.get(bin_id, np.zeros(12)) + chroma
                pending = pending[HOP:]; sample += HOP
                percent = min(84, 10 + int(at / duration * 74))
                if percent != last_progress:
                    emit(progress=percent, stage="音声解析" if analyze else "波形を準備")
                    last_progress = percent
        if process.wait() != 0: raise ValueError("音声のデコードに失敗しました。")
        # Include very short input and the final tail so end-of-file is visible.
        if len(pending):
            frame = np.clip(pending.astype(np.float64), -1, 1)
            bin_id = min(71999, int(sample / RATE * 10))
            values = wave_bins.setdefault(bin_id, [1., -1., 0., 0])
            values[0] = min(values[0], float(frame.min()))
            values[1] = max(values[1], float(frame.max()))
            values[2] += float(np.mean(frame * frame)); values[3] += 1
        if not wave_bins: raise ValueError("空の音源です。")
    finally:
        if process.poll() is None: process.kill()
        process.wait(); process.stdout.close()
    points = [{"time": round(i / 10, 6), "min": round(v[0], 6), "max": round(v[1], 6),
               "rms": round(math.sqrt(v[2] / v[3]), 6)} for i, v in sorted(wave_bins.items())]
    return points, chroma_bins, flux


def harmonies(chroma_bins, beats, duration):
    if not chroma_bins: return None, []
    chord_templates, labels, key_templates = templates()
    global_chroma = np.sum(list(chroma_bins.values()), axis=0)
    norm = np.linalg.norm(global_chroma)
    if norm < 1e-8: return None, []
    scores = key_templates @ (global_chroma / norm)
    key = int(np.argmax(scores))
    tonality = {"key": PITCHES[key % 12], "scale": "major" if key < 12 else "minor",
                "similarity": round(float(scores[key]), 6)}
    boundaries = sorted(set([0., duration] + beats)) if len(beats) >= 2 else list(np.arange(0., duration, 1.)) + [duration]
    output = []
    for start, end in zip(boundaries, boundaries[1:]):
        if end <= start: continue
        values = [chroma_bins[i] for i in range(int(start * 10), max(int(start * 10) + 1, int(math.ceil(end * 10)))) if i in chroma_bins]
        vector = np.sum(values, axis=0) if values else np.zeros(12)
        norm = np.linalg.norm(vector)
        label = labels[int(np.argmax(chord_templates @ (vector / norm)))] if norm > 1e-8 else "N"
        if output and output[-1]["label"] == label:
            output[-1]["end"] = round(float(end), 6)
        else:
            output.append({"start": round(float(start), 6), "end": round(float(end), 6), "label": label})
    return tonality, output


def process_audio(source, output, analyze):
    source, output = Path(source).resolve(strict=True), Path(output).resolve(strict=True)
    emit(progress=0, stage="音源を確認")
    identity = fingerprint(source)
    duration = probe(source)
    points, chroma, flux = decode(source, duration, analyze)
    bpm, beats = rhythm(flux, duration) if analyze else (None, [])
    tonality, chords = harmonies(chroma, beats, duration) if analyze else (None, [])
    preview = None
    if not analyze:
        emit(progress=86, stage="試聴用音源を準備")
        preview = "preview.wav"
        args = ["ffmpeg", "-nostdin", "-v", "error", "-threads", "1", "-i", str(source), "-map", "0:a:0", "-vn",
                "-t", str(MAX_SECONDS), "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-n", str(output / preview)]
        result = subprocess.run(args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=1500, creationflags=FLAGS)
        if result.returncode: raise ValueError("試聴用音源を作成できませんでした。")
    if fingerprint(source) != identity: raise ValueError("処理中に音源が変わりました。")
    result = {"format": "flamoris.audio-features", "schemaVersion": 1, "algorithm": VERSION,
              "mode": "analyze" if analyze else "prepare", "source": identity,
              "duration": duration, "sampleRate": RATE, "waveform": points,
              "bpm": bpm, "beats": beats, "tonality": tonality, "chords": chords, "preview": preview}
    payload = json.dumps(result, ensure_ascii=False, allow_nan=False).encode("utf-8")
    if len(payload) > 16 * 1024 * 1024: raise ValueError("解析結果が上限を超えました。")
    temporary = output / "audio.json.tmp"
    with temporary.open("xb") as stream:
        stream.write(payload); stream.flush(); os.fsync(stream.fileno())
    os.replace(temporary, output / "audio.json")
    emit(progress=100, stage="完了")
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["prepare", "analyze"])
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        process_audio(args.input, args.output, args.mode == "analyze")
    except (ValueError, OSError, subprocess.SubprocessError, KeyError, TypeError):
        emit(error="音源を処理できませんでした。形式・長さ・空き容量・FFmpegを確認してください。")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
