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

## Folders and namespaces (layout since 2026-08-29, namespaces since 2026-08-28)

Every script has a `CoreDawn.*` namespace decided by its folder (`LevelUp` is the old project
name — never use it). New files go in the namespace of their folder:

Top-level folders are **layers** (the phase-5 asmdef boundaries); inside a layer, **folder name = last segment of the
namespace**. `Data/**` is all `CoreDawn.Data`. The layer prefix (`Game/`, `Presentation/`) is not part of the namespace.

| Folder (`Assets/Scripts/…`) | Namespace | What lives there |
|---|---|---|
| `Sim` | `CoreDawn.Sim` | plain C# simulation — entities, modules, systems, grid geometry, navigation interface |
| `Data/**` | `CoreDawn.Data` | every ScriptableObject definition + databases + `EffectEntry`/`EffectSpecs` (items, buildings, recipes, effects, monsters, waves, maps, tutorial, weapons) |
| `Game/Factory` | `CoreDawn.Factory` | FactorySystem, Building, belts, processors, bootstrap/bridge |
| `Game/Combat` | `CoreDawn.Combat` | SimRunner, BattleManager, wave/nest spawning, projectiles, HostileIntentProbe |
| `Game/Navigation` | `CoreDawn.Navigation` | grid, flow fields, pathfinding, `SceneNavigation` adapter |
| `Game/Placement` | `CoreDawn.Placement` | build mode, placement, port overlay |
| `Game/Worlds` | `CoreDawn.Worlds` | World, WorldPopulator, tile rules |
| `Game/DayTime` · `Game/Save` · `Game/Interaction` · `Game/Inventories` · `Game/Tutorial` · `Game/Pings` · `Game/Managers` · `Game/Sound` · `Game/Settings` · `Game/ResourceNodes` | `CoreDawn.<FolderName>` | gameplay systems and managers |
| `Presentation/Entities` | `CoreDawn.Entities` | views: EntityView, BuildingView, MonsterView, PlayerView, MonsterNest, BattleTower, registry, visual controllers |
| `Presentation/Visuals` | `CoreDawn.Visuals` | VFX/animation/outline presentation (pooled effects, monster animation, outlines) |
| `Presentation/UI` | `CoreDawn.UI` | UITK views, uxml/uss, health bars, HUDs |
| `Presentation/FPS` · `Presentation/Inputs` · `Presentation/DayTime` | `CoreDawn.FPS` · `CoreDawn.Inputs` · `CoreDawn.DayTime` | player controller/weapons/camera, input, lighting/skybox views |
| `Editor` | `CoreDawn.EditorTools` | editor tooling |
| `Tests` | `CoreDawn.Tests` | tests |
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
- A monster is an `Entity` with `Health` · `Movement` · `Attack` · `MonsterBrain` modules, built by
  `MonsterSystem.Spawn(MonsterSpec, …)` (phase 3, 2026-08-29). `MonsterSpawner.Spawn(data, pos, rot, parent)` is the
  single gate (waves, nest bosses, save restore): sim entity first, prefab view attached after. `SimRunner`
  is the transitional static access point + runner (like `SimHost.World`).
- Views (`EntityView`, `BuildingView`, `MonsterView`, `PlayerView`, `MonsterNest`, …) hold `Entity` and relay its events;
  `MonsterView` draws the sim position/facing in `LateUpdate`, `PlayerView` pushes the physics-driven position back into
  the sim (`PushesPositionToSim`). Creation owners (phase 4, 2026-08-29): buildings = `FactorySystem`, monsters =
  `MonsterSystem`, the player = `PlayerSystem`, nests = `WorldPopulator`. No view creates its own entity any more
  (`CreatesOwnEntity` remains only for legacy scenes).
- Damage, effects and death end inside the sim. Effect *definitions* are `EffectSpec` (converted once per `EffectSO` by
  `EffectSpecs`), a hit is an `Effect[]` (spec + value) applied to the target's `Effects` module; `EffectSystem` ticks
  duration effects. Melee: the sim `Attack` module applies directly. Projectiles/auras: PhysX detects the hit, then
  `EntityView.ApplyEffects(Effect[], Entity source, point, dir)` hands it to the sim — the only view entry point.
  Incoming multipliers, shields, ally-ignore and nest invulnerability are all `IDamageInterceptor`s inside
  `Health.Damage` (`Effects`, `Building`, `DamageGate`). `EffectSO` subclasses are data only (`Kind` + fields).
- `SimRunner` (static, `Monsters` · `Effects` · `Players`) drives the sim every frame in that order — the transitional
  access point until the phase-5 `WorldRunner` (fixed tick, scene lifecycle).
- Entity identity is `EntityUUID` (a Guid; `Entity.Id`, `EntityUUID.New()`), minted by whoever creates the entity —
  no central counter, so client prediction, pasted structures and save restores keep their ids. The per-session
  integer handle for packets is the netcode library's, not ours. Not named `EntityId`: Unity 6 has `UnityEngine.EntityId`.
  The sim attack module is `Attack` (not `Combat` — that is a namespace); the effects module is `Effects`. Sim ↔ factory contact points are interfaces in `Runtime/Sim` (`IFootprint`, `INavigation`).
- **Rule:** sim code (`Runtime/Sim`, `Runtime/Factory` except the bridges) must not `using CoreDawn.Entities`.
  `python tools/check-sim-imports.py` enforces it until asmdefs do (phase 5). Death is decided by the sim:
  `Health.Die → EntityWorld.Died → FactorySystem.Remove → Removed → view destroyed`.
- Commit messages in this repo already tend to note the main edited directory
  (e.g. "main edit directory: Script - test - entity") — keep doing that, it
  mirrors the log format above.
