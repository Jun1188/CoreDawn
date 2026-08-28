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
- **Id**: 월드 엔티티는 64비트 단조 카운터 `EntityId`(월드 헤더에 `NextEntityId` 저장, 재사용 없음). 플레이어(프로필)만 `Guid`.
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

### 2. 의존 방향 뒤집기
- [ ] `EntityId` · `World`(엔티티 등록부 + 시계) · `EntityModule` 계약 도입
- [ ] `Building`을 엔티티의 모듈로 — HP는 `Health` 모듈이 유일한 주인 (뷰 `BuildingEntity`에서 이동)
- [ ] 심의 `BuildingEntity` 참조 7곳 제거 (`Building` · `FactorySim` · `PlacementBridge` · `CoreDataSO` · `NestDataSO` · `TreeDataSO` · `MachineProcessor`) — 이벤트로 대체
- [ ] `IsHostile`의 레이어("Monster") 판정 → 팩션/팀 필드 (레이어 리팩토링 선행 조건)
- [ ] 출구 조건: Runtime → 뷰 역참조 0, asmdef를 넣을 수 있는 상태

### 3. 몬스터 심/뷰 분리 (가장 큰 덩어리)
- [ ] `MonsterDataSO.Build(entity)` → Health · Movement(의도) · Combat · Brain(기존 상태기) 모듈
- [ ] `MonsterView`: Rigidbody 적분 · 애니 · 군중 분리 · 위치 보고(뷰→심)
- [ ] `SensorComponent`의 `OverlapSphere` → 심 공간 해시 (`CrowdSystem` 등록부가 씨앗)
- [ ] 둥지 · 타워도 같은 틀로 (`MonsterNest` · `BattleTower`)

### 4. 플레이어 · 피해 경로
- [ ] `PlayerSim`(Health · Effects · 위치 미러) + `PlayerController`(입력 · 이동 뷰) — 런타임에 `Player : Entity`를 붙이는 이중 구조 정리
- [ ] 투사체 명중: 콜라이더 → 뷰 → `EntityId` → `World.ApplyEffects(id, …)`. 피해 · 효과 · 사망이 심 안에서 끝남

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

## 8. 세션 재개 절차

1. 이 문서와 `AGENTS.md`를 읽는다. `git branch --show-current`, `git log --oneline -10`으로 어디까지 왔는지 본다.
2. Unity가 떠 있으면 `unity status` → `recompile_status`로 컴파일 상태를 본다.
3. 위 체크박스에서 첫 미완 항목부터 이어간다. 단계 하나가 끝나면 **진행 로그**에 날짜와 결과를 적는다.
