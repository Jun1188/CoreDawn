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

## 2026-08-03 — 건설 메뉴 UITK 이관 (표면 교체)

- `BuildController`가 uGUI `BuildMenuPopup` 대신 UITK 패널을 연다.
  **`BuildingDatabaseSO` → 카테고리별 자동 생성**이라는 데이터 경로(2026-07-24)는 그대로 —
  바뀐 건 표현 계층뿐. 상세: UI/LOG.md 2026-08-03
- `PlacementSystem`은 코어 자동 설치(Factory/LOG.md 2026-08-03)의 배치 경로로 재사용됨
- **행 클릭 = 즉시 배치 모드 진입** (2026-08-04) — "배치 시작" 확인 단계 제거
