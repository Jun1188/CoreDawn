# GridSystem 작업 로그

> 최신 항목이 아래. 입력 관련 이력(BuildController 등)은 Input/LOG.md 참조.

---

## 2026-07-14 — PlacementSystem 실사용 리팩토링

- 레거시 `Input.*` → New Input System 폴링, 이후 07-15 파이프라인 도입 때
  BuildController(입력 해석)와 분리 — 상세: Input/LOG.md

---

## 2026-07-18 — 철거 조준을 건물 몸체 우선으로

- 기존: 바닥(groundMask)만 레이캐스트 → 건물을 조준하면 레이가 통과해 뒤쪽 칸이 잡힘
- 변경: `TryGetAimedBuilding()` 2단 판정 — ① 건물 콜라이더 직접 히트(BuildingView로 판별)
  ② 실패 시 바닥 칸 폴백 (벨트처럼 낮아 몸체 조준이 어려운 건물 대비)
- **공용 쿼리로 공개** — 이후 기계 UI(E 상호작용) 등 "조준한 건물 찾기"는 전부 이 헬퍼로.
  상호작용 계열 현황: Interactable(E, 상자·아이템) / 철거(모드+좌클릭) / 전투(Entities.Building HP)
  — 프롬프트(PlayerInteractionManager)와 실행(TryInteract)의 레이캐스트 중복·마스크 불일치는 팀 협의 대기

---

## 2026-07-24 — 건물 SO 레지스트리 + 빌드 메뉴 자동 생성

- **BuildingDatabaseSO** (Resources/BuildingDatabase) — 전체 건물 SO 레지스트리.
  에디터 스캐너(Editor/BuildingDatabaseScanner)가 SO 생성/삭제/이동 시 자동 재수집
  (카테고리 → displayName 정렬). 수동 연결 작업 소멸. 수동 재수집: Tools/Factory/Rebuild
- **BuildingCategory 부활** (생산/물류/저장/방어) — UI 정렬이라는 실소비자가 생겨 재도입.
  기존 6종 에셋에 카테고리 지정 완료
- **PlacementSystem**: 수동 배열(buildingDataList) 제거 → 데이터베이스 참조
  (미연결 시 Resources 폴백 — 씬 배선 불필요)
- **BuildMenuPopup**: B키 → 카테고리별 버튼 자동 생성 팝업 (UIPopup 파이프라인 —
  열림 중 사격/건설 차단, ESC/B로 닫기). 버튼 클릭 = SelectBuilding + 배치 모드 진입.
  UI는 런타임 코드 조립 (씬 저작 0) — UI 담당이 프리팹로 교체 가능하게 표면 분리
- Addressables는 보류 — 로컬 소량 데이터라 async·빌드 단계 비용만 생김.
  DLC/스트리밍 필요 시 데이터베이스 인터페이스 뒤에서 교체 (소비자 무수정)

---

## 2026-08-04 — 건설 비용: 배치 시 차감, 철거 시 전액 환급

`BuildingDataSO.buildCost`(Factory/LOG.md 2026-08-04 참조)의 소비자.

- **전액 환급** — 부분 환급은 배치 실험을 망설이게 만들어 공장 게임의 재미를 깎음
- **`BuildCost` 정적 헬퍼 신설** — 판정(프리뷰 색)·차감·환급·건설 메뉴 표시가 전부 같은 규칙을
  써야 하므로 "얼마가 드는가"를 여기 한 곳에만 둔다.
  `CanAfford` / `TryGetMissing` / `TryCharge` / `Refund` / `PlayerCountOf`
- 소지품이 **핫바 + 가방** 두 컨테이너에 나뉘어 있으므로 셀 때는 항상 합산,
  차감은 **가방부터** (핫바를 최대한 유지)
- **차감은 전량 아니면 무효** — 반쯤 깎인 채 배치가 실패하면 아이템만 사라짐
- **인벤토리가 없으면 통과** — 비용 때문에 심 시나리오 테스트가 막히면 안 됨
- 환급 시 가방에 자리가 없으면 바닥에 드롭 (`PlacementBridge.DropAt`) — 조용히 증발 금지

### 배선 지점

| 위치 | 내용 |
|---|---|
| `PlacementSystem.lastCanPlace` | `&& BuildCost.CanAfford(current)` — 재료가 모자라면 프리뷰가 빨갛게 |
| `PlacementSystem.Place` | `TryCharge` 먼저, 실패 시 로그 남기고 조기 반환 |
| `PlacementSystem.Demolish` | **`PlacementBridge.Remove` 전에** 뷰 위치와 `b.Data`를 캡처 — 제거 후엔 읽을 수 없음 |
| `BuildMenuPopup` | 못 지으면 버튼을 옅게 + 이름 아래 "{재료} {N} 부족" |