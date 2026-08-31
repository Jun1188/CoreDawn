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
namespace**. `Data/**` is all `CoreDawn.Data`. The layer prefix (`Game/`, `Presentation/`) is not part of the namespace. **Entity module types end in `Module`** (`HealthModule`, `EffectsModule`, `AttackModule`, `BuildingModule` …); the property that exposes one may keep the short name (`Entity.Health`, `EntityView.Effects`, `BuildingView.Building`).

| Folder (`Assets/Scripts/…`) | Namespace | What lives there |
|---|---|---|
| `Sim/**` | `CoreDawn.Sim` | plain C# simulation. Root = entity/world/geometry/interfaces, `Inventory/` = `ItemStack`/`ItemContainer` (item storage shared by player and buildings), `Modules/` = entity modules (`*Module`, incl. `InventoryModule`·`CrafterModule`; `Modules/MonsterBrain/` holds the brain and its states), `Systems/` = systems, `Definitions/` = specs the sim reads (`EffectSpec`, `MonsterSpec`), `SimHost` = transitional static world access. All one namespace |
| `Data/**` | `CoreDawn.Data` | every ScriptableObject definition + databases + `EffectEntry`/`EffectSpecs` (items, buildings, recipes, effects, monsters, waves, maps, tutorial, weapons) |
| `Game/Factory` | `CoreDawn.Factory` | FactorySystem, BuildingModule, belts, processors, bootstrap/bridge; `Behaviors/` = building behaviors (`*Behavior`, `IBuildingBehavior`) chosen from the definition's modules by the `BuildingBehaviors` registry (not by the SOs) |
| `Game/Combat` | `CoreDawn.Combat` | SimRunner, BattleManager, wave/nest spawning, projectiles (`ProjectileSystem`·`ProjectileShot`·`FireMode`·`Bullet`), HostileIntentProbe, CombatEvents |
| `Game/Navigation` | `CoreDawn.Navigation` | grid, flow fields, pathfinding, `SceneNavigation` adapter |
| `Game/Placement` | `CoreDawn.Placement` | build mode, placement, port overlay |
| `Game/Worlds` | `CoreDawn.Worlds` | World, WorldPopulator, tile rules |
| `Game/DayTime`(DayCycle·TimeManager·DayRegenSystem) · `Game/Save` · `Game/Interaction` · `Game/Inventories` · `Game/Tutorial` · `Game/Pings` · `Game/Managers`(GameManager·GameBootstrap) · `Game/Sound`(SoundManager + audio settings) · `Game/Settings` · `Game/ResourceNodes` | `CoreDawn.<FolderName>` | gameplay systems and managers |
| `Presentation/Entities` | `CoreDawn.Entities` | views: EntityView, BuildingView, MonsterView, PlayerView, NestView, TowerView, registry, visual controllers. **Entity views end in `View`** |
| `Presentation/Visuals` | `CoreDawn.Visuals` | VFX/animation/outline presentation (pooled effects, monster animation, outlines) |
| `Presentation/UI` | `CoreDawn.UI` | UITK views, uxml/uss, health bars, HUDs, `UIPopup`/`UICursor` bases |
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
  (id · faction · position · modules), `HealthModule`, `EntityWorld`. Plain C#, no `UnityEngine.Object`.
- A building is an `Entity` with a `BuildingModule` module (`CoreDawn.Factory`). `FactorySystem.Place(EntityDef)`
  creates the entity from the pack definition (`def.Assemble` → `HealthModule`/`EffectsModule` from json) and the module; views only follow.
  Sim/game code holds definitions (`EntityDef`/`ItemDef`/`RecipeDef` from `SimHost.Database`); the SOs are view assets reached
  through `BuildingAssets.Of(def)` / `ItemAssets.Of(def)` / `RecipeAssets.Of(def)` (prefab, icon) until the 5a-3 view catalog replaces them.
  HP, faction, "is this the core", footprint and damage rules (`IDamageInterceptor`) live in the sim.
- A monster is an `Entity` with `HealthModule` · `MovementModule` · `AttackModule` · `MonsterBrainModule` modules, built by
  `MonsterSystem.Spawn(MonsterSpec, …)` (phase 3, 2026-08-29). `MonsterSpawner.Spawn(data, pos, rot, parent)` is the
  single gate (waves, nest bosses, save restore): sim entity first, prefab view attached after. `SimRunner`
  is the transitional static access point + runner (like `SimHost.World`).
- Views (`EntityView`, `BuildingView`, `MonsterView`, `PlayerView`, `NestView`, …) hold `Entity` and relay its events;
  `MonsterView` draws the sim position/facing in `LateUpdate`, `PlayerView` pushes the physics-driven position back into
  the sim (`PushesPositionToSim`). Creation owners (phase 4, 2026-08-29): buildings = `FactorySystem`, monsters =
  `MonsterSystem`, the player = `PlayerSystem`, nests = `WorldPopulator`. No view creates its own entity any more
  (`CreatesOwnEntity` remains only for legacy scenes).
- The player is assembled from the pack definition `coredawn:entity/player` (Health·Effects·Inventory `main` 25 slots of which the
  first `hotbar` 7 are the hotbar window — one container, Minecraft-style; adding fills from the front, consuming takes from the back·Crafter manual;
  v1 `player` block → exporter). `PlayerInventoryHolder` spawns that entity in Awake and exposes its `InventoryModule` containers;
  `BattleManager` only attaches the view. Hand crafting (inventory panel) and assemblers share `CrafterModule` — `AssemblerBehavior`
  is the factory adapter (wake scheduling, flush, unlock check). Inspector-authored stacks use `ItemStackAuthoring` (SO + amount),
  never the sim `ItemStack`, because `ItemDef` is not Unity-serializable.
- Guns are sim-owned (5a-2e-2, 2026-08-31): the pack `guns` section loads as `GunDef` (magazine, reload, fire interval in seconds,
  pellets, range in meters, ammo filter, damage multiplier + the view's feel values), and the player entity carries a `WeaponModule`
  (per-gun `Magazine`, equipped gun, fire cooldown, reload timer that really consumes rounds from the inventory, auto-reload, ammo
  switch, melee = unlimited) ticked by `PlayerSystem.Tick`. `Gun` (view) forwards input (`TryFire/StartReload/TrySwitchAmmo`) and turns
  the approved `WeaponShot` into `ProjectileSystem.Fire` calls with spread/pellets; `WeaponManager.Equip/Unequip` tell the module which
  gun is held. Saves store `player.weapons[{gun, round, loaded}]` keyed by gun id.
- Nests are sim-owned state (5a-2e-3, 2026-08-31): `NestModule` (pack module `Nest{bossRecoveryDays, nestRecoveryDays}`) holds the
  spawn points' destroyed/day state, the nest's destroyed/day state, the invulnerability rule as an `IDamageInterceptor`
  (any live spawn point → no damage; `DamageGateModule` is gone) and detects boss death via the entity `Died` event.
  `NestView` keeps only the point transforms, boss prefab spawning (answers `BossNeeded`, then `BindBoss`), visuals, the
  day/night bridge (`OnDayStarted/OnNightStarted`) and the day-defender spawn timing (player distance + screen-occlusion
  raycast). `NestView.SyncModule` pushes points/recovery days into the module; `CombatSaveModule` reads point state from it.
- Towers are not a module: a tower is `Building + (AmmoConsumer | FixedAmmo) + (Turret | AuraEmitter | Trigger)` (5a-2e-1, 2026-08-31).
  Emitters never know effects — they ask the entity's `IAmmoSource` ("can I fire, what is this shot": `HasAmmo`, `TryPeek`, `TryTake`,
  `Bake`). `AmmoConsumerModule` = magazine (input-container filter, one round per shot, damage-like × `damageMultiplier` → owner
  `BakeOutgoing`); `FixedAmmoModule` = the building's own inline ammo (unlimited, nothing consumed; mines, fuel-less auras — never a
  reference to an item). `TurretModule` (targeting, turning, alignment, lead via `Sim/Ballistics`, cooldown → `FireRequested(TurretShot)`),
  `AuraEmitterModule` (periodic pulse on every hostile in radius, applied in the sim, no PhysX), `TriggerModule` (mine: detonates once and
  kills itself). `TurretBehavior`/`AuraBehavior`/`TriggerBehavior` are the factory adapters (cell size, wake scheduling, save,
  `NotifyUpstream`). `TowerView` only draws: on `FireRequested` it re-aims from the rig muzzle at the sim's impact point and calls
  `ProjectileSystem.Fire`; `TowerState` derives from `TurretPhase`. Which modules a v1 tower gets is decided by its `fireMode`
  (Projectile/Hitscan → Turret, Aura → AuraEmitter, Trigger → Trigger, None → Blocker) and by `ammoFilter` (→ AmmoConsumer) vs
  `attackEffects` (→ FixedAmmo) — never by the building's name. `Building.walkable` (mine) makes pathfinding treat the cell as ground
  while the placement grid stays occupied. Ranges and radii (`Turret.range/minRange`, `AuraEmitter.radius`, `Trigger.radius`) are in
  **meters**, the same unit as the player gun — not grid cells.
- Resource deposits are sim entities (`ResourceDepositModule`, one cell each, faction Neutral, **no Health**, no Building, **no stock** —
  they never run out; only `extractInterval` — seconds per item, used as-is for hand mining and divided by the
  extractor's `speedMultiplier` for miners — and a `TotalExtracted` counter):
  `FactorySystem.Deposits` indexes them by cell, accrues production on the factory tick, and owns the placement rule
  (`CanPlace`: an extractor must cover only deposit cells of one resource — no partial coverage). `MinerBehavior` mines the
  covered deposits round-robin. The map stores only `item + cell` per deposit. Deposit definitions are **not authored**:
  every v1 item of type `Ore` carries `extractInterval`, and the v2 exporter emits `entities/<item>_deposit` from it
  (the importer rejects an Ore without it or a non-Ore with it). Edit the value in the GameData editor's item panel.
  `ResourceDepositView` (Game/ResourceNodes) is the view + hand-mining interaction. The map importer bakes the views into the
  scene (with a `PlacedMapObject` cell marker) so the map is visible without playing; at play `WorldPopulator.Connect` creates the
  sim entity at the marker's cell and attaches it (missing views are placed at runtime with a warning). Death drops: `Loot` module
  def + `LootSpawner` on `EntityWorld.Died`.
  `ItemStack` is a value (`readonly struct`): `PeekAt` returns a copy, so a slot changes only through `SetAt`/`TakeAt`/`TryPutAt`
  (which notify `Changed`); use `stack.With(n)` for a new amount and `IsEmpty` instead of null checks.
- Save files: definitions are referenced by pack id only, containers are saved as a role-keyed dictionary (`containers{}`; the role names
  come from `InventoryModule.Roles`/`ByRole`, nowhere else). **No read-side fallbacks for old ids or old keys** — every format change
  bumps `SaveFile.CurrentSchemaVersion` and adds one step to `SaveMigrations` that rewrites the JSON, logs what it did, and fails the
  load if it cannot. Silent "accept both" code is not allowed.
- Damage, effects and death end inside the sim. Effect *definitions* are `EffectSpec` (converted once per `EffectSO` by
  `EffectSpecs`), a hit is an `Effect[]` (spec + value) applied to the target's `EffectsModule` module; `EffectSystem` ticks
  duration effects. Melee: the sim `AttackModule` module applies directly. Projectiles/auras: PhysX detects the hit, then
  `EntityView.ApplyEffects(Effect[], Entity source, point, dir)` hands it to the sim — the only view entry point.
  Incoming multipliers, shields, ally-ignore and nest invulnerability are all `IDamageInterceptor`s inside
  `HealthModule.Damage` (`EffectsModule`, `BuildingModule`, `DamageGateModule`). `EffectSO` subclasses are data only (`Kind` + fields).
- `SimRunner` (static, `Monsters` · `EffectsModule` · `Players`) drives the sim every frame in that order — the transitional
  access point until the phase-5 `WorldRunner` (fixed tick, scene lifecycle).
- Entity identity is `EntityUUID` (a Guid; `Entity.Id`, `EntityUUID.New()`), minted by whoever creates the entity —
  no central counter, so client prediction, pasted structures and save restores keep their ids. The per-session
  integer handle for packets is the netcode library's, not ours. Not named `EntityId`: Unity 6 has `UnityEngine.EntityId`.
  The sim attack module is `AttackModule` (not `Combat` — that is a namespace); the effects module is `EffectsModule`. Sim ↔ factory contact points are interfaces in `Runtime/Sim` (`IFootprint`, `INavigation`).
- **Rule:** sim code (`Runtime/Sim`, `Runtime/Factory` except the bridges) must not `using CoreDawn.Entities`.
  `python tools/check-sim-imports.py` enforces it until asmdefs do (phase 5). Death is decided by the sim:
  `Health.Die → EntityWorld.Died → FactorySystem.Remove → Removed → view destroyed`.
- Commit messages in this repo already tend to note the main edited directory
  (e.g. "main edit directory: Script - test - entity") — keep doing that, it
  mirrors the log format above.
