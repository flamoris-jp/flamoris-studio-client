# FLAMORIS Studio Client

**Windows lyric timing and local music analysis — PONPON / ぐにぐに / AudioAnalyzer.**

音源を開き、歌詞を貼ってポンポン。ぐにぐにで微調整し、字幕を出力できます。
AudioAnalyzerは歌詞なしで音楽の波形・強弱・BPM・拍・推定キー／コードを調べます。
大きな音源はローカルに置いたまま扱います。

**Status: first local runtime implementation candidate.** Headless contracts,
synthetic audio and Windows build/startup are checked separately from real
Windows audio/UI acceptance. See [PROGRESS.md](PROGRESS.md). No Studio login,
network transport, discovery, remote filesystem API, MCP tools or tool launcher
are implemented in this slice.

## Local workspaces

| Workspace | Implemented scope |
| --- | --- |
| **PONPON** | Open music, paste lyrics and hold/release to record line times. Native Windows controls; no manual song directory or song.json. |
| **ぐにぐに / いじいじ** | Waveform and energy, drag move/trim, zoom/scroll, selected-line audition, numeric/text inspector, Undo/Redo. Optional aligned Vocal Stem. |
| **みるみる** | Preview the lyric at the playback position. |
| **Persistence / exchange** | Precise local `.fponproj`, original import snapshot, optimistic atomic save, SRT, legacy PONPON JSON and portable versioned timing JSON. |
| **AudioAnalyzer** | Independent audio-only worker; waveform, relative RMS dynamics, estimated BPM/beats, key and major/minor-triad chords. Cancel, source fingerprint/stale checks and JSON export. |

The UI is C# / WPF (.NET 10); a bounded Python/NumPy + FFmpeg worker processes
audio in chunks. Playback uses the prepared local PCM file and the media
transport's position. The UI timer redraws, rather than advancing a second clock.
No semantic interpretation, paid API, lyric prerequisite or model weight is used.

## Start on Windows

Build prerequisites: .NET 10 SDK and PowerShell. Audio prerequisites: Python 3.11+
and FFmpeg/FFprobe on PATH. From a checkout:

```powershell
./product/packaging/publish-windows.ps1
cd ./artifacts/windows/FLAMORIS-StudioClient-win-x64
./setup-audio.ps1
./StudioClient.exe
```

The portable .NET app is self-contained. `setup-audio.ps1` creates a local Python
environment and installs NumPy; it needs download access once. Ordinary editing
and analysis work offline. An existing environment can be selected with
`FLAMORIS_STUDIO_CLIENT_PYTHON`.

[Windows user guide](docs/windows-user-guide.md) ·
[physical acceptance](staging/windows-ponpon-audio.md) ·
[local schema, process and storage contracts](docs/local-runtime-contract.md)

## Boundaries and limits

PONPON owns its lyric document and history. AudioAnalyzer owns derived results
bound to source identity. The future bridge routes explicit references and will
not become a second owner of Studio, Kachinco, Cutwork or 2D documents.

Local file dialogs select the resources used by this app. Portable timing JSON
excludes local media paths; native projects retain explicitly selected paths.
Analysis/export verifies the source content hash. Original imports are not
rewritten; conflicts preserve the saved document.

Admission limits are one worker, 8 GiB input, two hours and a 30-minute deadline.
Waveform/energy output is reduced to 100 ms bins; a two-hour audition WAV can
need about 1.4 GB of temporary disk. These are bounds, not performance guarantees.
Key/chord/BPM outputs are estimates, not definitive musical annotations. Chords
currently cover major/minor triads; there is no automatic lyric transcription.
The current result can be exported, but there is no persistent analysis-job store
or saved-analysis import UI. Actual pointer/DPI, sound, latency and representative
large-file performance require Windows physical acceptance.

## Repository allocation and next work

The 2026-10-24 owner decision expands the original bridge-only repository plan.
PONPON/Analyzer live here. **KineticTypo / キネタイ** lives in
[Kachinco](https://github.com/flamoris-jp/flamoris-kachinco) as a separate C# UI /
C++ GPU executable. This app exports timing JSON/SRT; native KineticTypo JSON
import remains the consuming product's implementation/acceptance responsibility.

[Desktop migration plan](docs/desktop-lyrics-and-audio.md) ·
[implementation Issue #4](https://github.com/flamoris-jp/flamoris-studio-client/issues/4)

Later Studio/MCP integration must define explicit account/session ownership,
opaque asset references, allowed operations/roots, resource limits, revocation,
reparse/path rejection and reconnect without replay. No generic remote path or
command interface is exposed by the local workspaces.

## Development

```powershell
python -m pip install -r product/audio-worker/requirements.txt
python -m unittest discover -s test/audio -v
dotnet test test/StudioClient.Tests/StudioClient.Tests.csproj -c Release
dotnet build StudioClient.slnx -c Release
```

Normal tests use synthetic media and temporary files, without production assets,
private hosts or provider credentials. Windows CI also packages the app and
captures its startup view. An automated startup is not physical audio/UI QA.

## Related products

- [Studio](https://github.com/flamoris-jp/flamoris-studio) — creative control center
- [Kachinco](https://github.com/flamoris-jp/flamoris-kachinco) — video editing / KineticTypo
- [2D](https://github.com/flamoris-jp/flamoris-2D) — animation / character authoring
- [Cutwork](https://github.com/flamoris-jp/flamoris-cutwork) — image preparation
- [Commons](https://github.com/flamoris-jp/flamoris-commons) — ecosystem boundaries

## License and support

Code and documentation are Apache-2.0 unless stated otherwise. Commercial use is
welcome. Third-party software, media, fonts, models and other assets have their
own terms. This implementation uses .NET/WPF, Python, NumPy and an externally
installed FFmpeg; no model weights, fonts or production media are bundled.

FLAMORIS is provided as-is without guaranteed individual support. Source, Issues,
tests and diagnostics support AI-assisted self-service. 勝手に使ってください。
