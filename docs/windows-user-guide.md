# PONPON / AudioAnalyzer — Windows guide

This is the first local StudioClient implementation candidate. Studio login,
remote file access, MCP and automatic Kachinco launching are not implemented.
The local tools work without a network connection once dependencies are installed.

## Build and first start

Build prerequisites: Windows x64, .NET 10 SDK and PowerShell. Runtime prerequisites:
Python 3.11+ and FFmpeg/FFprobe on PATH. The portable .NET package is self-contained;
the destination does not need the .NET SDK. NumPy is the only Python dependency.

If you already have the portable ZIP, extract it and run `./setup-audio.ps1`,
then `./StudioClient.exe` in that folder. To build from a checkout:

```powershell
./product/packaging/publish-windows.ps1
cd ./artifacts/windows/FLAMORIS-StudioClient-win-x64
./setup-audio.ps1
./StudioClient.exe
```

`setup-audio.ps1` installs NumPy in `audio-env` next to the application. This setup
step needs package download access; ordinary use does not. An existing Python
environment with NumPy can be selected through `FLAMORIS_STUDIO_CLIENT_PYTHON`.
That setting is local OS configuration, never loaded from a PONPON document.
Close the running application before publishing an update into the same folder.
The ZIP contains only the fresh build, not documents or an audio environment
stored beside a previous executable.

## Record and adjust lyrics

1. Choose **音源を開く**. A local waveform and PCM audition file are prepared. No
   Analyzer run or manual project folder is required.
2. Choose **歌詞を読み込む／貼り付ける…**. In the dedicated window, paste one lyric per line,
   open a TXT/JSON/SRT file, or drop a single file into the white box. TXT is UTF-8
   (with or without BOM). Confirm with **この歌詞で始める** or **タイミングを保持して読み込む**.
   Closing/canceling the window leaves the document untouched; replacing existing
   lyrics asks for confirmation and can be undone. Editing imported JSON/SRT text
   turns it into new untimed lyrics; the window explains this before confirmation.
3. Play with the transport or **S** (outside text inputs). Hold **PON!** or **Space**
   for a line, then release. Recording uses the audio transport's position; the
   UI timer only redraws it. Releasing advances the selected lyric to the next row.
4. In **ぐにぐに**, drag the middle of a box to move it; drag either end to trim.
   Drag the divider above the lyric table vertically to resize the timeline/table.
   The PON button is centered, depresses while held, and NEXT uses the same font
   size as the current lyric. Use −/＋ and the horizontal scrollbar to navigate. The lower gold curve shows
   RMS energy. Overlapping lyrics occupy three visual lanes.
5. Double-click a lyric row, or press **選択行から再生**, to audition from its start.
   Start/end seconds and text can also be entered in the inspector. Edits commit
   once per gesture with Undo/Redo.
6. Optionally select an aligned Vocal Stem and switch Music/Vocal Stem. Both start
   at zero; choosing an unaligned stem does not automatically retime the lyrics.
   Switching playback source aborts an unfinished hold. Losing window/mouse focus
   also aborts it without committing a stuck/partial range.
7. **みるみる** previews the lyric at the current playback position. Starting PON
   turns this view off so the row being recorded remains explicit.
8. Distribution lyrics: use **ファイル → 配信用歌詞を整える／書き出す…** (also available in the lyrics import dialog). Review the editable cleaned text and warnings, then copy or save UTF-8 TXT. No audio/timing is required. See [配信用歌詞の操作と制限](distribution-lyrics.md).
9. Save `.fponproj` and export SRT, portable timing JSON or legacy PONPON JSON from
   the File menu. No analysis is required. The original imported file is preserved.

### Tag lines, automatic scrolling and restarting

Whole lines consisting only of `[verse1]`, `[Chorus]`, `(whisper)`, `（間奏）`
or similar complete bracket groups are excluded from PONPON, its timeline and
exports. Inline parentheses such as `歌詞（echo）` remain lyrics. This applies to
paste/TXT/JSON/SRT and reopening older projects. The original import snapshot
(including annotations and original timing) stays in `.fponproj`; source files
are never rewritten. Restoring the import also applies the same filter.

The lyric table follows the selected line during recording and the playback line
during audition/みるみる, aiming to put it on the second visible row. At the start
or end the scrollbar clamps naturally. Current rows are highlighted; playback
follow does not replace an edit selection or change the audio clock. A manual
scroll remains until the current line changes.

**↶ 戻る (Z)** cancels the most recent completed PON, selects that line, seeks to
its recorded start and pauses. Press S to play and record it again. Repeated Z
walks back consecutive PONs, including the final row; an overwritten range and
any predecessor-end follow are restored together. It stops at a non-PON edit;
use normal **Ctrl+Z / Ctrl+Y** for general history. Z does not intercept typing
in inputs, repeat key events or other workspaces. History is session-local.

**全クリア…** (also **編集 → タイミングを全クリア…**) asks for confirmation, clears
only the timing, pauses at zero and selects the first lyric. Lyrics, IDs, music,
stem and the original snapshot remain. One Ctrl+Z restores the entire timing
reset. This differs from **読み込み時の歌詞に戻す**, which restores imported text
and imported timing.

### Batch adjustment and 「ついてく」

Use Ctrl/Shift in the lyric table to select multiple lines; clicking a timeline
box selects one line. **対象：選択行／全行** controls the adjustment buttons. Choose
**1–20ms** or type a positive amount (e.g. `10ms`, `0.010秒` or `0.010s`).
Numbers without a suffix are **milliseconds**. **前へ／後ろへ** shifts both ends
by the same amount. Pending rows stay pending. A batch is one Undo/Redo step.
An invalid range rejects the entire batch without clamping individual rows.

**今そろえる** sets the immediately preceding lyric's end to each target lyric's
start. Example: previous `2.000–3.000`, selected `3.500–4.000` → previous
`2.000–3.500`. Enable **前行の終了がついてく** to keep the predecessor end aligned
when a start is changed by recording, dragging, inspector or shifting.
Checking the box alone does not edit the document; use **今そろえる** for initial
alignment. The same batch/history entry includes both changes. End-only trims
do not change the predecessor. First/pending rows are skipped without jumping
over them; zero/negative duration fails atomically. For all-line shifts with
follow enabled, aligning adjacent ends can also close existing gaps/overlaps.

Unrecorded lines are omitted from SRT only after a visible confirmation. Sub-ms
ranges that collapse at SRT precision fail explicitly. `.fponproj` retains precise
ticks, original imported lyrics, paths and IDs; the portable timing JSON excludes
machine paths. Relative media relocation is not automatic: use the file picker to
select replacement audio. Missing media does not prevent lyric edits or export.

## Recovery after an unexpected exit

Completed edits are also saved to a local recovery checkpoint. If StudioClient
ends unexpectedly, restart it and choose **はい** in **PONPONの復旧**. The recovered
project is unsaved: choose a destination with normal Save. The original project
is not overwritten automatically. Music/stem references remain; press Play to
prepare the selected source again. This does not recover an unfinished drag.

Normal Save or a confirmed normal close/discard clears this session's checkpoint.
If the status bar says **復旧保存なし**, use Ctrl+S: the recovery location is not
writable or the document exceeds its bound. Recovery cannot protect edits that
never completed, disk failure, or deleted checkpoints.

If a stretch is rejected (for example, predecessor following would make a line
zero-length), the gesture is canceled and the last committed timing remains.
For another unexpected exit, retain the log in
`%LOCALAPPDATA%/FLAMORIS/StudioClient/logs/studioclient.log` and note whether the
left/right edge was moved and whether **前行の終了がついてく** was enabled.
The log contains exception types and method frames, without raw lyrics or media
paths. Recovery files contain the local project; do not confuse them with logs.

## Analyze music without lyrics

Open the **AudioAnalyzer** workspace and choose a music file, or analyze the Music
or Vocal Stem already selected in PONPON. The waveform, relative dynamics, estimated
BPM/beats, key and major/minor triad chords are displayed. Analysis of a selected
PONPON source refreshes its waveform/beat guide without reopening the document.

The Analyzer does not need PONPON, song.json, semantic data, provider credentials or
model downloads. It is a signal-processing estimate, not automatic transcription
or definitive chord identification. Silence can legitimately have no key/BPM.

Export results as `audio.features.json`. Results are bound to source SHA256; an
audio modification marks them stale and exporting rechecks the content. No complete
audio is uploaded. A later session can reopen the audio and analyze again; there
is no persistent analysis-job database or analysis-result import UI in this slice.

## Limits and cancellation

Admission limits: one job, 8 GiB input, two hours, 30-minute wall deadline. These
are guardrails, not a performance guarantee. Actual wave/dynamic data is reduced
to 100 ms bins; the source is decoded in bounded chunks. Long inputs may need
roughly 1.4 GB of temporary disk for two hours of stereo PCM audition audio.
Completed analysis is separate from that audition cache.

Cancel joins the worker process tree before cleaning its unpublished output.
Close the application normally to remove its session cache. An OS crash can leave
an abandoned cache under the OS temporary directory; it contains derived audio
and is never scanned, restored or sent to Studio automatically.

If audio preparation fails, check Python/NumPy, FFmpeg/FFprobe, supported media,
free temporary disk space and sound-device availability. An error does not upload
the source or retry an ambiguous save. A save conflict asks for a separate name.

## Developer checks

```powershell
python -m pip install -r product/audio-worker/requirements.txt
python -m unittest discover -s test/audio -v
dotnet test test/StudioClient.Tests/StudioClient.Tests.csproj -c Release
dotnet build StudioClient.slnx -c Release
```

Windows CI publishes a portable ZIP, startup/dialog/workspace renders and a
synthetic UI smoke report (keyboard focus, selection, splitter and file-drop paths). Startup/build/synthetic
tests do not replace actual sound, drag/DPI, timing and large-file acceptance.
See [Windows acceptance](https://github.com/flamoris-jp/flamoris-studio-client/blob/main/staging/windows-ponpon-audio.md) and
[local file/exchange contract](https://github.com/flamoris-jp/flamoris-studio-client/blob/main/docs/local-runtime-contract.md).
