import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import numpy as np
import soundfile as sf

SCRIPT = Path(__file__).resolve().parents[2] / 'product/audio-worker/stem_export.py'
spec = importlib.util.spec_from_file_location('stem_export', SCRIPT)
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)


class StemExportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(); cls.addClassCleanup(cls.temp.cleanup)
        cls.root = Path(cls.temp.name); cls.source = cls.root / 'synthetic.wav'
        sr = 22050; t = np.arange(sr) / sr
        samples = np.concatenate([np.zeros(sr//2), .22*np.sin(2*np.pi*440*t),
                                  np.zeros(sr//2), .22*np.sin(2*np.pi*523.251*t)])
        sf.write(cls.source, samples, sr, subtype='PCM_16')
        cls.result = module.analyze(cls.source, cls.root/'analysis.json', song_id='synthetic-song',
             family_id='synthetic-family', stem_id='lead', role='vocals', offset=4.25)

    def test_actual_worker_and_pyin_roundtrip(self):
        result = self.result
        self.assertEqual(result, json.loads((self.root/'analysis.json').read_text()))
        self.assertEqual(result['audioFeatures']['algorithm'], 'stream-audio-1.0.0')
        self.assertEqual(result['source']['sha256'], hashlib.sha256(self.source.read_bytes()).hexdigest())
        self.assertEqual([n['pitch'] for n in result['pitch']['notes']], [69, 72])
        self.assertAlmostEqual(result['pitch']['notes'][0]['start'], .5, delta=.08)
        self.assertAlmostEqual(result['pitch']['notes'][0]['end'], 1.5, delta=.08)
        self.assertTrue(result['pitch']['reviewRequired'])

    def test_unknown_does_not_become_rest_or_note(self):
        frames = self.result['pitch']['frames']
        self.assertIn('unknown', {f['state'] for f in frames})
        self.assertIn('low_energy', {f['state'] for f in frames})
        for f in frames:
            if f['state'] != 'voiced': self.assertIsNone(f['f0Hz']); self.assertIsNone(f['midi'])
        for n in self.result['pitch']['notes']:
            self.assertTrue(all(f['state']=='voiced' for f in frames if n['start'] <= f['time'] < n['end']))

    def test_no_paths_or_training_authority_in_export(self):
        encoded = json.dumps(self.result)
        self.assertNotIn(str(self.source), encoded)
        self.assertEqual(self.result['time'], dict(unit='seconds', start=0, end=3., songOffset=4.25))
        self.assertTrue(all(n['reviewState']=='pending' for n in self.result['pitch']['notes']))

    def test_silence_is_low_energy_not_fake_melody(self):
        source = self.root/'silence.wav'; sf.write(source, np.zeros(16000), 16000, subtype='PCM_16')
        p = module.estimate_pitch(source, 'vocals', 0, 1)
        self.assertEqual(p['notes'], [])
        self.assertTrue(all(f['state']=='low_energy' for f in p['frames']))

    def test_bass_profile(self):
        source = self.root/'bass.wav'; t=np.arange(16000)/16000
        sf.write(source, .2*np.sin(2*np.pi*55*t), 16000, subtype='PCM_24')
        p=module.estimate_pitch(source, 'bass', 0, 1)
        self.assertEqual({n['pitch'] for n in p['notes']}, {33})
        self.assertEqual(p['settings']['frameLength'], 4096)

    def test_noise_is_retained_as_uncertain(self):
        source=self.root/'noise.wav'
        sf.write(source, np.random.default_rng(7).normal(0,.1,16000), 16000)
        p=module.estimate_pitch(source,'vocals',0,1)
        self.assertGreater(sum(f['state']=='unknown' for f in p['frames']), 90)

    def test_vibrato_keeps_contour_separate(self):
        source=self.root/'vibrato.wav'; t=np.arange(16000)/16000
        hz=440*2**(.25*np.sin(2*np.pi*5*t)/12)
        sf.write(source,.2*np.sin(2*np.pi*np.cumsum(hz)/16000),16000)
        p=module.estimate_pitch(source,'vocals',0,1)
        self.assertEqual({n['pitch'] for n in p['notes']},{69})
        contour=[f['midi'] for f in p['frames'] if f['state']=='voiced']
        self.assertGreater(max(contour)-min(contour),.1)

    def test_non_pitch_role_has_features_only(self):
        value=module.analyze(self.source,self.root/'drums.json',song_id='s',family_id='f',stem_id='d',role='drums',end=1)
        self.assertEqual(value['pitch']['method'],'not_applicable')
        self.assertEqual(value['pitch']['notes'],[])
        self.assertTrue(value['audioFeatures']['waveform'])

    def test_interval_role_and_id_bounds(self):
        args=dict(song_id='s',family_id='f',stem_id='v',role='vocals')
        for changes in (dict(start=-1), dict(end=4), dict(start=2,end=1), dict(role='guitar'),
                        dict(song_id='../private'),dict(offset=float('nan')),dict(end=.01)):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                module.analyze(self.source,self.root/'invalid.json',**(args|changes))
        self.assertFalse((self.root/'invalid.json').exists())

    def test_existing_output_is_preserved(self):
        path=self.root/'preserved.json'; path.write_bytes(b'original')
        with self.assertRaises(FileExistsError): module.publish(path,{'new':True})
        self.assertEqual(path.read_bytes(),b'original')
        self.assertFalse(list(self.root.glob('.stem-*')))

    def test_symlinks_in_file_and_parent_are_rejected(self):
        link=self.root/'link.wav'; link.symlink_to(self.source)
        with self.assertRaises(ValueError): module.inspect_wav(link)
        parent=self.root/'linked-dir'; parent.symlink_to(self.root,target_is_directory=True)
        with self.assertRaises(ValueError): module.publish(parent/'no.json',{})

    def test_non_wav_and_invalid_samples_rejected(self):
        source=self.root/'bad.wav'; source.write_bytes(b'not audio')
        with self.assertRaises((ValueError,RuntimeError)): module.inspect_wav(source)
        source=self.root/'nonfinite.wav'; sf.write(source,np.full(16000,float('nan')),16000,subtype='FLOAT')
        with self.assertRaises(ValueError): module.estimate_pitch(source,'vocals',0,1)

    def test_child_deadline_and_failure(self):
        with self.assertRaises(subprocess.TimeoutExpired):
            module.child([sys.executable,'-c','import time; time.sleep(20)'],.05)
        with self.assertRaises(ValueError): module.child([sys.executable,'-c','raise SystemExit(1)'],5)


if __name__=='__main__': unittest.main()
