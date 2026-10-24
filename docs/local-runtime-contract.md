# Local runtime and exchange contract v1

The first implementation is an offline Windows x64 WPF application. It introduces
no remote transport, Studio account binding, filesystem endpoint or MCP launcher.
Only paths explicitly chosen in native file dialogs or reopened local documents
are used. No project supplies an executable, interpreter arguments or shell code.

PONPON owns one immutable TimingProject and a bounded 200-snapshot command history.
Keyboard, waveform gestures and inspector edits use TimingSession. One gesture
commits once against the revision it started with. Pending lines have both times
null; timed ranges are half-open, nonnegative and shorter than 24 hours. IDs are
unique GUIDs. The original imported lyric snapshot is retained. Overlapping lyric
ranges are permitted. A playback source change retains the same zero origin;
the user is responsible for selecting aligned stems.

Native `.fponproj` files are strict schemaVersion 1 JSON, at most 16 MiB, with
up to 20,000 lines of 4,096 characters. Music/stem references are explicit local
absolute paths, never tool commands. Missing media does not prevent document edits
or SRT export. Saves use same-directory temporary publication, flush, a cooperating
writer lock and optimistic content hashes; conflicts retain the existing file.
No silent overwrite or original JSON/SRT rewrite occurs on import.

Portable lyric exchange uses format `flamoris.lyrics-timing`, schemaVersion 1,
timebase 10000000, documentId and lines containing id, text, startTicks, endTicks.
Ticks are invariant decimal strings; null means unrecorded. Source media paths
are excluded. Kachinco converts these integers through its existing native time
boundary. There is no dependency on a Kachinco assembly. Legacy PONPON JSON and
SRT adapters are supported. SRT uses nearest milliseconds (positive ties upward),
rejects collapsed ranges and omits pending lines with a visible count. It cannot
preserve IDs/styles. Original import files remain untouched.

The audio worker is a trusted packaged Python program launched with structured
arguments. It accepts only prepare/analyze modes and explicit selected input and
owned output paths. It uses FFprobe/FFmpeg and NumPy, without lyrics, semantics,
network, provider keys or model weights. Source cap: 8 GiB; duration cap: 2 hours;
one worker at a time; wall deadline: 30 minutes. Probe has a 30-second deadline.
Decode uses 11025 Hz mono blocks, not whole-file librosa arrays. Waveform/energy
output is reduced to 100 ms bins (at most 72,000); spectral working arrays are
bounded by the same duration limit. Preview WAV is a separate local 48 kHz stereo
16-bit file, at most about 1.4 GB for two hours. No preview waveform/cache is
editable timing authority. Cache output belongs to the active app session and is
deleted after players/workers close. Failed/cancelled jobs do not publish success.

Derived analysis uses format `flamoris.audio-features`, schemaVersion 1, explicit
seconds, source SHA256/length/mtime and algorithm/settings version. Waveform,
relative dynamics, BPM/beat and key/major-minor-triad chord outputs are estimates;
correlation is not probability. Stale-source detection invalidates displayed
results, and shared-storage paths are selectable like local files. Hash changes
invalidate results even when file size is unchanged. The supported size/duration
caps are admission limits, not evidence of measured performance at those limits.

Worker progress is bounded NDJSON without lyric/audio payloads or absolute input
paths. Results are bounded JSON files. The host kills and joins the process tree
on cancel/timeout before cleaning only its own work directory. Errors do not
automatically replay writes/jobs. Selected paths are never emitted to a remote
service; process arguments are local to the current OS user. Future remote access
must implement the scoped grant/reparse/concurrency/reconnect requirements in the
desktop plan and is not implied by this runtime.


## Lyrics entry and batch timing edits

Lyrics entry is a dedicated modal draft window; opening, dropping or canceling
does not mutate the session. Explicit confirmation replaces lyrics in one undoable
command after a replacement prompt. TXT uses strict UTF-8 (optional BOM) and
CRLF/LF/CR line breaks. File selection/drop share a bounded reader (16 MiB),
4,000,000-character input cap, 20,000-line and 4,096-character-per-line validation.
Only one selected fully qualified local TXT/JSON/SRT file is accepted; directories,
links/reparse files, UNC/URL inputs and multi-file drops are rejected. No recursive
scan or execution occurs. JSON/SRT retains timing until the draft text is edited.
The application reads only the selected file; it does not create a persistent grant.

Timing adjustments and predecessor alignment are pure snapshot transformations
validated before one TimingSession publication. An invalid row rejects the whole
batch. Untimed rows remain untimed; predecessor means the immediately preceding
document row. All-target alignment computes from one snapshot, independent of
iteration order. Start-changing gestures may include predecessor-end changes in
the same expected-revision edit; drag previews are transient and capture loss
discards them. Positive adjustment input uses integer ticks after decimal conversion;
unitless values and the 1–20 presets are milliseconds, explicit `s`/`秒` are seconds.


## PON retry and annotation handling

Complete annotation-only bracket/parenthesis lines are filtered from working
lyrics at import and project open, preserving the raw OriginalLines snapshot.
Inline groups are retained. Opening an old project with annotations marks the
working document modified; no file is rewritten until ordinary Save.
PON history entries attach the line ID and actual transport start to their
existing bounded snapshot. Z can undo only the top PON entry and restores all
changes in that command, including predecessor following; normal Undo/Redo keeps
the same metadata. A full timing reset is one validated ordinary command.
Lyric-list follow uses virtualized item scrolling and one preceding row. It
changes neither edit selection nor canonical timing nor the playback clock.


## Local recovery and failure diagnostics (#13)

After each completed document command, a synchronous bounded checkpoint of the
last validated project is flushed and atomically replaced under
`%LOCALAPPDATA%/FLAMORIS/StudioClient/recovery`. This is separate from ordinary Save
and never uses the source project's path. No partial drag preview is checkpointed.
Each running instance has its own GUID checkpoint and exclusive live marker;
startup lists at most 20 inactive candidates from at most 128 filenames, without
recursion. Files are at most 16 MiB, schema-validated, and reparse/link entries are
rejected. A denied/unwritable recovery location visibly disables this protection
without preventing normal editing or explicit Save. Very large documents or slow
local storage can make synchronous checkpoint publication perceptible.

The startup prompt restores an inactive candidate only on Yes, opens it as an
unsaved document and retains source media references. A new checkpoint must be
published before removing its predecessor. No leaves old candidates available;
explicit save, normal close/discard and replacement with a clean document remove
this instance's checkpoint. Other running instances are not recovery candidates.
Dispose alone preserves a checkpoint, modeling abrupt process closure. This is
not a guarantee against disk failure, power loss or an OS/native crash before the
last command completed. Audio caches and ongoing worker jobs are not recovered.

`Flamoris.Logging` 1.0.0 provides rotating local diagnostics in
`%LOCALAPPDATA%/FLAMORIS/StudioClient/logs/studioclient.log` (2 MiB, three files).
Only controlled operation codes, exception types/HResults, method frames and
assembly version are recorded. Raw exception messages, lyric text, media content,
source paths and credentials are excluded. No remote telemetry is added.
Unhandled UI/process errors are logged; unknown dispatcher errors are not globally
swallowed to keep running. Recovery uses a previously validated checkpoint.

Stretch/move previews are bounded before numeric conversion, validated including
predecessor changes, and published only when valid. Rejection or capture loss
cancels the entire gesture without a later release committing a stale draft.
A later physical report supplied an unhandled ArgumentException in WPF selector
storage while MainWindow.SelectGrid clears SelectedItems. LyricRow was a record
whose synthesized value hash included mutable highlight state and the
PropertyChanged event delegate. Highlight/binding changes invalidated WPF's item
hash keys. LyricRow is now a reference-identity view class: presentation changes
and event subscription never change equality/hash identity. No project/schema or
timing precision changes are needed. Boundary hardening remains complementary.
The linked CLR tests compile the exact WPF-independent view class; Windows UI
smoke also exercises the actual DataGrid/SelectGrid selection path.
