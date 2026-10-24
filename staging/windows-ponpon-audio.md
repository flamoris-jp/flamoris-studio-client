# Windows PONPON/Analyzer physical acceptance

These checks require an actual Windows audio device and pointer/keyboard. A
successful compile, CI startup screenshot or synthetic audio analysis does not
mark them complete.

- [ ] Install the portable package, set up audio dependencies and open a real
  WAV/MP3/M4A. No manual song directory/project metadata.
- [ ] Paste Japanese/English/emoji lyrics, record with mouse hold and Enter;
  auto-repeat does not advance several lines, text-input Enter remains a newline.
- [ ] Abort a hold through focus loss and source switch; no stuck range is saved.
- [ ] Drag/move/trim at multiple DPI/zoom levels, scroll, Undo/Redo and verify one
  history entry per gesture. Edit inspector text/time and retry a selected line.
- [ ] Double-click a row to audition immediately; compare Music and an aligned
  Vocal Stem at the same origin. Confirm pause/resume, EOF and rapid seek behavior.
- [ ] Preview timed lyrics with みるみる, save/reopen, verify missing media, restore
  original lyrics, and export SRT/JSON without running Analyzer.
- [ ] Open exported SRT in Kachinco. Verify that the other implementation imports
  the published versioned timing JSON before claiming native JSON integration.
- [ ] Analyze a real music file without lyrics and a Vocal Stem. Confirm guides
  refresh; listen to estimated beats/chords and record known algorithm limitations.
- [ ] Modify the source after analysis; observe stale state and export rejection.
- [ ] Measure selected-input preparation/analysis time, peak RSS/temp disk and
  audition/seek latency on representative large inputs and mounted storage.
- [ ] Cancel while hashing, decoding and making preview WAV. Quit during work;
  verify joined processes and no success/partial result after cancel.
- [ ] Change the saved document externally and verify conflict preservation;
  cancel Save As/Close and verify the original editor remains available.

Studio/MCP binding, remote grants, automatic tool launch and cross-app editing
are later scopes and are not part of the local-runtime acceptance claim.


## Follow-up UX acceptance (#7–#9)

- [ ] Dedicated lyrics window: paste, TXT open/drop, UTF-8 BOM/Japanese/newlines.
- [ ] JSON/SRT open/drop preserves timings; editing the draft clearly resets them.
- [ ] Cancel/rejected input/replacement cancel protects current document and draft.
- [ ] S play/pause, Space hold/release, mouse PON; text input, repeat, focus loss.
- [ ] Centered button depression and equally readable current/NEXT lyrics.
- [ ] Splitter drag resizes waveform/table at minimum size, maximized and varied DPI.
- [ ] Ctrl/Shift multi-selection and all/selected adjustment, units, 1–20ms presets.
- [ ] Predecessor alignment/follow during move/trim/numeric/recording; batch Undo/Redo.
- [ ] Boundary errors preserve every row; pending rows/first row are handled clearly.

Synthetic UI smoke checks in CI supplement these physical pointer/audio checks.


## PONPON retry/follow acceptance (Issue #11)

- Paste TXT with `[verse1]`, `(whisper)`, `（間奏）`, ordinary lyrics and inline
  parentheses. Only annotation-only rows disappear. Repeat with timed JSON/SRT
  and an older `.fponproj`; original snapshot and remaining timing/IDs survive.
- With a long lyric list, record beyond the first screen. Current is around the
  second visible row; test first/last rows and a resized table. Audition and
  みるみる follow playback without replacing the edit selection. Check gaps/seek.
- Make an accidental PON, use the back button and Z, then resume with S. Check
  repeated Z, the last lyric, overwriting a timed lyric, predecessor following,
  focus loss, key repeat and Z typed into text/combobox inputs.
- Confirm/cancel full clear. Confirm keeps music/stem/text/IDs, removes all timing,
  stops at zero and selects the first row. One Ctrl+Z restores every cleared time.
  Compare this with restoring the original imported lyrics/timing.

Synthetic CI checks are not physical audio/key/device/DPI acceptance.
