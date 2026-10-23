import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import wave

import numpy as np

SCRIPT = Path(__file__).resolve().parents[2] / "product/audio-worker/worker.py"
spec = importlib.util.spec_from_file_location("worker", SCRIPT)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class AudioTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / "日本語 audio.wav"

    def tearDown(self):
        self.temp.cleanup()

    def wav(self, samples, rate=22050):
        with wave.open(str(self.source), "wb") as output:
            output.setparams((1, 2, rate, 0, "NONE", "not compressed"))
            output.writeframes((np.clip(samples, -1, 1) * 32767).astype("<i2").tobytes())

    def run_job(self, analyze):
        output = self.root / "result"; output.mkdir()
        with contextlib.redirect_stdout(io.StringIO()):
            return worker.process_audio(self.source, output, analyze)

    def test_audio_only_c_major(self):
        t = np.arange(22050 * 8) / 22050
        self.wav(sum(.2 * np.sin(2 * np.pi * f * t) for f in [261.63, 329.63, 392.]))
        result = self.run_job(True)
        self.assertEqual("C", result["chords"][0]["label"])
        self.assertEqual("C", result["tonality"]["key"])
        self.assertEqual("major", result["tonality"]["scale"])
        self.assertIsNone(result["preview"])
        self.assertNotIn("lyrics", result)
        self.assertLessEqual(len(result["waveform"]), 81)
        self.assertEqual(result, json.loads((self.root / "result/audio.json").read_text()))

    def test_minor_template(self):
        t = np.arange(22050 * 5) / 22050
        self.wav(sum(.2 * np.sin(2 * np.pi * f * t) for f in [220., 261.63, 329.63]))
        self.assertEqual("Am", self.run_job(True)["chords"][0]["label"])

    def test_silence_has_no_false_harmony_or_bpm(self):
        self.wav(np.zeros(22050 * 2))
        result = self.run_job(True)
        self.assertIsNone(result["bpm"])
        self.assertIsNone(result["tonality"])
        self.assertEqual([], result["chords"])

    def test_dynamics_and_beat_spacing(self):
        sr = 22050; samples = np.zeros(sr * 12)
        click = np.sin(np.arange(800) * .5) * np.exp(-np.arange(800) / 180)
        for at in np.arange(.5, 11.6, .5):
            offset = int(at * sr); samples[offset:offset + len(click)] += click
        self.wav(samples)
        result = self.run_job(True)
        self.assertTrue(112 <= result["bpm"] <= 128, result["bpm"])
        self.assertGreater(len(result["beats"]), 15)
        self.assertGreater(max(p["rms"] for p in result["waveform"]), .03)
        self.assertTrue(all(b > a for a, b in zip(result["beats"], result["beats"][1:])))

    def test_prepare_produces_playable_pcm_without_semantics(self):
        self.wav(np.sin(np.arange(22050) * .1) * .2)
        result = self.run_job(False)
        self.assertEqual("prepare", result["mode"])
        self.assertIsNone(result["bpm"])
        with wave.open(str(self.root / "result/preview.wav")) as preview:
            self.assertEqual(48000, preview.getframerate())
            self.assertEqual(2, preview.getnchannels())
            self.assertEqual(2, preview.getsampwidth())
            self.assertAlmostEqual(1, preview.getnframes() / 48000, places=2)

    def test_invalid_media_does_not_publish_success(self):
        self.source.write_bytes(b"not audio")
        with self.assertRaises((ValueError, KeyError)):
            self.run_job(True)
        self.assertFalse((self.root / "result/audio.json").exists())

    def test_fingerprint_changes_with_same_length(self):
        self.source.write_bytes(b"aaaa")
        before = worker.fingerprint(self.source)
        self.source.write_bytes(b"bbbb")
        after = worker.fingerprint(self.source)
        self.assertEqual(before["sizeBytes"], after["sizeBytes"])
        self.assertNotEqual(before["sha256"], after["sha256"])


if __name__ == "__main__":
    unittest.main()
