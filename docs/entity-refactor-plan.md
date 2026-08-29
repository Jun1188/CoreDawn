# Entity 심/뷰 리팩토링 — 계획과 진행

> 이 문서가 정본이다. 세션이 바뀌거나 컨텍스트가 잘려도 여기서부터 이어서 한다.
> 갱신 규칙: 단계 하나를 끝내거나 결정을 바꿀 때마다 **진행 로그**와 체크박스를 고친다.

시작 2026-08-28 · 브랜치 흐름: `main` ← PR ← `feature/…` (gitflow, `develop`이 통합·회귀 기준)

---

## 1. 왜 하는가 — 실측

| 항목 | 2026-08-28 실측 |
|---|---|
| 네임스페이스 / asmdef | 0 / 0 (290파일 전부 `Assembly-CSharp`, 전역 네임스페이스) |
| `Scripts/Test/` 안의 실제 게임 코드 | 10,337줄 중 ≈8,000줄 (엔티티·몬스터·둥지·타워·길찾기·웨이브·광맥). 진짜 테스트 ≈1,800줄 |
| Runtime → Test 역참조 | 46파일 (`BuildingEntity` 17, `Entity` 16, `Player` 10, `BattleManager` 10) |
| 심 → 뷰 역참조 | `Building.cs`·`FactorySim.cs`·`PlacementBridge`·`CoreDataSO`·`NestDataSO`·`TreeDataSO`·`MachineProcessor`가 `BuildingEntity`를 직접 앎 |
| HP의 주인 | 뷰(MonoBehaviour `Entity.health`). 심 `Building`엔 HP 없음 → 뷰가 죽으면 `Sim.Remove` |
| 컴포넌트의 Unity 의존 | `MovementComponent` 16곳(Rigidbody/Physics) · `Monster` 9 · `SensorComponent` 3(OverlapSphere) · `Health`·`Combat` 0 |
| 이미 잘 된 것 | `FactorySim`/`Building`(plain C#) ↔ `BuildingEntity`(뷰) 분리, `DayCycle`·`FlowField`·`CostField`·상태기·`WaveBalanceSettings` plain C#, 데이터 SO + `CreateBehavior` |

**핵심 진단**: 공장은 심이 정본이고 뷰가 따라오는데, 전투 엔티티만 거꾸로다. HP·효과·사망이 뷰에 있으니
심이 뷰를 알아야 하고, 그래서 Runtime이 Test를 46군데 참조한다. 멀티플레이·세이브·최적화가 전부 이 지점에서 막힌다.

---

## 2. 확정된 결정 (사용자, 2026-08-28)

- **멀티플레이 = 서버 권위.** 록스텝 아님 → 물리 이동은 뷰(Rigidbody)에 남기는 하이브리드로 충분.
  서버도 Unity 물리를 돌린다. 심은 "어디로 얼마나"(의도)를 정하고, 뷰가 굴린 뒤 위치를 돌려준다.
- **네임스페이스 루트 `CoreDawn.*`.** `LevelUp`은 옛 프로젝트명 — 새 코드에 쓰지 않는다.
- **Entity는 가벼운 컨테이너** (Id · 위치 · 모듈 목록 · 이벤트). Health · Movement · Combat · Brain(상태기) · **Building은 모듈**.
  `Building : Entity` 상속은 안 한다 — 둥지(건물+스포너)·코어(건물+목표)·나무(건물+자원)가 곧 다이아몬드가 된다.
- **Monster · Player는 모듈이 아니라 아키타입**: `MonsterDataSO.Build(entity)`가 모듈을 조립한다 (`BuildingDataSO.CreateBehavior` 패턴 그대로).
- **이름은 `XxxSystem`** (FactorySystem · NavigationSystem · CombatSystem · SpawnSystem). 단 **순수 ECS는 아니다** —
  모듈은 상태 + 자기 로컬 로직(`Health.TakeDamage`), 시스템은 엔티티를 가로지르는 로직과 틱 순서.
- **심 호스트는 하나** — `WorldRunner`(MonoBehaviour)가 `World`(엔티티 + 시스템 + 시계)를 고정 틱으로 돌리고 뷰 등록부를 가진다.
  `FactoryBootstrap`은 시스템 등록 + 뷰 스포너로 줄어들고, `GameBootstrap`은 지금처럼 씬 경계 참조만 꽂는다.
- **Id**: 엔티티 정체성은 **UUID**(`EntityUUID`, Guid v4, `EntityUUID.New()`) — 2026-08-29 사용자 결정(처음엔 64비트 카운터 `EntityId`+헤더 `NextEntityId`). 이유: "발급자 하나" 가정을 버린다 — 클라이언트 예측·구조물 붙여넣기·세이브 병합·서버 간 이동·모드 도구가 만든 엔티티도 번호 재매김 없이 그대로 가고, 세이브 안의 참조(소유자·표적)가 보존된다. 세션용 정수 핸들은 넷코드 라이브러리가 따로 준다. 플레이어(프로필) Guid와 타입 통일. 복원은 `EntityWorld.Create(id, faction, pos)`(중복 id는 예외). 세이브엔 "N" 32자 문자열로(5단계 SharpNBT 엔티티 레코드).
- **공간 질의는 심 쪽 균일 격자 해시**(셀 4~8m). Job · ComputeShader 불필요. 사격 판정(레이캐스트)과 LOS만 PhysX에 남는다.
- **세이브는 JSON → SharpNBT**로 교체 예정 (5단계에서 심 스냅샷 직렬화와 함께).
- **테스트는 `Assets/Scripts/Tests/`** (`Assets/Tests` 아님).

---

## 3. 목표 구조

```
CoreDawn.Sim            plain C#. 정본. UnityEngine.Object 금지 (math 구조체만 허용)
  ├ Factory             FactorySystem · Building(모듈) · 벨트 · 행동        ← 이미 plain C#
  ├ Entities            World · Entity · EntityId · 모듈(Health · Movement · Combat · Brain · Building)
  ├ Navigation          Grid/CostField · FlowField · PathFinder                ← 이미 plain C#
  └ Waves / DayCycle
CoreDawn.Data           ScriptableObject — 숫자와 참조만. 행동은 Sim 클래스 (지금 패턴 유지)
CoreDawn.Presentation   MonoBehaviour 뷰 — EntityView · BuildingView · MonsterView · PlayerController · 애니 · 체력바 · 아웃라인 · 핑
CoreDawn.App            WorldRunner · GameBootstrap · 세이브 · UI 화면 · 입력
CoreDawn.Editor         임포터 · GameData 에디터
CoreDawn.Tests          Assets/Scripts/Tests
```

**불변식 세 가지 — asmdef로 컴파일러가 강제하게 하는 것이 이 작업의 진짜 산출물이다.**

1. **Sim은 뷰를 모른다.** 심은 이벤트(`Damaged` · `Died` · `Removed`)를 올리고 뷰가 구독한다. `FactorySim.Removed`가 이미 이 모양.
2. **뷰는 심 상태를 직접 바꾸지 않는다** — 명령(`World.Apply(cmd)`)으로만. 이 한 줄이 나중에 네트워크 송수신 지점이 된다.
3. **모든 엔티티는 `EntityId`를 갖고, id↔뷰 매핑은 한 곳뿐이다.** `FactoryBootstrap.GetView(Building)`를 일반화한 `EntityViewRegistry`.

---

## 4. 단계 — 각 단계 끝에 게임이 돈다

### 0. 안전망 · 준비 — 진행 중
- [x] 팀 프리즈 창 확보 (팀원 휴가 2026-08-28 ~ 30 — 충돌 나는 이동은 이 안에 main으로)
- [x] gitflow: `feature/…`·`hotfix/…` 이름, PR은 gh CLI
- [x] `develop` 브랜치 = 회귀 기준 — PR #111 머지(main `8fa89d35`) 직후 main에서 분기, origin에 푸시 (2026-08-28)
- [ ] 회귀 체크리스트 문서화: 새 게임 → 채굴 → 건설 → 밤 → 세이브/로드 (`Tests/PlayLoopTestSetup` · `FactoryScenarioTests` 기준)

### 1a. 기계적 이동 (동작 변경 0) — 완료 2026-08-28
- [x] `Scripts/Test/` → `Runtime/` (게임 코드) · `Tests/` (테스트) `git mv` — 119파일 전부 R100, meta 동반 → GUID 보존, 씬·프리팹 참조 무사
- [x] Unity 컴파일 확인 (`recompile` → `failed:false, errors:[]`), 열린 World 씬 missing script 0
- [x] AGENTS.md 작업 영역 안내 갱신

### 1b. 네임스페이스 — 완료 2026-08-28 (`feature/namespaces`)
- [x] 폴더별 `CoreDawn.*` 네임스페이스 부여 + `using` 정리 — 278파일 전부, 스크립트로 일괄(BOM·줄 끝 보존, 본문 4칸 들여쓰기; C# 9라 파일 범위 네임스페이스 불가). 대응표는 AGENTS.md
- [x] 함정 ① UXML 커스텀 요소 — 실측 `[UxmlElement]` 0개, UXML에 커스텀 태그 0개 → 해당 없음
- [x] 함정 ② 문자열 타입 조회 — `Type.GetType`·`AddComponent("…")`·`TypeNameHandling`·`SerializeReference`·Odin 직렬화·UnityEvent 타입명(라이브 에셋) 전부 0
- [x] 이름 충돌 — 네임스페이스는 안에서 쓰는 타입명과 겹치면 안 된다(`Entity`·`Ping`·`World`…) → `Entities`·`Pings`·`Worlds`·`Inventories`·`Interaction`·`Placement`·`Inputs`·`EditorTools`.
  우리 타입 vs Unity 타입 단순명 충돌 2건: `InputEvent`(UIElements)·`Ping`(UnityEngine) — 양쪽을 import하는 파일 4개에 alias. **후속 과제: 둘을 개명**(예: `InputSignal`)
- [x] Editor 어셈블리 타입을 가리키던 `using CoreDawn.EditorTools;`가 런타임 파일 23개에 들어갔던 것 제거(주석 속 이름 매칭) → 컴파일 0 오류
- [x] 씬·프리팹의 `m_EditorClassIdentifier`는 GUID 참조라 깨지지 않음 — 리임포트 시 갱신될 뿐

### 2. 의존 방향 뒤집기 — 완료 2026-08-28 (`feature/entity-sim-core`, 커밋 7개)
- [x] `CoreDawn.Sim` 핵심: `EntityId` · `EntityWorld`(등록부·번호·이벤트) · `Entity` · `EntityModule` · `Health` · `Faction` · `GridGeometry` · `IDamageInterceptor` (커밋 1)
- [x] 뷰 개명 `Entity → EntityView`, `BuildingEntity → BuildingView` (커밋 2)
- [x] 모든 뷰가 심 엔티티를 갖고 HP를 위임 — 몬스터·플레이어·둥지는 뷰 우선 생성(과도기), `Faction`으로 적대 판정 (커밋 3)
- [x] `FactorySim → FactorySystem(EntityWorld, GridGeometry)`, `Building : EntityModule, IDamageInterceptor`, HP 정본 `Data.maxHp`, 코어 보호막·isAttackable을 심 인터셉터로, 둥지는 MonsterNest 엔티티에 건물을 얹음(host) (커밋 4)
- [x] 파괴는 심이 결정: `Health.Die → World.Died → FactorySystem.Remove → Removed → 뷰 파괴`, 코어 파괴 판정도 월드 이벤트 (커밋 5)
- [x] 소비자 교체: FlowFieldManager·GridManager·DayRegen·CorePanel·HUD·부활·PortFlowOverlay·FactorySaveModule → `FactorySystem.Buildings` / `Owner.Health`. `BuildingView.All` 퇴역 (커밋 6)
- [x] 출구 조건: 심 폴더의 뷰 import 0 (`tools/check-sim-imports.py` 통과). 세이브 포맷 불변
- 남은 빚(다음 단계로): 몬스터·플레이어·둥지의 엔티티 생성 주체가 아직 뷰(3·4단계) · 효과(EffectController)·이동·전투 컴포넌트가 뷰(4단계) · SO 행동의 `Interact(PlayerController)`가 UI를 직접 엶(4단계) · `SimHost.World` 정적 접근점(5단계 WorldRunner) · PlacementSystem/GridManager의 격자 복사본(5단계)

#### 2단계 설계 초안 (2026-08-28, 착수 전 검토용)

**실측 근거**
- 심(`FactorySim`·`Building`·행동·SO)이 뷰를 직접 아는 곳은 사실상 하나 — `CoreBehavior.ApplyMaxHpBonus`가 `boot.GetView(_b).Health`로 HP를 만진다. 나머지 `using CoreDawn.Entities`는 주석 속 이름 매칭이 남긴 것.
- 뷰 쪽이 정본을 쥔 것: HP(`Entity.health`), 코어 여부(`BuildingEntity.isCore` 직렬화 플래그), 풋프린트 월드 사각형(`TryGetFootprintRect` — `PlacementSystem`의 셀 크기·원점 사용), 살아 있는 건물 목록(`BuildingEntity.All`), 코어 파괴 이벤트, 적대 판정(`IsHostile` = 레이어 "Monster").
- 건물 HP는 이미 `BuildingDataSO.maxHp`가 데이터 정본이고, 임포터(`GameDataImporter` 1245~1251)가 그 값을 프리팹의 `Entity.health.maxHealth`에 복사한다. 22종 값이 프리팹과 일치.
- HP를 읽는 소비자: `FactorySaveModule`·`CombatSaveModule`·`PlayerSaveModule`(캡처/복원), `CorePanelView`·`GameplayHUDView`·`DayRegenSystem`(코어·플레이어), `BattleManager`·`WaveSpawnManager`·`MonsterNest`(SetMaxHealth/Initialize), 효과 SO(`ReceiveDamage`·`Heal`).
- `BuildingEntity`를 쓰는 곳 17파일: FlowFieldManager(목표·돌파 대상), GridManager(칸→건물), DayRegenSystem, CorePanelView, GameplayHUDView, PlayerController.FindCore(부활 위치), BattleManager(CoreDestroyed), PortFlowOverlay, PlacementSystem(철거 조준), WorldPopulator, FactoryBootstrap, PlacementBridge, CoreBootstrap, MachineProcessor, FactorySaveModule, FactoryTest.
- 기존 `CoreDawn.Worlds.World`(MonoBehaviour)가 있으므로 심 등록부 이름은 `EntityWorld`.

**출구 조건**
1. `Runtime/Sim/` → `CoreDawn.Sim`: `EntityId`·`EntityWorld`·`Entity`·`EntityModule`·`Health`·`Faction`·`GridGeometry`·`IDamageInterceptor` (plain C#, UnityEngine.Object 없음).
2. HP의 유일한 주인은 심 `Health`. 뷰는 `Sim.Health`를 위임 노출만 한다(소비자 API 표면 유지 → 세이브 포맷 불변).
3. `Building : EntityModule`. `FactorySim.Place`가 심 엔티티를 먼저 만들고 모듈을 붙인다. HP 정본 = `Data.maxHp`, 임포터의 프리팹 HP 복사 제거.
4. 파괴는 심이 결정: `Health.Died → Entity.Died → EntityWorld.Died → FactorySim.Remove → Removed → 뷰 파괴`. 버퍼 드롭은 브리지(FactoryBootstrap의 Removed 처리)로 이동. 코어 파괴 = `EntityWorld.Died` + `Building.IsCore`.
5. 적대 판정 = `Entity.Faction`(Neutral/Player/Monster). 뷰 레이어는 물리·렌더링으로 되돌린다.
6. 심 폴더(`Runtime/Sim`·`Runtime/Factory`의 plain C#·SO 행동)에 `using CoreDawn.Entities/UI/FPS/…` 0 — 검사 스크립트로 강제(asmdef 전까지).
7. 뷰 개명: `Entity → EntityView`, `BuildingEntity → BuildingView` (파일·클래스 동시, GUID 유지).

**타입 계약(초안)**
```csharp
namespace CoreDawn.Sim
{
    public readonly struct EntityId : IEquatable<EntityId> { public readonly ulong Value; public static readonly EntityId None; }
    public enum Faction { Neutral, Player, Monster }
    public readonly struct GridGeometry { public readonly float CellSize; public readonly Vector3 Origin;
                                          public Vector3 CellToWorld(Vector2Int cell); public Vector2Int WorldToCell(Vector3 p); }

    public abstract class EntityModule { public Entity Owner { get; internal set; }
                                         protected internal virtual void OnAttach() {} protected internal virtual void OnDetach() {} }

    /// 받는 피해를 가로채는 모듈 — 코어 보호막(CoreBehavior). 뷰의 ReceiveDamage override를 대체한다.
    public interface IDamageInterceptor { float Intercept(float amount, Entity source); }

    public sealed class Health : EntityModule
    {
        public float Max { get; } public float Current { get; } public bool IsDead { get; }
        public event Action<float, float> Changed; public event Action Died;
        public float Damage(float amount, Entity source);   // Owner의 IDamageInterceptor 체인 → 감산 → Died
        public void Heal(float amount); public void SetMax(float max, bool refill); public void Kill();
        public void RestoreState(float max, float current, bool isDead);   // 세이브 복원 — 이벤트로 사망 연출을 다시 돌리지 않는다
    }

    public sealed class Entity
    {
        public EntityId Id { get; } public EntityWorld World { get; } public Faction Faction { get; set; }
        public Vector3 Position { get; set; }          // 건물은 배치 시 확정, 이동체는 뷰가 돌려준다(3단계)
        public bool IsRemoved { get; }
        public T Get<T>() where T : EntityModule; public T Add<T>(T m) where T : EntityModule; public bool Has<T>();
        public Health Health => Get<Health>();
        public event Action<Entity> Died, Removed;
    }

    public sealed class EntityWorld                      // 등록부·번호·이벤트뿐. 격자·시계는 여기 없다(2026-08-28 검토에서 Grid 제거)
    {
        public ulong NextId { get; }                    // 세이브 헤더(5단계) — 재사용 없음
        public Entity Create(Faction faction, Vector3 position);
        public void Remove(Entity e);                   // Removed 발화. 모듈 OnDetach
        public Entity Get(EntityId id); public IEnumerable<Entity> All { get; }
        public event Action<Entity> Created, Died, Removed;
    }
}

namespace CoreDawn.Factory
{
    public sealed class Building : EntityModule          // 지금 필드·메서드 그대로 + Owner
    {
        public bool IsCore => Data is CoreDataSO;
        public void WorldRect(out min, out max) => Sim.Geometry.RectOf(Origin, Size, …);   // 구 BuildingEntity.TryGetFootprintRect
    }
    // FactorySystem(EntityWorld world, GridGeometry geometry, float tps …)  ← FactorySim 개명. 이름 규칙(XxxSystem)
    //   Place: world.Create → Add(new Health(data.maxHp)) → Add(new Building(...)) → Position = 풋프린트 중심
    //   Geometry: 셀 크기·원점 — 공장의 속성이라 여기. PlacementSystem·GridManager의 복사본은 5단계에 통합
    // 프로퍼티 개명: FactoryBootstrap.Sim → .Factory, BuildingView.Sim → .Building (System은 System 네임스페이스를 가려 못 씀)
}
```

**소유권 이동표**

| 항목 | 지금(뷰) | 2단계 후(심) |
|---|---|---|
| HP·사망 | `Entity.health`(프리팹 인라인) | `Entity.Health` 모듈. 건물은 `Data.maxHp`, 몬스터·플레이어·둥지는 뷰가 프리팹 값으로 **씨드**(3·4단계에서 데이터로) |
| 코어 여부 | `BuildingEntity.isCore` 플래그 | `Building.IsCore => Data is CoreDataSO` |
| 풋프린트 사각형 | `TryGetFootprintRect`(PlacementSystem 의존) | `Building.WorldRect`(`EntityWorld.Grid`) |
| 살아 있는 건물 목록 | `BuildingEntity.All` | `EntityWorld.All` + `Has<Building>()` (FactoryBootstrap.Buildings도 여기서) |
| 코어 파괴 통지 | `BuildingEntity.CoreDestroyed` | `EntityWorld.Died` + `IsCore` |
| 적대 판정 | 레이어 "Monster" | `Entity.Faction` |
| 코어 보호막·MaxHp 보너스 | `BuildingEntity.ReceiveDamage` override + `view.Health` | `CoreBehavior : IDamageInterceptor`, `Owner.Health.SetMax` |
| isAttackable(아군 공격 무시) | `BuildingEntity.ApplyEffects` override | `Health.Damage`의 인터셉터(BuildingRules) |
| 버퍼 드롭(파괴 시) | `PlacementBridge.Remove` → 뷰 위치 | `FactoryBootstrap`의 `Removed` 처리(뷰 파괴 직전) |

**소비자 교체(파일별)** — FlowFieldManager·GridManager(`BuildingAt` → 심 `Building`), DayRegenSystem, CorePanelView, GameplayHUDView, PlayerController.FindCore(→ 심 코어 `Position`), BattleManager(Died 구독), FactorySaveModule(심 HP), CoreDataSO, WorldPopulator(`SetMaxHealth` 제거 — Data.maxHp가 자동), GameDataImporter(프리팹 HP 복사 제거). 몬스터 두뇌(FlowFieldState·AttackState)는 `FindBreachTarget`이 돌려주는 **심 엔티티**에서 뷰를 `EntityViewRegistry.ViewOf(id)`로 찾아 효과를 적용한다 — 효과(EffectController)는 4단계까지 뷰에 남으므로 이 다리가 과도기 접점이다.

**커밋 순서(각각 컴파일·플레이 가능)**
1. `CoreDawn.Sim` 핵심 타입 추가(아직 아무도 안 씀) + 심 폴더 import 검사 스크립트
2. 뷰 개명 `Entity→EntityView`, `BuildingEntity→BuildingView` (동작 변경 0)
3. 모든 `EntityView`가 심 엔티티를 갖고 HP를 위임(뷰 우선 생성) · `Faction` 도입 · `IsHostile` 레이어 제거
4. `FactorySim.Place`가 심 엔티티 선생성 + `Building` 모듈 · `Data.maxHp` 정본 · 임포터 프리팹 HP 복사 제거 · 코어 보호막/보너스/isAttackable을 심으로
5. 파괴 흐름 심 주도(Died→Remove→Removed) · 버퍼 드롭을 브리지로 · CoreDestroyed 대체
6. 소비자 교체(위 목록) · `BuildingEntity.All` 삭제
7. import 검사 0건 확인 · 문서 갱신

**리스크**
- 프리팹 인라인 `health.maxHealth`(몬스터·둥지·타워)는 직렬화 경로를 지켜야 값이 안 날아간다 → `HealthComponent` 타입·필드명은 **씨드 데이터 홀더**로 그대로 두고 런타임 로직만 뺀다.
- 세이브 포맷은 바뀌지 않는다(HpMax/HpCurrent 그대로). `NextId`는 5단계 SharpNBT와 함께.
- 팀 프리즈 창(휴가) 안에 2·3번(개명·위임)을 끝내야 한다 — 이 둘이 파일 수가 가장 많다.

### 3. 몬스터 심/뷰 분리 (가장 큰 덩어리)
- [x] 3a 데이터: `MonsterDataSO` + GameData `monsters` · `WaveDataSO` 개편(종류 참조 + 버프 효과) · `wave_settings.json` 폐기 — 2026-08-29 (`feature/monster-sim` 커밋 1)
- [x] 3b-1 공간 질의 `EntityWorld.QueryRadius/QueryClosest`(균일 격자 해시 8m) · `Entity.Facing` · `INavigation` + `SceneNavigation` 어댑터 · `EntityViewRegistry`(불변식 ③) · 플레이어/타워 센서를 심 질의로, `SensorComponent` 퇴역 · `EntityId → EntityUUID`(Unity 6 `UnityEngine.EntityId`와 충돌; EntityKey → Id를 거쳐 UUID 전환과 함께 EntityUUID) — 2026-08-29 (커밋 2)
- [x] 3b-2 심 모듈: `Movement` · `Attack`(이름이 Combat이 아닌 이유: `CoreDawn.Combat` 네임스페이스와 충돌) · `MonsterBrain`+상태 7종(구 Monster.cs 로직 동작 변경 0으로 이식) · `MonsterSystem`(두뇌→이동→군중 한 틱, 스폰/소멸, 시계, PlayerEntity, IsDay) · `MonsterSpec`/`EngagementZone`(순수 데이터, SO 참조 없음) · `IFootprint`(Building 구현 — 두뇌의 사거리 판정) · `Health.Damaged` 이벤트(보스 각성 경로)
  - 뷰: `Monster → MonsterView`(GUID 유지, CreatesOwnEntity=false, LateUpdate가 심 위치·방향을 그림, 옛 표면은 두뇌 위임) · `MonsterSpawner.Spawn(data, pos, rot, parent)` 한 관문(웨이브·둥지 보스·복원) · `MonsterSystemHost`(정적 접근점 + 러너, SimHost.World와 같은 과도기) · 공격은 `Attack.AttackRequested` → 뷰 `CombatComponent.TryAttack`(효과 적용, 4단계까지의 다리)
  - 퇴역: `MovementComponent` · `StateMachineComponent` · `State/*` · `CrowdSystem`(뷰). `KnockbackEffectSO`·타워 곡사 예측·연출 속도는 `Entity.Get<Movement>()`
  - 검증 2026-08-29: 밤 강제 — 보스 8 대기, 웨이브 4마리 이동·건물 2채 피해, 플레이어 접근 시 3마리 추적→공격(HP 300→180), 뷰 12 동기·애니메이터 12, 오류 0 (커밋 3)
- [x] 3c 뷰: `Monster → MonsterView`(심 위치·방향을 LateUpdate에서 그림) · 스폰은 `MonsterSpawner.Spawn` 한 관문(심 엔티티 먼저, 프리팹은 따라옴 — 웨이브·둥지 보스·복원) · 둥지/스포너/프로브/아웃라인/연출/넉백/타워 예측이 심 API(`Entity.Get<Movement>()`, `MonsterSystemHost.System.Monsters` + `EntityViewRegistry`) — 2026-08-29 (커밋 3)
- [x] 3d 세이브: `CombatSaveModule.MonsterDto.DataId`(`"data"`, MonsterDataSO.Id) — 저장 시 `MonsterView.Data`, 복원 시 `MonsterDatabaseSO.FindById` → `RestoreMonster(pos, rot, data)`. 추가 필드라 스키마 버전 그대로(옛 세이브·모르는 id → 기본 종류). 검증 2026-08-29: 밤 제자리 왕복(Capture→Restore) — Basic 4 + Spitter(35/60) 종류·HP 복원, 보스 8 재생성·뷰 8, 오류 0 (커밋 4)
  - 기존 결함 수정(사용자 지시): 복원된 웨이브 몬스터가 `nightWaveMonsters`(정량 웨이브 생존 수)에 안 들어가고 진행(스폰 수·목표)도 저장되지 않아, 불러온 밤이 0부터 다시 세다 새로 스폰한 것만 잡아도 "전멸"→아침→되살린 몬스터 일괄 소멸. `WaveDto`에 `spawned`/`target`/`completed` 추가(옛 세이브는 -1 → 예전 동작), `RestoreMonster(nightWave)`가 명단에 넣고 `RestoreState`가 진행을 이어받는다. 검증: 4/4 스폰·1 처치 상태 왕복 후 8초 — 밤 유지, 생존 3 = 명단 3
- [x] 3e 정리: 뷰 쪽 `MovementComponent`·`SensorComponent`·`StateMachineComponent`·`State/*`·`CrowdSystem` 삭제 (커밋 2·3). `CombatComponent`는 타워·플레이어·몬스터 효과 적용기로 4단계까지 유지
- [x] 3f PR #114 → develop `c97f21e0` (2026-08-29). 같은 PR에 `EntityKey → Id → EntityUUID`(값도 카운터 → Guid)·정량 웨이브 세이브 결함 수정 포함

#### 3단계 설계 초안 (2026-08-29, 착수 전 검토용)

**실측 근거**
- 몬스터의 정의는 데이터가 아니라 **프리팹 3개**(`Monster`·`Monster_Spitter`·`BossMonster`)의 인라인 값이다:
  `health.maxHealth`(30) · `movement.moveSpeed`(4)/rotateSpeed/crowdRadius/knockbackDamping/stickToGround · `combat.attackRange`(1.5)/attackCooldown(2)/attackEffects ·
  보스 리쉬·인내심 8개. `WaveDataSO`(GameData `waves`)는 규모(day·amount·interval)와 `monsterMaxHp`(절대값 덮어쓰기)뿐이고 종류는 `WaveSpawnManager.monsterPrefab`(인스펙터, 프리팹 하나).
  둥지 보스는 `NestSpawnPoint.bossPrefab`+`bossMaxHp`. HP를 덮어쓰는 곳이 셋(프리팹·WaveDataSO·`wave_settings.json`).
- `MovementComponent`(320줄)는 **Rigidbody가 아니라 transform을 직접 적분**한다 — 경로/플로우 방향 이동, 격자 통행 판정(`GridManager.IsWalkable`), 지면 높이(`GroundSampler`), 넉백, 지형 배율.
  Rigidbody는 kinematic으로 플레이어 접촉에만 쓰인다. 즉 이동은 물리가 아니라 **심 로직**이고, 통째로 심에 둘 수 있다(하이브리드 불필요).
- `SensorComponent`는 `OverlapSphere`+레이어. 플레이어(몬스터 감지→콜백)와 타워(가장 가까운 몬스터)가 쓴다.
- `Monster.cs`(621줄) = 두뇌: 상태기 구동, 보스 인내심(자리·반경·드레인·복귀 재생·타임아웃), 둥지 방어자(호위 보스·교전 구역), 플레이어 센서 콜백, 세이브 복원.
  뷰 참조: `Player`(표적), `NestEngagementZone`(구역 반경), `Monster`(호위 보스), `MonsterVisualController`(연출), `Transform`, `Time.time`, `FlowFieldManager.Instance`, `PathRequest`.
- `CrowdSystem`은 정적 등록부 + LateUpdate 한 패스(위치 보정). `EffectController`·효과 SO는 `EntityView` 타입(4단계).

**결정 사항(사용자, 2026-08-29)**: `wave_settings.json`/`WaveBalanceSettings` 폐기. `monsterMaxHp` 절대값 대신 웨이브가 몬스터에게 **효과**(주는 피해 증가·받는 피해 감소)를 건다.

**3a 데이터 계약**
```
MonsterDataSO : GameDataSO   (GameData.json "monsters", Data/Monster/*.asset, GdMonsterTab)
  id · displayName · description · prefab(뷰 프리팹, 에셋 참조라 인스펙터/guid) · maxHp
  moveSpeed · rotateSpeed · crowdRadius · knockbackDamping · stickToGround
  attackRange · attackCooldown · attackEffects[]           ← 프리팹 3개의 현재 값을 그대로 옮긴다(마이그레이션 스크립트)
  boss: maxPatience · patienceRadius · outsidePatienceDrain · rangedPokePatienceDrain · patienceRecoverRate · absoluteLeashMultiplier · returnRegenPerSecond · returnTimeout
  Build(Entity e): e.Add(Health(maxHp)) · e.Add(Movement(...)) · e.Add(Combat(...)) · e.Add(MonsterBrain(this))

WaveDataSO: day · requiredCoreTier · baseAmount · maxAliveAmount · spawnInterval
          + monster(MonsterDataSO 참조, "Monster:Basic") + buffs[](EffectEntry — 스폰 시 적용, 죽을 때까지)
          − monsterMaxHp (삭제)
효과 에셋 추가: Effect:AttackUp (AttackModifierEffectSO, affects=[Damage]) · Effect:Armor (IncomingDamageEffectSO)
  duration ≤ 0 = 영구(사망까지) — EffectController.Add가 remaining = +∞로 처리 (지금은 3초 기본)
둥지: NestSpawnPoint.bossPrefab/bossMaxHp → bossData(MonsterDataSO) · DefenderSpawnSlot.monsterMaxHp → 둥지의 defenderData
WaveSpawnManager.monsterPrefab(인스펙터) → currentWave.monster
```

**3b 심 계약(초안)**
```csharp
namespace CoreDawn.Sim
{
    public sealed class Movement : EntityModule      // 구 MovementComponent 그대로, transform 대신 Owner.Position/Facing
    { MoveSpeed·RotateSpeed·CrowdRadius·SpeedMultiplier·Velocity·IsMoving·HasPath·IgnoreKnockback;
      StartMoving(path)·SetDirection(dir)·StopMoving()·AddKnockback(dir, dist)·Tick(dt, INavigation nav); OnDestinationReached·OnPathBlocked }
    public sealed class Combat : EntityModule        // 사거리·쿨다운·공격 정의. 실제 효과 적용은 4단계까지 뷰가 다리를 놓는다
    { AttackRange·AttackCooldown·AttackEffects; CanAttack(now)·TryAttack(target Entity, now) → AttackRequested(target) 이벤트 }
    public interface IEntityState { Enter(Brain)·Update(Brain, dt)·Exit(Brain) }
    public sealed class MonsterBrain : EntityModule  // 구 Monster.cs의 로직 전부: 상태기·보스 인내심·둥지 방어·표적. Player 대신 Entity, Zone 대신 EngagementZone 구조체, Time.time 대신 now
    { SetState·CurrentState·IsBoss·HasBeenAttacked·IsNestDefender·DefendOrigin·PatienceRatio; SetAsBoss(zone)·SetAsNestDefender(target, zone, escort)·Provoke(attacker)·OnDetected(by)·OnLost(); Alerted 이벤트(연출) }
    public readonly struct EngagementZone { Center·ChaseRange·LeashRange; CanChase(origin, pos) }
    public interface INavigation                      // 길찾기 어댑터 — GridManager·FlowFieldManager·PathRequest·GroundSampler를 감싼다. 5단계에서 심 내부로
    { bool IsWalkable(Vector3)·float TerrainSpeedAt(Vector3)·float GroundHeightAt(Vector3)·bool HasFlowField·Vector3 FlowDirectionAt(Vector3)·
      Building FindBreachTarget(Vector3, float)·void FindPath(Vector3, Vector3, Action<IReadOnlyList<Vector3>>)·void FindBlockingBuilding(Vector3, Vector3, Action<Building>) }
    public sealed class MonsterSystem                 // 틱 주체: 두뇌 → 이동 → 군중 보정. BattleManager가 구동(FactoryBootstrap이 FactorySystem을 구동하듯)
    { MonsterSystem(EntityWorld world, INavigation nav); Entity Spawn(MonsterDataSO data, Vector3 pos, Faction f = Monster); Despawn(Entity); Tick(dt);
      IReadOnlyList<Entity> Monsters; event Action<Entity, MonsterDataSO> Spawned; }
    // EntityWorld에 공간 질의 추가: QueryRadius(Vector3 center, float radius, Faction? faction, List<Entity> out) — 균일 격자 해시(셀 8m), Position setter가 갱신
    // CrowdSystem → MonsterSystem 안의 한 패스 (Movement.CrowdRadius·IsMoving·Owner.Position 위에서, walkable은 nav)
}
```
- `Entity`에 `Facing`(수평 방향) 추가 — 이동이 몸을 돌리고 뷰가 따라 그린다.
- 시계: `MonsterSystem.Now`(dt 누적). 고정 틱·`World.Now` 통합은 5단계.

**3c 뷰**
- `Monster → MonsterView`(파일·클래스 개명, GUID 유지). `CreatesOwnEntity=false`. 매 프레임(LateUpdate) `transform.position/rotation ← Entity.Position/Facing`. `Alerted`·`AttackRequested`·`Died` 이벤트로 연출.
- **스폰 브리지**: `MonsterSystem.Spawned(entity, data)` → `data.prefab` Instantiate → `MonsterView.AttachEntity`. `BattleManager`(Combat 씬)가 소유 — 건물의 `PlacementBridge`와 같은 자리.
- `AttackRequested(target)`: 뷰가 `EntityViewRegistry`로 표적 뷰를 찾아 `target.ApplyEffects(...)` — 효과 시스템이 뷰에 있는 4단계까지의 다리(FlowFieldState의 GetOrAttach와 같은 성격).
- `Player`(뷰)의 `SensorComponent` → `MonsterSystem.TickPlayerSensor(playerEntity, range)`: `QueryRadius(Monster)`로 감지/해제 콜백. `BattleTower`의 `GetClosestTarget` → `QueryRadius` 최근접. `HostileIntentProbe`·`MonsterOutlineProximity`·`MonsterAnimationSystem`은 `MonsterSystem.Monsters` + 뷰 등록부.
- `MonsterNest`(뷰, 781줄)는 이번엔 **오케스트레이터로 남긴다** — 보스·방어자 스폰을 `MonsterSystem.Spawn` + `brain.SetAsBoss/SetAsNestDefender`로 바꾸고, `NestEngagementZone`(뷰)의 반경을 `EngagementZone` 구조체로 넘긴다. 둥지 자체의 심화(스포너 모듈)는 4단계 이후.
- `WaveSpawnManager`: 프리팹 Instantiate → `MonsterSystem.Spawn(wave.monster, pos)` + `view.Effects.ApplyAll(wave.buffs)`(브리지). 생존 수·전멸 판정은 심 목록.

**3d 세이브** — `CombatSaveModule.MonsterDto`에 `DataId` 추가(어떤 종류인지), 복원은 `MonsterSystem.Spawn(data, pos)` + `brain.RestoreSaveState`. 옛 세이브의 몬스터는 `DataId` 없음 → 기본 종류로 복원(버전 상승, `SaveMigrations`).

**3e 정리** — `MovementComponent`·`SensorComponent`·`StateMachineComponent`·`State/*`·`CrowdSystem`(뷰 쪽) 삭제. `CombatComponent`는 타워·플레이어가 쓰므로 4단계까지 남는다.

**커밋 순서(각각 플레이 가능)**
1. 3a — 효과 영구 지속(duration ≤ 0) · `MonsterDataSO`·임포터·Gd 탭·json `monsters`·프리팹 값 마이그레이션 · `WaveDataSO`(monster·buffs) · 스포너가 데이터에서 프리팹/버프를 읽음 · `wave_settings` 폐기. **몬스터 로직은 아직 뷰** — 동작 변화는 "HP 덮어쓰기 → 버프"뿐
2. `EntityWorld.QueryRadius`(공간 해시) · `Entity.Facing` · `INavigation` 어댑터 · 플레이어/타워 센서를 심 질의로(`SensorComponent` 삭제)
3. 심 `Movement`·`Combat`·`MonsterBrain`·상태기 이식 · `MonsterSystem` · `Monster → MonsterView`(따라 그리기) · 스폰 브리지 · `CrowdSystem` 이식
4. 둥지·웨이브 스포너·HostileIntentProbe·아웃라인·애니를 심 API로 · 세이브 dataId · 뷰 쪽 컴포넌트 삭제
5. 문서 · 검사 스크립트(`Runtime/Sim`에 Navigation/Entities import 금지) · PR

**리스크**
- `Monster.cs`·`MonsterNest.cs`·`WaveSpawnManager.cs`·`BattleTower.cs`는 **팀원이 가장 자주 만지는 파일**이다(AGENTS 옛 안내: "Main active work area"). 커밋 3·4는 휴가 창 안에 끝내거나, 복귀 후 하루 프리즈를 잡아야 한다.
- 보스 인내심·둥지 방어 로직은 검증이 어렵다(밤 강제 + 보스 도발 시나리오를 eval로 재현). 이식은 **동작 변경 0**을 원칙으로, 변수명·주석까지 그대로 옮긴다.
- 세이브 포맷이 바뀐다(몬스터 dataId). 베타 전이라 버전 상승으로 처리.

### 4. 효과 · 공격 · 플레이어 · 둥지 — 완료 2026-08-29 (`feature/combat-sim`, 커밋 6개)

목표: 피해·효과·사망이 **심 안에서 끝난다**. 뷰는 명중을 감지(PhysX)해 심에 넘기고, 심이 정한 결과(HP·넉백·상태)를 그린다.

**설계 초안 (실측 2026-08-29)**
- 지금: 효과는 `EffectSO`(에셋)의 `Apply(EntityView, ctx)`가 **뷰**를 상대로 실행되고, 활성 지속 효과는 뷰가 가진 `EffectController`가 `Time.deltaTime`으로 돈다. 받는 배율은 `EntityView.ReceiveDamage`가 곱한 뒤 심 `Health.Damage`로 넘긴다. 몬스터 공격은 심 `Attack.AttackRequested` → 뷰 `CombatComponent.TryAttack`(다리). 투사체·오라(`ProjectileSystem`)는 콜라이더 → `EntityView.ApplyEffects`. 플레이어·둥지는 뷰 `Awake`가 엔티티를 만든다.
- 심 계약(`Runtime/Sim`, 순수 C#):
  - `EffectKind { Damage, Heal, Knockback, DamageOverTime, MoveSpeed, AttackModifier, IncomingDamage }`
  - `EffectSpec`(sealed class, 정의): `Id`·`Kind`·`Duration`(≤0 영구)·`Stacks`(bool, Refresh/Stack)·`TickInterval`·`RadialKnockback`·`Affects: EffectSpec[]`. **SO에서 한 번 변환**해 참조 동일성으로 "같은 효과"를 판정(Refresh·affects). 심은 SO·에셋을 모른다 — `MonsterSpec`과 같은 방식.
  - `Effect`(readonly struct): `Spec` + `Value` — "무엇을 얼마나". 데이터 쪽 `CoreDawn.Combat.EffectEntry`(SO 참조 + value, 인스펙터·json)는 그대로 두고 브리지가 변환한다.
  - `Effects : EntityModule, IDamageInterceptor` — 활성 지속 효과 목록·`MoveSpeedMultiplier`·`IncomingDamageMultiplier`(= `Intercept`가 곱한다: 받는 배율이 `Health.Damage` 안으로 들어가 출처와 무관하게 적용)·`Apply(effects, source, hitPoint, hitDirection)`·`BakeOutgoing`·`AttackMultiplierFor`·`Has`·`Tick(dt)`·`Clear`·`Changed`. 넉백은 `Owner.Get<Movement>().AddKnockback`, 피해·회복은 `Owner.Health`. **Health가 있는 엔티티는 전부 Effects를 갖는다**(FactorySystem.Place·MonsterSystem.Spawn·PlayerSystem·뷰 우선 Awake).
  - `EffectSystem(EntityWorld)`: 매 틱 각 엔티티의 `Effects.Tick(dt)`; `World.Died`에 `Clear`(DoT가 시체를 때리지 않게).
  - `Attack`: `Effects: Effect[]`를 갖고 `TryAttack(target, now)`가 **심에서 적용**(`target.Get<Effects>().Apply(Owner의 BakeOutgoing, Owner, target.Position, dir)`) — 몬스터 다리 제거. `AttackRequested`는 연출용 이벤트로 남는다. 투사체처럼 효과를 위임하는 공격은 `MarkPerformed(now)`로 쿨다운만 소비(타워).
  - `MonsterSpec.AttackEffects`(Effect[]) — `MonsterDataSO.ToSpec()`이 채운다.
  - `PlayerSystem`: 플레이어 엔티티(Faction.Player + Health + Effects)를 심이 만든다. `Spawn(spec, pos)`·`Entity`·`Respawn()`. `MonsterSystem.PlayerEntity`는 여기서 받는다.
- 브리지(`Runtime/Combat`·`Entities`):
  - `EffectSO`는 **데이터만**: `Kind` 추상 프로퍼티 + 서브클래스 필드(duration·stacking·tickInterval·mode·affects). `Apply/OnStart/OnTick/OnEnd`·`EffectContext`·`EffectController` 삭제. `EffectSpecs.Of(so)`(SO → EffectSpec 캐시)·`EffectSpecs.ToSim(EffectEntry[])`.
  - `EntityView.Effects` = `Entity.Get<Effects>()`(심 모듈). `ApplyEffects(entries, sourceView, point, dir)`는 변환해 심에 넘긴다. `ReceiveDamage`는 얇은 호환(배율은 이제 심 안). `OnAttackAction`은 심 `Attack.AttackRequested` 릴레이(애니메이션).
  - `ProjectileShot.Effects`는 심 `Effect[]`(발사 시점에 변환·베이크 확정), `Source`(뷰)는 물리 무시·플레이어 피격 연출용으로 유지하고 효과 출처는 `Source.Entity`. `ScaleDamage`/`AppendDamageKnockback`은 변환 전 데이터 항목에서 그대로.
  - 웨이브 버프: 웨이브 결정 시 한 번 변환해 스폰된 심 엔티티에 `Apply`. 총(`Gun`)은 소유자 심 `Effects.BakeOutgoing`.
  - 타워(`BattleTower`): 심 `Attack`(사거리·연사는 `FactorySystem.Place`가 `TowerDataSO`에서 넣는다)으로 쿨다운·즉시 적용 폴백; 투사체는 `MarkPerformed`. `CombatComponent` 삭제.
  - 플레이어: `Player → PlayerView`(`CreatesOwnEntity=false`, BattleManager가 `PlayerSystem.Spawn` 뒤 `AttachEntity`). 물리 이동은 뷰(하이브리드 결정) — 위치는 뷰 → 심으로 미러. `MonsterSystemHost → SimRunner`(몬스터·효과·플레이어 시스템을 한 곳에서 구동; 5단계 WorldRunner의 전신).
  - 둥지: `MonsterNest`도 `CreatesOwnEntity=false` — WorldPopulator가 심 엔티티를 먼저 만들고(Health = NestDataSO.maxHp, Effects) `Place(host)` 뒤 `AttachEntity`.
- 커밋 순서(각 커밋 컴파일·플레이 가능): 1 문서 → 2 심 효과 모델(+SO 데이터화, 투사체·총·웨이브 경로 전환) → 3 공격을 심에서(타워·몬스터, CombatComponent 삭제) → 4 플레이어(PlayerSystem·PlayerView·SimRunner) → 5 둥지 → 6 문서·PR.
- 리스크: 효과 SO의 `Apply` 서명을 지우면 팀원 브랜치의 새 효과 클래스가 깨진다(휴가 중 — PR 본문에 "새 효과는 Kind + 심 Effects 분기" 안내). 웨이브 버프 재적용(세이브 복원 후)은 기존에도 없던 것 — 범위 밖(기록만).

- [x] 4a 설계 초안 (위) — 2026-08-29
- [x] 4b 심 효과 모델: `EffectKind/EffectSpec/Effect/Effects(IDamageInterceptor)/EffectSystem` · `EffectSpecs` 브리지 · SO 데이터화(`Kind`+`BuildSpec`) · `EffectController`/`EffectContext` 삭제 · 투사체(`ProjectileShot.Effects`는 심 `Effect[]`)·총·타워·웨이브 버프 경로 전환 · Health 있는 엔티티마다 Effects · `Movement`가 속도 배율을 심에서 읽음 · 둥지 무적은 `DamageGate` 인터셉터, 플레이어 피격 연출은 `Health.Damaged` — 2026-08-29. 검증: 엔티티 188 전부 Effects, 웨이브 버프 ×1.5가 `Health.Damage` 안에서 적용(4→6), DoT 1초/0.5초 = 2틱 −6, 넉백, 몬스터→플레이어 300→200, 오류 0
- [x] 4c 공격을 심에서: `Attack.Effects`·`TryAttack`이 대상 `Effects`에 직접 적용·`MarkPerformed`(위임 공격) · `MonsterSpec.AttackEffects` · 몬스터 다리(`AttackRequested → CombatComponent`) 제거 · 타워는 `FactorySystem.Place`가 `TowerDataSO`에서 넣은 심 `Attack`(사거리·연사·심 시계) · `CombatComponent` 삭제(타워 프리팹 8개의 폴백 `combat.attackEffects` → `fallbackAttackEffects`로 YAML 이전) · `EntityView.OnAttackAction`은 심 `Attack.Attacked` 릴레이 — 2026-08-29. 검증: 몬스터 12 전부 스펙 효과 보유·연출 이벤트 배선, 건물 피해 3채, 플레이어 300→260, 타워 엔티티에 Attack(36m/0.5s), 오류 0
- [x] 4d 플레이어: `PlayerSystem`(Spawn/Despawn/Spawned·Despawned, 플레이어는 하나) · `Player → PlayerView`(CreatesOwnEntity=false, `PushesPositionToSim`로 뷰→심 위치 미러, OnDestroy가 Despawn) · `MonsterSystemHost → SimRunner`(`Monsters`·`Effects`·`Players`, 러너가 효과→몬스터 순으로 틱; `Players.Spawned`가 `Monsters.PlayerEntity`를 잇는다) · BattleManager가 `Players.Spawn(playerMaxHealth)` 뒤 `AttachEntity` · `ApplicationQuitting`을 EntityView로 — 2026-08-29. 검증: 심 플레이어 Faction.Player 300/300·Effects, 뷰=심·등록부·두뇌 참조 일치, 텔레포트 후 위치차 0.00, 피해 300→240, 오류 0
- [x] 4e 둥지: `MonsterNest.CreatesOwnEntity=false`, WorldPopulator가 심 엔티티(편=데이터 Faction, Health=프리팹 시드 500 — 둥지 데이터 maxHp 1000은 아직 정본 아님, Effects)를 만들어 붙인다. 굳어 있는(씬에 구운) 둥지는 `ConnectPlaced`에서, 런타임 생성은 `PlaceNests`에서 — 같은 `AttachFreshEntity`. WorldPopulator를 안 거친 옛 씬 둥지만 Start에서 경고와 함께 스스로 세운다. `EntityView.SeedMaxHealth` — 2026-08-29. 검증: 둥지 4 전부 심 엔티티·Monster·Building 모듈·DamageGate·500/500, 보스 생존 중 피해 0, Monster 편 엔티티 12(보스 8+둥지 4, 중복 없음), 경고·오류 0
  - 사고: 편집 스크립트를 두 번 돌려 `SeedMaxHealth`·`CreatesOwnEntity`·Start 폴백이 중복됨 — "이미 적용됨" 판정이 접두 일치라 걸러지지 않았다(3단계 사고의 재발). 컴파일 폴링도 컴파일 시작 전에 끝나 "0 오류"를 믿었다 → `EditorUtility.scriptCompilationFailed`가 정본
- [x] 4f 문서(계획서·AGENTS "Sim vs view")·메모리 갱신, `check-sim-imports` 통과 — 2026-08-29. PR은 사용자 승인 뒤

### 5. asmdef · 고정 틱 · 세이브
- [ ] asmdef 분리(Sim / Data / Presentation / App / Editor / Tests)로 불변식 강제
- [ ] 심 자기 시계(`World.Now`, 고정 20Hz — `FactorySim` 10Hz 틱과 같은 방식), 뷰 보간
- [ ] 세이브 = 심 스냅샷, **SharpNBT** 도입. 베타 전이라 구 세이브 호환은 끊고 `SaveMigrations` 버전만 올림
- [ ] 이후(범위 밖): 명령 송신 + 스냅샷 수신(멀티), 행동 등록부(모딩), SoA/Burst(최적화)

총 3~5주. 단계 사이에 기능 작업을 끼워 넣을 수 있다.

---

## 5. 폴더 이동 대응표 (1a, 2026-08-28)

| 이전 `Scripts/Test/…` | 지금 |
|---|---|
| `Entity/{Entity, BuildingEntity, BattleTower, Monster, MonsterNest, Player, TowerState, MonsterVisualController, TowerVisualController, HealthBarUI, WorldHealthBar, NestEngagementZone, HostileIntentProbe}.cs` | `Runtime/Entity/` |
| `Entity/Component/*` · `Entity/State/*` | `Runtime/Entity/Component/` · `Runtime/Entity/State/` |
| `Entity/{FlowField, CostField, PathFinder, PathRequest, Node, pathNode}.cs` · `Entity/Manager/{GridManager, FlowFieldManager, FlowFieldDebugView, PathRequestQueue, GroundSampler}.cs` | `Runtime/Navigation/` |
| `Entity/Manager/{BattleManager, CrowdSystem, MonsterAnimationSystem, NightSpawnPointProvider, NightWaveRewardManager, WaveBalanceSettings, WaveSpawnManager}.cs` | `Runtime/Combat/` |
| `Resource/ResourceNode.cs` (+ `ResourceNodeRegistry`) | `Runtime/Resource/` |
| `UI/ItemSocket.cs` (uGUI, ItemTree 씬·프리팹이 씀) | `Runtime/UI/` |
| `DayTime/DayCycleDebugHUD.cs` | `Runtime/DayTime/` |
| `Factory/{FactoryScenarioTests, FactoryTest}.cs` · `Resource/{ResourceNodeSceneTest, ResourceNodeTests, ResourceNodeTestBehaviour, ResourceNodeStatusLog}.cs` · `Entity/TestCombatBootstrap.cs` | `Tests/` |
| `Resource/Editor/{PlayLoopTestSetup, ResourceNodeAuthoring, ResourceNodeSceneSetup, ResourceNodeTestRunner}.cs` | `Tests/Editor/` |

폴더 meta도 1:1로 옮겼다 (`Test.meta→Tests.meta`, `Entity/Manager.meta→Navigation.meta` 등). 새 GUID는 하나도 없다.

---


## 5b. 폴더 정리 — 계층 배치 (2026-08-29, `feature/folder-layout`)

최상위 = 계층(5단계 asmdef 경계), 계층 안 폴더 이름 = 네임스페이스 마지막 조각. `Data/**`는 전부 `CoreDawn.Data`.

| 이전 `Runtime/…` | 지금 | 네임스페이스 |
|---|---|---|
| `Sim/` | `Sim/` | `CoreDawn.Sim` |
| `Factory/SO/**`·`Combat/SO/**`·`Combat/Effects/**`·`Tutorial/SO/**`·`World/MapDataSO`·`FPS/Weapon/GunData` | `Data/{Items,Buildings,Recipes,Effects,Monsters,Waves,Maps,Tutorial,Weapons}/` | **`CoreDawn.Data`(신설)** |
| `Factory/*`(+`SO/ItemDataHolder`) · `Combat/{BattleManager,Bullet,MonsterSpawner,NightSpawnPointProvider,NightWaveRewardManager,ProjectileSystem,SimRunner,WaveSpawnManager}` · `Entity/HostileIntentProbe` | `Game/Factory/` · `Game/Combat/` | 유지 (`HostileIntentProbe`만 Entities → Combat) |
| `Navigation/` · `GridSystem/` · `World/{World,WorldPopulator,TileRules,PlacedMapObject}` · `DayTime/{DayCycle,TimeManager}` · `Save/**` · `Interactable/` · `Inventory/` · `Tutorial/{Manager,Observer,Progress,InputProbe}` · `Ping/{PingService,PingTargeting,Ping,IPingable}` · `Manager/` · `Sound/` · `Settings/` · `Resource/` | `Game/{Navigation,Placement,Worlds,DayTime,Save,Interaction,Inventories,Tutorial,Pings,Managers,Sound,Settings,ResourceNodes}/` | 유지 |
| `Entity/{EntityView,BuildingView,MonsterView,PlayerView,MonsterNest,BattleTower,EntityViewRegistry,NestEngagementZone,TowerState,*VisualController}` | `Presentation/Entities/` | `CoreDawn.Entities` |
| `Combat/{PooledEffect,MonsterAnimationSystem,MonsterOutlineProximity}` · `Ping/{EpoOutlines,PingOutlineView}` | `Presentation/Visuals/` | **`CoreDawn.Visuals`(신설)** |
| `UI/**`(uxml·uss·PanelSettings 포함) · `Entity/{WorldHealthBar,HealthBarUI}` · `Tutorial/TutorialHUD` | `Presentation/UI/` | `CoreDawn.UI` |
| `FPS/**` · `Input/` · `Ping/PlayerPingInput` · `DayTime/{DayNightLightingView,SkyboxTimeView,DayCycleDebugHUD}` | `Presentation/{FPS,Inputs,DayTime}/` | 유지 (`PlayerPingInput`만 Pings → Inputs) |

GUID는 하나도 바뀌지 않았다(.cs와 .meta를 함께 git mv). `Ping`은 `UnityEngine.Ping`과 겹쳐 Pings 밖에서는 alias. 같은 PR에 둥지 HP 정본(데이터 500)·`HealthComponent`·뷰의 옛 엔티티 생성 경로 삭제.

## 6. 리스크 · 주의

- **팀 충돌이 기술보다 큰 위험.** 대형 이동·이름 변경은 팀원이 없는 창에 짧은 브랜치로 하루 안에 머지. 팀원 복귀 후 첫 안내: "Test 폴더는 Runtime/Tests로 갔다, 리베이스하라".
- **클래스 이름 변경(`BuildingEntity → BuildingView` 등)은 파일 이름과 meta를 함께 옮겨야** 씬·프리팹 참조가 산다 (스크립트는 GUID로 참조). `[MovedFrom]`은 `SerializeReference`·SO 서브클래스 쪽에만 필요.
- **세이브 호환은 5단계에서 깨진다.** 베타 전이라 초기화. `SaveMigrations` 버전 상승.
- **Unity CLI로 매 단계 확인**: `unity command recompile` → `recompile_status` → `get_console_logs level=error`. 플레이 중 recompile 금지.
- 요청 안 한 최적화·정리는 하지 않는다 (이동 단계는 내용 변경 0을 지킨다).

---

## 7. 진행 로그

- **2026-08-28** 계획 합의. `feature/ping-system` 브랜치에 핑 시스템 + UI 분수 + hideFromMenu + 데이터 + 체력바 상한 커밋,
  이어서 1a 파일 이동 커밋. 컴파일·missing script 확인 완료. 사용자 로컬 에셋 작업(Merger/Splitter 모델·프리팹, 저장고 프리팹,
  YellowLight, World 바위 배치, 타이틀 카메라)도 같은 PR에 포함.
- **2026-08-28** uGUI 잔재 제거 (같은 PR). 삭제: `RecipeSelector`/`RecipeSocket`(루트, 미사용) · `ItemSocket` · `InventoryUI` ·
  `InventorySlotUI` · `HotbarUI` · `ItemTooltipUI` · `InventoryManager` · `InventoryPopup` · `CoreRequirementRowUI` · `RecipeSlotUI` ·
  `VolumeSliderUI` · `BuildMenuPopup` · `SplitterFilterPopup` + uGUI 프리팹 5종. 살아 있는 코드의 폴백·바인딩 제거
  (BuildController · SplitterDataSO · PlayerController · PlayerInventoryHolder · HotbarController · GameplayHUDView · PlayerSaveModule).
  **남은 uGUI는 `WorldHealthBar`/`HealthBarUI`(런타임 월드 캔버스) 하나** — 현역이라 유지, UITK 월드 공간 UI 이관은 별도 과제.
  옛 테스트 씬(ItemTree · BuildingTest · Test/*)에는 삭제된 스크립트의 missing script가 남는다 — 게임 씬(World · Title · Bootstrap)은 무관.
- **2026-08-28** PR #111 (`feature/ping-system` → main, 커밋 10개) 머지 완료 → main `8fa89d35`. 같은 지점에서 `develop` 분기·푸시.
  이후 작업은 `develop`에서 `feature/…`로 분기해 develop으로 PR. 다음: 1b 네임스페이스.
- **2026-08-28** 1b 완료 (`feature/namespaces`). 278파일에 `CoreDawn.*` 부여, 런타임 파일의 EditorTools using 23건 정리, alias 4파일.
  검증: 컴파일 0 오류, World 플레이 스모크(부트스트랩 4씬 탑재, 콘솔 오류 0), `CoreDawn.Save.SaveManager` 반사 해석 정상.
  일괄 커밋은 `.git-blame-ignore-revs`에 등록. 다음: 2단계(의존 방향 뒤집기 — `EntityId`·`World`·`Building` 모듈화).
- **2026-08-28** 2단계 완료 (`feature/entity-sim-core`, 커밋 7개 — 위 체크리스트). 매 커밋 컴파일·World 플레이 검증
  (심 엔티티 184개, 나무 HP가 심으로, 코어 플레이어 공격 0·몬스터 50, Kill → 심 제거가 뷰 릴레이보다 먼저, 코어 조회 3경로 일치).
  사고 기록: 개명 스크립트의 `\.Sim\b`가 `CoreDawn.Sim` 네임스페이스까지 바꿨고 그 상태에서 Unity가 재컴파일하지 않아 "0 오류"가 거짓이었다 —
  HEAD로 되돌려 재적용. 교훈: 컴파일 결과는 `recompile_status`가 아니라 콘솔의 `error CS`로 본다.


### 2026-08-29 — 3단계 몬스터 심/뷰 분리 (`feature/monster-sim`, 커밋 5개)
- 커밋 1 데이터(3a) → 2 공간 질의·센서(3b-1) → 3 심 모듈·MonsterView(3b-2·3c·3e) → 4 세이브 DataId(3d) → 5 문서. 각 커밋마다 World 씬 밤 강제 플레이로 검증(오류 0).
- 이름 충돌 둘: Unity 6에 `UnityEngine.EntityId`가 있어 `EntityId → EntityUUID`(EntityKey·Id를 거침; 값도 카운터 → Guid, 아래 참고); 심 공격 모듈은 `CoreDawn.Combat` 네임스페이스와 겹쳐 `Attack`(AGENTS 규칙 "네임스페이스와 타입 단순명 불일치"의 실례).
- 사고 1: 편집 스크립트의 "이미 적용됨" 판정을 접두 일치로 하면 안 된다 — 옛 문구가 새 문구의 접두라 두 번 적용돼 `Health.Damaged`·`Player.OnEntityAttached`가 중복 선언됨. 고유 마커로 판정할 것.
- 사고 2: Unity가 새 파일을 안 집는 경우가 있다 — `AssetDatabase.Refresh()`+`RequestScriptCompilation()`로는 부족했고 `ImportAsset("Assets/Scripts", ImportRecursive|ForceUpdate)` 뒤에 돌아왔다. 컴파일 반영은 eval로 타입 존재(`Type.GetType`)를 확인해야 한다.
- 사고 3: 세이브 왕복 검증에서 복원된 몬스터가 사라져 리팩토링 버그로 오인 — `DespawnAll`에 스택 로그를 넣어 보니 아침 전환(`EndNightEarly → OnDayStarted`)이었다. 시간 기반 사라짐은 먼저 주야 상태를 찍어 볼 것. 임시 진단 로그의 문자열에 `
`을 쓰다 줄바꿈이 끼어 컴파일이 깨졌다 — `Environment.NewLine`으로.
- 정체성 카운터 → UUID(2026-08-29, 사용자 질문 "서버를 고려하면 uuid가 낫지 않나"에서): 카운터의 이점(8바이트·순서·결정론)은 넷코드의 세션 핸들과 서버 권위가 대신하고, 약점(발급자 하나 가정 — 클라 예측·붙여넣기·병합 시 재매김)은 실제 비용이라 바꿈. 아직 어떤 세이브도 번호를 적지 않아 지금이 가장 쌌다. `EntityWorld.NextId/RestoreNextId` 제거, `Create(id, …)` 오버로드 추가. 검증: 엔티티 188 전부 고유·조회 일치, 고정 id 생성·중복 예외, 오류 0.
- 남은 빚(4단계로): 효과 시스템(EffectController)·CombatComponent가 뷰 → 공격 적용이 `AttackRequested` 이벤트 다리 · 플레이어·둥지는 아직 뷰가 엔티티 생성 · `MonsterSystemHost`·`SimHost.World` 정적 접근점(5단계 WorldRunner). (복원된 웨이브 몬스터 카운트 결함은 같은 브랜치에서 수정 — 3d 참고.)


### 2026-08-29 — 4단계 효과·공격·플레이어·둥지를 심으로 (`feature/combat-sim`, 커밋 6개)
- 커밋 1 설계 초안 → 2 심 효과 모델(EffectSpec/Effects/EffectSystem, SO 데이터화, 투사체·총·타워·웨이브 경로) → 3 공격을 심에서(Attack.Effects, CombatComponent 삭제, 타워 프리팹 8개 YAML 이전) → 4 플레이어(PlayerSystem·PlayerView·SimRunner) → 5 둥지(WorldPopulator가 생성 주체) → 6 문서. 각 커밋마다 밤 강제 플레이 검증, 마지막에 세이브 왕복(전투·플레이어) 회귀.
- 이제 피해·효과·사망은 심 안에서 끝난다: 뷰의 진입점은 `EntityView.ApplyEffects(Effect[], Entity, point, dir)` 하나(투사체·오라가 PhysX로 감지해 넘김)이고, 근접은 심 `Attack`이 직접 건다. 받는 배율·보호막·무적·아군 무시는 전부 `Health.Damage`의 인터셉터(Effects·Building·DamageGate).
- 생성 주체: 건물 = FactorySystem, 몬스터 = MonsterSystem, 플레이어 = PlayerSystem, 둥지 = WorldPopulator(월드 생성기가 심에 요청). 뷰 우선(`CreatesOwnEntity=true`)으로 남은 것은 없다 — `EntityView.Awake`의 생성 경로는 옛 씬 호환용.
- 사고: 편집 스크립트 재실행으로 멤버 중복(접두 일치 함정 재발) · 컴파일 폴링이 컴파일 시작 전에 끝나 "0 오류"를 믿음 — `EditorUtility.scriptCompilationFailed`를 정본으로, 폴링은 `isCompiling`이 true→false를 본 뒤에만 끝낸다.
- 남은 빚(5단계로): `SimRunner`·`SimHost.World` 정적 접근점 → WorldRunner(고정 틱, 씬 생명주기, World.Clear) · 둥지 규칙(무적·스폰 포인트)이 뷰 → 술어만 `DamageGate` · 둥지 HP 정본(프리팹 500 vs 데이터 1000) 정리 · 웨이브 버프가 세이브 복원 뒤 재적용되지 않음(기존) · 타워 표적 선택이 뷰(Update) · `HealthComponent` 시드는 옛 씬 호환용.


### 2026-08-29 — 폴더 정리 + 둥지 HP·HealthComponent 정리 (`feature/folder-layout`)
- 사용자 질문(심이 SO 대신 Spec을 쓰는 이유·둥지 HP 500·폴더 분류)에서 출발. 4단계 PR #115 머지 뒤 별도 브랜치.
- 계층 배치(§5b): Sim / Data / Game / Presentation. 새 네임스페이스는 `CoreDawn.Data`·`CoreDawn.Visuals` 둘, 나머지는 유지. 253파일 이동, using 135개 보정, 컴파일 오류는 `Ping` 모호성뿐.
- 둥지 HP 정본을 데이터(500)로 통일하고, 이제 어떤 뷰도 엔티티를 만들지 않으므로 `HealthComponent`·`CreatesOwnEntity`·`EntityView.Awake` 생성 경로·`SeedMaxHealth` 삭제.
- 검증: 컴파일 0 오류, 밤 강제 플레이(둥지·몬스터·플레이어 검증값 동일, UI 문서 렌더), 오류 0.


### 2026-08-29 — 모듈 이름 규칙 + Sim 하위 폴더 (`feature/module-naming`)
- 사용자 지시: 모듈인데 이름에 Module이 없는 타입을 고칠 것. `Health·Effects·Movement·Attack·MonsterBrain·DamageGate·Building` → `*Module`(7개, 60파일). 같은 이름의 프로퍼티(`Entity.Health`·`EntityView.Effects`·`BuildingView.Building`·`MonsterBrainModule.Attack`)는 그대로 — 타입 위치만 바꿨다(생성자 포함).
- `Sim/`을 루트(엔티티·월드·기하·인터페이스) / `Modules/` / `Systems/` / `Definitions/`(EffectSpec·MonsterSpec·Effect)로 나눴다. 네임스페이스는 전부 `CoreDawn.Sim`(Data와 같은 예외).
- 검증: 컴파일 0 오류, 밤 강제 플레이(둥지·몬스터·플레이어 검증값 동일), 오류 0.


### 2026-08-29 — 이름·위치·분할 정리 (`feature/module-naming` 2번째 커밋)
- 이름: `pathNode → PathNode`, `BattleTower → TowerView`, `MonsterNest → NestView`(엔티티 뷰는 `*View`).
- 위치: `SoundManager → Game/Sound`, `DayRegenSystem → Game/DayTime`, `CombatEvents → Game/Combat`, `CameraShakeManager → FPS/Camera`, `UIPopup·UICursor → Presentation/UI`, `ItemDataHolder → Game/Interaction`(네임스페이스는 폴더를 따름).
- 분할: `SimHost` 별도 파일, 몬스터 두뇌 상태 7개 → `Sim/Modules/MonsterBrain/`, `ResourceNode{Registry,Runtime}`, `ProjectileShot`·`FireMode`, **건물 행동 11개 + `IBuildingBehavior`/`IInteractiveBehavior` → `Game/Factory/Behaviors/`**(데이터 SO 파일에서 분리 — 5a 등록부의 전 단계), `Direction·Dir·PortDefinition·BuildingCategory·BeltShape` 각자 파일.
- 검증: 컴파일 0 오류(`entity is not BattleTower` 패턴 하나 수동), 밤 강제 플레이 — 둥지·몬스터·플레이어·타워 배치 정상, 오류 0.

## 8. 세션 재개 절차

1. 이 문서와 `AGENTS.md`를 읽는다. `git branch --show-current`, `git log --oneline -10`으로 어디까지 왔는지 본다.
2. Unity가 떠 있으면 `unity status` → `recompile_status`로 컴파일 상태를 본다.
3. 위 체크박스에서 첫 미완 항목부터 이어간다. 단계 하나가 끝나면 **진행 로그**에 날짜와 결과를 적는다.
