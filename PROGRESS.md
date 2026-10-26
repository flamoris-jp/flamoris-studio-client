# StudioClient delivery evidence

## 2026-10-27: distribution lyrics review/export (#17)

- Add an independent editable lyrics preview from the current project or the
  lyrics import dialog. Conservative section-tag, line-ending punctuation,
  initial-capital and blank-line cleanup; spelling/acronyms/parenthetical vocals
  require review. Copy and UTF-8 TXT use the checked text, with atomic local save.
- Preserve canonical edits, timing and history. Restore skipped annotation-only
  rows by original IDs; old projects cannot reconstruct original blank lines.
- Add managed regression coverage and Windows synthetic UI checks/screenshots.
  Build/CI results are recorded on the implementation PR. Physical clipboard,
  save-dialog and representative lyric review remain separate acceptance checks.


## 2026-10-24: selection crash root cause found from Windows event log (#13)

- The owner supplied .NET Runtime evidence: unhandled ArgumentException for a
  duplicate ItemInfo key in Selector.InternalSelectedItemsStorage.Add, reached
  from SelectedItemCollection.ClearItems -> MainWindow.SelectGrid -> timeline click.
- LyricRow's generated record hash/equality included mutable IsCurrent and
  PropertyChanged subscribers. Follow/binding changes invalidated keys already
  stored in WPF selection collections, explaining failures beyond stretching.
- Replace the row with a reference-identity view class. Highlight notifications
  remain; text/IDs/timing document and multi-selection behavior are preserved.
- Before fixing, three CLR regression tests fail (highlight hash stability,
  subscription hash stability, distinct row instances). After fixing, all 44
  managed tests pass locally. Windows-targeted WPF Release compilation passes.
- Windows regression smoke selects 128 rows with repeated text, changes highlight
  and repeatedly uses the actual SelectGrid clear/reselect path. Before/after CI
  evidence is recorded on PR #14; physical acceptance remains separate.


## 2026-10-24: stretch-crash report hardening and recovery (#13)

- Owner reports unexpected exit while stretching a lyric-box edge. No exception
  trace from that run is available; the original root cause is not confirmed.
- Bound pointer deltas before conversion/addition, choose the nearest edge for
  short boxes, and validate all preview rows before rendering/committing.
  Invalid previews, capture loss and zoom cancel the gesture without publication.
- Add per-command atomic recovery checkpoints, inactive-instance detection and
  explicit startup restoration without overwriting a manual save.
- Reuse `Flamoris.Logging` 1.0.0 for bounded local diagnostics with only operation
  codes, exception types/HResults and method frames. Unknown UI exceptions remain
  fatal rather than being globally swallowed.
- 41 managed tests pass locally, including new drag-boundary, sub-tick, recovery
  isolation/validation/reparse and diagnostic privacy checks. WPF Release build
  passes with zero warnings/errors. Windows synthetic UI adds both-edge resize,
  undo, invalid-follow rejection, capture loss and invalid-coordinate checks.
  CI execution evidence is recorded on the implementation PR.
- Subsequent Windows event-log investigation is recorded above. The original
  boundary hardening alone was not the selection-hash root-cause fix.

## 2026-10-24: PONPON retry, annotations and lyric follow (#11)

- Whole annotation-only tag/parenthesis rows are excluded from working lyrics,
  including existing projects, while the original snapshot and inline groups stay.
- Virtualized lyric follow keeps one preceding row during recording/audition;
  current highlight is independent of edit selection and the audio clock.
- Back button / Z undoes consecutive PON commands with their overwritten ranges
  and predecessor changes, pauses at the recorded start and reselects that row.
- Confirmed full clear keeps lyrics, IDs, source media and original imports;
  all cleared timing is restored by one ordinary Undo.
- 35 managed tests passed locally, including five workflow cases for filtering,
  existing/timed imports, PON undo/redo boundaries, stale holds and reset atomicity.
  Windows-targeted WPF Release build passed with zero warnings/errors.
- Windows CI smoke adds long-list timer follow/virtualization, annotation rows,
  real Z handler/input focus, PON progression and full-reset history checks.
  CI execution evidence is recorded on the implementation PR. Physical sound,
  key-repeat cadence, pointer/DPI and device timing remain acceptance checks.

## 2026-10-24: PONPON usability follow-up (#7–#9)

Implemented in PR #10:

- Dedicated draft lyrics window: paste, strict UTF-8 TXT, existing JSON/SRT,
  shared picker/drop import path, replacement confirmation and undoable publication.
- S play/pause, Space hold/release, centered pressed PON face, equal current/NEXT
  text size and a timeline/table splitter with vertically responsive drawing.
- Multi-row/all-row shifts, 1–20ms presets, explicit manual units and optional
  predecessor-end alignment/follow in one validated snapshot/history update.
- 30 managed tests pass locally; 11 new cases cover import validation, file/drop
  rejection, batch failure atomicity, Undo/Redo, alignment, precision and export.
  WPF Release compilation passes with zero warnings/errors.
- Windows CI executes synthetic UI checks and captures workspace/dialog/pressed
  renders; exact run results and visual inspection are recorded on PR #10.
  These checks do not certify physical Explorer drops, real key-repeat cadence,
  sound-device timing, pointer feel or varied-DPI behavior.

The app is not deployed through Studio/MCP by this change. Overall #4 remains open.

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
