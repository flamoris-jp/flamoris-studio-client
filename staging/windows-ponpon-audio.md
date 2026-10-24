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
