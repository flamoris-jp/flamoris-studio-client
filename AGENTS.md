# AGENTS.md

## Scope

These instructions apply to the entire repository.

This repository contains FLAMORIS Studio Client. The owner-selected 2026-10-24 direction expands the original machine-local bridge plan with native Windows PONPON lyric-timing and independent AudioAnalyzer modules. Read `docs/desktop-lyrics-and-audio.md` before implementation. KineticTypo belongs to Kachinco as a separate C# UI / C++ GPU executable.

**Current status:** local PONPON / ぐにぐに and audio-only Analyzer implementation candidate. `docs/local-runtime-contract.md` defines the local document/worker boundary; `docs/windows-user-guide.md` explains operation. No Studio transport, authentication, filesystem endpoint, MCP tool surface, discovery or remote launcher is implemented. Build/synthetic checks and physical Windows audio/UI acceptance remain distinct.

## Core authority

Studio Client may eventually own:

- explicit Studio/client session state;
- local discovery metadata;
- bounded local file/media transfer operations;
- local tool-launch bridging;
- local resolution of opaque asset references;
- machine-local connection adapters.

Studio Client must not become a competing authority for:

- Cutwork/Kachinco/FLAMORIS 2D project or document state;
- Studio's user-facing product state;
- generation workflows/jobs/assets owned by `flamoris-generation-mcp`;
- GPU runtime state owned by `flamoris-lime-manager`.

The bridge remains a bridge, not a hidden project database. PONPON owns its local lyric document/history; AudioAnalyzer owns derived results bound to source identity. These modules do not compete with another product's document/session. Local tools work offline; analysis has no mandatory PONPON, lyrics or semantic phase. The local slice is implemented as a candidate; remote bridging remains planned.

## Architecture principles

1. **Asset references are not arbitrary paths**
   - Prefer opaque scoped references over remotely supplied filesystem paths.
   - Do not expose broad filesystem traversal APIs.
   - Resolve references locally under explicit authorization.

2. **Least privilege**
   - Access only the files, projects, directories, or tools required for the current operation/session.
   - Any future grants should be explicit, inspectable, and revocable.

3. **Keep large files local by default**
   - Do not mirror complete projects to the Studio server as a default behavior.
   - Transfer full content only when required.
   - Prefer metadata, previews, ranges, checksums, or references where sufficient.

4. **Preserve product authority**
   - Production applications remain authoritative for their own documents and editing semantics.
   - Integrate through explicit APIs/MCP/contracts rather than mutating application files behind their backs.

5. **Remote input is untrusted**
   - Validate references, names, URLs, commands, tool arguments, and metadata before local use.
   - Prevent path traversal, symlink escapes, arbitrary command execution, and confused-deputy access.

6. **Bound everything expensive**
   - Bound file sizes, transfer sizes, concurrent operations, timeouts, process launches, retries, and resource use.
   - Do not invisibly retry non-idempotent local writes or tool actions after ambiguous failure.

7. **No private topology in public contracts**
   - Do not hard-code developer machine names, private hostnames, drive layouts, tunnel IDs, credentials, or personal paths.

8. **Reuse stable shared foundations**
   - Reuse FLAMORIS MCP/logging/security foundations when their boundaries fit.
   - Do not vendor/copy shared package source for convenience.

## Security-sensitive operations

Local read/write and tool launch are privileged capabilities.

Before implementing them, the governing Issue/design must define:

- authorization/session scope;
- allowed roots or resource grants;
- path/reference validation;
- symlink/reparse-point behavior where relevant;
- overwrite semantics;
- size and concurrency limits;
- cancellation behavior;
- audit/logging behavior without leaking content/secrets;
- failure and reconnect semantics.

Do not introduce a generic "run arbitrary command" or "read arbitrary path" endpoint.

## Development workflow

Before substantial changes:

- read README.md and this file;
- read CONTRIBUTING.md and SECURITY.md;
- read the relevant Issue/design document;
- inspect Studio and relevant product authority boundaries;
- inspect shared packages before duplicating infrastructure;
- keep scope limited to the current phase.

Keep changes reviewable and commit meaningful units frequently.

## Testing and CI

Normal CI must not require:

- access to a developer's real home directory or production files;
- a private workstation;
- a live Studio deployment;
- private tunnels or credentials;
- large media collections.

Use temporary directories, synthetic media, fake tools/processes, and mock transports.

Security-sensitive path/reference logic requires rejection-path tests.

## Privacy and logging

Never commit, log, or return credentials, tokens, private keys, authentication cookies, sensitive file contents, or unnecessary absolute local paths.

Treat file names, project metadata, media, prompts, and local tool state as potentially private.

## Licensing

Unless stated otherwise, code and documentation are licensed under Apache License 2.0.

Do not add third-party code, AI models, model weights, datasets, fonts, media, generated assets, or other non-code material unless licenses and redistribution terms are compatible and clearly documented.

## Support

FLAMORIS does not provide guaranteed individual support.

Repository documentation, Issues, tests, logs, and source code are the primary references. AI-assisted self-support is encouraged.

## Audio/video storage policy

Do not commit audio or video files (including MP3, MP4, WAV, FLAC, M4A, OGG, MOV, WebM and equivalents), regardless of size, ownership, license, or whether they are generated demos or test samples. This also prohibits Git LFS pointers, renamed/encoded media and media hidden inside archives. Do not bypass the rule with `git add -f` or ignore exceptions.

Store media outside Git in approved artifact/media storage. Commit references, SHA-256 digests, provenance, analysis results, scripts and small text/symbolic fixtures instead. Generate temporary synthetic media during tests when needed. Licensing review does not waive this storage policy. MIDI/project data and images retain their existing repository-specific rules.

The `.gitignore` covers common audio/video extensions case-insensitively. Where installed, the `Media policy / No tracked audio or video` PR check also detects force-added files and LFS pointers by filename; it is not a content/archives scanner or a server-side push restriction. Do not rewrite history or remove external assets as part of this rule. See the [shared policy](https://github.com/flamoris-jp/flamoris-commons/blob/main/docs/repository-policy.md#121-audio-and-video-storage).
