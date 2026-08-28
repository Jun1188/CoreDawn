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

## Namespaces (since 2026-08-28)

Every script has a `CoreDawn.*` namespace decided by its folder (`LevelUp` is the old project
name — never use it). New files go in the namespace of their folder:

| Folder (`Assets/Scripts/…`) | Namespace |
|---|---|
| `Runtime/Entity` | `CoreDawn.Entities` |
| `Runtime/Navigation` | `CoreDawn.Navigation` |
| `Runtime/Combat` | `CoreDawn.Combat` |
| `Runtime/DayTime` | `CoreDawn.DayTime` |
| `Runtime/FPS` | `CoreDawn.FPS` |
| `Runtime/Factory` | `CoreDawn.Factory` |
| `Runtime/GridSystem` | `CoreDawn.Placement` |
| `Runtime/Input` | `CoreDawn.Inputs` |
| `Runtime/Interactable` | `CoreDawn.Interaction` |
| `Runtime/Inventory` | `CoreDawn.Inventories` |
| `Runtime/Manager` | `CoreDawn.Managers` |
| `Runtime/Ping` | `CoreDawn.Pings` |
| `Runtime/Resource` | `CoreDawn.ResourceNodes` |
| `Runtime/Save` | `CoreDawn.Save` |
| `Runtime/Settings` | `CoreDawn.Settings` |
| `Runtime/Sound` | `CoreDawn.Sound` |
| `Runtime/Tutorial` | `CoreDawn.Tutorial` |
| `Runtime/UI` | `CoreDawn.UI` |
| `Runtime/World` | `CoreDawn.Worlds` |
| `Editor` | `CoreDawn.EditorTools` |
| `Tests` (incl. `Tests/Editor`) | `CoreDawn.Tests` |

Rules behind the odd names: a namespace must not share a name with a type used inside it
(`Entity`, `Ping`, `World`, `Inventory`, `Interactable`, `GridSystem` are classes), nor with a
Unity type that code refers to by simple name (`UnityEngine.Input`, `UnityEditor.Editor`) —
inside `namespace CoreDawn.X`, a sibling namespace `CoreDawn.Input` would shadow `Input`.
Two of our types collide with Unity names: `InputEvent` (vs `UnityEngine.UIElements.InputEvent`)
and `Ping` (vs `UnityEngine.Ping`). Files that import both sides carry an alias line
(`using InputEvent = CoreDawn.Inputs.InputEvent;`); the long-term fix is renaming ours.
The bulk-namespace commit is listed in `.git-blame-ignore-revs` — run
`git config blame.ignoreRevsFile .git-blame-ignore-revs` once to keep blame useful.

## Sim vs view (since 2026-08-28, refactor phase 2)

- `Assets/Scripts/Runtime/Sim` (`CoreDawn.Sim`) is the authoritative simulation: `Entity`
  (id · faction · position · modules), `Health`, `EntityWorld`. Plain C#, no `UnityEngine.Object`.
- A building is an `Entity` with a `Building` module (`CoreDawn.Factory`). `FactorySystem.Place`
  creates the entity, its `Health` (from `BuildingDataSO.maxHp`) and the module; views only follow.
  HP, faction, "is this the core", footprint and damage rules (`IDamageInterceptor`) live in the sim.
- Views (`EntityView`, `BuildingView`, `Monster`, …) hold `Entity` and relay its events. Monsters,
  the player and nests still create their own entity in `Awake` — a transitional state until phases 3–4.
- **Rule:** sim code (`Runtime/Sim`, `Runtime/Factory` except the bridges) must not `using CoreDawn.Entities`.
  `python tools/check-sim-imports.py` enforces it until asmdefs do (phase 5). Death is decided by the sim:
  `Health.Die → EntityWorld.Died → FactorySystem.Remove → Removed → view destroyed`.
- Commit messages in this repo already tend to note the main edited directory
  (e.g. "main edit directory: Script - test - entity") — keep doing that, it
  mirrors the log format above.
