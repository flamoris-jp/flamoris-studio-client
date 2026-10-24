# StudioClient delivery evidence

## 2026-10-24: PONPON / AudioAnalyzer local candidate

Implemented local scope for Issue #4:

- C#/.NET 10 WPF PONPON shell; automatic document state, audio preparation,
  paste/hold-release, native waveform move/trim, selected-line audition, aligned
  Vocal Stem switching, みるみる, inspector and bounded Undo/Redo.
- Strict local document v1 and portable timing v1; legacy PONPON JSON/SRT import,
  original snapshot, tick precision, SRT precision rejection and atomic
  fingerprint-checked save. No paths in portable lyric exchange.
- Independent bounded streaming Python/NumPy audio worker; no lyric/song/semantic
  prerequisite; waveform/RMS, estimated BPM/beats, key and triad chords.
- Source hashes, stale/export checks, one worker, cancellation/process-tree join,
  owned temp cleanup and local PCM audition cache.
- Windows portable packaging, dependency setup, Linux/Windows product CI and
  explicit physical acceptance checklist.

Local evidence:

- 19 managed tests passed, including actual C# → Python/FFmpeg → validated JSON,
  no-lyrics analysis, preview PCM, concurrent-job rejection and cancellation cleanup.
- 7 Python synthetic-audio tests passed: C/Am harmony, regular beat detection,
  silence, source fingerprint, PCM preparation and invalid-media non-publication.
- Windows-targeted WPF Release compilation passed without warnings/errors on
  the Linux build host. This is compilation, not Windows execution.
- Streaming measurement on this Linux build host: a synthetic 30-minute mono
  WAV (79,380,078 bytes) analyzed in 5.390 seconds, with 18k reduced waveform bins
  and a 1,284,142-byte JSON. Peak worker-process RSS was 46,684 KiB; this excludes
  FFmpeg child/UI memory. This sine-wave workload does not certify real-song
  throughput, Windows performance or the two-hour/eight-GiB admission limits.

Windows CI packaging/startup evidence is recorded on the implementation PR when
available. Physical sound-device, pointer/DPI, pause/seek timing, real song
estimation and representative large-file/shared-storage checks remain open in
[`staging/windows-ponpon-audio.md`](staging/windows-ponpon-audio.md).

No Studio/MCP binding, remote grants/filesystem/launcher or automatic Kachinco
editing is delivered by this local slice. KineticTypo import belongs to Kachinco.
Do not close the overall Issue #4 as complete based solely on this candidate.
