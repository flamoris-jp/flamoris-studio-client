# AudioAnalyzer stem export to Mousa (experimental CLI)

Tracking: Studio Client #19 and [Mousa #7](https://github.com/flamoris-jp/Mousa/issues/7).
This is an **optional local research CLI**, not a new PONPON screen, deployed
service, automatic training path or production singing-transcription guarantee.
Existing `worker.py`, the Windows UI and `flamoris.audio-features` v1 are unchanged.

## What is implemented

`product/audio-worker/stem_export.py analyze` invokes the real adjacent
AudioAnalyzer worker for waveform/RMS, estimated tempo/beats and major/minor
harmonies. Vocal/bass excerpts additionally use **librosa 0.11.0 pYIN** for a
continuous F0 contour and conservative note candidates. No model weights or
provider calls are involved. Other roles retain features only; a drum stem is
not silently converted into melody notes.

Install the optional dependencies in a separate environment, with FFmpeg/FFprobe
already installed. Ordinary `requirements.txt` and the existing worker stay
lightweight. Python 3.11+ is required; Linux is the tested CLI profile here.

```bash
python -m pip install -r product/audio-worker/requirements-stems.txt
python product/audio-worker/stem_export.py analyze \
  --input /chosen/music/vocals.wav --output /chosen/results/vocals.analysis.json \
  --role vocals --song-id song-001 --family-id work-family-001 --stem-id lead \
  --start 20 --end 28 --offset 0
```

Paths are examples, never deployment defaults. Inputs are explicitly selected
local files. Output parents must exist and the output name must be new. There
is no upload or recursive scan. Run one CLI operation at a time. The source WAV
is not copied into the JSON or repository. The analysis is a **candidate**, not
verified annotation or consent to train.

## Time and identity

The format is `flamoris.stem-analysis`, `schemaVersion: 1`. Its closed fields are:

| Field | Meaning |
| --- | --- |
| `producer` | AudioAnalyzer/version, hashes of both scripts, dependency versions |
| `source` | SHA-256, byte size, original duration/sample rate/channel count |
| `identity` | Opaque `songId`, common `familyId`, `stemId`, and `role` |
| `time` | `unit: seconds`, source-relative excerpt `start/end`, signed `songOffset` |
| `audioFeatures` | Existing complete audio-features v1 result for the source |
| `pitch` | Method/settings, frame estimates, pending note candidates, required review |
| `warnings` | Fixed uncertainty/unsupported-behavior codes |

**Song time = source-WAV time + songOffset.** A fully aligned stem normally has
offset zero; a crop beginning at song second 20 uses offset 20 if its own file
begins at zero. Every related stem, crop and transposed variant must keep the
same family ID. This ID is a caller assertion, not automatic song recognition.
Stem-estimated beats/chords are not authoritative global tempo or harmony. Keep
a mix analysis separately for future shared beat alignment.

Frames use a 10 ms hop. They distinguish `voiced`, `low_energy` and `unknown`.
The latter two have null pitch; neither becomes a supervised REST token.
`voicingProbability` estimates voicing, **not correct note probability**. Notes
are median-smoothed rounded-pitch runs of at least 80 ms, retaining raw F0 and
seconds. No sixteenth-grid rounding occurs. Overlapping voices, same-pitch
syllable re-attacks, slides, octave errors and separation artifacts require human
review. Window-centered note boundaries are approximate, not exact phoneme/onset
measurements. No semantic verse/chorus or aesthetic-score inference is claimed.

## Bounds and publication

Source: regular WAV/WAVEX, PCM 16/24/32-bit or float32, 1–2 channels, 8–192 kHz,
up to 512 MiB and 600 seconds. Pitch excerpt: 0.1–30 seconds, at most 3000 frames.
Pitch profiles are 65.4–1108 Hz vocals and 32.7–392 Hz bass, decoded at 16 kHz;
out-of-range estimates remain unknown, never pitch-clamped.

Each feature/pitch child has a 180-second deadline; cancellation/timeout kills
its process tree. Source hashes are checked before/after processing. These are
local operator tools, not a hostile multi-user filesystem sandbox. Links/reparse
components, missing parents and existing outputs are rejected. JSON is written
fully, fsynced and hard-linked into the final new name. Publication needs a
filesystem supporting hard links; failure does not overwrite an old result.
Private source paths are not embedded in reports or error messages.

## Tests and limits

```bash
python -m unittest discover -s test/audio -v
python -m unittest discover -s test/stems -v
```

The 7 existing audio tests and 13 new stem tests use temporary synthetic audio.
They cover actual worker/pYIN calls, tone/bass/rest/noise/vibrato, timing identity,
role handling, bounds, no overwrite and subprocess termination. This is not a
claim of physical Windows UI acceptance or transcription accuracy on real songs.
The new CI runs this separate optional profile without adding dependencies to
ordinary AudioAnalyzer setup.

Mousa consumes this format with its own strict validator and source-WAV hash
check. It requires content-bound note review and a rights-evidence declaration
before exporting selected annotations. That export still does not train a model.

## Dependencies

This code is Apache-2.0. It imports rather than vendors librosa (ISC), SoundFile
(BSD-3-Clause; separately installed libsndfile has its own license), and NumPy
(BSD-3-Clause). FFmpeg remains separately installed and is not redistributed by
this change. Data, stems, generated audio and model weights have independent
rights. pYIN reference: https://librosa.org/doc/0.11.0/generated/librosa.pyin.html

## Follow-on work

Real-stem auditions and corrected-note references, UI review and packaging,
separation provenance/parent mix linkage, drum/polyphonic transcription,
shared tempo maps, and Cantabile handoff remain open under the tracking Issues.
