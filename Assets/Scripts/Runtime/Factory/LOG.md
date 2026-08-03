# Factory 시스템 작업 로그

> Claude Code와 진행한 구조 검토·수정 기록. 최신 항목이 아래.

---

## 2026-07-05 — 코드 정리 (로직 변경 없음)

| 파일 | 내용 |
|---|---|
| `PlacementBridge.cs` | 미사용 using(`com.IvanMurzak.ReflectorNet.Utils`), 디버그 `Debug.Log`, 대화 잔여 주석 제거. 클래스 역할 `<summary>` 추가 |
| `BuildingGraph.cs` | 파일 분리 이후 실제 내용과 어긋난 헤더 주석 수정. 떠돌던 `BuildingInstance` 설명 주석을 `BuildingInstance.cs`로 이전 |
| `BuildingSimulation.cs` | 헤더의 "포함" 목록에서 분리된 `BeltSegment`/`BeltSegmentManager`를 "관련 (별도 파일)"로 이동 |
| `PlacementSystem.cs` (GridSystem) | 미사용 `using VInspector` 제거, 중복 `RotatedSize()` 삭제 → `BuildingDataSO.GetRotatedSize()` 사용, 무의미한 `[SerializeField] GridSystem grid` 제거, 미사용 지역 변수 정리 |
| `SO/IIdentifier.cs` | 빈 인터페이스 + 참조 0건 확인 후 삭제 (.meta 포함) |

---

## 2026-07-05 — 구조 검토 및 팀 결정

데이터 구조/알고리즘 장기 리스크 검토 후, 항목별 문답으로 결정.

### 확정 (구현 완료 — 아래 섹션 참조)
1. **멀티타일 회전 버그** → 회전 기능 재설계 (4방향 포트 사전 계산·캐싱)
2. **아이템 증발** → 생산 정지(stall) 정책 채택
3. **Dirty Queue 무력화** → wake-up 예약(min-heap) 도입
4. **벨트 분기/합류** → 합류기·분배기는 전용 건물(비 Transport)로 취급, 세그먼트는 선형 유지 (팀 기존 합의)

### 보류 (추후 별도 논의)
- **틱 유실**: `SimulationSystem.Update`가 프레임당 최대 1틱만 처리 → 저사양에서 시뮬레이션이 실시간보다 느려짐. 더 고민하기로 함
- **인벤토리 문자열 키**: `BuildingInventory`가 `item.name`(string)으로 아이템 구분 + `new string name` 필드가 `Object.name`을 가림. 큰 문제로 인식, 별도 논의 예정
- **선언만 된 규칙들**: `PortDefinition.AcceptedTypes` 미검사, `maxInputBuffer`가 총량이 아닌 "아이템 종류당 상한"으로 동작. 별도 논의 예정

---

## 2026-07-05 — 확정 사항 구현

### ① 회전 재설계 — `SO/BuildingDataSO.cs`
- **버그**: 기존 `RotateOffset`은 원점 기준 수학 회전 `(x,y)→(y,−x)`만 수행 → 2×1 이상 건물을 회전하면 포트가 풋프린트 밖 셀에 붙음 (1×1만 쓰던 동안엔 미발현)
- **수정**: 재앵커링 공식 `(x,y)→(y, w−1−x)`을 쓰는 `Dir.RotateCellCW` 도입. `GetRotatedPorts`가 4방향 포트 배열을 최초 1회 계산해 캐싱(`BuildPortRotations`), `OnValidate`에서 캐시 무효화
- **주의**: 같은 SO·같은 회전의 건물들이 동일 `PortDefinition` 인스턴스를 공유하게 됨. 런타임에 포트를 수정하려면 복사 후 수정할 것

### ② stall 정책 (아이템 유실 방지)
- `BuildingInstance.NotifyUpstream()` 추가 — 입력 버퍼에 자리가 생기면 상류를 깨우는 역방향 이벤트. stall이 데드락 없이 돌기 위한 핵심
- **Miner**: 출력 버퍼에 자리 있을 때만 채굴 예약. 밀린 출력 버퍼를 매 Tick 먼저 배출 (기존엔 버퍼에 넣은 아이템을 다시 꺼내는 코드가 없어 영구 잔류 + 유실)
- **Assembler**: 결과물 들어갈 자리 확인 후 조합 시작, 완료 시점에 막혀 있으면 완료 보류 (기존엔 `TryAddOutput` 반환값을 버려 재료 소모 후 결과물 증발)
- **Storage**: 입력→출력 버퍼 이동 단계 추가 (기존엔 이 단계가 없어 저장소가 아무것도 내보내지 못했음 — 검토 중 발견한 추가 버그)

### ③ wake-up 스케줄러 — `BuildingSimulation.cs`
- `SimulationSystem`에 시뮬레이션 시계 `Now`(초, 틱마다 증가)와 `ScheduleWake(b, delay)`(이진 min-heap) 추가
- 타이머 건물(마이너·어셈블러)이 매 틱 `MarkDirty` 재등록하는 대신 **완료 시점에만** 깨어남
- 벨트 세그먼트도 소속 벨트 전부가 아닌 **대표(입구, `Belts[^1]`) 벨트 1개만** 재등록 — 세그먼트당 매 틱 1건
- 행동 타이머는 dt 누적에서 절대 시각(`_readyAt`) 비교로 변경

### ④ 벨트 세그먼트 안전장치 — `BeltSegmentManager.cs`
- `OnNewConnection`에 1:1 병합 가드 추가: `From` 출력 연결 또는 `To` 입력 연결이 2개 이상이면 병합 안 함 → "세그먼트는 선형"이라는 팀 합의를 코드로 강제
- 병합/분할 시 새 세그먼트의 대표 벨트만 `MarkDirty` (기존: 전체 벨트)

---

## 2026-07-05 — 철거 시 MissingReferenceException 수정

- **증상**: 건물 철거 후 `FactoryTest.cs` gizmo(구 197행)에서 `seg.Belts[0].transform` 접근 시 예외
- **근본 원인**: `Object.Destroy`는 프레임 끝에 실행됨. 철거된 벨트가 그 프레임의 dirty 큐에 남아 있으면 `RunTick`이 정상 벨트로 보고 Tick → `EnsureSegment`가 죽기 직전 벨트로 **유령 세그먼트**를 등록 → 다음 프레임부터 파괴된 transform 접근
- **수정**:
  1. `BuildingInstance.IsRemoved` 플래그 추가
  2. `PlacementBridge.Remove()` 첫 단계에서 `IsRemoved = true`
  3. `SimulationSystem.RunTick`이 `IsRemoved` 건물을 스킵
  4. `FactoryTest.cs` gizmo에 파괴된 벨트 방어 가드 (철거 직후 1프레임 과도기용)

---

## 2026-07-05 — stall 데드락 수정 (마이너 먼저 설치 시 영구 정지)

- **증상**: 마이너 → 벨트 순서로 설치하면 아이템이 마이너 버퍼에 갇힘. 벨트 → 마이너 순서는 정상
- **원인**: stall 정책의 커버리지 구멍. 마이너가 출력할 곳 없이 버퍼를 채우면 wake 예약을 멈추는데(stall), 이후 벨트를 연결해도 깨우는 이벤트가 없음 — `NotifyUpstream`은 하류가 아이템을 *소비*할 때만 발동하므로, 아무것도 받은 적 없는 새 벨트는 마이너를 영원히 깨우지 않음 (데드락)
- **수정**: `BuildingGraph.RegisterConn`에서 새 연결의 양단(From/To)을 `MarkDirty` — "연결 토폴로지 변화"도 깨우기 이벤트로 승격. stall 규칙: **정지한 건물은 (a) 하류의 소비, (b) 새 연결 생성, 둘 중 하나로 반드시 깨어난다**
- **부수 개선**: 마이너/어셈블러/저장소의 출력 배출 루프를 "아이템 타입당 1개/틱"에서 "하류가 받는 만큼 전부"로 변경 → 정지 해제 후 밀린 버퍼가 빠르게 빠짐

---

## 2026-07-05 — 틱 유실 수정 (보류 항목 처리)

- **문제**: `SimulationSystem.Update`가 프레임당 최대 1틱만 처리 → 프레임 시간이 틱 간격(0.1초)을 넘으면 시뮬레이션이 실시간보다 느려지고 `_timer` 빚이 무한 누적
- **수정**: 고정 틱 루프 + 따라잡기 상한
  - 밀린 틱을 `while`로 몰아서 처리 (프레임 드랍 후 실시간 복귀)
  - 프레임당 최대 `_maxCatchUpTicks`(기본 5, 인스펙터 조절)까지만 — 저사양에서 "틱 몰아치기 → 프레임 더 느려짐" 나선 방지
  - 한도 초과분 빚은 버림 — 지속 저사양에서는 시뮬레이션이 느려지는 걸 허용하되 무한 누적 차단
- **의미**: TPS 10 기준 10 FPS까지는 정확히 실시간 유지, 2 FPS까지는 따라잡기로 커버(50 TPS 순간 처리), 그 이하는 슬로모션

---

## 2026-07-05 — 특성화 테스트 하네스 추가 (재설계 준비)

- **목적**: BuildingSimulation/Behavior/SO 전면 재설계 전에 "현재의 올바른 동작"을 박제 → 재설계 중 회귀 감지
- **위치**: `Test/Factory/FactoryScenarioTests.cs` — 빈 씬에 빈 GameObject + 컴포넌트 부착 후 플레이하면 6개 시나리오 자동 실행, PASS/FAIL 표시
- **시나리오**: ① 기본 체인 운반 ② 설치 순서 무관(stall 데드락 회귀) ③ 막힌 체인 무유실·정지 ④ 중간 철거 분할·복구 ⑤ 회전 배치 연결 ⑥ 어셈블러 조합 체인
- **NUnit이 아닌 이유**: 테스트 asmdef는 Assembly-CSharp을 참조할 수 없는데, Runtime↔Test 코드가 상호 참조 중(`PlayerController→Entity`, `InventoryUI→ItemSocket`)이라 어셈블리 분리 불가. 심/뷰 분리 후 EditMode NUnit으로 이전 예정
- **부수 수정**: 틱 유실 커밋에서 `_maxCatchUpTicks` 필드 선언 누락 발견 → 추가 (컴파일 에러였음)

---

## 2026-07-05 — 재설계 1단계: SO 상속 분리 (god-SO 해체)

- **문제**: `BuildingDataSO` 하나에 모든 건물 종류의 필드가 뭉쳐 있고(`processingTime`은 마이너만, `availableRecipes`는 어셈블러만 사용), `category` enum → `BuildingBehaviorFactory` switch로 행동 분기 (새 건물마다 enum·switch·SO 전부 수정 필요 — OCP 위반)
- **변경**:
  - `BuildingDataSO`를 abstract 베이스로: 공통 필드(name/size/ports/버퍼) + `abstract CreateBehavior(instance)` + 표시용 `abstract Category`
  - 서브클래스 4종 신설 — **각 파일에 SO와 행동을 함께 배치** (팀 컨벤션):
    - `MinerDataSO.cs` — SO(processingTime) + `MinerBehavior` + `MiningService`
    - `BeltDataSO.cs` — SO(speedTilesPerSec 신설) + `BeltBehavior`
    - `AssemblerDataSO.cs` — SO(availableRecipes) + `AssemblerBehavior`
    - `StorageDataSO.cs` — SO + `StorageBehavior`
  - `IBuildingBehavior`는 `BuildingDataSO.cs`로 이동
  - `BuildingBehaviorFactory`·serialized `category` 필드 삭제. `BuildingInstance.Initialize`는 `data.CreateBehavior(this)` 호출
  - `BeltSegmentManager`의 Transport 검사 → `is BeltDataSO`. 벨트 속도는 하드코딩(2f) 대신 `BeltDataSO.speedTilesPerSec`에서 읽음
  - `BuildingSimulation.cs`는 `SimulationSystem`만 남음
  - 기존 `.asset` 5개의 `m_Script` GUID를 대응 서브클래스로 리포인트 (MinorSO→Miner, Belt/커브×2→Belt, Assembler→Assembler). 기존 필드값(processingTime, availableRecipes) 그대로 보존됨
- **효과**: 새 건물 종류 추가 = `XxxDataSO.cs` 파일 1개(SO+행동)만 추가하면 끝, 기존 코드 무수정
- **주의**: 에셋 생성 메뉴가 Factory/Building → Factory/Miner·Belt·Assembler·Storage로 분화됨. Storage 에셋은 아직 없으니 필요 시 새로 생성
- **특성화 테스트 6/6 통과 확인** — 재설계 후 동작 보존 검증됨

---

## 2026-07-05 — BuildingCategory 삭제

- 로직 분기가 전부 타입 기반(`CreateBehavior`, `is BeltDataSO`)으로 대체돼 표시용 1곳만 남았기에 enum과 `Category` 프로퍼티 삭제. FactoryTest는 `Data.GetType().Name` 표시
- **남은 TODO (팀 결정)**: 속도가 다른 벨트는 세그먼트 병합 금지 — 현재는 병합 시 상류 세그먼트 속도로 통일되어 설치 순서 의존적. 고속 벨트 티어 추가 전에 필수. 가드 한 줄이 `BeltSegmentManager.OnNewConnection`에 TODO 주석으로 준비돼 있음

---

## 2026-07-05 — 재설계 2단계 착수: GameDataSO + TagSO 도입 (식별 체계 통일)

- **배경**: 식별 방식이 3갈래였음 — Factory(문자열 `name` 키 + ItemType enum) / 팀원 인벤토리(SO 참조 비교 — 이게 정답) / FPS(`gunName` + WeaponType). 공통 필드(name/desc/icon)가 SO마다 중복 정의 + `new string name`이 `Object.name` 섀도잉
- **도입**:
  - `GameDataSO` — 모든 데이터 SO의 공통 베이스: `Id`(수동 지정 가능. 비우면 **"Data 기준 폴더 경로::displayName"** 자동 생성, 예: `Item::IronOre` — displayName이 채워진 뒤에만 생성됨 + 에디터 중복 검사), `displayName`, `description`, `icon`, `tags`. **런타임 구분은 SO 참조, id는 세이브 직렬화 전용**이 원칙. 한번 생성된 id는 폴더 이동/개명해도 유지, 세이브가 존재하는 id는 변경 금지
  - `TagSO` — 마인크래프트식 태그(#ore)의 마커 에셋. 문자열 대신 에셋 참조라 오타·리네임 안전. 용처: 레시피 태그 재료, 포트 필터(AcceptedTypes 대체 예정), 분류
  - `ItemDataSO`/`RecipeDataSO`/`BuildingDataSO`가 GameDataSO 상속. `new string name` 전부 제거, `[FormerlySerializedAs("name")]`로 기존 에셋 값 무손실 이전
  - `ItemType` enum은 ItemDataSO.cs로 이동, 태그 대체는 TODO (팀원 ItemTooltipUI가 사용 중이라 합의 필요)
- **주의 (팀원 코드 영향)**: `item.name`을 쓰던 곳(ItemTooltipUI, InventoryManager, DroppedItem, PlayerController)은 이제 `Object.name`(에셋 파일명)으로 폴백됨 — 컴파일은 되지만 표시 텍스트가 파일명이 됨. **표시 용도는 `displayName`으로 바꾸는 걸 해당 팀원과 협의할 것**
- 기존 에셋의 `id`는 인스펙터에서 열리거나 리임포트될 때 OnValidate로 자동 부여됨

---

## 2026-07-06 — BuildingInventory 제거, 슬롯 기반 ItemContainer로 교체

- **결정**: 건물 버퍼를 플레이어 인벤토리(팀원 작성)와 같은 데이터 모델로 통일 — `ItemStack` 공유 + SO 참조 비교. 보류했던 "버퍼 의미"는 **슬롯 × 스택**으로 확정
- **신설 `ItemContainer`** (plain C#, Mono 아님): `TryAdd`/`TryConsume`(전량 아니면 실패)/`CountOf`/`RoomFor`/`HasRoomFor`(stall 판정)/`Snapshot`. 스택 병합은 팀원 Inventory와 같은 규칙(기존 스택 먼저, 빈 슬롯 순서)
- **`BuildingInstance`**: `Inventory` 1개 → `Input`/`Output` 컨테이너 2개. 3곳에 복붙돼 있던 배출 루프를 `FlushOutputs()`로 공용화
- **`BuildingDataSO`**: `maxInputBuffer/maxOutputBuffer`(종류당 개수) → `inputSlots/outputSlots`(칸 수) + `bufferStackCap`(0=아이템 기본 64, 기계는 작게). **기존 에셋의 버퍼 값은 의도적으로 이전하지 않음** (의미가 달라서) — 인스펙터에서 재설정 필요 (기본 1칸/1칸/0)
- **`BuildingInventory` 삭제** — 문자열(name) 키 식별이 코드베이스에서 소멸. "큰 문제"였던 인벤토리 키 논의 종결
- **효과**: 벨트→상자→플레이어 인벤 아이템 이동이 같은 모델이라 변환 불필요. 향후 팀원 Inventory가 내부를 ItemContainer 위임으로 바꾸면 완전 통일 (합의 대기)
- **주의**: 어셈블러 `CanStoreOutputs`는 다중 출력 레시피에서 근사 검사 (현 레시피 전부 단일 출력이라 정확). `ItemStack.maxStackSize`를 `ItemDataSO.maxStack`으로 옮기는 건 팀 합의 항목

---

## 2026-07-06 — 어셈블러 슬롯 규칙 + AcceptedTypes 삭제 (보류 항목 종결)

- **문제 1 — 레시피 교체 제약**: `inputSlots`가 SO 고정값이라 재료 종류가 더 많은 레시피로 바꾸면 영구 stall
  → `AssemblerDataSO.OnValidate`가 슬롯 부족 레시피를 에디터 에러로 표시 (자동 확장하지 않음 — 슬롯 수는 디자이너 의도 값). `SetRecipe`에도 슬롯 초과 가드
- **문제 2 — 재료 독점 데드락**: 컨테이너가 마크 상자처럼 넘침(같은 아이템이 빈 슬롯 점유) → 철광석이 2칸을 다 차지하면 구리가 영원히 못 들어옴
  → `ItemContainer.SingleStackPerType` 도입 (같은 아이템은 슬롯 1개까지). 어셈블러 입력에 적용, 저장소는 기본(넘침) 유지
- **AcceptedTypes 삭제**: 포트 수준 필터는 레시피와 이중 장부가 되는 데다 처음부터 검사 코드가 없던 죽은 선언
  → `ItemContainer.AcceptFilter`(수용 필터)로 대체 — 어셈블러 입력이 "현재 레시피의 재료만" 수용. 거절된 push는 상류로 배압 전달. 필터 벨트 등 진짜 포트 필터 수요가 생기면 그 건물의 기능으로 그때 추가(TagSO)
- **TODO(레시피 UI 시점)**: 레시피 변경 시 입력 버퍼 잔여 재료 처리 (출력 배출 또는 플레이어 환불)

---

## 2026-07-06 — TagSO 철회

- 도입 근거였던 용처 3개가 전부 소멸/연기됨: 포트 필터는 `AcceptFilter`(레시피 참조)로 해결, 태그 재료 레시피는 기획 미확정, UI 분류는 UI 자체가 없음 → **소비자 0곳인 죽은 선언**이라 `AcceptedTypes`·`BuildingCategory`를 지운 것과 같은 기준으로 삭제
- `TagSO.cs`, `GameDataSO.tags`, `HasTag()` 제거
- **재도입 조건** (그때 파일 하나 + 필드 하나로 복원 가능):
  1. "X 계열 아무거나" 재료 레시피가 기획으로 확정될 때
  2. 필터 벨트/분배기 등 플레이어 설정형 필터 건물을 만들 때
  3. UI 카테고리 필터가 생길 때

---

## 2026-07-07 — 벨트 통합: 직선/L/R 에셋 3종 → 벨트 1종 + 배치 시 모양 결정

- **문제**: 커브가 별도 SO라 (a) 플레이어가 3종을 오가며 배치해야 하고, (b) "같은 종류만 병합" 가드를 넣으면 코너마다 세그먼트가 끊김
- **구조**: 모양(BeltShape: Straight/CurveL/CurveR)은 SO가 아니라 **배치 시점에 결정되는 인스턴스 상태**
  - `BuildingInstance.PortOverride` 신설 — null이면 SO 포트, 벨트는 `BeltDataSO.BuildPorts(shape, rot)` 주입 (12조합 캐시)
  - `BeltDataSO`에 커브 프리팹 2개 필드 — 어떤 메시를 보여줄지는 뷰 관심사
  - `PlacementBridge.Place`에 portOverride/prefabOverride 옵션 파라미터
- **배치 UX**: **T키**로 직선/L/R 수동 순환 (이웃 기반 자동 판별은 구현했다가 제거 — 단순함 우선, 필요해지면 DetectBeltShape를 git 히스토리에서 복원)
- **병합 가드**: `c.From.Data != c.To.Data`면 병합 안 함 — **같은 벨트 에셋끼리만**. 속도 비교 가드 TODO는 이걸로 종결 (고속 벨트 = 다른 에셋 = 경계에서 자동 분리, 아이템은 버퍼 push로 통과)
- **에셋**: BeltLCurve/BeltRCurve 삭제, 커브 프리팹 참조는 Belt.asset으로 이식
- **한계 (TODO)**: 배치 후 이웃이 바뀌어도 모양이 자동 재계산되지 않음 (Factorio식 re-curve는 연결 재평가 시스템 필요). 당장은 재배치로 해결
- **주의**: 씬의 PlacementSystem buildingDataList에 남은 LCurve/RCurve 참조는 Missing이 되므로 제거 필요. 커브 프리팹의 기본 방향이 "출력=동쪽" 기준과 다르면 프리팹 메시를 회전시켜 맞출 것
- 테스트: S7(커브 코너 체인 — 커브 포함 단일 세그먼트 병합 + 운반) 추가, 벨트 SO를 시나리오 간 공유하도록 변경

---

## 2026-07-07 — 재설계 3단계: 심/뷰 분리 완료

- **구조**: 시뮬레이션 전체가 plain C#으로 이동 — Unity 접점은 딱 2개
  - `FactorySim` — 루트: 시계(Now)·Dirty Queue·Wake heap·배치/제거(`Place`/`Remove`)·`GridIndex`(구 GridRegistry)·`BuildingGraph`·`BeltSegmentManager`를 소유. `Advance(dt)`로 구동
  - `Building` — 심 엔티티 (구 BuildingInstance). 행동은 `_b.Sim`으로 심 서비스 접근 (싱글톤 전멸)
  - `FactoryBootstrap` — 유일한 드라이버 Mono: 심 생성 + 매 프레임 Advance + Building↔View 매핑
  - `BuildingView` — GO와 심을 잇는 다리 (필드 하나). **BuildingInstance.cs를 GUID 유지한 채 리네임**해서 프리팹의 기존 컴포넌트 참조가 그대로 BuildingView로 살아있음
- **삭제**: BuildingSimulation.cs(SimulationSystem), GridRegistry.cs, MiningService(→ `FactorySim.GetResourceAt` 주입)
- **효과**:
  - 특성화 테스트가 씬·GameObject·프레임 대기 없이 **동기 실행** — 전체 스위트가 첫 프레임에 즉시 완료 (기존: 배속 10으로 20~30초)
  - 세이브/로드 기반 완성: 심 상태 전체가 plain 데이터 (Building/ItemContainer/BeltSegment)
  - 철거 시 Unity Destroy 지연에 의존하던 코드 소멸 (IsRemoved 플래그로 일원화)
- **씬 요구사항**: FactoryBootstrap 컴포넌트 하나만 있으면 됨 (기존 씬의 FactoryBootstrap이 그대로 드라이버가 됨 — 씬 수정 불필요). tps/maxCatchUpTicks는 드라이버 인스펙터에서 설정

---

## 2026-07-07 — 합류기/분배기 건물 추가

- 팀 합의("분기/합류는 전용 건물, 벨트 세그먼트는 선형 유지")의 실제 구현. 둘 다 벨트가 아니므로 세그먼트가 앞뒤에서 자연스럽게 끊김
- **Splitter** (입력 1 → 출력 East/North/South): **라운드로빈 분배** — `TryPushOutput`의 "첫 연결 독식" 문제를 여기서 해결. 막힌 출구는 건너뜀 (Factorio 스타일)
- **Merger** (입력 West/North/South → 출력 1): 통과 버퍼(1칸). 입력 공정성은 선착순 — 굶는 라인이 보이면 입력 연결별 라운드로빈으로 개선 (TODO 주석)
- 에셋 Splitter/Merger 생성 + 프리팹 연결. 새 건물 = SO+행동 파일 1개 규칙의 첫 실전 사례 (기존 코드 무수정)
- 테스트 S8(분배 균등성: 양쪽 저장소 차이 ≤2), S9(합류: 광석/주괴 두 소스 모두 통과) 추가 — 9/9 통과

---

## 2026-07-14 — PlacementSystem 실사용 리팩토링 (Input System 전환)

- **입력**: 레거시 `Input.*` → Input System(`Keyboard.current`/`Mouse.current` 폴링).
  `PlayerControls.inputactions`는 수정하지 않음 — 팀 병합 충돌 방지. 액션 에셋으로 옮길 땐 폴링부만 교체
- **테스트 UI 제거**: OnGUI 버튼(_SelectBuildingTest 등) 삭제 → 실사용 표면으로 대체
  - 핫키: **B** = 배치 모드 토글(마지막 선택 건물), **X** = 철거 모드 토글 (인스펙터에서 키 변경 가능)
  - UI 연동 API: `SelectBuilding(so)` / `SelectBuildingByIndex(i)` / `EnterDemolishMode()` / `ExitMode()` + `Mode`/`CurrentBuilding`/`Buildings` 조회 — 배치 UI(빌드 메뉴)가 붙을 자리
- **FPS 겸용 조준**: 커서 잠금 상태(FPS 플레이)면 화면 중앙 크로스헤어로, 아니면 마우스 커서로 레이캐스트
- **실사용 방어**: UI 위 클릭이 배치/철거로 새지 않게 `EventSystem.IsPointerOverGameObject` 가드, 지형 미조준 시 프리뷰 숨김, 벨트 프리뷰가 모양(T키)에 맞는 커브 메시로 교체되도록 수정
- **주의**: 플레이어 핫바(숫자키)와 충돌하지 않도록 숫자키 바인딩은 넣지 않음 — 건물 선택은 UI/API 경유가 정석

---

## 2026-07-22 — BuildingView 제거 (팀 결정) — Entities.Building 직접 사용

- 구 BuildingView는 Entities.Building 상속 후 `Building` 프로퍼티(→Sim)만 남은 빈 껍데기였음
  → 삭제하고 전 참조를 `Entities.Building` + `.Sim`으로 교체
- 영향 범위: FactoryBootstrap(뷰 매핑 타입), PlacementBridge(AddComponent + Sim 연결),
  PlacementSystem.TryGetAimedBuilding(Sim 없는 코어는 철거 대상 제외), FactoryTest, Entities.Building.GetOrAttach 단순화
- 프리팹/씬 참조 없음(런타임 부착 전용)이라 에셋 수정 불필요.
  프리팹에 Entities.Building을 미리 붙이면(타워 canAttack 등) PlacementBridge가 그대로 사용
- 이는 건물 상호작용(E키) 작업의 선행 정리 — 다음: Interactable 어댑터가 Entities.Building을 직접 참조

---

## 2026-07-25 — GameDataSO id 정책 변경: 자동 생성 폐기, 수동 지정

- **자동 생성 제거** — RecipeImporter가 만든 한국어 displayName이 그대로 id로 굳는 사고
  ("Recipe::철 기어 제작") 발견. id는 사람이 의도적으로 정하는 안정 키로 전환
- **형식: 콜론 1개** ("분류:이름", 예: Item:IronOre). 기존 에셋 12종 일괄 마이그레이션,
  한국어로 생성됐던 레시피 id 2건은 비움 (경고 뜨는 상태 — 담당자가 영문으로 지정)
- 비어 있으면 OnValidate가 경고. id 중복 검사는 유지
- id 소비자는 아직 없음(세이브 예정) — 형식 변경 무해 확인

---

## 2026-07-25 — ItemDatabaseSO 레지스트리 (건물 DB 패턴 재사용)

- **ItemDatabaseSO** (Resources/ItemDatabase) — 전체 아이템 SO 자동 수집 (타입 → 표시명 정렬).
  `FindById`(세이브용 id↔SO 해석), `GroupedByType` 제공
- 스캐너 통합: BuildingDatabaseScanner가 건물/아이템 DB를 함께 재수집
  (메뉴: Tools/Factory/Rebuild Data Databases). 커스텀 인스펙터도 동일 패턴(타입 그룹, 읽기 전용)
- Inventory.Awake의 죽은 테스트 코드(Resources.Load "TestItemName") 제거
- 예정 소비자: 자원 배치(광석 목록), 레시피 UI(재료 표시), 세이브. RecipeImporter의
  아이템 자동 생성 확장은 팀 논의 후 (id 부여 방식 포함)

---

## 2026-07-25 — 월드 드롭 아이템 정형화 (마크식 공용 프리팹)

- **공용 DroppedItem.prefab** (Prefabs/Item/) — 모든 아이템이 같은 프리팹, 아이콘만 교체.
  참조는 ItemDatabaseSO.droppedItemPrefab (아이템 시스템 전역 설정 자리)
- DroppedItem.Spawn: 프리팹 Instantiate + Setup(아이콘 주입). 프리팹 미지정 시
  기존 코드 조립 폴백 — 호출자(Q드롭·캐리지 드롭) 무수정
- ItemRotator를 별도 파일로 분리 (파일명 불일치 클래스는 프리팹에 못 붙음)
- 아이템별 프리팹(worldPrefab) 안은 기각 — 표현 차이는 아이콘으로 충분, 정형화가 관리 우위.
  디자이너는 공용 프리팹 하나만 다듬으면 전 아이템 반영

---

## 2026-07-25 — 건물 파괴 시 아이템 드롭 (버퍼 + 벨트 위 아이템)

- **PlacementBridge.Remove가 단일 관문** (철거·전투 파괴 공통): 심 제거 전에
  Input/Output 버퍼 내용물을 파괴 지점 주변에 드롭 (스택 64 단위, 흩뿌림)
- **벨트 위 아이템**: 세그먼트 분할 시 폐기되던 제거-벨트 구간 아이템을
  BeltSegmentManager.ItemDiscarded 이벤트로 통지 (기존 TODO 해소) —
  심은 버리기만 하고, FactoryBootstrap(드라이버)이 구독해 월드 드롭. 심/뷰 분리 유지
- 순수 심 테스트는 이벤트 미구독 — 기존 시나리오 무영향

---

## 2026-07-26 — 벨트 위 아이템 시각화 (BeltItemView, immediate-mode 풀)

- 심 데이터(BeltSegment.Items의 (item, pos))를 매 프레임 그리는 순수 뷰 —
  물리·콜라이더·상호작용 없음, 아이템 정체성 추적 없음 (동기화 버그 원천 차단)
- pos(0=입구, n=출구) → 월드: 벨트 타일 중심 폴리라인 선형 보간.
  커브 벨트는 모서리를 살짝 가로지름 (v1 타협 — 어색하면 포트 방향 베지어)
- FactoryBootstrap이 자기 GO에 자동 부착 — 씬 배선 불필요
- **FBX 전환 대비**: 비주얼 적용은 ApplyVisual() 한 곳 — 메시 전환 시 여기만 수정.
  수천 개 규모가 되면 같은 위치 계산 위에 Graphics.RenderMeshInstanced(v2)로 교체

---

## 2026-07-26 — GameDataImporter: 외부 툴용 통합 JSON 임포터

- **Assets/Data/Import/*.json → GameDataSO 일괄 생성/갱신** (Tools/Factory/Import Game Data)
- 공통 파이프라인: id("분류:이름")가 기본 키 — 있으면 갱신·없으면 생성 (멱등 재임포트, guid 보존).
  아이템 → 레시피 순서로 처리해 레시피가 새 아이템 참조 가능. 완료 시 DB 자동 재수집
- 타입별 매핑: 아이템(type enum·아이콘 이름 검색), 레시피(재료/결과 id 해석 — 미해석 시
  레시피 단위 스킵). 새 타입 추가 = DTO + Import 함수 한 쌍
- **완전 범용(리플렉션) 기각**: 에셋 참조(프리팹·gunData)가 JSON에 못 들어가는 본질 한계 +
  오류 메시지 품질. 건물 등 에디터 저작물은 인스펙터 유지
- GameData.json 샘플 = 스키마 문서 겸 (기존 실데이터와 일치 — 재임포트 무해)
- 기존 RecipeImporter(팀원)와 역할 중복 — 통합 여부 팀 논의 필요

---

## 2026-08-01 — 분배기 필터 (출구 방향별 아이템 지정, O(1))

- **SplitterBehavior.AddFilter(Direction, ItemDataSO)** — 방향당 여러 아이템 지정 가능.
  지정 아이템은 그 출구로만, 필터 출구는 다른 아이템 통과 금지.
  나머지는 무필터 출구 라운드로빈 (기존 동작 유지). RemoveFilter/ClearFilter 제공
- **O(1) 판정**: 아이템→방향 딕셔너리 조회 + "전용 출구" 여부는 방향 키 존재로 —
  아이템·필터 종류 수와 무관. 연결 수(≤4)는 상수 순회
- 필터는 출구 "방향" 기준 저장 — 이웃 설치/철거로 연결이 재구축돼도 유지.
  지정 출구가 막히면 그 아이템만 대기(배압), 다른 종류는 계속 흐름
- 시나리오 10 추가: 혼합 라인 → 필터 분배 순수성(교차 오염 0) 검증
- 설정 UI는 추후 (기계 상호작용 UI와 함께) — 지금은 심 API

---

## 2026-08-01 — 분배기 필터 설정 UI (E 상호작용)

- SplitterBehavior에 IInteractiveBehavior 구현 — 조준 프롬프트 "필터 설정", E로 열기
  (Storage 보관함과 같은 opt-in 패턴, Entities.Building 브리지 재사용)
- **SplitterFilterPopup**: 출구 방향 탭(출력 포트 기준, 연결 여부 무관) + ItemDatabase
  아이템 그리드 토글 — 클릭으로 지정/해제/이동. 상태색: 선택 출구=초록, 타 출구=노랑(+방향 표기)
- 런타임 코드 조립(씬 저작 0), ESC/E 닫기, UIPopup 파이프라인(열림 중 사격·건설 차단) —
  BuildMenuPopup과 동일 방식. UI 담당이 프리팹으로 다듬을 수 있게 표면 분리

---

## 2026-08-03 — ItemType을 용도/계통 2축으로 분리

- **문제**: `ItemType` 하나가 "무엇인가"(원광·부품)와 "어느 생산 라인인가"(철·구리·크리스탈)를
  겸하려다 어느 쪽도 제대로 못 표현. UI가 계통색을 칠할 근거가 없었음
- **`ItemType` 재정의(9종)** — Ore/Ingot/Part/RepairPart/Ammo/Weapon/Armor/Placeable/Salvage.
  **정수값을 명시**해 기존 에셋의 Ore(0)/Ingot(1)/Component(2)가 자동 승계되게 함 (enum은 int로 직렬화)
- **`ItemLine` 신설** — None/Iron/Copper/Crystal/Beast. 용도와 직교하는 축
- `ItemDatabaseSOEditor` 표시명 갱신. 아이템 에셋은 외부 툴이 재생성하므로 코드만 수정
- **주의**: 값이 바뀐 enum이라 **코드와 재생성된 에셋은 같은 커밋에 있어야 한다** —
  갈라지면 중간 커밋에서 기존 에셋의 type 정수가 다른 의미가 됨

---

## 2026-08-03 — 게임 시작 시 코어 자동 설치

- 코어를 건설 메뉴로 직접 지어야 했음 — 시작 조건이 플레이어 조작에 달려 있으면
  테스트마다 상태가 달라짐
- `FactoryBootstrap`이 시작 시 코어를 배치. 배치 경로는 기존 `PlacementSystem`을 그대로 사용
  (자동 설치 전용 경로를 따로 만들면 두 벌이 갈라짐)

---

## 2026-08-04 — FactoryBootstrap의 DontDestroyOnLoad 제거

- 씬 전환 유지가 아직 필요 없는데 붙어 있어, 씬을 다시 로드하면 심이 둘이 됨
- 씬 전환 간 공장 상태 유지가 필요해지면 세이브/로드로 푸는 편이 맞다
  (심 상태는 전부 plain 데이터 — 2026-07-07 심/뷰 분리 참조)
