# Interactable(상호작용) 작업 로그

> 최신 항목이 아래. 설계 원칙: 마인크래프트식 — 동사 하나(E), 행동은 타겟이 결정, 화면은 시스템이 관리.

---

## 2026-07-22 — 상호작용 재설계 ①: IInteractable 계약 + 조준 판정 단일화

- **IInteractable 인터페이스 신설** — 발견 계약. 구현 경로 2가지:
  단독 오브젝트는 `Interactable` 베이스(기존 Chest/DroppedItem 무수정), 상속이 차 있는
  엔티티(Entities.Building 등)는 직접 구현
- **조준 판정 단일화**: PlayerInteractionManager가 "조준 중인 IInteractable"을
  `Current`로 캐시(프레임당 1회) + 프롬프트 표시. E키 실행(PlayerController.TryInteract)도
  같은 Current 사용 — 표시/실행이 각자 레이캐스트하며 어긋나던 구조(마스크 불일치 포함) 해소
- `Prompt`가 null/빈 문자열이면 "지금은 상호작용 불가" — 프롬프트도 안 뜨고 E도 무시
- PlayerInteractionManager를 Scripts 루트 → Runtime/Interactable/로 이동 (guid 유지, 프리팹 참조 안전)

---

## 2026-07-22 — 상호작용 재설계 ②: 타겟이 화면을 요청 (OpenInventory 해체)

- **인벤 화면 소유권을 InventoryManager로**: `OpenPlayerScreen()`(I키) /
  `OpenContainerScreen(Inventory)`(상자·건물이 호출) / `CloseScreen()`(팝업이 호출)
- **PlayerController에서 인벤 오케스트레이션 전부 제거** — OpenPlayerInventory/
  OpenTargetInventory/CloseInventory/isInventoryOpen 삭제. 남은 건 `HaltMomentum()`(아바타 상태)뿐.
  플레이어는 이제 화면을 중개하지 않는다 (마인크래프트식)
- Chest는 `InventoryManager.OpenContainerScreen(자기 컨테이너)` 직접 호출 — player 인자 사용 안 함
- 패널·UI 참조는 당분간 playerController 필드 경유 (UI 소유권 이관은 UI 담당과 협의 후)

---

## 2026-07-22 — 상호작용 재설계 ③: 건물 상호작용 (Storage 보관함)

- **IInteractiveBehavior** (BuildingDataSO.cs, opt-in) — 상호작용 있는 건물 행동만 구현.
  새 상호작용 건물 = 행동 클래스에 인터페이스 붙이기 (기존 코드 무수정, 심 테스트 무관)
- **Entities.Building : IInteractable** — Prompt/Interact를 행동으로 위임 (분기 없음).
  상호작용 없는 건물(벨트 등)은 Prompt=null → 프롬프트 안 뜨고 E 무시
- **StorageBehavior**: "보관함 열기" → 출력 버퍼를 인벤 화면과 함께 연다
- **ContainerInventoryBridge** — 심 ItemContainer를 기존 인벤 UI(Inventory)로 중계하는 프록시.
  LateUpdate(심 Advance 이후)마다 ① 플레이어 변화(종류별 개수 차이)를 컨테이너에 반영
  ② 컨테이너 상태를 프록시로 재미러링 (공장 유입 실시간 표시).
  넣기 실패분은 플레이어 가방으로 반환 (유실 방지)
- **Entity 레이어 신설(12)** + 건물 프리팹 8종 전부 Entity 레이어로 —
  Player.prefab interactableLayers = Obstacle|Interactable|Entity(4864).
  Default를 마스크에 넣지 않아 지형·소품이 상호작용 레이에 안 잡힘.
  몬스터→건물 판정은 레지스트리(FindClosestInRange) 기반이라 레이어 변경 무영향,
  센서(OverlapSphere)는 Monster 레이어만 스캔 — 전투 로직 안전 확인
- **v1 한계**: 같은 프레임에 플레이어와 공장이 같은 아이템을 동시에 옮기면 개수 차이 기반
  조정이 드물게 어긋날 수 있음. "Inventory의 ItemContainer 위임" 팀 협의가 근본 해결

---

## 2026-07-23 — 상호작용 재설계 ④: Inventory → ItemContainer 위임 (저장 계층 단일화)

- **Inventory가 ItemContainer 위임 어댑터로** — 씬 접점(MonoBehaviour)만 남고 데이터·규칙은
  전부 컨테이너로. 공개 `slots` 배열 제거 → 규칙(필터·스택 캡·종류당 1스택)이 항상 지켜짐.
  기존 `AddItem`의 스택 상한 초과 버그도 자연 해소
- **ItemContainer에 위치(슬롯 인덱스) 연산 추가**: PeekAt/TakeAt/TryPutAt/TryExchangeAt —
  UI 드래그·분할·교환용. 공장 물류는 기존 위치 무관 API만 사용 (심 계약 최소 확장)
- **Version 변경 추적**: 내용이 바뀌는 모든 연산에서 증가. 건물 보관함이 열려 있는 동안
  InventoryManager.Update가 버전 변화를 감지해 화면 갱신 (인플레이스 수정은 Touch() 규약)
- **ContainerInventoryBridge 삭제** — 프록시 동기화가 통째로 불필요.
  건물 보관함은 프록시 Inventory에 `Bind(container)`로 꽂아 **같은 객체를 직접** 본다.
  ③의 유령 아이템/개수 어긋남 한계 소멸
- 전환 범위: InventoryManager 클릭 핸들러(좌/우/쉬프트/무기검사), InventoryUI,
  InventorySlotUI, HotbarUI, HotbarController — `.slots[]` 직접 접근 0건 확인

### 다음 후보
- Assembler 레시피 UI (IInteractiveBehavior 재사용)
- Chest 시작 아이템 등 Inventory 직렬화가 필요해지면 컨테이너 직렬화 방식 협의 (세이브 시스템과 함께)
