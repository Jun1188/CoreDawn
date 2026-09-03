# AI Agent Notes (Claude Code / Antigravity)

This repo is worked on by multiple AI agents. Shared conventions live here.

## Unity tooling

- **에디터 조작은 Unity CLI(`unity` 명령)로 한다** — `unity status`로 연결 확인,
  `unity command eval "<C#>"`이 주력(도메인 리로드 없이 즉시 실행), `unity command editor_play` 등.
  구 Unity MCP 플러그인(unity-mcp-cli / `mcp__unity__*`)은 PR #56에서 제거됐다 — 그 경로는 더 이상 없다.
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
| `Sim/**` | `CoreDawn.Sim` | plain C# simulation. Root = entity/world/geometry/interfaces (incl. `ISteppable`·`ISaveableModule`), `Inventory/` = `ItemStack`/`ItemContainer` (item storage shared by player and buildings), `Factory/` = the factory sim (5a-2f: `FactorySystem`·`BuildingModule`·`BuildingGraph`·`BuildingPorts`·`BeltSystem`·`BeltSegment` + `Direction`/`Dir`/`PortDefinition`/`BeltShape`), `Modules/` = entity modules (`*Module`, incl. `InventoryModule`·`CrafterModule`·`RouterModule`·`ExtractorModule`·`CoreModule`; `Modules/MonsterBrain/` holds the brain and its states), `Systems/` = systems, `Definitions/` = specs the sim reads (`EffectSpec`, `MonsterSpec`), `SimHost` = transitional static world access. All one namespace |
| `Data/**` | `CoreDawn.Data` | the few Unity assets that remain after 5a-3e (2026-09-01): `ViewCatalogSO` (pack id → icon/prefab/sound clips, baked from the pack `view` blocks; also holds the shared `droppedItemPrefab`), `MapDataSO` (maps are not pack content), `BuildingCategory`, plus the view-block readers `ViewSpec`/`SoundUse`/`ViewSchema` (5a-4a). **All game definitions (items, recipes, effects, entities, guns, tutorial, wave, dayCycle) are pack json only** — no `*DataSO`/`*DatabaseSO` exist anymore. |
| `Game/Factory` | `CoreDawn.Factory` | Unity-facing factory bridges only (since 5a-2f, 2026-09-01): `FactoryBootstrap` (driver + `WireGameRules`), `PlacementBridge`, `CoreBootstrap`, `BeltItemView`, `CoreSystem` (core tier/UI wiring), `RecipeRewardUnlockService`. The factory sim itself lives in `Sim/Factory`. The behavior layer (`*Behavior`, `IBuildingBehavior`, `BuildingBehaviors`) is gone — building tick is decided by what the entity *has* |
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
- A building is an `Entity` with a `BuildingModule` module (`CoreDawn.Sim`, `Sim/Factory` since 5a-2f). `FactorySystem.Place(EntityDef)`
  creates the entity from the pack definition (`def.Assemble` → `HealthModule`/`EffectsModule` from json) and the module; views only follow.
  Building tick (5a-2f, 2026-09-01) is decided by what the entity **has** — no behavior objects: conveyors run per segment
  (`BeltSystem.Tick`), everything else runs `BuildingModule.TickModules` (flush pending outputs → step every `ISteppable`
  module → flush again if outputs grew → `NotifyUpstream` if inputs shrank → schedule the wake the modules asked for; container
  `Changed` wakes hand-fed buildings). Routing state (per-outlet filters/blocks + round-robin cursor) is `RouterModule`
  (mergers and splitters are the same module); pumping itself is the building's (`PumpRouted`/`PumpPassThrough`). Modules with
  save state implement `ISaveableModule`, saved under `buildings[].modules{}` keyed by module type name (save schema v3
  migrates old `behavior` blobs). Game rules the sim must not know (core tier delegates via `CoreSystem.Wire`, recipe unlock
  vetting) are wired on the `FactorySystem.Placed` event by `FactoryBootstrap.WireGameRules`.
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
  `BattleManager` only attaches the view. Hand crafting (inventory panel) and assemblers share `CrafterModule`, which steps
  itself on the common building tick (`ISteppable`); recipe unlock checks are game/UI (MachinePanelView,
  `FactoryBootstrap.WireGameRules`), never the sim. Inspector-authored stacks use `ItemStackAuthoring` (pack id string + amount, Game/Inventories),
  never the sim `ItemStack`, because `ItemDef` is not Unity-serializable. The same rule holds for every scene/prefab field that names a
  definition (`DroppedItem.authoredItemId`, `ResourceDepositView.resourceId`, `PlacedMapObject.dataId`, `CoreBootstrap.coreId`,
  `NestView.bossId/defenderId`, `Gun.gunId`, `NightWaveRecipeReward.unlockedRecipeId`, `MapDataSO.ResourceNodeSpec.itemId`): a pack id
  string resolved through `SaveRefs.Item/Entity/Recipe/Gun/Effect` (warns once, no fallback).
- Data pipeline (5a-3e-2 ③, 2026-09-03): **the pack (`StreamingAssets/packs/coredawn/data.json` + `maps/*.json`) is the only source of
  definitions, for runtime and editor alike.** The GameData editor shell (`Editor/GameDataEditor/GdShell`) reads it with `GdPack.ReadPack`,
  converts to the legacy form-tab DTOs (`GdPack.ToV1` → `GameDataJson.Root`) and writes back with `GdPack.ToPack` (lossless round trip,
  disk key order kept by `GdPack.OrderLike`, validated by `SimDatabase.Load` before `WritePack`). The **[UI ⇄ Raw]** bar button swaps
  a form tab with the same section in the Raw tab (models synced both ways). Maps: `MapImporter.LoadAll/SaveAll`. v1 `GameData.json`,
  `GameDataExporterV2`, `PackMaterialHarvester` and the python migration tools are gone; file references (models, icons, clips) are
  pack-relative paths picked with `GdPackAssets`. There is no
  SO importer, no `Resources/*Database`, and no `EnsurePrefab` — building prefabs under `Assets/Prefabs/Buildings` are static
  assets until the 5a-4 view assembler retires them. Tutorial steps are pack `tutorial` entries (`TutorialStepDef`); condition
  logic is `Game/Tutorial/Conditions/*` plain classes registered in `TutorialConditions` (add a class + one table line — the
  editor's tutorial tab discovers kinds from `TutorialCondition` subclasses and draws their public fields). Save tutorial keys
  are pack ids (schema v5). Editing the editor to write v2 directly is 5a-3e-2 (pending).
- View schema and sounds (5a-4a, 2026-09-01): **everything about how a definition looks and sounds is data → view; scenes only author
  placement.** Every entity/gun `view` block carries `type` (explicit, `ViewSchema.Types`: Building · Tower · Deposit · Nest · Monster ·
  Player · Gun — the 5a-4b assembler keys components/colliders off it) and `sfx{name: {sound, volume, spatial}}` (`SoundUse` — the
  *use site* owns volume/spatiality, like `EffectUse` owns value/duration). Sounds are their own pack section `sounds/<key>` (`SoundDef`,
  `view.clips[]` = variant clips, one picked at random per play); the pack root `sfx` holds common uses (`ui_click`, `construct`,
  `destroy`, `warning`, `item_pickup`, `mine`…) replacing the old `CommonSFX` enum — play them with `SoundManager.PlayCommon("mine")`,
  and definition sounds with `SoundManager.Instance.Play(ViewSchema.Of(def).SfxOf("fire"), position)`. Adding a sound slot = one name in
  the `ViewSchema.Types` table; adding a sound = the editor's 사운드 tab (clips are guid refs baked into `ViewCatalog.Entry.clips`).
  Nest boss/defender kinds come from the map (`NestSpec.spawnPoints[].boss`, `NestSpec.defender`), never from the MonsterNest prefab.
  Prefab default values must be cleared *before* re-baking World (instance values equal to the prefab are not recorded as overrides).
- View assembler (5a-4b, 2026-09-01): **guns and buildings have no prefabs.** `WeaponManager.AssembleGuns` builds every pack gun from
  `guns.view` (`model`, `pose`, `muzzle`/`sight` anchors — a `MuzzlePoint`/`SightPos` node inside the model wins over the data offsets —
  and `knockback`). `BuildingAssembler.Build/BuildGhost` (Presentation/Entities) builds buildings: root scaled by the cell size (**building
  models are authored in cell units**), the catalog `model` instance at `view.pose` (`poseCurveL/R` for belt curves) or a placeholder cube
  when there is no model, one non-convex `MeshCollider` per renderer, layer `Entity`, then `TowerView`+`TowerVisualController` or
  `BuildingView` by `view.type`. Tower rigs are found by node name (`YawPivot`, `PitchPivot`, `Droop`, `Recoil`, `Muzzle_*`; override via
  `view.rig`) and the starved droop is code, not an Animator. `PlacementBridge.Place`, the placement preview and `WorldPopulator.PlaceCore`
  all go through the assembler — never instantiate a building prefab. Guns are assembled **when equipped** (`WeaponManager.EquipWeapon`
  builds, unequip destroys — magazine state lives in the sim). Monsters are assembled too (`MonsterAssembler`: `view.collider`,
  `MonsterVisualController.Wire`, model prefabs under `Art/Models/Monsters` that carry the rig, Animator override controller and URP
  materials). **The cell size is map data** (map `cellSize` ← pack `maps/<name>.json`); `World.CellSize` returns it and `GameBootstrap`
  injects it into the factory geometry, placement and the nav grid — there are no inspector copies. The nav grid subdivides a cell into
  `nodeSize` (1 m) nodes, never a hard-coded 4. Remaining prefabs: MonsterNest, ResourceNode, DroppedItem, vegetation trees.
- Guns are sim-owned (5a-2e-2, 2026-08-31): the pack `guns` section loads as `GunDef` (magazine, reload, fire interval in seconds,
  pellets, range in meters, ammo filter, damage multiplier + the view's feel values), and the player entity carries a `WeaponModule`
  (per-gun `Magazine`, equipped gun, fire cooldown, reload timer that really consumes rounds from the inventory, auto-reload, ammo
  switch, melee = unlimited) ticked by `PlayerSystem.Tick`. `Gun` (view) forwards input (`TryFire/StartReload/TrySwitchAmmo`) and turns
  the approved `WeaponShot` into `ProjectileSystem.Fire` calls with spread/pellets; `WeaponManager.Equip/Unequip` tell the module which
  gun is held. Saves store `player.weapons[{gun, round, loaded}]` keyed by gun id.
- Nests are sim-owned state (5a-2e-3, 2026-08-31): `NestModule` (pack module `Nest`, no fields) holds the spawn points'
  destroyed state, the invulnerability rule as an `IDamageInterceptor` (any live spawn point → no damage; `DamageGateModule`
  is gone) and detects boss death via the entity `Died` event. **Nests never recover** (user decision 2026-08-31): kill the
  bosses, break the nest, and it stays broken — no waves come from it again. `NestView` keeps only the point transforms,
  boss prefab spawning (answers `BossNeeded`, then `BindBoss`), visuals and the day-defender spawn timing (player distance +
  screen-occlusion raycast). `NestView.SyncModule` pushes points into the module; `CombatSaveModule` reads point state from it.
- Night waves are score-based (2026-08-31, design "둥지 시스템 — 낮의 공격 루프"): no per-day wave table (`WaveDataSO` is
  gone). The pack has one `wave` rule (`WaveRuleDef`, GameData editor → Wave tab). `WaveSystem` (`Sim/Systems`, ticked by
  `SimRunner`) computes `score = (basePoints + day·dayPoints + gate·gatePoints) × total factor` — gate is additive,
  the score **is** the point budget, roster entries have `cost` (price) and `weight` (pick ratio among currently eligible
  entries; bosses use the same weights). Night total = living share (1 − r) + stimulus bonus `stimulusAmplitude`·r^`stimulusExponent` + `stimulusLinear`·r
  (r = destroyed / total nests; additive, the user's own curve — first destruction is a loss, the last nest is stronger than
  two). Stimuli for buffs = total ÷ living share (strength of one remaining nest), and `stimulusBuffs` (effects scaled per stimulus) go on **every
  monster except trickle groups** — bursts, nest bosses, day defenders (`WaveSystem` hooks `MonsterSystem.Spawned` and re-applies
  to living monsters when the destroyed count changes; non-stacking effects refresh in place). Each night it picks a random number of living nests, uses their live
  spawn points as exits, and spawns in **bursts** (`burstsPerNight`, interval = `targetNightLength` / count — both rule values; `TimeManager`'s
  night duration is the moon's rise/set time, not the night length; each burst takes one exit
  and a slice of the remaining score). `trickle` = anti-boredom groups of un-buffed basics at `NightSpawnPoints` until 90% of
  the score monsters are dead, independent of score. No fallbacks (no edge spawn). The night ends when nothing is left to
  spawn and nothing is alive (`NightCleared` → `EndNightEarly`). Views: `WaveSpawnManager` attaches prefabs on `Spawned`
  (`MonsterAssets.OfEntity`), `BattleManager` only feeds day/gate/night length/entrances/seed. Save: `combat.wave` = full
  system state, monsters carry `wave: burst|trickle`. **F3** toggles `WaveDebugHUD` (editor/dev builds only, self-spawned,
  OnGUI): score/total factor, bursts, next burst/trickle timers, stimuli + buff values, and an event log (night start, bursts,
  trickle groups, clear). A burst slice smaller than the cheapest eligible monster is raised to that cost so early bursts are never empty. The day/night clock lengths live in a separate top-level pack block `dayCycle{dayDuration, nightDuration}`
  (`DayCycleDef`; `nightDuration` = moon rise/set time, not the night length) — `TimeManager` reads it from the pack and throws
  if it is missing (no inspector values). GameData editor previews are charts (`BarChart`/`LineChart` in GdWaveTab.cs — Painter2D + `DrawText`, styled by
  `gd-chart`/`gd-legend` in `GdEditor.uss`, fixed widths — never stretched to the window); nest counts come from the map data, never from hardcoded sample sizes, and never fake
  tables with padded monospace labels. Editor number fields apply per keystroke but push history on FocusOut only, and never
  rebuild the focused body while typing (rebuild a sub-host instead); every tab must override `Undo/Redo` (delegate if the
  model lives in another tab).
- Towers are not a module: a tower is `Building + (AmmoConsumer | FixedAmmo) + (Turret | AuraEmitter | Trigger)` (5a-2e-1, 2026-08-31).
  Emitters never know effects — they ask the entity's `IAmmoSource` ("can I fire, what is this shot": `HasAmmo`, `TryPeek`, `TryTake`,
  `Bake`). `AmmoConsumerModule` = magazine (input-container filter, one round per shot, damage-like × `damageMultiplier` → owner
  `BakeOutgoing`); `FixedAmmoModule` = the building's own inline ammo (unlimited, nothing consumed; mines, fuel-less auras — never a
  reference to an item). `TurretModule` (targeting, turning, alignment, lead via `Sim/Ballistics`, cooldown → `FireRequested(TurretShot)`),
  `AuraEmitterModule` (periodic pulse on every hostile in radius, applied in the sim, no PhysX), `TriggerModule` (mine: detonates once and
  kills itself). All three implement `ISteppable` and run on the common building tick — the old `*Behavior` factory adapters
  are gone (5a-2f). `TowerView` only draws: on `FireRequested` it re-aims from the rig muzzle at the sim's impact point and calls
  `ProjectileSystem.Fire`; `TowerState` derives from `TurretPhase`. Which modules a v1 tower gets is decided by its `fireMode`
  (Projectile/Hitscan → Turret, Aura → AuraEmitter, Trigger → Trigger, None → Blocker) and by `ammoFilter` (→ AmmoConsumer) vs
  `attackEffects` (→ FixedAmmo) — never by the building's name. `Building.walkable` (mine) makes pathfinding treat the cell as ground
  while the placement grid stays occupied. Ranges and radii (`Turret.range/minRange`, `AuraEmitter.radius`, `Trigger.radius`) are in
  **meters**, the same unit as the player gun — not grid cells.
- Resource deposits are sim entities (`ResourceDepositModule`, one cell each, faction Neutral, **no Health**, no Building, **no stock** —
  they never run out; only `extractInterval` — seconds per item, used as-is for hand mining and divided by the
  extractor's `speedMultiplier` for miners — and a `TotalExtracted` counter):
  `FactorySystem.Deposits` indexes them by cell, accrues production on the factory tick, and owns the placement rule
  (`CanPlace`: an extractor must cover only deposit cells of one resource — no partial coverage). `ExtractorModule` mines the
  covered deposits round-robin (the factory hands them over on placement via `SetDeposits` — the module never sees the grid).
  The map stores only `item + cell` per deposit. Deposit definitions are **not authored**:
  every v1 item of type `Ore` carries `extractInterval`, and the v2 exporter emits `entities/<item>_deposit` from it
  (the exporter rejects an Ore without it or a non-Ore with it). Edit the value in the GameData editor's item panel.
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
- Damage, effects and death end inside the sim. Effect *definitions* are `EffectSpec` (pack `effects` section, resolved by id —
  `SaveRefs.Effect` for view-authored ones such as `Gun.knockbackEffectId`), a hit is an `Effect[]` (spec + value) applied to the target's `EffectsModule` module; `EffectSystem` ticks
  duration effects. Melee: the sim `AttackModule` module applies directly. Projectiles/auras: PhysX detects the hit, then
  `EntityView.ApplyEffects(Effect[], Entity source, point, dir)` hands it to the sim — the only view entry point.
  Incoming multipliers, shields, ally-ignore and nest invulnerability are all `IDamageInterceptor`s inside
  `HealthModule.Damage` (`EffectsModule`, `BuildingModule`, `NestModule`). `EffectSpec.Kind` selects the channel; value is the amount.
- `SimRunner` (static, `Monsters` · `EffectsModule` · `Players`) drives the sim every frame in that order — the transitional
  access point until the phase-5 `WorldRunner` (fixed tick, scene lifecycle).
- Entity identity is `EntityUUID` (a Guid; `Entity.Id`, `EntityUUID.New()`), minted by whoever creates the entity —
  no central counter, so client prediction, pasted structures and save restores keep their ids. The per-session
  integer handle for packets is the netcode library's, not ours. Not named `EntityId`: Unity 6 has `UnityEngine.EntityId`.
  The sim attack module is `AttackModule` (not `Combat` — that is a namespace); the effects module is `EffectsModule`. Sim ↔ factory contact points are interfaces in `Runtime/Sim` (`IFootprint`, `INavigation`).
- **Rule:** sim code (`Assets/Scripts/Sim/**` — since 5a-2f this includes the factory sim in `Sim/Factory`) must not import
  any non-`CoreDawn.Sim` project namespace; the `Game/Factory` bridges may know views but not `CoreDawn.Entities`.
  `python tools/check-sim-imports.py` enforces it until asmdefs do (phase 5). Death is decided by the sim:
  `Health.Die → EntityWorld.Died → FactorySystem.Remove → Removed → view destroyed`.
- Commit messages in this repo already tend to note the main edited directory
  (e.g. "main edit directory: Script - test - entity") — keep doing that, it
  mirrors the log format above.
