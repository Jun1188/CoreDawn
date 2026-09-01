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
- **Id**: 엔티티 정체성은 **UUID**(`EntityUUID`, Guid v4, `EntityUUID.New()`) — 2026-08-29 사용자 결정(처음엔 64비트 카운터 `EntityId`+헤더 `NextEntityId`). 이유: "발급자 하나" 가정을 버린다 — 클라이언트 예측·구조물 붙여넣기·세이브 병합·서버 간 이동·모드 도구가 만든 엔티티도 번호 재매김 없이 그대로 가고, 세이브 안의 참조(소유자·표적)가 보존된다. 세션용 정수 핸들은 넷코드 라이브러리가 따로 준다. 플레이어(프로필) Guid와 타입 통일. 복원은 `EntityWorld.Create(id, faction, pos)`(중복 id는 예외). 세이브엔 "N" 32자 문자열로(5단계 fNBT 엔티티 레코드).
- **공간 질의는 심 쪽 균일 격자 해시**(셀 4~8m). Job · ComputeShader 불필요. 사격 판정(레이캐스트)과 LOS만 PhysX에 남는다.
- **세이브는 JSON → fNBT**로 교체 예정 (5단계에서 심 스냅샷 직렬화와 함께).
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
- 세이브 포맷은 바뀌지 않는다(HpMax/HpCurrent 그대로). `NextId`는 5단계 fNBT와 함께.
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

### 5a. 데이터 정본을 json으로 · 모듈 조립 — 확정 설계 (2026-08-30, 팀 협의 반영)

목표: **json이 정의의 정본**(`SimDatabase`), 심은 `UnityEngine.Object` 참조 0(결정론적 공장), SO는 뷰 카탈로그(아이콘·프리팹·SFX)로 줄고 나중에 리소스팩(건물·아이콘 — 몬스터는 내장 유지)이 된다. 엔티티는 json `modules[]`에서 조립한다.

**원칙**
- 정본 하나: `GameData.json`(→ `StreamingAssets/packs/<pack>/`). 심 정의 타입이 곧 json 스키마(`[JsonProperty]`), 변환 캐시(`EffectSpecs`·`ToSpec`) 없음. 임포터는 둘로: GameDataEditor는 json만 쓰고, 표현 임포터는 뷰 카탈로그 갱신·id 검증만.
- id는 **위치에서 파생, 저장하지 않음**: `pack:section/name`, 소문자 snake(`coredawn:item/iron_plate`). 팩 폴더 = 네임스페이스. 옛 id(`Item:IronPlate`)는 `SaveMigrations` 변환표.
- 정의(`*ModuleDef`: 불변·정의당 하나·공유)와 런타임 모듈(엔티티당 하나·상태)은 분리. `ModuleDef.Create(entity) → EntityModule`, 모듈은 `Def` 참조를 든다(마크의 Item/ItemStack).
- json 모듈 ↔ 런타임 모듈은 **정의 하나 → 모듈 0 또는 1개**. 0개 = 데이터 전용 정의(다른 시스템이 `entityDef.Get<XDef>()`로 읽음). json에 없는 런타임 모듈은 두지 않는다(`Effects`도 명시; 과도기 `DamageGate`만 예외, `NestSpawner`와 함께 삭제).
- **정체성 마커 없음**: 행동이 있으면 진짜 모듈(`Core`·`NestSpawner`·`ResourceDeposit`), 없으면 값(나무 = Neutral + Health + Loot, 배치 불가). 종류 판정은 `Get<CoreModule>() != null`.
- 모듈 판별은 **명시 표** 컨버터(`"Health" → HealthModuleDef`): 리플렉션·타입명을 json에 쓰지 않는다.
- `Building` 정의의 키는 **평행**(블록으로 묶지 않음): `size·placeable(기본 true)·isDemolishable·isAttackable·requiredCoreTier·threatSeedCost·menuOrder·cost[]`. 둥지·나무·코어만 `placeable: false`.
- 효과 적용은 `{ effect, value, duration[, tickInterval] }` — 레벨 없음, 빠진 값은 정의 기본값, 같은 정의 재적용은 새 값으로 갱신. 반경은 전달(`AuraEmitter`·폭발)의 것.
- 엔티티 모듈 조회는 사전(종류당 하나), 인터페이스(`IFootprint`)는 순회 폴백. 피해 인터셉터 체인은 `HealthModule`이 소유하고 모듈이 자기 등록한다.

**스키마 v2 개요** — 최상위 `items · recipes · effects · entities · waves · tutorial`. `entities` 항목 = `{ displayName, faction, modules[] }`(id는 파생). 모듈 종류(초안, 팀 검토 대상):
| 모듈 | 핵심 값 | 런타임 |
|---|---|---|
| `Health` | maxHp | `HealthModule` |
| `Effects` | — | `EffectsModule` |
| `Building` | size · placeable · isDemolishable · isAttackable · requiredCoreTier · threatSeedCost · menuOrder · cost[] | `BuildingModule`(풋프린트·격자·아군 무시) |
| `Ports` | ports[] (x·y·dir·isInput) | I/O 컨테이너 연결 — `InventoryModule`의 input/output 역할과 짝 |
| `Inventory` | slots{main·hotbar·input·output…} | `InventoryModule`(역할 키별 `ItemContainer`) — 플레이어·저장고·기계 공용 |
| `Crafter` | manual(bool) · recipeFilter · speed | `CrafterModule` — 플레이어 수제작·조합기 공용(레시피·진행·입출력) |
| `Conveyor` | speedTilesPerSec | 벨트 |
| `Extractor` | interval · amount | 채굴기(광맥 `ResourceDeposit`을 격자로 직접 찾음) |
| `Router` | mode(merge/split) | 병합기·분배기 |
| `Core` | tiers[] · shield · hpBonus | `CoreModule`(티어·보호막 인터셉터) |
| `NestSpawner` | spawnPoints · defender · boss · recoveryDays · engagement | `NestModule`(무적 인터셉터 — `DamageGate` 삭제) |
| `ResourceDeposit` | resource · maxStock · regenInterval · manual{seconds, yield} | 광맥 엔티티(현재 MonoBehaviour `ResourceNode` → 심) |
| `Loot` | drops[] | 사망 드롭(현재 `EntityView.dropItem`) |
| `TowerBrain` | range · minRange · fireRate · turnSpeed · aimTolerance · preferHighArc | 표적·조준·발사 시점, 뷰엔 `FireRequested`만 |
| `AmmoConsumer` | ammoFilter · damageMultiplier | 탄약 소비 |
| `AuraEmitter` | radius · interval · effects[] | 감속장 등 |
| `Blocker` / `Trigger` | — / effects[] · once | 울타리 / 지뢰 |
| `Movement` · `Attack` · `MonsterBrain` | 몬스터 값(기존 `MonsterSpec`) | 기존 모듈 |

**순서(각 단계 플레이 가능)**
- [ ] 5a-0 모듈 사전 조회(종류당 하나) · 인터셉터 체인을 `HealthModule`로(자기 등록) · 인터페이스 순회 폴백
- [ ] 5a-1 스키마 v2 + `SimDatabase` + 컨버터 표 + 파생 id + 효과 value/duration + **제네릭 모듈 편집기** + 마이그레이션 + `SaveMigrations` id 변환
  - [x] 5a-1a 골격(2026-08-30): `Sim/Definitions/`에 `Def`·`EntityDef`·`ItemDef`(+Ammo·Weapon 모듈)·`RecipeDef`·`WaveDef`·`EffectSpec`(json)·`Effect`(value·duration·tickInterval)·엔티티 모듈 정의 22종·`SimSchema`(명시 표)·`ModuleDefConverter`·`SimDatabase`(파생 id, 키 규칙 검사, Resolve 패스, strict). `tools/migrate`: v1 `GameData.json` → `StreamingAssets/packs/coredawn/data.json`(entities 25·items 30·recipes 24·effects 7·waves 5, guns·tutorial는 원본 보관) + `tools/id-migration-v1-v2.json`(114개). 검증: 로드 오류 0, 참조 해석(벨트 cost·터렛 탄약·코어 티어·웨이브 몬스터·attack_up affects), `EntityDef.Assemble` 동작. 게임은 아직 SO로 돈다(플레이 회귀 통과)
  - [x] 5a-1b-1 C# 내보내기(2026-08-30): `GameDataExporterV2`(python 마이그레이션의 C# 판, 구조 동일 검증 0 diff)가 GameData 에디터 **저장 시 자동 실행** — v1(편집 형식) → v2 `packs/coredawn/data.json`(게임·모드 형식) + id 변환표, 내보낸 결과를 `SimDatabase`로 즉시 검증(깨진 참조를 저장 시점에 잡음)
  - [x] 5a-1b-2 편집기 방향(2026-08-30 결정, "간단한 쪽"): **A** — 기존 GameDataEditor가 편집 UI로 남고 v2는 저장 시 생성. SO 퇴역(5a-3) 때 편집기를 v2 직접 편집으로 바꾸고 v1 파일 퇴역 — 최종은 v2 하나만 남는다
  - [x] 5a-1c 세이브 마이그레이션(2026-08-30): `SaveMigrations` v1→v2 — 세이브 안의 옛 SO id(건물·아이템·레시피: 건물 id·그릇 슬롯·벨트 아이템·조립기 레시피·채굴기 목표·분배기 필터·바닥 드롭)를 `LegacyId`로 팩 id로 바꾼다. `SaveRefs`는 팩 id만 알고 옛 id 폴백은 없다(없으면 경고). 몬스터(`combat.monsters[].data`)는 아직 SO로 복원하므로 그대로(5a-3). 디스크의 실제 세이브 4개 v1→v3 변환 확인
- [ ] 5a-2 심 모듈화: 정의에서 조립(공장·몬스터·플레이어), 건물 행동 → 모듈, `InventoryModule`·`CrafterModule`(수제작·조합기 통합, `InventoryPanelView.CraftOnce` 제거)·`ResourceDeposit`(광맥 → 엔티티)·`Loot`·`CoreModule`·`TowerBrain`·`NestModule`, 공장을 `Sim/Factory/`로, `IInteractiveBehavior.Interact`는 뷰 등록부로
  - [x] 5a-2a 런타임 팩 로드 + 효과·몬스터를 정의에서(2026-08-30): `PackLoader`(StreamingAssets, BeforeSceneLoad에 `SimHost.DatabaseLoader` 등록) · `SimHost.Database` · `SimDatabase.LegacyId`(옛 id → v2 id, 순수 규칙) · `MonsterSystem.Spawn(EntityDef)` = `def.Assemble` + 내비게이션·시스템 후주입(`MovementModule(def)`, `MonsterBrainModule(def).Bind`) · `MonsterSpawner`가 SO id로 정의를 찾음 · `EffectSpecs.Of`는 팩 정의 우선(SO 폴백 카운트 0 검증) · `MonsterSpec`·`ToSpec` 삭제, `EngagementZone` 분리. 검증: 몬스터 12 정의 조립, 웨이브 버프 = 팩 정의 참조, DoT·넉백·공격·세이브 왕복, 오류 0
  - [x] 5a-2b 아이템·레시피를 정의로(2026-08-30, `feature/item-defs`): 게임 코드의 `ItemDataSO`/`RecipeDataSO` 참조를 `ItemDef`/`RecipeDef`로(인벤토리·컨테이너·벨트·건물 행동·배치 비용·자원 노드·세이브 DTO·UI 뷰·테스트). `ItemType`/`ItemLine`은 `Sim/Definitions/ItemKinds.cs`로 이동. SO는 표현 에셋(아이콘·탄·무기 모듈 프리팹)으로만 남고 `ItemAssets.Of(def)`/`RecipeAssets.Of(def)`(LegacyId 색인)와 SO↔Def 암시 변환 브리지로 UI(`UIItemIcon`·`UIItemOrder`)와 잇는다. `SaveRefs.Item/Recipe`는 옛 id를 `LegacyId`로 읽어 정의를 돌려주므로 세이브 id 변환은 필요 없음. 미사용 `BaseProcessor`·`MachineProcessor` 삭제. 검증: SO 30 ↔ 정의 30 전부 대응, 인벤토리 추가·세이브 왕복·밤 전투·효과·공장 회귀 통과, 오류 0
  - [x] 5a-2c 건물을 정의로(2026-08-30, `feature/building-defs`): `FactorySystem.Place(EntityDef)` — 엔티티는 `def.Assemble`(Health·Effects는 팩 정의, HP 정본 = json maxHp), `BuildingModule`은 `Def`·`Building`(BuildingModuleDef)만 들고 크기·포트는 `BuildingPorts`(정의별 회전 4벌 캐시), 버퍼는 Inventory 정의. **행동 등록부 `BuildingBehaviors`**가 정의의 모듈 조합으로 행동을 고른다(Conveyor→Belt, Crafter→Assembler, Extractor→Miner, Router→Splitter/Merger, Core→Core, TowerBrain/AuraEmitter+AmmoConsumer→Tower, Inventory+Ports→Storage, 그 외 null — `NestBehavior`·`TreeBehavior` 삭제). 행동은 SO 대신 모듈 정의를 든다(`CrafterModuleDef.Recipes`·`ExtractorModuleDef.SpeedMultiplier`·`AmmoConsumerModuleDef.AmmoFilter`·`CoreModuleDef` — 보호막 값 `burnSurplusIntoShield·shieldPerItem·shieldValueByType·baseMaxShield`·`maxShieldBonus`를 정의에 추가, 기본값 = 옛 에셋 값). `SaveRefs.Building`은 정의를 돌려주고 세이브 id는 v2(`coredawn:entity/belt`), 옛 id도 LegacyId로 읽음. `BuildCost`·`ResourceNodeRegistry.CanPlace`·`PlacementSystem.TryPlaceAt`·`PlacementBridge`·길찾기 시드·격자 HP가 정의를 본다. SO는 `BuildingDataSO.Def` + `BuildingAssets.Of(def)`(프리팹·아이콘·커브 메시) 브리지 — 빌드 메뉴·미리보기·TowerView 수치는 아직 SO(5a-2e·5a-3). 테스트 3종은 SO 대신 정의를 코드로 조립(`ItemAmount(item, amount)` 생성자). 검증: 헤드리스 공장 시나리오 15/15, 광맥 테스트 10/10, 실제 배치 경로(벨트 2개 세그먼트 병합·제련로 Assembler·포탑 Tower+AttackModule 사거리 36·광맥 밖 채굴기 거부·세이브 왕복 재사용·철거), 코어 티어 4/보호막 100/HP 1000, 둥지 4/4, 밤 전투·효과·세이브 회귀, 오류 0
  - [x] 5a-2d-1 인벤토리·제작 모듈(2026-08-30, `feature/inventory-module`): `ItemStack`·`ItemContainer`를 `Sim/Inventory/`로(심의 그릇), **`InventoryModule`**(역할 input·output·main·hotbar, `InventoryModuleDef.Create`) — `BuildingModule.Input/Output`은 OnAttach에서 엔티티의 모듈을 받고(없으면 빈 1칸), 행동도 그때 만든다. **`CrafterModule`**(`CrafterModuleDef.Create`) — 수제작(`Hold`/`CraftOnce`, 가방→핫바 소비·핫바→가방 적재·`Overflow`)과 자동(`Step(now)`: 완료·잔여물 배출·시작, 완료 시각 반환·`Delivered`)이 한 모듈; `AssemblerBehavior`는 공장 어댑터(입력 필터·깨우기·Flush·해금 검사·세이브 키 동일). **플레이어 정의 `coredawn:entity/player`**(v1 `player` 블록 → Health 300·Effects·Inventory 7/18·Crafter manual): `PlayerSystem.Spawn(EntityDef)`, `PlayerInventoryHolder`가 Awake에서 엔티티를 먼저 만들고 그 모듈의 그릇을 낸다(BattleManager는 같은 엔티티에 뷰만 붙임). `InventoryPanelView.CraftOnce`·`HandCrafted` 삭제 → 플레이어 `CrafterModule`(튜토리얼은 `Crafted` 구독). `MachineState`는 Sim으로. **회귀 수정**: 5a-2b 이후 `ItemStack.item`이 정의가 되어 인스펙터 시작 아이템·상자 내용물이 직렬화되지 않던 것 — `ItemStackAuthoring`(SO+개수, 옛 필드명 그대로)로 저작하고 런타임에 정의로 바꿈. 검증: 플레이어 정의 조립(HP 300·7/18·홀더·뷰 같은 엔티티)·시작 아이템 6종 복구·CraftOnce/Hold(3초)·플레이어 세이브 왕복·제련로 자동 제작 2회·일시정지·공장 세이브 재사용·시나리오 15/15·광맥 10/10·회귀 오류 0
  - [x] 5a-2d-1 보강(2026-08-30, 외부 조언 반영): ① `ItemContainer(0)`이 1칸이 되던 것을 진짜 0칸으로(아무도 안 세는 유령 칸 제거) ② `InventoryModule`은 프로퍼티 4개 + `Roles`/`ByRole`(역할 이름표는 여기 한 곳) ③ `CrafterModule`이 `InventoryModule`을 직접 읽음(`Bind` 삭제) ④ **세이브 v3**: 건물 `in/out`·플레이어 `hotbar/main` → 역할 키 사전 `containers{}`(`SaveContainers.Capture/Restore`, 저장된 역할이 정의에 없으면 경고) — 옛 키는 `SaveMigrations` v2→v3가 옮기고 읽는 쪽 폴백은 없음 ⑤ 회귀 수정: 5a-2b에서 `DroppedItem.item`이 정의가 되어 씬의 `StartItem_*` 시작 잔해가 안 뿌려지던 것 — 저작은 SO(`FormerlySerializedAs("item")`), 런타임은 정의. 검증: 시작 잔해 15개 스폰, 나무 0칸, 코어 roles=input, 세이브 키 containers, 합성 v1 세이브 → v3, 디스크 슬롯 4개 변환, 회귀 전부 통과
  - [x] 5a-2d-1 보강 2(2026-08-30): **`ItemStack`을 값(`readonly struct`)으로** — `PeekAt`은 복사본, 슬롯 변경은 `SetAt`(상한 초과는 예외)·`TakeAt`·`TryPutAt`만. UI의 in-place `amount` 수정(드래그·분할·QuickMove·핫바 드롭)을 `With(n)`+`SetAt`으로, `MoveStack`은 남은 몫을 반환. `IsEmpty`가 null·item null·amount 0 세 갈래를 대신함. (Unity 6은 C# 9라 `record struct`는 못 씀.) 검증: 복사본 수정이 그릇에 안 닿음, 상한 초과 `TryPutAt` 거절·`SetAt` 예외, Take/Exchange/Consume/MoveStack 잔여/세이브 왕복, 시작 아이템·시나리오 15/15·광맥 10/10·회귀 통과
  - [x] 5a-2d-1 보강 3(2026-08-30, 사용자 지시): **핫바를 가방과 합침**(마크식) — 플레이어 소지품은 그릇 하나(`main` 25칸), 핫바는 "앞 `hotbar`(7)칸"이라는 창(`InventoryModule.HotbarSize`)일 뿐 그릇이 아니다. 그러면 순서 규칙이 코드에서 사라진다: 넣기는 앞 칸(핫바)부터, 빼기는 뒤 칸(가방)부터 — `AddItemToPlayer`·`QuickMove`·수제작·`BuildCost`·`Gun`·코어 납품이 각자 들고 있던 "핫바/가방 먼저"가 전부 없어졌다(건물에서 꺼내면 가방으로 가던 불일치의 원인). `PlayerInventoryHolder.HotbarContainer` 삭제, 패널은 같은 그릇의 0~6/7~24를 두 줄로 그리고 Shift-클릭은 구간 이동(`MoveStack(src, dst, from, to)`). 정의 `main`은 핫바 포함 전체(v1 `player` 블록 25/7). **세이브 단계 통합**(사용자 지시: 팀 작업 전이라 중간 버전을 두지 않음): v1→v2 한 단계 = 팩 id + 역할 키 그릇 + 핫바 병합(옛 가방 슬롯 인덱스 +7), `CurrentSchemaVersion = 2`. 이 기계에 있던 v3 자동 저장 1개는 "더 새로운 버전"으로 거부됨(로컬 전용). 검증: 정의 25/7, 새 아이템이 첫 빈 핫바 칸(6)에, 소비는 가방(10번 칸)부터, 구간 이동, 세이브 키 main, 합성 v1 → v2(권총@0, 옛 가방 0번 → 7), 디스크 v1 슬롯 3개 → v2, 시나리오 15/15·광맥 10/10·회귀 통과
  - [x] 5a-2d-2a `Loot` 사망 드롭(2026-08-30, `feature/resource-deposit`): `LootModuleDef{drops[], dropInventory=true}` → `LootModule`(정의 carrier). 게임의 `LootSpawner`가 `EntityWorld.Died`를 듣고 정의 드롭 + 그릇(Inventory) 내용물을 그 자리에 뿌린다. exporter: 건물에 `drops`가 있거나 버퍼가 있으면 `Loot`(둥지 `drops: [BeastCore×1]`은 v1에 적음; 플레이어·나무·몬스터는 Loot 없음). `NestView.dropItem` 삭제, `FactoryBootstrap`의 제거 시 드롭은 철거(살아 있는 건물)만 — 죽어서 제거된 건물은 건너뜀(이중 드롭 방지). 검증: 둥지 사망 → 괴수핵 1, 파괴된 보관소 내용물 10(두 배 아님), 철거 5, 회귀 통과
  - [x] 5a-2d-2b `ResourceDeposit` 광맥 → 엔티티(2026-08-30, `feature/resource-deposit`): **한 칸짜리** 광맥 엔티티(`ResourceDepositModule`: 재고·다음 생산·누적 채굴, `Accrue(now)`·`TryExtract`), faction Neutral, **Health 없음**(부서지지 않음 — 무적 인터셉터 마커 대신 모듈이 없다), Building 없음(공장 격자를 차지하지 않음). `FactorySystem.Deposits`(칸 색인 `PlaceDeposit/DepositAt/ResourceAt/DepositsUnder/RemoveDeposit`, 생산은 공장 틱에서 정산, 배치 규칙 `CanPlace/CanPlaceMiner`: 덮는 칸 전부가 같은 자원의 광맥 — **부분 덮기 금지**)가 `ResourceNodeRegistry`·`ResourceNodeRuntime`과 세 델리게이트(`GetResourceAt·TryExtractResourceAt·GetExtractIntervalAt`)를 대체. `MinerBehavior`는 덮는 광맥들을 **돌아가며** 1개씩 캔다(2×2 채굴기 = 광맥 넷의 재생 합이 상한). 정의: v1 `deposits` 섹션(자원별 하나 — 철·구리·수정: extractInterval 1·productionInterval 0.5·amount 1·maxStock 20·손 채굴 3초/1) → `entities/*_deposit`(`ResourceDeposit{resource, maxStock, regenInterval, amountPerCycle, manualSeconds, manualYield, extractInterval}`). 맵 광맥은 **자원 + 칸**뿐(`size·extractInterval·maxStock` 제거, `MapData.json`의 2×2·3×3 9개를 1칸 광맥 46개로 펼침, GdMapTab 크기 UI 삭제, `WorldMapPanelView` 1칸). 뷰 `ResourceDepositView`(구 `ResourceNode`, 파일 GUID 유지 → 프리팹 그대로): 모형·기즈모·손 채굴만; 굳은 씬에서도 광맥은 항상 런타임이 세운다(베이커는 굽지 않음). 세이브 `world.nodes` 형식 그대로(칸 키). 테스트: `ResourceNodeTests` 10개를 GameObject 없이 심만으로 다시 씀(부분 덮기·2×2 돌아가며 채굴 포함), `FactoryScenarioTests`는 채굴기 아래 "바닥나지 않는 광맥"을 깔아 줌. 검증: 월드 광맥 46(철 22·구리 14·수정 10) 뷰·색인 일치, 채굴기 배치·생산·mk2 부분 덮기 차단·손 채굴·세이브 왕복 46·시나리오 15/15·회귀 통과
  - [x] 5a-2d-2c 매장량 삭제·광맥 뷰 굽기(2026-08-30, 사용자 지시): 광맥은 **바닥나지 않는다** — `maxStock·regenInterval·amountPerCycle`(정의)·재고·생산 정산 삭제, `ResourceDepositModule`은 자원·난이도·손 채굴·누적 채굴량만. 세이브 `world.nodes`는 `{cell, extracted}`(v1→v2 단계 ④가 `stock/nextAt`을 뗀다). 맵의 광맥 뷰는 다시 **에디터가 씬에 굽고**(맵 임포트 → 베이커, 마커에 칸) 플레이 때 `WorldPopulator.Connect`가 마커의 칸으로 심에 세워 붙인다 — 그래야 플레이하지 않고도 광맥이 보인다. 굳힌 씬에 광맥 뷰가 빠져 있으면(맵만 바뀌고 씬 저장 전) 런타임이 세우되 경고한다. 검증: 굳힌 뷰 46 → 플레이 46 부착·색인, 채굴 7·손 채굴 +1·세이브 키 `cell,extracted`·합성 v1 세이브의 `stock/nextAt` 제거, 심 테스트 6/6, 시나리오 15/15, 회귀 통과
  - [x] 5a-2d-2d 채굴 시간 하나로(2026-08-31, 사용자 지시): `manualSeconds·manualYield` 삭제 — 광맥 정의는 **`extractInterval` 하나**(1개 캐는 초, 손 채굴은 그대로 = 3초에 1개), 채굴기는 `extractInterval ÷ Extractor.speedMultiplier`. 게임 속도를 지키려고 v1 `deposits.extractInterval` 1→3, `Miner.speedMultiplier` 1→3, `MinerMk2` 2→6(채굴기는 여전히 1초·0.5초에 1개). `ResourceDepositView.HoldSeconds = Deposit.ExtractInterval`, 완료 시 `Extract(1)`. 채굴기에 광물 티어 제한은 **없음**(배치 규칙은 "덮는 칸 전부 같은 자원의 광맥"뿐) — 필요하면 광맥 정의에 `requiredExtractorTier` 같은 값을 더한다. 검증: 심 테스트 6/6, 플레이 채굴 7/7초·손 채굴 +1·시나리오 15/15·회귀·콘솔 0
  - [x] 5a-2d-2e 광맥을 Ore 아이템에서 자동 생성(2026-08-31, 사용자 지시): v1 `deposits` 섹션 삭제. Ore 아이템이 `extractInterval`(1개 캐는 초)을 갖고, v2 exporter가 **Ore 아이템마다 `entities/<item>_deposit`**(`ResourceDeposit{resource, extractInterval}`, faction Neutral, 표시명 "<아이템> 광맥")을 낸다 — 팩 id는 전과 같아(`iron_ore_deposit`…) 씬·테스트·`DepositDefs` 무변경. 임포터가 짝을 검사(Ore인데 값 없음 / Ore 아닌데 값 있음 → 오류). 값: 철 2·구리 5·수정 10초, 채굴기 배율 mk1 2·mk2 10(→ 철 1/0.2초, 구리 2.5/0.5초, 수정 5/1초; 손은 2·5·10초). GameData 에디터 Graph 탭의 아이템 패널에 Ore일 때만 "채굴 시간" 필드 + 채굴기별 실제 시간 미리보기. `SimDatabase.LegacySections`·python 도구·id 표에서 `Deposit:` 제거(세이브에 쓰인 적 없는 접두). 검증: 임포터 오류 0(기존 DronePort kind 제외), C#·python 팩 일치(기존 float 노이즈 1건), 심 테스트 6/6, 플레이 구리 채굴 2.5초/개·손 채굴·시나리오 15/15·회귀·콘솔 0
  - [x] 5a-2e-1 포탑·오라·지뢰를 심 모듈로(2026-08-31, `feature/turret-module`, 사용자 결정 A안 "쪼개기"): `TowerBrain` → **`Turret`**(`TurretModuleDef{range·minRange·fireRate·turnSpeed·aimTolerance·preferHighArc·muzzleHeight·aimHeight·hitscan}` → `TurretModule`: 표적(최근접·최소 사거리·유지 여유 0.5m·0.2초 재탐색)·선회(yaw 적분)·정렬·리드(`Sim/Ballistics` — `LinearLead`/`BallisticLead`를 ProjectileSystem에서 이동)·쿨다운·탄 소비 → `FireRequested(TurretShot)`), **`AuraEmitterModule`**(반경 안 적대 전원에게 주기 펄스, 효과는 연료(AmmoConsumer) 또는 정의 effects, 심이 직접 `Effects.Apply` — PhysX 없음), **`TriggerModule`**(지뢰: 반경에 적이 들어오면 터지고 once면 `Health.Kill`). 발사기는 효과를 모른다 — **`IAmmoSource`**(`HasAmmo·TryPeek·TryTake·Bake·Consumed`)에 "쏠 수 있나 · 이번 발은 무엇인가"만 묻는다(사용자 제안). 구현 둘: **`AmmoConsumerModule`**(`TowerBehavior`의 심 절반: 입력 그릇 필터·한 발 소비·피해형 × damageMultiplier → 소유자 `BakeOutgoing`)과 **`FixedAmmoModule`**(`FixedAmmoModuleDef{speed·gravity·explosionRadius·lifetime·pierce·effects}` — 무한·소비 없음; 지뢰의 장약·연료 없는 오라. 아이템을 가리키지 않는다 — 고정 데이터의 건물이 유탄을 알 이유가 없다는 사용자 지적). "타워"는 모듈이 아니라 Building + (AmmoConsumer | FixedAmmo) + (Turret | AuraEmitter | Trigger) 조합. **`Building.walkable`**(지뢰 true): 길찾기(`GridManager`·`PathRequest`·`FlowFieldManager`)가 그 칸을 땅으로 본다 — 몬스터가 밟고 지나간다; 배치 격자는 그대로 차지. **사거리·반경 단위는 m**(플레이어 총 `GunData.range`와 같은 단위 — 사용자 지시): 정의 값이 그대로 미터, 칸 크기 주입 삭제; v1 타워 사거리를 칸→m로 환산(기본 36·중 44·박격포 72·레이저 56·감속장 28), 지뢰 반경 2m. 공장 어댑터 `TurretBehavior`·`AuraBehavior`·`TriggerBehavior`(칸 크기 주입, 깨우기 예약 — 표적 있으면 매 틱·없으면 0.2초·굶으면 안 깨우고 그릇 `Changed`가 깨움, `Consumed` → `NotifyUpstream`, 세이브 `{readyAt, yaw}`/`{readyAt}`, 탄약함 열기). `FactorySystem.Place`의 `AttackModule` 후주입 삭제. `TowerView`는 연출·탄 생성만: `FireRequested` → 리그 총구에서 심의 탄착점을 다시 겨눠(심 총구와 높이가 달라 그대로 쓰면 가까운 표적 머리 위로 지나간다 — 플레이에서 잡음) `ProjectileShot` → `ProjectileSystem.Fire`; 상태(TowerState)는 `TurretPhase`에서 파생; `fallbackData`·`fallbackAttackEffects`·`TowerDataSO` 읽기 삭제. 정의 선택은 **v1 `fireMode`**(Projectile·Hitscan → Turret(hitscan), Aura → AuraEmitter, Trigger → Trigger, None → Blocker; 건물 이름 분기 삭제, 없으면 exporter가 던짐; v1 음수 = SO 기본값으로 채움), 탄의 출처는 v1 `ammoFilter`(→ AmmoConsumer) 또는 `attackEffects`(→ FixedAmmo; 둘 다 없으면 던짐) — GameData 에디터 타워에 Fire Mode 드롭다운·Attack Effects 편집·Walkable 토글, `FireMode.Trigger` 추가. 지뢰 v1: `attackEffects [Damage 420, KnockbackRadial 2]`·`walkable`, `ammoFilter·damageMultiplier` 삭제. `EntityWorld.QueryRadius/QueryClosest`에 조건(`Func<Entity,bool>`)판 — 편 하나가 아니라 "내 편의 적". `AmmoConsumerModuleDef.Resolve`가 필터 항목이 탄약인지 검사. 테스트 `TurretTests` 8개(리드 수식·발사/소비/배율·굶음/깨움·사거리·선회 지연·오라·지뢰·세이브). 검증: 플레이(World)에 기본 포탑·레이저·감속장·박격포·지뢰 + 몬스터 9 → 3초 안에 전부 제거(레이저 즉시 판정·박격포 곡사·감속 3·지뢰 폭발), 시나리오 15/15·광맥·세이브·밤 웨이브 회귀, 콘솔 0
  - [x] 5a-2e-2 플레이어 총을 심으로(2026-08-31, `feature/weapon-module`): 팩 `guns` 섹션을 심이 읽는다 — **`GunDef`**(탄창·재장전·연사 간격(초)·펠릿·사거리 m·받는 탄·배율 + 뷰가 읽는 감각 값; `Resolve`가 ammoFilter가 탄약인지·fireMode가 Projectile|Hitscan인지 검사), 아이템의 Weapon 모듈은 `WeaponItemModuleDef`(총 참조 해석). 플레이어 엔티티에 **`WeaponModule`**(정의 `Weapon`): 총별 `Magazine{round, loaded}`, 든 총, 연사 쿨다운, 재장전 타이머(소지품 main에서 실소비, 없으면 시작 안 함), 빈 탄창 자동 재장전, 탄종 전환(장전 탄 반환, 자리 없으면 거부), 근접(무한)은 늘 가득, 총을 내리면 재장전 취소 → 뷰엔 `Fired(WeaponShot{gun, round, ammo, pellets, effects(배율·버프 구움), hitscan, range})`. 시계는 `PlayerSystem.Now`(러너가 `Players.Tick`). `Gun`(뷰)은 입력을 심에 넘기고(`TryFire/StartReload/TrySwitchAmmo`), 승인된 방아쇠를 조준축·탄퍼짐·펠릿으로 풀어 `ProjectileSystem.Fire`, 소리·총구 화염·피해 비례 넉백(뷰 손맛 값)만; 상태 프로퍼티(CurrentAmmo·IsReloading·ReloadProgress·ReserveAmmo)는 심 창구라 HUD·입력 무변경. `WeaponManager.Equip/Unequip`이 심에 든 총을 알린다. 세이브 `player.weapons[{gun, round, loaded}]`(총 id 키) — 옛 `ammo`(인스펙터 배열 순서)는 v1→v2 단계 ⑤가 뗀다(순서 기반이라 옮길 수 없음). 테스트 `WeaponTests` 7개(자동 재장전·실소비·연사 간격·배율·펠릿·탄종 전환·근접·취소·세이브). 검증: World 플레이 권총 장전 12/예비 48 → 사격·재장전(11→12, 예비 47)·세이브 `weapons`, 플레이어·세이브 회귀, 콘솔 0
  - [x] 5a-2e-3 둥지를 심으로(2026-08-31, `feature/nest-module`): `NestSpawner` → **`Nest`**(`NestModuleDef` — 필드 없음 → `NestModule`): 스폰 포인트의 심 상태(`NestPoint{position, hasBoss, boss, isDestroyed}`), **무적 규칙을 `IDamageInterceptor`로**(스폰 포인트가 하나라도 살아 있으면 0 — `DamageGateModule` 삭제), **보스 사망은 엔티티 `Died` 이벤트로**(뷰 폴링 삭제). **둥지는 복구되지 않는다**(사용자 결정 2026-08-31: 보스를 잡아 둥지를 부수면 끝, 그 둥지에서는 웨이브가 더 나오지 않음 — `recoveryDays`·`destroyedDay`·밤 보충·낮 자리 복구 전부 삭제, 맵 데이터 `NestSpec`의 복구 필드도 삭제). 보스를 **세우는** 것은 아직 뷰(프리팹이 뷰 에셋) — 모듈이 `BossNeeded(i)`로 부르고 뷰가 `MonsterSpawner`로 세워 `BindBoss`. 낮 방어 스폰의 시점·자리(플레이어 거리·화면 가림 = PhysX 레이캐스트)는 뷰에 남음. `NestView`는 자리(Transform)·보스 프리팹·외형·낮 스폰 판정만; `SyncModule`이 포인트를 심에 민다(WorldPopulator 배치·프리팹 둘 다). 세이브(`CombatSaveModule`)는 자리 상태를 모듈에서 읽고 같은 표면으로 복원. 테스트 `NestTests` 4개(무적·보스 사망→자리 파괴·전멸→피해·복원). 검증: World 둥지 4 모듈·무적, 보스 처치 → 자리 파괴·피해 통과, 세이브 왕복·콘솔 0
  - [x] 5a-2e-3b 밤 웨이브를 점수 기반 심 시스템으로(2026-08-31, `feature/nest-module`, 사용자 기획 "둥지 시스템 — 낮의 공격 루프"): `WaveDataSO`·팩 `waves`(일차별 표)·`WaveDef` 삭제 → 팩에 **`wave` 규칙 하나**(`WaveRuleDef`, v1 `wave` 블록, GameData 편집기 웨이브 탭이 규칙 편집기 + 밤별 미리보기). **점수 = (basePoints + 일차×dayPoints + 게이트×gatePoints) × 총량**(basePoints는 사용자 요청으로 추가, 2026-08-31, 데이터 0) — 게이트는 합, 점수가 곧 포인트(명단의 `cost`가 몹 가격: basic 10·spitter 20·boss 60). 밤 총량 = 살아 있는 몫 (1 − r) + **자극 강화분 h(r) = amplitude·r^exponent + linear·r**(r = 파괴 수/전체, 데이터 2·4·0.1 — 사용자가 GeoGebra로 만든 식, 2026-08-31: 곱이 아니라 **합**, 첫 파괴엔 줄고 마지막 둥지는 둘 남았을 때보다 세다, 둥지 수에 무관). 버프 축의 자극 = 총량 ÷ 살아 있는 몫(남은 둥지 하나의 강도; 4/5 파괴면 5.5)이고 **자극 버프**(`stimulusBuffs{effect, base, perStimulus, min, max}` — AttackUp +25%/자극, DamageTaken −15%/자극)는 **진입로 무리를 뺀 이 세계의 모든 몬스터**(버스트·둥지 보스·낮 방어자)에 붙는다 — `WaveSystem`이 `MonsterSystem.Spawned`를 구독하고, 파괴 수가 바뀌면 살아 있는 몬스터에 다시 건다(비중첩 효과라 값 갱신; 복원된 진입로 무리는 `Register(Trickle)`이 `EffectsModule.Remove`로 뗀다). 낮·밤(달) 길이는 웨이브 규칙이 아니라 팩 최상위 **`dayCycle{dayDuration, nightDuration}`**(`DayCycleDef`, 사용자 지적으로 wave에서 분리)이고 `TimeManager`가 팩에서 읽는다(인스펙터 값 삭제, 없으면 예외). 편집기 웨이브 탭 "주야 시계" 그룹이 편집. **`WaveSystem`**(`Sim/Systems`, 러너가 `Waves.Tick`): 밤 시작에 살아 있는 둥지 중 무작위 개수(`nestsPerNightMin..Max`, 0 = 전부)를 골라 그 둥지들의 살아 있는 스폰 포인트를 이 밤의 출구로; 버스트 수 = `burstsPerNight`, 간격 = `targetNightLength` ÷ 버스트 수(둘 다 규칙 — 주야 시계의 밤은 달이 뜨고 지는 시간이지 밤 총 길이가 아니다, 사용자 정정), 버스트마다 출구 하나를 골라 남은 점수 / 남은 버스트를 주고 **명단 가중치**(현재 조건(`minDay`·`minGate`)에 맞는 항목들의 weight 합이 분모)로 살 수 있는 만큼 뽑는다 — 보스도 같은 명단(별도 확률 없음), 자투리는 이월. **진입로 무리**(`trickle`: `NightSpawnPoints`에 basic 3마리 20초마다, 점수 몹의 90%가 죽을 때까지, 자극 버프 없음, 점수와 무관 — 지루함 방지). 폴백(가장자리 스폰 등) 전부 삭제. 밤은 **더 나올 몹도 남은 몹도 없으면** 끝(`NightCleared` → `EndNightEarly`; 죽거나 파괴된 둥지·자리는 출구에서 빠짐). 뷰: `WaveSpawnManager`는 `Spawned(entity, kind)`에 프리팹을 붙이는 어댑터(`MonsterAssets.OfEntity` — `Entity.Def`로 SO 역색인, 지면 스냅), `BattleManager`는 밤 시작에 일차·게이트(코어 티어)·밤 길이·진입로·시드를 넘길 뿐. 세이브 `combat.wave`는 시스템 상태 전체(점수·버스트·타이머·RNG·출구·진입로), 몬스터마다 `wave: burst|trickle`. 테스트 `WaveTests` 9개(점수식·둥지 선택·버스트 분배·명단 가중치·자극 버프·진입로 무리·클리어·둥지 없음·복원). 검증: World 밤 1(40점) → 출구 5·basic 4 스폰(뷰 4)·20초 뒤 진입로 3·전멸 시 즉시 아침·세이브 `wave`·콘솔 0
  - [x] 5a-2f 공장을 심으로(2026-09-01, `feature/factory-sim`): 행동(IBuildingBehavior) 계층 전부 삭제 — 건물 틱은 무엇이 **있느냐**로: 벨트(Conveyor)는 세그먼트 단위 **`BeltSystem`**(구 BeltSegmentManager 개명 + BeltBehavior 흡수), 나머지는 **`BuildingModule.TickModules`**(밀린 출력 → `ISteppable` 전부 한 걸음 → 산출 늘면 재배출 → 입력 줄면 상류 깨움 → 원하는 시각에 깨움 예약; 그릇 `Changed`가 손 넣기 경로를 깨움). 새 인터페이스 **`ISteppable{float Step(now, dt)}`**(다음 깨울 시각 반환 — 공장 이름을 모듈에 안 박음: 건물은 FactorySystem 10Hz 깨움 큐, 플레이어는 매 프레임)·**`ISaveableModule{CaptureState/RestoreState}`**(구 ISaveableBehavior 후계, 저장 키는 옛 행동과 동일). 행동 11개의 행방: Turret·Aura·Trigger·Crafter는 기존 모듈이 ISteppable로(②③), Storage는 공통 틱의 통과 펌핑(PumpPassThrough — 보관함=입력 버퍼, 출력 버퍼 안 거침), Merger+Splitter는 **`RouterModule`** 하나(합류 = 출구 하나짜리 분배 — 출구별 필터·차단·라운드로빈 커서는 모듈, 흘리기는 `PumpRouted`)(④), Miner는 **`ExtractorModule`**(광맥은 배치 확정 때 공장이 `SetDeposits`로 — 모듈은 그리드를 모름)(⑤), Core는 **`CoreModule`**(보호막·준비·소각·흡수 = 상태+로컬 로직) + **`CoreSystem.Wire`**(티어 대리자·확인창 판단·상류 깨움 = 게임)(⑥). E 상호작용은 뷰 등록부 **`BuildingInteractions`**(정의 모듈 조합 → 화면, 패널은 행동 대신 심 모듈을 받음)(①). 심이 몰라야 하는 게임 규칙은 **`FactorySystem.Placed` 이벤트 → `FactoryBootstrap.WireGameRules`**(코어 배선·제작기 기본 레시피 해금 검증). `PortDefinition`·`Direction`·`Dir`·`BeltShape`와 공장 심 6파일(FactorySystem·BuildingModule·BuildingGraph·BuildingPorts·BeltSystem·BeltSegment)을 **`Sim/Factory/`·CoreDawn.Sim**으로(⑧, `check-sim-imports` 통과). 세이브 **v3**: 건물 `behavior` → `modules{키}`(키 = 정의의 모듈 조합, 내부 키 불변) 마이그레이션 + 웨이브 출구를 런타임 UUID → **씬 경로**로(구운 둥지 UUID는 세션마다 새로 나 새 세션 로드에서 출구 전부 유실되던 결함 — v2 출구는 복구 불가라 소리 내어 비움). 같은 가지 선행 수정: 보관 분류 삭제(물류로), 조립기 타이머 규칙 복원(타이머 → 완료 순간 소비·산출, 재료 빼면 초기화 — 손 제작과 동일), 포탑 차폐(가려진 적은 안 쏨, `SimHost.LineOfSight`)·조준점(표적 중심). 검증: 단계마다 컴파일 0 오류 + TurretTests 9/9·ResourceNodeTests 6/6·FactoryScenarioTests 16/16(플레이) + 야간 포탑 실사격·코어 소각·세이브 왕복(웨이브 활성, 모듈 7개 일치)·마이그레이션 단위 확인·콘솔 오류 0
- [ ] 5a-3 뷰 카탈로그(id → 프리팹·아이콘) · SO 삭제 · 인벤토리·UI·배치·세이브가 `Def`+id로
  - 설계(2026-09-01 제안, 승인 대기). 원칙: **구조 일관성** — 팩 id 체계·view 블록 관례·세이브 마이그레이션 관례를 그대로 따르고, SO id와 팩 id의 이중 장부를 단계 안에서 완결한다(과도기 하이브리드 금지).
  - 실측 근거: 팩 v2에 `view` 블록이 이미 있다(items `{icon, iconGuid}` — 시트 GUID + 서브스프라이트 이름, entities `{model, modelGuid}`, monsters `{prefab, prefabGuid}`) — **exporter가 쓰기만 하고 읽는 코드는 0곳**. UI가 쓰는 게임값(type·line·maxStack·hideFromMenu·tier)은 전부 Def에 이미 있음. `*Assets.Of` 4종은 def id → SO 역색인(SO가 살아야 동작). SO↔Def 암시적 변환은 Item·Recipe 양방향. 씬·프리팹의 SO 직렬화 참조 컴포넌트 ~15개(NestView 보스·방어자, Core/FactoryBootstrap 코어, PlacedMapObject, ResourceDepositView, DroppedItem, World 맵, NightWaveRewardManager, 테스트 하네스). `SaveRefs`의 SO Lookup/Index는 호출처 0(죽은 코드). MonsterDataSO 스탯은 폴백 2곳(NestView·BattleManager) 빼고 죽은 무게 — 실사용은 프리팹뿐.
  - [x] 5a-3a 뷰 카탈로그(2026-09-01): **정본은 팩 view 블록(guid)**, 런타임은 GUID 로드가 안 되므로 에디터 **베이커**가 view 블록을 읽어 `ViewCatalogSO`(id → Sprite·GameObject 직접 참조, 평평한 사전 — id는 팩 전역 유일) 하나로 굽는다(Resources, `LoadDefault` 관례). 건물은 모델(fbx)이 아니라 **프리팹**(콜라이더·스크립트 포함)이 필요하므로 v1 건물에 `prefab`/`prefabGuid`(+벨트 커브 `prefabCurveL/R…`)를 **monsters와 같은 키 꼴**로 추가하고 exporter가 view에 실음(v1 채우기는 SO.prefab에서 에디터 유틸로 1회 자동 기록). `*Assets.Of` 4종을 카탈로그 기반 API(`IconOf(id)`·`PrefabOf(id)`·`BeltPrefabOf(id, shape)`)로 교체. 검증: 카탈로그 vs SO 전수 diff(아이콘·프리팹 참조 동일).
  - [x] 5a-3b 소비처 치환(2026-09-01): UI·배치·드롭·둥지·무기 뷰의 SO 참조(ItemDataSO 42파일 등)를 Def+카탈로그로 파일 단위 치환, 암시적 변환 연산자 제거가 마지막 스위치. 인스펙터 SO 필드 ~15개 컴포넌트는 id 문자열(+카탈로그 조회)로 바꾸고 씬 재저장 — **5a-2b 직렬화 회귀 전례**가 있으므로 검증 기준은 "시작 상태(시작 아이템·상자·씬 배치물)가 예전과 같은가"까지.
  - [x] 5a-3c 해금 id 키(2026-09-01, 보상 해금이 세이브에 안 실리는 기존 동작이라 마이그레이션 불필요 — `RecipeUnlocks` 신설): 티어 게이트는 이미 정의 기반. 보상 해금(`RecipeRewardUnlockService`)의 저장 키를 SO id → 팩 id로(세이브 v4 마이그레이션 한 단계), `RecipeDatabaseSO` 퇴역(호출처 6곳).
  - [x] 5a-3d 몬스터(2026-09-01, 세이브 v4·기본 종류 폴백 삭제·뷰 다리 4종 삭제): `CombatSaveModule.MonsterDto.DataId`를 SO id("Monster:Spitter") → 팩 id로(같은 v4 단계 — v1→v2 때 "5a-3에서 함께"로 예약한 항목), 스폰 프리팹은 카탈로그, MonsterDataSO 스탯 폴백 2곳 제거, 보스·방어자 스폰 심 직접(`MonsterSystem.Spawn(def)`).
  - [x] 5a-3e-1 SO·임포터 퇴역(2026-09-01, `feature/retire-so`, 사용자 결정 "3e-1 먼저"): 세 커밋 — ① **튜토리얼을 팩 정의로**(`TutorialStepDef`·`TutorialConditionDef`, SimDatabase `tutorial` 섹션 order→id 순; 조건 19종은 `Game/Tutorial/Conditions` plain C# + `TutorialConditions` 명시 표(SimSchema 방식), `TutorialStep`이 정의+조건 인스턴스; 세이브 **v5**: `tutorial.done` 키 "Tutorial:Mine" → 팩 id) ② **저작 SO 필드 → 팩 id 문자열**(DroppedItem·ItemStackAuthoring(→Game/Inventories)·ResourceDepositView·PlacedMapObject·CoreBootstrap·FactoryBootstrap·NestView·NightWaveRewardManager·Gun(+소리·볼륨·피격 레이어를 컴포넌트로)·MapDataSO 광맥·테스트 하네스; 읽기는 `SaveRefs.Item/Entity/Recipe/Gun/Effect`; WorldPopulator는 코어·둥지를 팩 모듈(Core/Nest)로, 나무는 팩 키 "tree"로; 공용 드롭 프리팹은 `ViewCatalogSO.droppedItemPrefab`) — 일회성 `SoRefMigrator`가 프리팹 4·맵 1·씬 13을 옮기고 삭제됨(프리팹 인스턴스는 오버라이드만) ③ **삭제**: SO 클래스 전부(Data/에는 ViewCatalogSO·MapDataSO·BuildingCategory만), 에셋 100개 + Resources DB 5개, `GameDataImporter`의 SO 생성부·EnsurePrefab/EnsureContract(→ `GameDataJson` = v1 DTO만), DB 인스펙터·스캐너·IDFixer, EffectEntry/EffectSpecs. 편집기 "저장" = v1 저장 + v2 내보내기 + ViewCatalog 베이크, "저장 + 맵 임포트"(맵은 팩 밖이라 SO 유지). `MapDataSO`는 GameDataSO 상속 해제. **프리팹은 남는다**(정적 에셋 — 퇴역은 5a-4). 검증: 커밋마다 컴파일 0·check-sim-imports, 이관 전후 World 시작 상태 스냅샷 동일(소지품 6·상자·잔해 15·광맥 46·배치물 219·건물 174·둥지 4·총 7·튜토리얼; 잔해 14/15는 산포 재시도 랜덤의 기존 동작), 에디트 테스트 5종(9·6·7·4·11) + FactoryScenarioTests 16/16, 세이브 왕복(모듈 7·건물 174/174), v4→v5 단위 확인, 편집기 저장 경로(v2 id 109·카탈로그 52·맵 임포트) 에디트 모드 실행, 콘솔 0
  - [ ] 5a-3e-2 편집기 v2 직접 편집: v1 `GameData.json`·`GameDataExporterV2`·python 이관 도구·`id-migration` 퇴역, 편집기가 `data.json`을 읽고 쓴다. 먼저 결정할 것 — 건물 탭의 역할 프리셋(fireMode 드롭다운 등)을 편집기 편의 계층으로 유지하고 저장만 모듈 조합으로 할지(권장), 모듈 목록을 직접 편집할지. 검증 기준: 편집기 v2 저장 결과 == 현 exporter 출력(바이트 동일).
  - 리스크: 씬 재저장 회귀(시작 상태 비교로 방어) · 아이콘 시트 서브스프라이트 해석(guid → 시트 → 이름으로 베이커가 해석; 이름 해시 fileID라 이름 유지 시 안전) · 몬스터는 5a-4에서도 내장 유지이므로 프리팹 참조는 카탈로그에 남는다(리소스팩 대상 아님).
- [ ] 5a-4 리소스팩: `StreamingAssets/packs/`, 모델(glTFast)·텍스처(`LoadImage`)·팔레트·emission — 건물 → 아이콘 순. 몬스터(애니메이션)는 내장 유지
  - [x] 5a-4a 데이터 형태(2026-09-01, `feature/view-schema`, 사용자 문답 "data에서 view component를 지정하는 게 낫지 않나 · 소리를 따로 분리 · volume·spatial은 재생 쪽"): ① 팩 **`view.type`**(명시 — `ViewSchema.Types` 표: Building·Tower·Deposit·Nest·Monster·Player·Gun, 종류마다 허용 sfx 자리; 없으면·모르면 오류) + **`sounds` 섹션**(`SoundDef` — 표현 전용, `view.clips[]` 변형 클립 묶음 → 재생 때 무작위) + **소리 자리 `SoundUse{sound, volume, spatial}`**(EffectSpec↔EffectUse와 같은 구분 — 볼륨·공간감은 쓰는 자리의 값)이 `view.sfx{이름}`과 팩 최상위 **`sfx`**(공용 자리: ui_click·ui_hover·construct·destroy·warning·item_pickup·mine — 구 CommonSFX enum)에. `Def.View`는 베이스로(총·소리도 view). `ViewSpec`(Data)이 정의당 한 번 읽어 캐시·검증. 카탈로그 Entry에 `clips`, 베이커가 sounds를 굽는다. exporter가 v1 `view{type,sfx}`를 view 블록에 병합하고 ViewSchema로 검증, 생성 엔티티(광맥·플레이어)에 type. 일회성 `SoundHarvester`가 Player.prefab Gun 7·타워 프리팹 6·SoundManager 표 7을 v1 sounds 27종·sfx 7자리·view로 거둠(자리마다 제 이름의 소리 — 합치면 "경고음 = 타워 굶음"처럼 뜻이 섞여 합치지 않음) ② 런타임: `SoundManager.Play(SoundUse, at)`·`PlayCommon(name)`·`ClipOf(id)`; Gun·TowerVisualController·BuildingView의 클립 필드 삭제 → `ViewSchema.Of(def).SfxOf(...)`; TowerRigBuilder 클립 배선 삭제; WeaponSfxBaker는 v1 sounds에 배선 ③ 편집기: **사운드 탭**(소리 묶음 ObjectField·공용 자리 표) + 총·건물·몬스터 패널의 **뷰 조각**(GdViewUI: type 드롭다운 + 자리마다 소리·볼륨·3D); GdGraphTab이 탄약의 bullet/muzzleFlash/hitEffect를 저장 때 지우던 결함(5a-3a부터) 수정 ④ **둥지 보스·방어자 종류는 맵**(`NestSpec.spawnPoints[].boss`·`NestSpec.defender`, v1 `hasBoss` → `boss:"Monster:Boss"`; 프리팹 템플릿 복사 삭제; 편집기 맵 탭은 몬스터 드롭다운). 검증: 커밋마다 컴파일 0, 플레이 sounds 27 전부 클립 해석·엔티티 29 view.type·총 7 자리, 야간 포탑 실사격(소리 경로) 콘솔 0, 편집기 저장 왕복 v1 의미 동일, 시작 상태 스냅샷 동일(둥지 보스 자리 8 = 맵). 남은 것: 몬스터·플레이어 발소리 등 자리 미정(추가 시 ViewSchema 표에 이름만), BGM은 아직 SoundManager 인스펙터.
  - [x] 5a-4b 뷰 조립기 — 총·건물(2026-09-01, `feature/view-assembler`): ① **총**: `WeaponManager.AssembleGuns`가 팩 guns.view(model/modelGuid·pose{position,rotation,scale}(홀더 기준)·muzzle/sight[3](모델 기준 앵커; 모델 안에 MuzzlePoint/SightPos 노드가 있으면 그것)·knockback{effect,perDamage})로 총 7정을 세운다 — Player 프리팹의 손 저작 총 오브젝트 삭제, `weapons` 직렬화 배열 삭제, 피격 레이어는 매니저 필드. PlasmaCutter의 모델 자식 스케일은 pose.scale로 접음. ② **건물**: `BuildingAssembler.Build/BuildGhost` — 루트(칸 크기 배율 = "건물 모델은 칸 단위로 저작" 규약, Entity 레이어) + 카탈로그 model 인스턴스(view.pose, 벨트 커브는 poseCurveL/R 180°) 또는 자리표시 큐브(풋프린트×0.9, 높이 0.6칸, 밑면 지면 — PivotLift 삭제) + 렌더러마다 MeshCollider(비볼록, 스킨드 포함) + view.type별 컴포넌트(Tower: TowerView+TowerVisualController, Building: BuildingView). `TowerVisualController.WireRig`가 모델 노드 이름(YawPivot·PitchPivot·Droop·Recoil·Muzzle_*, view.rig로 재지정 가능)으로 리그를 배선하고 굶음 처짐은 코드(Droop 노드 회전) — Animator·TowerCommon 삭제. 타워 6종 model = `Art/Models/Towers/*Rig.fbx` + pose(구 View 노드 자세), 저장고는 프리팹 시각을 FBX Exporter로 구운 `Storage.fbx`, 자리표시 건물 5종은 모델 없음. **Assets/Prefabs/Buildings 21개 + TowerRigBuilder·TowerAnimationBuilder·TowerRigTest 씬 삭제**, 팩 buildings의 prefab* 키 삭제, 카탈로그 curveL/RModel·ModelOf(def, shape). PlacementBridge(prefabOverride 삭제)·PlacementSystem(미리보기 = BuildGhost)·WorldPopulator.PlaceCore(코어도 조립)가 쓴다; World 재베이크. 검증: 에디트 모드 조립 vs 구 프리팹 렌더러 바운즈 전수 일치(17종 + 벨트 3모양; 자리표시는 밑면 기준·Manufacturer는 정의 크기 2×2), 리그 배선 4종, FactoryScenarioTests 16/16, 야간 포탑 실사격, 총 7정 자세·앵커 일치, 스냅샷 동일, 테스트 5종, 콘솔 0. ③ 사용자 문답(2026-09-01) 후 보강: **칸 크기는 맵의 것** — `MapDataSO.cellSize`(맵 json, 임포터 검증), `World.CellSize`가 맵 값을 내고 씬 인스펙터 사본 삭제(GameBootstrap이 공장·배치·길찾기에 주입), 길찾기 세분화는 `nodeSize`(1m)/칸 크기로 명시(4 가정 제거); **총은 장착 시점에 조립**(EquipWeapon이 만들고 내리면 지움 — 미리 7정을 세우지 않음); **몬스터도 조립**(`MonsterAssembler`: Monster 레이어 + CapsuleCollider(view.collider) + kinematic Rigidbody + `MonsterVisualController.Wire` + MonsterView(deathDelay); 모델은 구 프리팹의 View 노드를 구운 `Art/Models/Monsters/{Basic,Spitter,Boss}.prefab` — 리그·Animator(오버라이드 컨트롤러·아바타)·URP 머티리얼 포함; 팩 monsters.view: model·pose(루트 배율 접음)·collider·attackVariants·hitVariants·deathStyle·sinkDepth·deathDelay). 몬스터 프리팹 3·MonsterRigBuilder·MonsterAssetSetup 삭제(MonsterCatalog·MonsterAnimationBuilder는 컨트롤러 생성용으로 남음). 검증: 조립 vs 구 프리팹 바운즈·캡슐·Animator 일치, 플레이 총 교체(심 동기)·몬스터 14 조립·스냅샷 동일. **남은 프리팹**: MonsterNest(둥지 뷰 — 스폰 자리 Transform 저작), ResourceNode(광맥 — 씬에 굽는 뷰), DroppedItem(공용), 나무(Vegetation 프리팹) — 조립기 적용은 5a-4c와 함께 판단.
  - 순서 결정(2026-09-01, 사용자 동의): **5a-4c 리소스팩 로더 → 5단계 asmdef → 3e-2 편집기 v2 직접 편집 → 고정 틱·SharpNBT 세이브.** 편집기는 데이터 형태가 멈춘 뒤 한 번만 개편한다(4c가 view 블록을 또 바꾸고 asmdef가 편집기 어셈블리 경계를 건드린다). 그동안 v1 편집기에 새 UI를 얹지 않는다(새 view 키는 JsonExtensionData로 왕복 보존).
  - [ ] 5a-4c 리소스팩 로더: `StreamingAssets/packs/<pack>/` 안의 파일(glb·png·ogg)을 런타임에 읽는다 — glTFast(모델·클립), `LoadImage`(텍스처·팔레트·emission), `UnityWebRequestMultimedia`(ogg). view.model/clips/icon이 guid 대신 팩 상대 경로. Animator·오버라이드 컨트롤러 퇴역 → 상태→클립 재생기(몬스터 walk/attack/hit/die, 벨트 블렌드셰이프). 과도기 산출물(`Art/Models/Towers/*Rig.fbx`·`Art/Models/Monsters/*.prefab`·`Conveyor.prefab`·`Storage.fbx`)을 팩 파일로 변환. `ViewCatalogSO`·베이커 퇴역(런타임 로더+캐시). 남은 프리팹(MonsterNest·ResourceNode·DroppedItem·나무) 정리. 아이콘 시트(`iconGuid` + 서브스프라이트 이름)도 팩 png + 좌표표로.
  - [x] 5a-4c-1 파일럿 — 나무 glb 5종(2026-09-01, `feature/resource-pack`): ① **glTFast 6.20.0** 도입 — 런타임 `PackAssets`(Managers)가 `StreamingAssets/packs/<pack>/models/*.glb`를 `GltfImport(UninterruptedDeferAgent)`로 읽어 비활성 템플릿으로 들고, 조립기가 복제. `PreloadAsync(db)`가 정의들의 view.model/modelCurveL/R에 적힌 팩 모델과 그 재질을 전부 읽는다(검증 없이 원시 읽기 — 아이템 view는 type이 없다). ② **`view.model`은 배열**(사용자 결정 "모델 필드 자체를 배열로, 첫 요소가 기본") — 항목은 `{file, materials[]}`(사용자: "모델이 배열이라 재질이 모델이랑 같이 있어야") — `materials[i]`는 glb 재질 **슬롯 i**(프리미티브가 가리키는 재질 인덱스; 사용자: "glb 그냥 인덱스 쓰면 안 됨?")에 꽂을 팩 재질 id. 옛 guid 문자열은 과도기(ViewCatalog). `ViewSpec.Models(key)` → `ModelRef{File, Materials, IsPack}`. `BuildingAssembler.Build(..., variant)`가 `ResolveModel`(팩이면 PackAssets + `BindSlots`, 아니면 카탈로그). ③ **재질은 팩 데이터** — `materials` 섹션(`MaterialDef`, sounds처럼 표현 전용): `view{shader(내장 셰이더 이름), textures{프로퍼티: {file, linear}}, colors, vectors, floats, keywords, renderQueue, tags}`. 셰이더는 코드처럼 내장(원래 결정 "material 내장하고 값만 외부로"), 값·텍스처는 팩(png → `LoadImage`, 4의 배수면 DXT 압축). `PackAssets.MaterialOf(id)`가 Material을 만들고, glb 로드 때 `SlotGenerator`(IMaterialGenerator)가 슬롯마다 자리표시(자홍)를 만들어 인덱스를 기억 → 조립 때 `BindSlots`. 편집기 UI가 없는 동안은 `PackMaterialHarvester.ToV1(material, id)`가 .mat을 v1 항목으로 거둔다(셰이더 기본값과 다른 값만 + 텍스처 guid + 키워드 + 태그); v2 exporter가 텍스처를 `packs/coredawn/textures/`로 복사(png/jpg만). 처음엔 커스텀 셰이더를 glTF PBR로 굽는 바인딩을 넣었다가(사용자: "우리 머티리얼은 내보내기로 한 적 없지 않나") 되돌렸고, 그다음 Resources 폴더 바인딩도(사용자: "왜 material을 Resources에 박아버린거야") 버렸다 — glb에는 슬롯만, 재질은 데이터. 셰이더 이름 `TeamProj/Vegetation Lit` → **`CoreDawn/Vegetation Lit`**. ④ **굳힌 씬은 마커만**: 팩 모델을 씬에 굳히면 런타임 생성 메시가 World.unity에 로컬 객체로 통째로 박힌다(실측 — 나무 5종 메시 내장, 76k줄 diff). 그래서 `BuildingAssembler.Marker`(루트 배율 + `ViewMarker{dataId, variant}` + view.type 뷰 컴포넌트)만 굳히고, 런타임 `WorldPopulator.DressWhenReady`가 preload 뒤 `BuildingAssembler.Dress`로 모델·콜라이더를 입힌다(Connect는 즉시 — BuildingView가 마커에 있다). 코어도 마커(CoreBootstrap RequireComponent). `WorldPlaceableBaker`는 동기로 복귀. ⑤ **내보내기 `PackModelExporter`**(에디터 전용 asmdef `CoreDawn.PackExport.Editor` — `glTFast.Export`가 autoReferenced=false): 프리팹 → LOD0만·콜라이더/LODGroup 제거·**칸 단위로 굽기**(원본 4m 칸 기준이라 1/4)·재질은 빈 임시 머티리얼로 갈아 슬롯만(3.3MB → ~250KB). ⑥ 부팅: `GameBootstrap`이 preload를 **시작만** 하고 씬은 동기로 얹는다 — 기다리면 프레임 1 `Start()`(InputManager·심 엔티티 조회)보다 늦어져 깨진다(실측). 굳지 않은 나무 경로는 `PackAssets.IsReady` 아니면 오류. 검증: 조립 vs 구 프리팹 LOD0 바운즈 5종 정확 일치(렌더러 1·MeshCollider 1·BuildingView·layer 13·재질 = 팩 데이터에서 만든 tree_bark/broadleaf_green), 굳힌 씬 마커 170·렌더러 0, 플레이에서 170개 입힘(코어 렌더러 11·심 연결), 렌더 확인(우리 셰이더 그대로), 스냅샷 PLAYER·DEPOSITS 46·PLACED 219·BUILDINGS 174 동일, 콘솔 오류 0(경고: leaves.png 1366×1600은 4의 배수가 아니라 비압축). ⑦ **부팅 씬(로딩 게이트)** `Assets/Scenes/Boot.unity` + `BootScene`(Managers): 팩 정의 → `PackAssets.PreloadAsync` 완료 → 목표 씬. `BootScene.Enter(scene[, pack])` — `SaveManager.NewGame/Load`가 이 길로 World를 연다(타이틀 복귀는 직행); `pack`을 주면 `PackLoader.CurrentPack` 갱신·`SimHost.Database=null`·`PackAssets.Clear()` 뒤 다시 읽는다(타이틀에서 데이터팩 선택 — 후속). `SaveManager.OnSceneLoaded`는 Boot 씬을 건너뛴다(복원은 World에서). 빌드 씬 목록 Title·Boot·World·…; 에디터에서 World를 바로 재생하는 개발 경로는 preload-후-입히기(DressWhenReady)로 그대로. 로딩 화면은 임시 OnGUI(`PackAssets.Progress`). 검증: Boot 재생 → 26프레임에 World(ready, 7/7, 마커 170/170, 부트스트랩 씬 4, 플레이어), Title→NewGame→Boot→World 21프레임, 오류 0. **남은 것(4c 계속)**: 팩이 쓰는 셰이더의 빌드 포함 보장(참조 없는 셰이더는 스트립됨 — ShaderVariantCollection/AlwaysIncluded), 타워·건물·벨트(블렌드셰이프)·몬스터(Animator → 클립 재생기) glb 이관, 아이콘(png + `frames` 좌표표 json 사이드카)·ogg, `ViewCatalogSO`·베이커 퇴역, 남은 프리팹(MonsterNest·ResourceNode·DroppedItem), 편집기 재질 탭(3e-2). 주의: glTFast 설치가 Burst를 갱신해 **에디터 재시작 필요** 모달("Burst Package Update Detected")이 뜬다 — 미루면 GPUResidentDrawer의 Burst 컴파일 예외가 매 프레임 콘솔을 채운다. CLI 검증 시 플레이 루프는 eval이 들어올 때만 진행된다(에디터 스로틀링) — 비동기 preload 검증은 폴링 eval로.
  - [x] 5a-4c-2 모델 전부 팩으로 — 정적 20종 + Blender 원본 9종(2026-09-01, `feature/resource-pack`, 사용자 "마저 옮겨"): ① **정적 모델(타워 7·건물 7·총 7)**: `PackMigrationTool.MigrateStatic`(Editor)이 v1의 `modelGuid`(건물)·`view.modelGuid`(총)를 읽어 에셋 → glb(`PackModelExporter.ExportObject`, 배율 1 — 칸 단위 저작 그대로) → glb 재질 슬롯 이름으로 Unity 머티리얼을 찾아 `PackMaterialHarvester`로 v1 `materials`에 거두고(id `Material:<Pascal 이름>`) → v1 `models[{file, materials[슬롯]}]`(총은 `view.models`). 내보내기 임시 머티리얼은 원본 이름당 하나(슬롯 중복 방지). v2 exporter: 총 `view.models`→`view.model`, 벨트 `modelsCurveL/R`→`view.modelCurveL/R`, **tif·psd·tga 텍스처는 png로 변환**(`TexturePng` — Blit→ReadPixels; 노멀맵은 linear). `WeaponManager.AssembleGun`이 팩 모델 + `BindSlots`. 재질 22종(FactoryColor·Glass·MoltenColor·타워 텍스처 6·총 팔레트 5·저장고 4·플라즈마 2·나무 2). ② **Blender 원본**(`…/Univ/Blender/CoreDawn/*.blend`, 사용자 제공): 서브에이전트가 Blender 4.4 headless로 glb 9종 — miner·smelter·constructor·splitter·merger(정적, `Body` 루트), **belt·belt_curve_l·belt_curve_r**(셰이프키 모프 41/148/148 + `Belt_Action`/`Belt.L_Action`/`Belt.R_Action` 웨이트 애니), **core**(`SpaceShip_Rig` 아마추어 17본 스킨 + Landing/Takeoff 액션; 익스포터가 아마추어 스케일 0.21을 정규화해 버려 JSON 패치로 복원). 스크립트·보고서는 `tools/blender/`. **Blender glb는 fbx 임포트 대비 Y축 180° 뒤집혀** 나온다(비스킨 오브젝트; 커브 중심·미너 c.x·스멜터 c.z 부호 반전) → `tools/blender/glb_yaw180.py`가 루트 노드에 yaw 180°를 넣어 규약을 맞춤(코어는 아마추어라 안 뒤집힘). 미너 rest 높이는 원본이 0.884(fbx 1.084 — 드릴 포즈 차, 원본 기준으로 감). `Constructor.blend`=`Conveyor_Port.blend`(md5 동일 — 원본 정리 필요). ③ **glb 애니메이션 런타임**: `PackAssets.Load`가 `Animation`(legacy) 자동 재생을 끄고, `BuildingAssembler.PlayLoop`이 `view.loop`/`loopCurveL`/`loopCurveR`에 적힌 클립을 반복 재생(벨트). 상태 애니(타워·몬스터)는 각 뷰 재생기 몫(후속). 검증: 팩 템플릿 vs 카탈로그 원본 바운즈 19종 정확 일치(미너·코어는 위 사유로 차이), 타워 리그 노드(YawPivot·PitchPivot·Droop·Recoil·Muzzle_*) 전부 존재, 플라즈마 커터 MuzzlePoint/SightPos, 재질 바인딩 누락 0, 벨트 3모양 클립 재생(Loop), 에디트 테스트 5종, Boot→World 코어 스킨 11 렌더러·팩 재질, FactoryScenarioTests 16/16, 스냅샷 동일, 렌더 확인(타워·저장고·스멜터·미너·벨트), 콘솔 오류 0. 카탈로그 entries 81→60. **남은 것**: 몬스터 3종(Blender 변환 진행 중 → 클립 재생기), 광맥·둥지·드롭 프리팹, 아이콘·소리·탄환 효과, 카탈로그 퇴역.
  - [x] 5a-4c-3 아이콘·소리·탄환 연출·드롭(2026-09-01, `feature/resource-pack`): ① **아이콘**: v2 exporter가 `iconGuid`의 png(시트 `Item_Icon_Sheet.png` 1536² 22장 + 총 아이콘 7장)를 `textures/`로 복사하고 **좌표표 사이드카** `<파일>.json`(`pixelsPerUnit`, `frames{이름: x,y,w,h,px,py}` — TexturePacker 식, 사용자 결정 "json")을 쓴다; `view.icon = {file, frame}`. 런타임 `PackAssets.IconOf(def)`가 `Sprite.Create`(캐시). 소비처 6곳(`BeltItemView`·`DroppedItem`·`BuildMenuView`·`BuildModeHUDView`·`UIItemIcon`) 교체. ② **소리**: 클립 파일(wav 46·ogg 1)을 `sounds/`로 복사, `sounds.view.clips = ["sounds/x.wav"]`; 런타임 preload가 `UnityWebRequestMultimedia.GetAudioClip`으로 읽고 `PackAssets.ClipsOf(soundId)`; `SoundManager.ClipOf`가 쓴다. ③ **탄환 연출은 내장 등록부**: 파티클·Bullet 프리팹은 팩 파일이 될 수 없어(셰이더처럼 코드 쪽) `Resources/Builtin/Effects/<이름>.prefab`로 옮기고(15 + Polygon Arsenal 2 — guid 유지) 팩은 **이름만**(`view.bullet/muzzleFlash/hitEffect`), `BuiltinEffects.Of(name)`/`AmmoOf(item)`; `TowerView`·`Gun`이 쓴다. ④ **드롭 아이템은 코드 조립**(`DroppedItem.Build` — Rigidbody·콜라이더 2·Visual(SpriteRenderer+ItemRotator+Outlinable+TargetStateListener), 아이콘은 팩) — `DroppedItem.prefab`·`droppedItemPrefab` 참조 퇴역 예정. 검증: preload 아이콘 29/29·소리 47/47(27 sound), IronOre rect (0,1280,256,256) ppu 100, 내장 연출 basic_ammo·plasma_arc 3/3, Boot→World 시작 드롭 15개 아이콘, FactoryScenarioTests 16/16, 스냅샷 동일, 콘솔 오류 0. 카탈로그 entries 60→3(몬스터만).
  - [x] 5a-4c-4 몬스터 — glb 스킨·클립 + Animator 퇴역(2026-09-01, `feature/resource-pack`): ① 서브에이전트가 Blender 4.4로 3D Game Kit fbx(Chomper·Spitter·Grenadier 모델 + `@클립.fbx` 27개)를 `basic/spitter/boss.glb`로 — 스킨(75/75/26 본) + 클립 10/12/20(오버라이드 컨트롤러 슬롯을 모두 덮음; 다중 클립 fbx는 Unity 프레임 범위대로 분할). 루트 모션 채널(`Root`/`Grenadier_Root`)은 `tools/blender/glb_strip_root_motion.py`로 제거(인플레이스). `basic.glb`만 옛 프리팹 대비 180° 뒤집혀 루트 yaw 180°(spitter·boss는 원래 맞음 — 옛 프리팹 바운즈 z 중심 부호로 판정). ② 재질 7종 거둠(`<슬롯>_URP.mat`; Chomper/Spitter/Grenadier 알베도 tif → png, Grenadier 눈 노멀·에미션). v1 `MonsterDto.models`, `view.pose`는 옛 조립 바운즈에 맞춰 계산(basic 0.8811/−0.5742 = 옛 값 그대로, spitter 0.8212/−0.5752, boss 0.6022/−1.793), `view.anim{idle, walk, run, alert, attack[], hit[], death}`(attackVariants/hitVariants 키 삭제 — 배열 길이가 변형 수). ③ **`MonsterVisualController` 재작성**: Animator 파라미터 대신 legacy `Animation`(glTFast) CrossFade — 속도(감쇠)로 idle/walk/run, 한 번 재생(alert·attack·hit·death)은 끝날 때까지 이동 클립을 덮음(`busyUntil`), death는 ClampForever, SinkAway는 피격 후 가라앉기(기존). 없는 클립은 오류 한 번. `MonsterAnimationSystem`: Reduced 티어는 `Advance(step)`(상태 시간 밀고 `Sample`), Full은 `Animation.enabled`(CrossFade 가중치는 Reduced에서 안 풀린다 — 먼 LOD 한정). `MonsterAssembler`가 팩 모델 + `BindSlots` + Animation. 검증: 조립 바운즈 3종 옛 조립과 일치(발·키·앞뒤 부호), 렌더 확인, 플레이 idle 루프·alert·피격·사망 클립 전환, FactoryScenarioTests 16/16, 오류 0. 카탈로그 entries 3→0 — **퇴역 가능**.
  - [x] 5a-4c-5 뷰 카탈로그 퇴역(2026-09-01): `ViewCatalogSO`·`ViewCatalogBaker`·`Resources/ViewCatalog.asset` 삭제, 편집기 "저장"은 v1 저장 + v2 내보내기만. 조립기(건물·몬스터·총)의 guid 폴백 제거 — `view.model` 항목이 `{file, materials}`가 아니면 오류. 옛 몬스터 모델 프리팹(`Art/Models/Monsters`)·Animator 컨트롤러(`Art/Animation/Monsters`)·`MonsterAnimationBuilder`·`MonsterCatalog` 삭제. `DroppedItem.prefab`은 **씬 저작 마커로만 남음**(World의 `StartItem_*` 인스턴스가 시작 잔해 사양 — 지웠더니 DROPS가 비어 되돌림; 사양을 맵 데이터로 옮기는 것은 후속). 검증: preload 136/136, 정적 모델 검증 MISSING 0, Boot→World 170 입힘, 드롭 15, FactoryScenarioTests 16/16, 스냅샷 동일, 오류 0. 남은 프리팹: `ResourceNode_IronOre_A`(광맥)·`MonsterNest`(둥지) — 다음 단계.
  - 설계 결정(2026-09-01, 사용자와 문답): 프리팹의 몫을 셋으로 가른다 — ① 계약 컴포넌트·콜라이더·레이어는 **코드**(런타임 뷰 조립기, 모듈 조합 기준 — EnsureContract의 후계). ② **리그는 블렌더 규약으로 모델에 굽는다**(YawPivot→PitchPivot→Recoil 회전 사슬·총구 empty 포함 — 임포터·조립기를 무겁게 만들지 않는다는 사용자 결정; 현재 타워 프리팹의 리그는 전부 손 저작 빈 오브젝트라 타워 모델들 블렌더 수정 필요). 팩 view.rig는 노드 이름 매핑만. ③ 애니메이션은 glb 내장 클립 + view의 상태→클립 매핑(벨트 ConveyorRun이 이미 fbx 내장 메시 애니 — 모범 케이스). AnimatorController 에셋은 유니티 전용이라 팩에 못 싣고, 상태기는 심(TurretPhase)에 이미 있으므로 얇은 재생기(상태→클립 CrossFade)로 대체. 단순 회전(드릴 등)은 코드 절차 연출 허용. 별도 `anim` 팩 섹션은 공유 수요가 생길 때까지 보류. 등장(Deploy) 연출은 **삭제됨**(2026-09-01 사용자 지시, 아래). `TowerVisualController`는 조립기 도입 때 `TowerView`로 통합 검토(호출자가 TowerView 1:1, 리그가 이름 조회로 바뀌면 별도 컴포넌트일 이유가 소멸 — 소리 에셋 참조도 팩 view로). **프리팹 퇴역은 3e가 아니라 여기(5a-4)다.**

### 5. asmdef · 고정 틱 · 세이브
- [ ] asmdef 분리(Sim / Data / Presentation / App / Editor / Tests)로 불변식 강제
- [ ] 심 자기 시계(`World.Now`, 고정 20Hz — `FactorySim` 10Hz 틱과 같은 방식), 뷰 보간
- [ ] 세이브 = 심 스냅샷, **fNBT** 도입. 베타 전이라 구 세이브 호환은 끊고 `SaveMigrations` 버전만 올림
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

### 2026-08-30 — 5a-2b 아이템·레시피를 정의로 (`feature/item-defs`)
- 심·게임 코드는 `ItemDef`/`RecipeDef`만 본다. SO(`ItemDataSO`·`RecipeDataSO`)는 아이콘·모듈 프리팹을 가진 표현 에셋으로 격하 — `Def` 속성(팩 정의를 `LegacyId`로 찾음)과 암시 변환으로 과도기 브리지, `ItemAssets`/`RecipeAssets`가 정의 → SO 역색인. 5a-3에서 뷰 카탈로그로 대체·삭제.
- 멤버 이름이 바뀐 자리(`craftTime→Seconds`, `inputs→Inputs(List<ItemAmount>)`, `displayName→DisplayName`, `maxStack→MaxStack`, `type→Type`)와 SO↔Def `==` 모호(한쪽을 캐스팅) 48건을 두 차례 스크립트로 정리. `ResourceNodeRegistry`의 파일 전체 `displayName` 치환이 `BuildingDataSO` 줄까지 건드린 것 하나 되돌림(건물은 아직 SO).
- 검증: 컴파일 0 오류, 플레이 — SO 30/정의 30 양방향 대응, `SaveRefs.Item("Item:IronOre")` → `coredawn:item/iron_ore`, 인벤토리 추가 3/3, 밤 전투·DoT·넉백·세이브 왕복·둥지 4/4, 콘솔 오류 0.

### 2026-08-30 — 5a-2c 건물을 정의로 (`feature/building-defs`)
- 심의 건물은 `EntityDef`만 안다: `FactorySystem.Place(EntityDef)`가 `def.Assemble`로 Health·Effects를 만들고(타워는 TowerBrain 정의에서 `AttackModule` 후주입 — 5a-2e까지 과도기), `BuildingModule`은 `Def`/`Building`을 든다. `IsCore`·`IsConveyor`는 정의의 모듈 유무(정체성 마커 없음).
- 행동 등록부 `BuildingBehaviors.Create(b)`가 `BuildingDataSO.CreateBehavior`(SO 종류별 가상 메서드)를 대체 — 순서가 우선순위(포탑도 Inventory+Ports를 갖지만 TowerBrain이 먼저). 행동 없는 건물(나무·둥지·울타리·지뢰)은 null.
- 포트 회전 캐시는 `BuildingPorts`(정의 키). `PortDefinition`·`Direction`·`BeltShape`는 아직 `CoreDawn.Data` — 5a-2f에서 공장과 함께 Sim으로.
- 코어 보호막 값은 v1 json에 없던 값(옛 에셋에만) — `CoreModuleDef`의 기본값으로 옮기고 exporter는 v1에 적히면 실어 준다(v1 편집기 UI는 안 만듦, 5a-3에서 v2 직접 편집).
- 사고: 편집 스크립트가 원문 불일치(빈 줄 하나)로 두 번 중간에 멈춤 — 완료 표식으로 재실행을 막고 나머지만 담은 후속 스크립트로 이어감(중복 없음). `compile_and_check.sh`가 콘솔을 비운 뒤 결과를 읽어 "0 오류"로 보이던 사고 하나 — `Editor.log`의 `error CS`로 교차 확인.
- 검증: 컴파일 0 오류, ResourceNodeTests 10/10(에디트 모드, 팩 아이템 사용), FactoryScenarioTests 15/15(플레이 중 헤드리스), 실제 배치·세이브·철거 경로, 기존 회귀 전부 통과, 콘솔 오류 0. 레시피 해금 SO/정의 판정 불일치 0.

### 2026-08-30 — 5a-2d-1 인벤토리·제작 모듈 (`feature/inventory-module`)
- 그릇(`ItemContainer`)은 심의 것이 됐고 `InventoryModule`이 역할별로 든다. 건물은 정의의 Inventory가 만든 그릇을 OnAttach에서 받는다 — 생성자에서 만들던 것을 옮긴 이유: 엔티티에 붙기 전에는 형제 모듈을 볼 수 없다. 행동 생성도 같은 시점으로.
- `CrafterModule`이 수제작·조립기 제작 로직을 하나로: 조립기 쪽은 `Step(now)`가 상태 기계(완료 보류·잔여물 배출·시작)를 돌리고 공장은 깨우기·Flush·상류 알림만. 원래 코드의 틱 순서(완료 직후 Flush → 같은 틱에 다음 시작)는 `Delivered` 이벤트로 보존.
- 플레이어도 정의로 조립된다(`coredawn:entity/player`). SO가 없는 유일한 엔티티라 v1에 `player` 블록(GameDataImporter.Root.player)을 두고 exporter·python 도구가 같은 모듈로 낸다. 엔티티 생성 주체는 `PlayerInventoryHolder.Awake`(핫바·HUD가 Awake/Start부터 그릇을 읽기 때문) — `PlayerSystem.Spawn`은 이미 있으면 그것을 돌려주므로 BattleManager 경로와 충돌하지 않는다.
- 사고(회귀): 5a-2b에서 `ItemStack.item`을 `ItemDef`로 바꾸자 Unity 직렬화가 그 필드를 버려 Player.prefab의 시작 아이템(권총·절단기…)과 씬 상자 내용물이 조용히 사라져 있었다(플레이 검증이 "아이템 3개 추가"만 봤다). `ItemStackAuthoring`으로 복구 — 교훈: 심 타입을 인스펙터 필드에 바로 쓰지 말 것, 검증은 "시작 상태가 예전과 같은가"까지.
- 편집 스크립트가 원문 불일치(빈 줄)로 두 번 멈춤 → 완료 표식 + 나머지 스크립트. python 마이그레이션 도구는 player까지 같게 유지(slow_field_tower 한 항목의 사소한 차이는 기존 것, 5a-3에서 도구 퇴역).

### 2026-08-30 — 5a-2d-1 보강: 세이브 마이그레이션·역할 키 그릇·유령 칸 (`feature/inventory-module` 2번째 커밋)
- 규칙(사용자): **읽는 쪽에 폴백을 두지 않는다. 조용히 넘어가지 않는다.** 옛 id·옛 키는 `SaveMigrations`가 버전 단계로 한 번에 바꾸고 로그를 남기며, 바꿀 수 없으면 로드가 실패한다. `SaveRefs`의 `LegacyId` 폴백은 제거.
- 세이브 그릇은 역할 키 사전 — 역할이 늘어도 세이브 코드·형식은 그대로. 역할 이름표는 `InventoryModule.Roles/ByRole` 한 곳에서만 붙는다("문자열은 경계에만, 런타임은 타입").
- 외부 조언에서 잡은 실제 버그: `Mathf.Max(1, slotCount)`가 만든 유령 1칸(`InventoryModule`에 없어 아무도 세지 않는 칸). 0칸이 진짜 0칸이면 모든 연산이 자연히 실패해 특수 처리가 필요 없다.
- 시작 잔해 회귀는 시작 아이템 회귀와 같은 원인(정의를 인스펙터 필드에) — 5a-2b가 바꾼 직렬화 필드는 둘뿐임을 확인.

### 2026-08-30 — 5a-2d-1 보강 2: `ItemStack` 값 타입 (`feature/inventory-module` 3번째 커밋)
- 외부 조언의 앨리어싱 지적: mutable 클래스 스택을 `PeekAt`이 라이브 참조로 내주고 "고쳤으면 Touch를 부르라"는 계약은 컴파일러가 못 지킨다(`QuickMove`가 정확히 그 패턴). 값 타입으로 바꾸면 슬롯 변경이 반드시 그릇의 쓰기 경로를 지나 Touch를 빠뜨릴 수 없고, 두 그릇이 같은 스택을 나눠 갖는 일이 타입 수준에서 불가능하다.
- 규모(건물 수백)에서 성능은 이유가 아니다 — 이유는 앨리어싱 차단. 이름(`item`·`amount`)은 그대로 둬 소비처 변경을 최소화했다.

### 2026-08-30 — 5a-2d-1 보강 3: 핫바 병합 (`feature/inventory-module` 4번째 커밋)
- 버그의 원인은 그릇이 둘인 것이었다 — 경로마다 "어느 쪽 먼저"를 따로 정하니 하나는 어긋난다. 그릇 하나 + 인덱스 창으로 바꾸자 규칙이 그릇의 `TryAdd`(앞부터)·`TryConsume`(뒤부터) 하나로 모였다.
- 세이브 마이그레이션은 단계 셋을 v1→v2 하나로 합쳤다(사용자: 다른 사람은 아직 시작 전). 앞으로 형식이 바뀌면 v2→v3부터 다시 단계를 쌓는다.

### 2026-08-30 — 5a-2d-2 사망 드롭·광맥을 심으로 (`feature/resource-deposit`)
- Loot: 무엇을 떨굴지는 정의(`Loot{drops, dropInventory}`), 뿌리기는 게임(`LootSpawner`가 `Died`를 듣고). 철거(살아 있는 건물)는 `FactoryBootstrap`의 제거 경로가, 죽음은 `LootSpawner`가 — 죽어서 제거된 건물은 제거 경로가 건너뛴다(이중 드롭 방지).
- 광맥: 사용자 결정 — 한 칸짜리만(2×2·3×3 노드가 1×1 모형 하나로 보이던 버그의 뿌리), 부분 덮기 금지, 덮는 광맥을 돌아가며 채굴, Health 없음. 그러면 `ResourceNodeRegistry`(정적 색인·세 델리게이트·거절 큐·러너)가 통째로 사라지고 공장이 광맥을 직접 안다.
- 맵 임포트의 베이커가 열린 씬에 배치물을 다시 구워 씬이 dirty가 됐다(디스크 변경 없음). 광맥은 이제 굽지 않는다 — 에디터에는 공장이 없다.
- 사고: 처음 `Populate`가 "굳은 씬이면 잇기만" 해서 광맥이 0개 — 광맥은 굳은 씬에서도 항상 런타임이 세우도록 고침. 검증이 "광맥 수"를 먼저 봤기에 잡혔다.

### 2026-08-30 — 광맥 매장량 삭제 (`feature/resource-deposit` 3번째 커밋)
- 사용자: 매장량은 없애고, 맵에 광맥이 안 보이는 것을 고칠 것. 매장량을 빼자 광맥 모듈은 자원·난이도·누적 채굴량만 남았고 공장 틱의 생산 정산도 사라졌다.
- 광맥 뷰를 다시 굽는 이유: 베이커의 목적("플레이하지 않고도 맵이 보인다"). 심 엔티티는 굽지 않고, 마커(`PlacedMapObject.Cell`)가 칸의 정본이라 격자 수학 없이 런타임이 잇는다.

## 8. 세션 재개 절차

1. 이 문서와 `AGENTS.md`를 읽는다. `git branch --show-current`, `git log --oneline -10`으로 어디까지 왔는지 본다.
2. Unity가 떠 있으면 `unity status` → `recompile_status`로 컴파일 상태를 본다.
3. 위 체크박스에서 첫 미완 항목부터 이어간다. 단계 하나가 끝나면 **진행 로그**에 날짜와 결과를 적는다.

### 2026-08-31 — 채굴 시간 하나로 (`feature/resource-deposit` 4번째 커밋)
- 사용자: `manualSeconds·manualYield`는 없애고 채굴 시간을 늘려 하나로 합치고, 채굴기는 배율로 조절할 것. 광맥은 `extractInterval` 하나만 갖는다 — 손은 배율 1(3초에 1개), 채굴기는 ÷`speedMultiplier`(3·6 → 1초·0.5초). "얼마나 캐기 어려운가"는 땅, "얼마나 빠른가"는 채굴기.
- 채굴기 광물 티어 제한은 아직 없다(정의에 없고 배치 규칙도 자원 일치만 본다). 사용자 답을 기다림.

### 2026-08-31 — 광맥을 Ore 아이템에서 (`feature/resource-deposit` 5번째 커밋)
- 사용자 물음 "광맥을 아이템 목록에서 자동 생성하지 않나?" — 원래(develop)는 광맥 정의가 없었다: 숫자는 맵 노드(`extractInterval·maxStock`, Map 탭)와 `ResourceNode` 프리팹 인스펙터(손 채굴 3초/1 등)에 흩어져 있었고, 5a-2d-2b에서 내가 별도 `deposits` 섹션을 손으로 적었다. 광맥을 구분하는 건 결국 아이템뿐이었으므로 Ore 아이템에서 파생시키는 쪽이 맞다.
- 임포터 `ImportAll`은 SO 사이드카 60여 개를 재직렬화한다(옛 값·`hideFromMenu` 누락·Merger 프리팹 구조) — 이 작업과 무관해 커밋에서 뺐다(git checkout). 5a-3 SO 퇴역 때 정리.

### 2026-08-31 — 5a-2e-1 포탑을 심으로 (`feature/turret-module`)
- 사용자 지적: `AuraEmitterModule`은 하는 일이 타워인데 이름에 타워가 없다 → 이름을 맞추거나 더 쪼갤 것. 결정 A안: 모듈 이름에서 "타워"를 뺀다 — `Turret`(조준 사격)·`AuraEmitter`(일반 펄스)·`Trigger`(기폭)·`AmmoConsumer`(발사 문). 타워는 조합이다.
- 사용자 물음으로 확인: 총(`Gun`)은 아직 전부 뷰(탄창·재장전·연사·탄퍼짐, `GunDataSO`, 팩 `guns`는 심이 안 읽음). 2e-2에서 `WeaponModule`·`GunDef`로 같은 "심 승인 → 뷰 발사" 틀을 적용하기로 함. 그 다음 2e-3 둥지.
- 사고: 첫 플레이에서 포탑이 쏘는데 아무도 안 맞음 — 심은 자기 총구(1.2m)에서 각을 풀고 탄은 리그 총구(더 높음)에서 나가 3m 표적 머리 위로 지나갔다. 뷰가 진짜 총구에서 심의 탄착점(`TurretShot.Impact`)을 다시 겨누도록 고침(표적·리드는 심, 기하는 뷰).
- 사고: exporter가 v1의 음수(생략 신호 `minRange -1`·`muzzleHeight -1`)를 그대로 팩에 실었다 — `minRange -1`은 `minSq = 1`이 되어 1m 안 표적을 건너뛴다. SO 기본값(0·1.2·180·3)으로 채우게 고침.
- 사용자 지적 셋(2026-08-31): ① 지뢰는 밟고 지나가야 한다 → `Building.walkable`(광맥 취급이 아니라 건물 값 — 배치·철거·HP가 있으니). ② 효과는 발사기가 아니라 탄 쪽이 알아야 한다 → `IAmmoSource` + `FixedAmmoModule`(탄이 없어도 쏘는 출처). ③ 지뢰가 유탄 데이터를 끌어오는 건 옛 타워 문법의 잔재 → 지뢰는 자기 정의의 고정 탄. 곡사 리드는 한 걸음 + 등비 외삽으로(별도 커밋).
- 사용자 지시(2026-08-31): 사거리·반경을 m로(총과 같은 단위), 지뢰 2m, 강 진입 비용 30→50. 강 감속(0.5×)은 심 `MovementModule.TerrainMultiplier` → `INavigation.TerrainSpeedAt` → `GridManager.TerrainSpeed` → `TileRules.SpeedMultiplier` 경로가 그대로임을 플레이로 확인(강 위 몬스터 실효 속도 2 = 4 × 0.5, 땅 4).
- 남은 빚: 공장 시계(10Hz)로 포탑이 돈다 — 선회·정렬이 0.1초 단위(연출은 뷰가 매 프레임 보간). 5단계 20Hz 심 시계로 흡수. `Interact`(탄약함 열기)는 아직 행동에 — 2f 뷰 등록부로.

### 2026-08-31 — 5a-2e-2 총을 심으로 (`feature/weapon-module`)
- 포탑에서 만든 "심 승인 → 뷰 발사" 틀을 총에 그대로 적용. 탄창은 총 정의당 하나(같은 총을 둘 들 일이 없다 — maxStack 1)라 세이브도 총 id 키. 총의 감각 값(반동·탄퍼짐·스윙)도 `GunDef`에 두어 총 하나의 수치가 한 정의에 모이게 — 뷰는 `gunData`(SO)를 소리·프리팹의 뷰 에셋으로만 쓴다(5a-3 카탈로그로).
- 남은 빚: 피해 비례 넉백(`Gun.knockbackEffect·knockbackPerDamage`)은 뷰 인스펙터 값 — `GunDef`로 옮길 것. 총의 fireMode·range는 아직 뷰가 SO에서 안 읽고 정의에서 읽지만 SO 필드는 남아 있다(5a-3에서 정리).

### 2026-08-31 — 5a-2e-3 둥지를 심으로 (`feature/nest-module`)
- `DamageGateModule`(뷰의 술어를 꽂던 임시 문)이 사라졌다 — 무적 규칙의 주인(스폰 포인트 상태)이 심으로 왔으므로 모듈이 직접 인터셉터다.
- 남은 빚: 보스·방어자의 종류(MonsterDataSO 프리팹)가 뷰라 보스 세우기는 뷰(`BossNeeded` 응답) — 5a-3 카탈로그 뒤 `MonsterSystem.Spawn(def)`로 심이 직접. 낮 방어 스폰 판정(화면 가림 레이캐스트·플레이어 거리)은 뷰 — 5단계에서 거리는 심, 가림은 뷰 힌트로 나눌 것. 주야 시계도 뷰(`TimeManager`) — 심 시계로 흡수.

### 2026-08-31 — 밤 웨이브를 점수 기반으로 (`feature/nest-module`, 같은 가지)
- 사용자가 원 기획("둥지 시스템 — 낮의 공격 루프")을 꺼내며 웨이브를 다시 설계: 일차별 `WaveDataSO` 표를 없애고 일차·코어 티어·둥지 상태를 종합한 점수만큼 동적으로 스폰. 확정 규칙 — ① `score = (day + gate) × stimuli × (remaining / total)`, 게이트는 **합**(곱 아님) ② 자극은 버프도 준다(피해 감소·공격력 증가) ③ 밤마다 살아 있는 둥지 중 무작위 개수를 골라 그 둥지의 스폰 포인트에서 점수만큼 ④ 명단 weight = 뽑힐 확률 비(분모 = 현재 조건에서 가능한 항목의 weight 합), 보스도 이 weight로(별도 확률 없음) ⑤ 줄줄이가 아니라 **뭉쳐서**(버스트), 목표 밤 길이로 횟수·간격 조절, 버스트마다 스폰 포인트 하나에 점수 일부 할당 ⑥ 레이드 = 낮에 플레이어가 둥지를 습격(지금과 같음), 최종 방어전 = 코어 티어 최대 시 탈출 이벤트(웨이브 시스템 밖) — 내가 낸 '비트 오버라이드'는 오해 ⑦ `nightSpawnPoints`는 지루함 방지 — 점수 몹 90% 처치까지 자극 없는 기본 몹 무리를 주기적으로, 점수와 무관; 가장자리 등 폴백 전부 제거 ⑧ 둥지는 리스폰하지 않는다 ⑨ "score 자체가 point — pointsPerScore는 필요없다": 버스트 100pt에 basic 10·spitter 20·boss 60이면 weight로 차감하며 스폰 ⑩ 더 나올/나온 몹이 없으면 밤을 끝낸다 ⑪ 가지는 부작용 적은 쪽으로 내가 정하라 → 둥지 복구 제거를 `feature/nest-module` 위에서 그대로.
- 점수식의 '일차·게이트' 항에 각각 점수 계수(`dayPoints` 40·`gatePoints` 80)를 두어 점수가 곧 포인트가 되게 했다(사용자의 'pointsPerScore 필요 없음'을 이렇게 해석 — 확인 필요). 초기 수치: 1일 40pt(basic 4) · 3일 120pt(spitter 등장) · 4일+게이트 1 = 240pt(boss 60pt 가능) · 5일 게이트 2 = 360pt.
- 실측: Bootstrap `Systems` 씬의 `TimeManager`는 낮 360초·밤 10초 → 첫 구현(버스트 수 = 밤 길이 ÷ 간격)은 버스트 1회. 사용자 정정: **그 밤 길이는 달이 뜨고 지는 시간**이지 밤 총 길이가 아니다 → 규칙이 `targetNightLength`(40초)·`burstsPerNight`(4)를 들고 간격을 유도, `StartNight`는 밤 길이를 받지 않는다.
- 사용자 정정 2·3: 자극 계수 0.5는 내 임의 튜닝이고 "1보다 작은 계수"가 이상하다는 지적 → 1로 올리자 "2는 너무 커, 기획대로면 일시적으로 공세가 줄어야" + 기획 차트(둥지 5: −1 ≈ 4.3, −2 ≈ 3.7, −3 ≈ 3.2, −4 ≈ 2.7, 마지막은 보스급). 표(파괴 수 → 배율)를 제안했더니 "표 말고 다른 수식을 찾고 편집기 밑에 표를 그려라", 정수는 내 오독. 확정: **자극 = growth^파괴 수**(growth ≥ 1, 1.2 → 총량 5 → 4.8 → 4.3 → 3.5 → 2.1) — 계수가 1 이상이면서 초반엔 손실을 못 메우고 뒤로 갈수록 가속. 자극은 웨이브만이 아니라 둥지가 세우는 보스·잡몹에도 걸려 마지막 둥지가 뚫기 어려워진다("전멸 시도 → 보스급"은 별도 웨이브가 아니라 이 뜻). 뷰(`TimeManager` 인스펙터)의 낮·밤(달) 길이도 팩으로 — 처음 wave 안에 넣었다가 사용자 물음("낮밤 길이도 wave에 저장되나?")으로 별도 `dayCycle` 블록으로 분리. 편집기 미리보기 표는 처음 모노스페이스 라벨에 문자를 채운 것이라 정렬이 깨져 지적받음("uss는 어디에 두고") → USS 클래스 표로 고쳤더니 "4개 5개는 무슨 기준임? 이것도 꺾은선 그래프로" — 둥지 수는 **맵 데이터의 둥지 수**(맵마다 선 하나, 임의 값 없음)로, 둥지 파괴 영향은 기획 차트 꼴의 **누적 막대**(`BarChart`: 붉은 조각 = 살아 있는 둥지의 스폰, 주황 = 자극 강화분, 맵마다 하나), 일차별 점수는 **꺾은선**(`LineChart`, 폭 640px) — 둘 다 Painter2D + `DrawText`, `gd-chart`/`gd-legend` USS. 처음 꺾은선 두 개를 화면 폭으로 늘였더니 "첫 번째는 막대로, 가로로 너무 넓다" 지적.
- 등비식은 "더 가팔라야" 요구와 첫 파괴 감소가 g < N/(N−1)로 묶여 양립 불가 → 후보(비율 거듭제곱·남은 비율 역수·마지막 목표) 표를 냈고, 사용자가 GeoGebra로 직접 식을 정함: g = (n−x)/n, h = 2(x/n)⁴ + 0.1(x/n), f = g + h. 심·편집기·테스트를 그 식으로 교체(`stimulusAmplitude`·`stimulusExponent`·`stimulusLinear`). 둥지 5: 1 → 0.82 → 0.69 → 0.72 → 1.10(마지막 둥지 자극 5.5).
- 사용자 요청으로 인게임 **웨이브 디버그 패널**(`WaveDebugHUD`, F3, 에디터·개발 빌드에서만 스스로 생성, OnGUI): 점수·총량 배율(살아 있는 몫+강화분)·버스트 진행·다음 버스트/무리 시간·자극과 버프값·몬스터 수·이벤트 시간표(밤 시작·버스트·진입로 무리·클리어). 켜자마자 결함 발견 — 40pt를 버스트 5회로 나누면 조각 8pt < basic 10pt라 **첫 버스트가 빈 채** 지나감 → 조각이 가장 싼 몹값보다 작으면 그 값까지 올림(`WaveTests` 11). 사용자 물음에 답: 진입로 무리는 주기마다 진입로 **하나를 무작위로** 골라 `group`(3)마리를 **한 번에** 세운다.
- 웨이브 탭 입력 결함(사용자: "한 글자 입력하면 포커싱 끊기고 ctrl z y도 안 됨"): 숫자 셀이 키마다 `PushHist`하고 자극·기본점수 셀은 본문 전체를 다시 그려 포커스가 끊겼음 + 탭이 `Undo/Redo`를 override하지 않아 셸 단축키가 no-op. → 값은 키마다 반영(`Touch`: 경고·미리보기만, 미리보기는 `previewHost`만 다시 그림), 히스토리는 FocusOut에 한 단계(`Commit`), Undo/Redo는 전투 탭 히스토리로 위임(전투 탭 관례와 같음).
- 남은 빚: 버스트 몹의 자극 버프는 세이브에 안 실린다(효과 미저장, 기존 빚과 같음). `NightSpawnPoints`(진입로)는 아직 씬 오브젝트(뷰) — 맵 데이터로 옮길 것. 보스 세우기는 여전히 뷰(`MonsterAssets`로 프리팹만 붙이므로 5a-3 카탈로그 뒤 심 직접 스폰).

### 2026-09-01 — 5a-2f 공장을 심으로 (`feature/factory-sim`)
- 세션 사고 둘: 편집 스크립트가 5단계(주석 문자열 불일치)에서 중간에 멈춤 — 관례대로 완료 표식 + 나머지만 담은 후속 스크립트로 이어감(1~4단계는 이미 저장돼 있었고 git 상태로 교차 확인). 그 후속 스크립트를 실행하기 직전에 세션 자체가 끊겨, 다음 세션이 jsonl 기록·git 상태·스크래치패드 스크립트로 지점을 복원해 이어감.
- 설계 확정(사용자 문답): ① `Crafter`는 플레이어도 쓰므로 인터페이스에 "Factory"를 안 박는다 — `ISteppable`은 "다음 깨울 시각을 돌려주는 진행"이고 누가 부르느냐만 다르다. ② 필터는 별개 모듈이 아니라 라우팅 규칙의 일부 — `RouterModule` 하나면 합류·분배·다입출력이 전부 되고 Merger/Splitter 구분이 사라진다. ③ 벨트 건물에는 모듈이 없다 — 상태(아이템 위치)가 세그먼트에 있기 때문(벨트 위 집기는 `BeltItemTarget`이라 건물 상호작용 경로를 안 탄다). ④ `ISaveableBehavior`는 저장 주체가 모듈이 됐으므로 `ISaveableModule`로.
- 코어 분할은 5a 결정("모듈 = 상태+로컬 로직, 시스템 = 횡단 로직")대로: 보호막·준비·소각·흡수는 `CoreModule`, 티어 승급·확인창 존재 판단은 게임이라 `CoreSystem.Wire`가 대리자(`TierIndexProvider`·`TierAdvancer`·`AutoRepairAllowed`)로 꽂는다 — 심은 GameManager도 UI도 모른다. 보호막 인터셉트 순서(아군 무시 → 보호막)는 BuildingModule 피해 체인이 보존.
- 공장 심의 마지막 게임 의존 둘(제작기 기본 레시피의 해금 검증 `RecipeDatabaseSO`, 코어 배선)을 `FactorySystem.Placed` 이벤트로 뒤집었다 — 심은 첫 레시피를 고르고, 게임 배선(`FactoryBootstrap.WireGameRules`)이 해금 전이면 도로 물린다. 헤드리스(FactorySystem 직접 생성)는 배선이 없어도 옛 동작과 같다.
- 세이브 정본 결함 수정(사용자 승인 ①): 웨이브 출구가 런타임 UUID 참조라 새 세션 로드에서 전부 유실 — 구운 둥지의 UUID는 `EntityWorld.Create(EntityUUID.New(),…)`로 세션마다 새로 나기 때문. 심(WaveSystem)은 그대로 UUID로 말하고 `CombatSaveModule`이 저장·로드에서 씬 경로(둥지의 안정 키)로 번역한다. v2 세이브의 옛 출구는 복구 불가 — 마이그레이션이 로그와 함께 비운다.
- 세이브 v3 마이그레이션: `behavior` → `modules{키}` — 어느 모듈의 상태였는지는 건물 정의의 모듈 조합이 말한다(`SaveRefs.Building` + `Has<…Def>`, 옛 등록부와 같은 순서). 내부 키(readyAt·yaw·recipe·filters…)는 그대로라 값 변환이 없다.
- 남은 빚: 레시피 해금 판정(`RecipeDatabaseSO`)이 게임 배선·설비 패널에 — 5a-3에서 정의 기반 해금으로. 코어의 그릇 정책(SingleStackPerType·AcceptFilter)은 배선 시점이라 미배선 헤드리스엔 없음(구 동작과 동일 범주). CoreDawn.Factory에 남은 것은 브리지(FactoryBootstrap·PlacementBridge·CoreBootstrap·BeltItemView)와 CoreSystem·RecipeRewardUnlockService뿐. 세이브 = 심 스냅샷(fNBT)·20Hz 심 시계는 5단계 그대로.

### 2026-09-01 — 5a-3a~d 뷰 카탈로그·SO 읽기 제거 (`feature/view-catalog`)
- 실측이 설계를 뒤집었다: 팩 v2에 `view` 블록(items icon+iconGuid·entities model·monsters prefab)이 **이미 있었고 읽는 코드가 0곳** — 카탈로그는 새 구조가 아니라 "이 블록을 읽기 시작"하는 일. 런타임은 GUID 로드가 안 되므로 에디터 베이커가 `ViewCatalogSO`(Resources, id → Sprite·프리팹 직접 참조)로 굽는다. 건물은 fbx가 아니라 프리팹(임포터가 model에서 굽는 산출물 `Assets/Prefabs/Buildings/`)이 필요해 v1에 icon·prefab(+벨트 커브) 참조를 monsters와 같은 키 꼴로 채웠다(1회 유틸, SO에서). 검증은 카탈로그 vs SO 전수 diff 97건 일치.
- 씬·프리팹의 SO 직렬화 필드(~15 컴포넌트)는 **저작층**이라 이번 PR에서 그대로 두고(경계에서 `.Def` 변환 — ItemStackAuthoring 전례), 런타임 읽기만 끊었다. Def → SO 암시 변환을 제거하자 컴파일러가 남은 SO 읽기를 전부 열거해 줬다 — 씬 재저장 없음 = 직렬화 회귀 위험 0. 씬 필드 교체·SO 클래스 삭제는 3e(별도 PR).
- 벨트 모양·회전 기하(InputDirFor·OutputDirFor·MeshYaw·BuildPorts)는 BeltDataSO 정적 → `Sim/Factory/BeltGeometry`. 빌드 메뉴·배치 목록은 팩 정의에서(placeable = 메뉴 노출 — exporter가 hideFromBuildMenu를 placeable=false로 이미 접었음), 총 장착은 `GunDef` 매칭(HotbarController → WeaponManager), 탄 연출(bullet·muzzle·hit)은 카탈로그 항목.
- 몬스터: `MonsterSpawner.Spawn(EntityDef)` + 카탈로그 프리팹, **기본 종류 폴백 삭제**(모르는 id는 소리 내고 건너뜀 — 읽기 폴백 금지 규칙), `MonsterView.Data`(SO 기억) 삭제 — 세이브가 `Entity.Def.Id`를 적는다. 세이브 v4: monsters[].data·둥지 보스 id를 LegacyId로 팩 id 변환(v1→v2 때 예약). 소비처가 사라진 `*Assets` 다리 4종 삭제.
- 남은 빚(3e — 후속 PR): SO 클래스·에셋·데이터베이스·GameDataImporter(v1→SO) 퇴역, GameData 편집기 v2 직접 편집, 씬·프리팹의 SO 필드를 id로 교체 + 씬 재저장(시작 상태 비교 검증), `DroppedItem` 공용 프리팹(ItemDatabaseSO.droppedItemPrefab)·`SaveRefs`의 죽은 SO Lookup 정리. `NightWaveRewardManager`의 누적 클리어 수·보상 해금이 세이브에 안 실리는 것은 기존 동작(별도 논의).
- 검증: 단계(커밋)마다 컴파일 0 오류. 에디트 테스트 5종(Turret 9·ResourceNode 6·Weapon 7·Nest 4·Wave 11) 전부 통과, FactoryScenarioTests 16/16(플레이), 카탈로그 실경로 배치(벨트 커브 BeltLCurve·채굴기 Miner — TryPlaceAt 탐색 배치), 야간 웨이브 몬스터 14/14 카탈로그 프리팹 스폰, 세이브 왕복 일치, v3→v4 변환 단위 확인(팩 로드가 필요해 플레이에서), 콘솔 오류 0.

### 2026-09-01 — 5a-3e-1 SO·임포터 퇴역 (`feature/retire-so`)
- 사용자 결정: 3e를 둘로 — 3e-1(SO 퇴역, 기계적·플레이로 검증 가능)을 먼저, 3e-2(편집기 v2 직접 편집, UX 결정 필요)는 별도 PR.
- 튜토리얼이 마지막 SO 소비자였다(팩에 `tutorial` 섹션은 있는데 런타임이 SO를 읽음): 정의는 심(`TutorialStepDef`), 판정은 게임(조건 클래스 + 명시 표). 조건 값은 팩 json의 평평한 필드(count·seconds·tier·itemType·item) — 편집기 탭이 클래스의 public 필드를 반사로 그리는 관례를 그대로 유지했다.
- 이관은 "id 필드를 옆에 추가 → 에디터 도구로 SO 참조를 id로 복사(프리팹 → 맵 → 씬 순, 인스턴스는 오버라이드만) → SO 필드·클래스 삭제" 세 단계. 시작 상태 스냅샷(플레이 eval)을 이관 전에 떠 두고 매 단계 비교 — 5a-2b 직렬화 회귀 전례에 대한 방어. World.unity의 죽은 오버라이드(`startingItems.Array.data[6].item` — 배열 크기 밖)는 이전에도 적용되지 않던 것이라 그대로 뒀다.
- 총의 뷰 값(소리·볼륨·피격 레이어)은 SO에서 `Gun` 컴포넌트로 — 팩 view에는 소리 자리가 아직 없다(5a-4). `GunDef.BaseDamage`(반동·표기용)는 정의의 기본 탄 즉발 피해 합으로 계산.
- 코어·둥지 정의는 모듈(Core/Nest)로 찾고 나무는 팩 키 "tree"로 — 나무는 역할 모듈이 없다. 맵이 종류를 고르게 되면 맵 데이터로 옮긴다.
- `SimDatabase.LegacyId`는 세이브 마이그레이션 전용으로 남는다(런타임 조회에 쓰지 말 것). `SaveRefs`가 세이브·씬 저작 공용 해석기.
- 남은 빚: 3e-2(편집기 v2 직접 편집), 프리팹 퇴역(5a-4), 씬 파일에 남은 죽은 SO guid 참조(스크립트 필드가 사라져 무시됨 — 다음 저장 때 자연 소멸), 소리 에셋의 팩 view 자리(5a-4).

### 2026-09-01 — 5a-4a 뷰 스키마·소리 (`feature/view-schema`)
- 사용자 지적으로 방향을 바로잡았다: 3e-1 뒤의 연결이 애매했다 — 건물·몬스터는 데이터 → 뷰(카탈로그)인데 총·둥지 보스는 뷰(프리팹) → 데이터(id 문자열)였다. 원칙: **배치(어디에 무엇)만 씬이 적고, 무엇이 어떻게 보이고 들리는가는 전부 데이터 → 뷰.** 그래서 `view.type`은 데이터에 명시(모듈 조합에서 유도하지 않는다 — 모듈 표와 같은 명시 표, 모드가 뷰를 바꿀 수 있다), 소리는 `sounds` 섹션으로 분리, 둥지 보스 종류는 맵으로.
- 사용자 정정: 볼륨·공간감은 소리의 성질이 아니라 **재생 쪽(쓰는 자리)의 값** — 같은 클립을 포탑에선 크게 3D로, UI에선 작게 2D로. `sounds` 항목은 파일 등록부(변형 클립 묶음)만.
- 프리팹 오버라이드 함정: 맵 재임포트가 World를 다시 구울 때 MonsterNest 프리팹의 bossId가 아직 남아 있어 인스턴스 값 = 프리팹 값이라 오버라이드가 기록되지 않았고, 그 뒤 프리팹을 비우자 씬의 보스가 전부 사라졌다 → 프리팹을 먼저 비우고 다시 구워 해결(오버라이드 8). 순서: 프리팹 기본값 정리 → 베이크.
- 편집 스크립트 재실행 함정을 또 겪음(삽입 뒤에도 앵커가 남는 pair는 skip 판정이 안 됨 → 멤버 중복). 재실행 전엔 git checkout으로 되돌리는 게 안전.
- 다음: 5a-4b 뷰 조립기 — 총부터(정의의 view: model·muzzle·sight 노드 이름 + sfx → Player 프리팹의 Gun 7개 삭제), 그다음 건물(`Towers/*Rig.fbx` 직접 소비, rig 노드 이름, 처짐 코드화, TowerVisualController→TowerView), 몬스터, 드롭·광맥, 프리팹 퇴역.

### 2026-09-01 — 5a-4b 뷰 조립기: 총·건물 (`feature/view-assembler`)
- 총이 먼저 — 손 저작 총 7개를 하나의 규칙(모델 + 자세 + 앵커)으로 환원할 수 있는지가 시금석이었다. 앵커는 "모델 안의 이름 노드가 있으면 그것, 없으면 데이터 좌표"라 서드파티 모델(SciFi 팩)과 자체 모델(PlasmaCutter_Model)이 같은 길을 탄다.
- 건물 프리팹 21개의 실체는 셋뿐이었다: 모델(fbx/모델 프리팹) + 루트 배율 4(= 칸) + 콜라이더·레이어·컴포넌트. 그래서 조립기는 "칸 단위 저작" 규약 하나로 루트를 키우고 나머지는 코드가 붙인다. 손으로 만든 유일한 시각(저장고 73 프리미티브)은 FBX Exporter로 구워 모델로 만들었고, 자리표시 큐브 5종은 모델 없음이 정상 상태(조립기가 큐브를 세운다 — PivotLift가 사라진 이유).
- 벨트 커브 함정: 커브 프리팹은 래퍼 프리팹 인스턴스에 180° 오버라이드가 있었다 — 바운즈 비교(중심 부호 반전)로 잡았고 `poseCurveL/R`로 데이터화. 스킨드 렌더러(블렌드셰이프 벨트)엔 콜라이더가 빠졌던 것도 같은 비교로 잡음.
- 검증 방식: "조립 결과 vs 구 프리팹 인스턴스의 렌더러 바운즈 전수 비교"가 이번의 결정적 도구였다 — 눈으로 보지 않고도 자세·배율·커브 방향 회귀를 전부 잡았다. 스냅샷·시나리오 테스트는 심 쪽 회귀용.
- 5a-4a 브랜치(feature/view-schema, PR #131) 위에 쌓았다 — PR은 #131 머지 뒤 base를 develop으로.
- 남은 빚: 몬스터·둥지·광맥·드롭·나무 프리팹(위 목록), 총 view 편집 UI(pose·앵커는 json 직접 — 편집기 뷰 조각엔 type·sfx만), 소리 자리 추가 시 ViewSchema 표, 5a-4c 리소스팩 로더(glb·ogg 런타임 로드 → 카탈로그 베이커 퇴역).
- ③ 사용자 지적 넷: "총은 미리 안 올리고 소켓에 만들어 붙이는 것" → 장착 시점 조립으로; "건물에 칸 배율이 왜 들어감" → 배율의 출처를 맵 데이터(cellSize)로 옮겨 씬 사본 넷을 지움(모델은 여전히 칸 단위 저작); "총 pose·앵커는 json 직접?" → 값은 프리팹에서 거둬 팩에 있고 편집기 UI만 없음; "몬스터도 뺄 수 있지 않나" → 조립기로(Animator 컨트롤러는 모델 프리팹 안에 — 팩 파일 이관은 4c). 길찾기 세분화는 칸 크기/노드 크기(1m)로 명시하라는 지시에 따라 `nodeSize` 필드.

### 2026-09-01 — 5a-4c-1 리소스팩 파일럿: 나무 glb (`feature/resource-pack`)
- glTFast 도입, `PackAssets` 런타임 로더(모델·재질·텍스처), `view.model` 배열(`{file, materials[슬롯]}`), `materials` 섹션(셰이더 내장·값 외부), 굳힌 씬은 마커만(`ViewMarker` + `DressWhenReady`), `PackModelExporter`(칸 단위·LOD0·슬롯만), `PackMaterialHarvester` — 상세는 위 5a-4c-1 항목.
- 사용자 지적 셋: "우리 머티리얼은 내보내기로 한 적 없지 않나"(→ glb에서 재질 값 제거), "재질을 왜 glb에 싣나 · 왜 Resources에 박나"(→ 슬롯 인덱스 + 팩 materials 섹션), "모델이 배열이라 재질이 모델이랑 같이 있어야"(→ 항목 객체). 원래 결정 = "material 내장하고 값만 외부로".
- 실측 교훈: 부팅에서 preload를 await하면 씬 로드가 프레임 1 뒤로 밀려 `Start()` 조회가 깨진다; 런타임 생성 메시를 굳히면 씬 파일에 박힌다 — 씬은 배치만.
- 부팅 씬(`Boot.unity`·`BootScene.Enter`)이 로딩 게이트 — 사용자 지시 "부팅 씬 만들어, 나중에 타이틀에서 데이터팩 수정하면 부팅 씬 불러올 수 있게".
- 다음: 4c 계속(위 "남은 것") → asmdef → 3e-2 → 고정 틱·SharpNBT.
