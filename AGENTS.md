# AI Agent Notes (Claude Code / Antigravity)

This repo is worked on by multiple AI agents. Shared conventions live here.

## Unity tooling

- **Unity MCP가 미연결(unconnect)이면 Unity CLI로 작업한다.** 즉
  `mcp__unity__*` 툴(Pipeline 패키지 기반)이 붙어 있지 않거나 `unity status`가
  `STATUS_NO_INSTANCES`를 반환하면, Unity 관련 작업은 Unity CLI(`unity.exe`,
  `unity-cli` skill / `mcp__unity__unity_run`)로 수행한다.
- 이 규칙은 Claude Code / Antigravity / 기타 AI 툴 모두에 적용된다.

## Project notes

- Unity project. Game code lives under `Assets/Scripts/Runtime/` — entities
  (Entity/Monster AI: state machine + components) in `Runtime/Entity`, pathfinding in
  `Runtime/Navigation`, waves/battle in `Runtime/Combat`. `Assets/Scripts/Test/` was
  retired on 2026-08-28: real tests and test-scene harnesses now sit in
  `Assets/Scripts/Tests/` (editor-only helpers in `Tests/Editor`). Test scenes: `Assets/Scenes/Test`.
- Entity sim/view refactor in progress — plan and progress log: `docs/entity-refactor-plan.md`.
- Commit messages in this repo already tend to note the main edited directory
  (e.g. "main edit directory: Script - test - entity") — keep doing that, it
  mirrors the log format above.
