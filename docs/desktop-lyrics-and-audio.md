# Desktop PONPON and AudioAnalyzer

Date: 2026-10-24 (JST)

Status: Local PONPON/AudioAnalyzer implementation candidate. See
[implemented local contracts](local-runtime-contract.md), [user guide](windows-user-guide.md)
and [physical acceptance](../staging/windows-ponpon-audio.md). The later Studio/MCP
bridge is not implemented. The sections below retain the product direction;
algorithm, packaging and limitations are authoritative in the local contract.
The owner selected this repository for the following local production modules;
the scope expands the original bridge-only plan without transferring ownership
of Kachinco, 2D or Studio documents to the bridge.

Implementation tracking: [#4](https://github.com/flamoris-jp/flamoris-studio-client/issues/4).

## Product allocation

| Component | Owner | Initial experience |
| --- | --- | --- |
| PONPON | StudioClient local lyric module | Open audio, paste lyrics, record line timing immediately |
| ぐにぐに / いじいじ / みるみる | PONPON | Adjust timing, audition from a selected lyric, preview and export |
| AudioAnalyzer | StudioClient local analysis module | Open an audio file and analyze it without lyrics or PONPON |
| KineticTypo / キネタイ | Kachinco repository, separate EXE | C# UI, native C++ GPU text/effects editing |
| Studio bridge | StudioClient | Explicit authorized connection, asset references and bounded handoff |

Windows x64, Japanese C# / WPF UI and .NET 10 are the initial implementation
defaults, aligned with the existing desktop family. A single StudioClient shell
can host PONPON and Analyzer workspaces. Keep their domains separate so either
works independently and the bridge does not own an extra copy of a document.
Offline local creation/editing/analysis must work without a Studio connection.

## PONPON workflow

Open an audio file (and optionally a Vocal Stem), paste multiline lyrics or import
existing PONPON JSON/SRT, then start recording. Users do not manually prepare a
song directory or song.json. Create explicit local document state automatically
and ask for a save destination through the ordinary Save workflow.

Press/release records a line's range. Ignore auto-repeat and text-entry key events;
focus loss cannot leave a line indefinitely held. Derive times from the audio
transport/sample position, not UI repaint or keyboard-event wall clocks. Allow
retry and Undo/Redo. Music and stem must share an explicit origin; do not silently
shift lyrics when switching playback sources.

ぐにぐに provides a native waveform/timing surface, drag move/trim, zoom, scroll,
selection and playback starting at the selected lyric. One drag commits one
command; pixels are a transient projection of time. Original data remains
recoverable. Save/reopen preserves IDs and edits; SRT export derives from the
canonical timing document with explicit millisecond rounding/loss diagnostics.

Timing edits and SRT export work before Analyzer runs. Vocal guides, if present,
are optional overlays and refresh after a successful analysis without restarting
the app. Derived waveform/guide caches never become timing authority. Preserve
the source project and old export variants during migration.

## Independent audio analysis

Required input is the selected music file. Analyze waveform, relative energy/
peaks, BPM/beat positions, estimated key/scale and estimated chords. Vocal/stem
analysis is optional. Exclude semantic interpretation and paid API dependencies
from the initial workflow.

Reuse the supplied legacy Python audio algorithms behind a bounded local worker
where practical; do not force a C# rewrite for symmetry. The legacy CLI currently
detects lyric format before its audio phase, so it is not an audio-only entry
point. Introduce a separate audio job with no lyric imports, song-profile
requirement, PONPON setup or semantic execution. Inspect/reconcile reusable
shared packages and source licensing before importing code.

Current chord estimation is based on major/minor triad templates. Label results
as estimates, document limitations and allow later correction/versioning. Do not
present correlation scores as calibrated probabilities or infer semantic verse/
chorus labels from energy alone. Preserve source identity and analyzer/settings
versions so results become stale visibly when audio changes.

Large files stay at the selected local or explicitly mounted storage location.
Avoid whole-file server uploads and unbounded full-resolution waveform arrays.
Use reduced waveform caches, bounded analysis working sets, progress/cancellation
and atomic result publication; partial results cannot appear as successful jobs.
Prototype existing librosa working sets before selecting a supported file-size
limit. Select a measured limit in the implementation Issue before shipping.

## Document and interoperability boundaries

PONPON owns its timing document and command history. Analyzer owns derived results
bound to source identity, not a second editable lyric project. The bridge routes
explicit references; Studio owns orchestration/account state, and Kachinco owns
its native project/session. No bridge service directly rewrites another product's
open document. Avoid a general cross-product project database.

The initial interchange is versioned lyric timing JSON plus existing PONPON JSON
and SRT adapters; audio features remain optional separately versioned data. The
wire contract must explicitly define text IDs, source origin, units/timebase,
range rounding and unknown-version behavior before implementation. Kachinco
imports convert through its native TimelineTime; do not duplicate its evaluator
inside StudioClient. Save canonical numeric precision; never make display-rounded
timestamps authoritative. Sending an asset ID is not an authorization grant.

Studio integration is a later explicit slice, not a prerequisite for local tools.
It reuses compatible shared connection/logging foundations after inspection and
requires account/session ownership checks. No automatic full-project sync. Local
and user-selected shared storage are supported choices; production hostnames and
paths are configuration, never public defaults.

## Local operations and future remote boundary

Local operations begin with a user file picker or a selected document. Future
remote requests use opaque IDs scoped to the bound account/session and explicit,
revocable resource grants. No generic remote read-path or run-command API.

Before remote file/launcher implementation, its contract must specify:

- Allowed roots/files and exact read/write/launch capabilities; unknown/revoked
  IDs fail closed. Symlinks/reparse points cannot escape a grant.
- Maximum request/metadata/file/chunk sizes, job duration and concurrency, bounded
  retries and cancellation. Initial UI runs at most one analysis job at a time.
- Save/overwrite semantics: explicit save destination, atomic publication and
  expected revision/fingerprint; preserve the old file on failure/conflict.
- Tool launch by installed known tool identity, with structured arguments and
  bounded lifetime. Never run project-supplied command strings.
- Cancellation joins workers and cleans only owned temporary outputs. Reconnect
  does not replay ambiguous writes, launches or analysis submissions.
- Logs record job IDs, versions, status and backend/diagnostic codes; do not emit
  lyric contents, audio, credentials or unnecessary absolute paths.

These are implementation acceptance requirements, not a claim that a transport,
authentication model, discovery service or remote tool surface already exists.

## Delivery and acceptance

1. Local PONPON vertical slice: open audio, paste lyrics, record, save/reopen,
   native ぐにぐに adjustment, selected-line playback, Undo/Redo and SRT export.
2. Audio-only Analyzer worker: no lyric files, no semantic dependency; import
   arbitrary selected supported audio, display measured outputs, cancel and
   detect stale results. Optional stem guides integrate into existing PONPON.
3. KineticTypo/Kachinco interchange: transfer lyric data and optional audio
   features, preserving source timing/IDs and explicit import/save behavior.
4. Matched Studio/MCP bridge: explicit bindings, scopes and implemented producer
   contracts; document server/Hub dependencies only when these tools exist.

Tests use synthetic audio and temporary files. Cover timing commands/history,
serialization, PONPON/SRT precision, no-lyrics analysis, stale inputs and failed
publication/cancel cleanup. Remote slices require rejection-path tests for
scope/revision/path/reparse/size limits and reconnect without replay. Physical
Windows acceptance covers keyboard holds, focus loss, drag/DPI, audition latency,
real large-file working sets and waveform/stem timing.

## Planning change and legacy sources

This replaces the Web-first implementation destination in
[Studio #96](https://github.com/flamoris-jp/flamoris-studio/issues/96).
Its useful behavior requirements (Vocal Stem audition, selected-line playback,
timing persistence and portable storage) are retained. A Web editor and a
StudioClient-free workflow are no longer requirements of this desktop slice.

The supplied legacy Studio source contains Web PONPON/timing UI, Python analysis
and Python/ASS typography engines. It is migration reference material, not a
claim of an installed native client. KineticTypo's native GPU renderer is planned
in [Kachinco #109 / ADR 0027](https://github.com/flamoris-jp/flamoris-kachinco/issues/109).
