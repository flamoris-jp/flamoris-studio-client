# FLAMORIS Studio Client

Local bridge for FLAMORIS Studio, connecting desktop files, media, and production tools to the Studio.

**Status: planning/foundation only. No runtime, transport, authentication, discovery protocol, or tool surface is implemented yet.**

FLAMORIS Studio Client is the planned machine-local bridge between FLAMORIS Studio and large projects, media files, and production tools that should remain on a user's workstation.

## Why this exists

FLAMORIS projects can contain large video, audio, image, and project files.

Keeping those files permanently on the Studio server would create unnecessary storage, transfer, and authority problems.

Studio Client therefore keeps machine-local resources local and exposes only explicit, bounded capabilities to Studio.

```text
FLAMORIS Studio
      |
      +-- Studio Client
              |
              +-- local projects
              +-- large media
              +-- production tools
              +-- bounded upload/download bridge
```

## Planned responsibilities

The client may eventually provide:

- local project discovery
- local file read/write through explicit contracts
- large media access
- local tool launch
- upload/download bridging
- Studio session association
- connection to machine-local production tools

The exact protocol and implementation are not defined yet.

## Authority boundary

Studio Client is a bridge, not a second owner of application state.

FLAMORIS products such as Cutwork, Kachinco, and FLAMORIS 2D remain authoritative for their own project/document state and editing behavior.

The client must not silently copy entire projects into a competing state model.

## Asset references

Studio-facing integration should prefer opaque asset references over exposing arbitrary local paths.

```text
AssetRef != FilePath
```

A future Studio request might refer to an asset owned by a specific client session or machine. The client resolves that reference locally and performs only the explicitly authorized operation.

Large files should be transferred only when needed. Metadata, previews, ranges, or references should be preferred when the complete payload is unnecessary.

## Security direction

This component will sit close to local files and desktop tools, so security is part of the architecture.

Future implementations must:

- use least-privilege access;
- avoid arbitrary filesystem exposure;
- bind access to an explicit Studio/client session;
- validate all remote input before treating it as a path, command, URL, or tool argument;
- never log credentials, tokens, private keys, or sensitive file contents by default;
- make local read/write/tool-launch permissions explicit and revocable;
- bound transfer sizes, concurrency, timeouts, and resource use.

## Current status

Repository foundation only.

Runtime, transport, authentication, discovery protocol, and tool surface are intentionally not claimed as implemented yet.

The first implementation phase is planned after the new `flamoris-studio` AI Prompt Console is established.

## Related repositories

- [FLAMORIS Studio](https://github.com/flamoris-jp/flamoris-studio) — creative control center
- [FLAMORIS Commons](https://github.com/flamoris-jp/flamoris-commons) — shared foundations and repository policy
- [FLAMORIS MCP Core](https://github.com/flamoris-jp/flamoris-mcp-core) — shared .NET MCP infrastructure where applicable
- [FLAMORIS Cutwork](https://github.com/flamoris-jp/flamoris-cutwork) — image decomposition and repair
- [FLAMORIS Kachinco](https://github.com/flamoris-jp/flamoris-kachinco) — AI-native video editing and compositing
- [FLAMORIS 2D](https://github.com/flamoris-jp/flamoris-2D) — 2D animation and character authoring

## Philosophy

Use it however you like.

Commercial use is welcome and does not require permission.

FLAMORIS software is provided as-is and does not include guaranteed individual support. AI-assisted self-support is encouraged.

If FLAMORIS helps you or you find it interesting, your support helps fund development and keeps the project growing. 🌱  
<sub>Mostly GPU bills.</sub>

## License

Code and documentation in this repository are licensed under the [Apache License 2.0](LICENSE), unless otherwise noted.

Third-party software, AI models, media, project files, and other non-code assets may use separate licenses and terms.

---

## 日本語

FLAMORIS Studio Clientは、FLAMORIS StudioとローカルPC上のproject / media / production toolsをつなぐためのローカルブリッジです。

大容量ファイルをStudioサーバーへ恒久保存せず、必要なものだけを明示的な契約で扱うことを目的にします。

Studioへ任意のローカルパスをそのまま公開するのではなく、可能な限りasset referenceを使います。

また、Cutwork / Kachinco / FLAMORIS 2Dなど各アプリ自身が持つauthorityを奪わず、Studio Clientは橋渡しに徹します。

現在はリポジトリ基盤のみです。実装済みでないtransport、認証、filesystem APIなどをREADME上で先取りして約束しません。

勝手に使ってください。  
改造しても、組み込んでも、面白いものや変なものを作ってもOKです。
