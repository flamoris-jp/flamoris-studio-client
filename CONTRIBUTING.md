# Contributing to FLAMORIS Studio Client

Thank you for your interest in FLAMORIS Studio Client.

This repository sits close to local files and desktop tools. Security, privacy, and authority boundaries are therefore part of every architectural decision.

## Before contributing

For small documentation or isolated fixes, feel free to open a pull request directly.

For filesystem access, asset-reference design, upload/download behavior, session/authentication, local tool launch, discovery, or production-app integration, open an Issue first.

Read `README.md`, `AGENTS.md`, `SECURITY.md`, and the governing design/Issue before implementation.

## Pull requests

Please:

- keep changes focused and reviewable;
- include rejection-path/security tests for local-resource access;
- avoid arbitrary path or command surfaces;
- document privilege, limits, and failure behavior;
- avoid personal paths, private topology, credentials, and production data in code/tests;
- use temporary/synthetic test resources;
- commit meaningful units frequently.

AI-assisted contributions are welcome. Contributors remain responsible for reviewing, testing, licensing, privacy, and security.

## Licensing

Unless explicitly stated otherwise, code contributions are submitted under Apache License 2.0.

Third-party code and non-code assets must have compatible, documented licenses.

## Support

FLAMORIS does not provide guaranteed individual support. Repository documentation, Issues, tests, logs, and source code are the primary references. AI-assisted self-support is encouraged.

---

# FLAMORIS Studio Client へのコントリビューション

このRepositoryはローカルファイルやdesktop toolsの近くで動く予定なので、filesystem accessやtool launchは通常の機能追加より慎重に扱ってください。

任意パスや任意コマンドをそのまま公開する設計は避け、権限・対象範囲・サイズ制限・失敗時の挙動をIssue/設計で明示してください。
