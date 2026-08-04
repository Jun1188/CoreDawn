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

---

## 2026-08-04 — 포트 흐름 시각화 (배치 중 "어디에 이을 수 있나")

- **문제**: 건물을 놓을 때 "이 면에 벨트를 물릴 수 있나"를 알 방법이 인스펙터의 포트 배열을
  읽는 것뿐이었음. 연결이 안 되면 런타임에는 조용한 stall로만 나타남
- 에디터 표시(면에서 번지는 반투명 그라디언트)를 게임으로 이식. **파랑 = 입력, 귤색 = 출력**

### 조각 3개 (`PortFlowVisualizer`)

| | 크기 (바깥 × 좌우/높이) | 역할 |
|---|---|---|
| 짝대기 | 0.10 × 0.90, 면에 밀착 | 포트의 위치와 폭 |
| 바닥판 | 0.58 × 0.90 | 바닥에 번지는 흐름 방향 |
| 지느러미 | 0.58 × 0.36, 수직 | 위에서 봐도 납작해 보이지 않게 |

- **짝대기만 알파 합성, 나머지는 가산.** 전부 가산이면 겹친 자리의 알파가 더해져 채널이 1에
  물리고 흰색으로 날아간다 — 그러면 계통색이 사라져 입력/출력 구분이 안 된다.
  셰이더가 프래그먼트를 알파 프리멀티플라이드로 내보내므로 dst 인자(`One` /
  `OneMinusSrcAlpha`)만 갈아끼우면 두 방식이 다 나온다
- **Quad는 로컬 +X가 "면에서 바깥"이 되도록 눕힌다** — 셰이더가 `uv.x`를 흐름 축으로 읽는다.
  축을 손볼 일이 생기면 셰이더와 컴포넌트 중 **한쪽만** 건드릴 것 (양쪽 뒤집으면 원위치)
- **내장 Quad 메시를 직접 붙인다.** `CreatePrimitive`는 콜라이더를 강제로 붙이는데 `Destroy`는
  프레임 끝에야 실행돼 그 프레임의 레이캐스트를 가로막고, 에디트 모드에선 아예 안 지워진다

### 언제 보여주나 (`PortFlowOverlay`)

| 상황 | 표시 |
|---|---|
| 배치 모드 | 시야 안 모든 건물의 **열린 포트** + 프리뷰 건물의 전체 포트 |
| 건물 조준 | 그 건물의 전체 포트 (연결된 것 포함) |
| 그 외 | 끔 |

- **열린 포트** = 이웃 칸에 맞물리는 포트가 없는 포트. 이미 물린 포트는 숨긴다 — 공장이 커질수록
  표시가 저절로 줄고, 남은 흐름이 전부 "여기에 이을 수 있다"는 뜻이 된다
- 맞물림 판정은 `BuildingGraph`의 연결 규칙(방향 반대 + 입출력 반대)과 같되 **포트의 칸까지 본다**.
  칸을 빼면 2×2 보관소에 벨트 하나만 물려도 나머지 면이 전부 닫힌 것으로 보인다
- 갱신은 그리드가 바뀔 때만. 프리뷰는 매 프레임 움직이지만 재생성하지 않고 루트 위치만 옮기고,
  회전·건물·벨트 모양이 바뀐 프레임에만 다시 만든다. 반경 30타일 밖은 만들지 않는다
- 머티리얼은 (입력/출력) × 조각 3개 = 6개 공유. `MaterialPropertyBlock`을 안 쓰는 이유는
  URP SRP Batcher가 MPB를 지원하지 않아 배칭이 깨지기 때문
- **빌드 주의**: 씬의 PlacementSystem에 `PortFlowOverlay`를 붙이고 `flowMaterial`에 `PortFlow.mat`을
  연결해야 한다. 비워두면 `Shader.Find` 폴백이라 빌드에서 셰이더가 스트립된다

### 곁들여

- `Building.HasPortAt` — 지정 칸에서 지정 방향을 향하는 포트가 있는지 묻는 기하 질의.
  연결 성립 규칙 자체는 `BuildingGraph`가 계속 소유한다
- `PlacementSystem.TryGetAimedBuilding`에 심 null 가드 — 이 쿼리가 이제 대기 모드에서도 매 프레임
  도는데, 심이 없는 씬(UI 테스트 등)에서 매 프레임 예외가 났을 것

---

## 2026-08-04 — 철거를 홀드식으로 (SCR-06)

- **문제**: 클릭 한 번에 건물이 사라진다. 벨트 옆 조립기를 실수로 날리기 쉽고, 밤이 오기 전
  급할 때 특히 그렇다
- **변경**: 좌클릭을 **누르고 있는 동안**만 진행한다 (기본 0.4초, 인스펙터 조절).
  `BuildController`가 Attack의 `Started`/`Canceled`를 받고, `PlacementSystem`이 센다
  — 이 파이프라인은 원래 `Performed`만 보던 곳이라 진입부 가드를 갈랐다
- 누른 채로 다른 건물을 조준하면 **그쪽부터 다시 센다**. 손을 뗐다 다시 누르게 하면
  연속 철거가 번거로워진다
- `ConfirmAtAim`의 즉시 철거는 남겨 뒀다 — UI 버튼·테스트처럼 이미 확인을 거친 호출자용
- 진행 상황은 HUD의 홀드 링이 그린다 (UI/LOG.md 2026-08-04).
  `HoveredBuilding` · `DemolishHoldProgress` · `DemolishHoldRemaining`을 조회용으로 공개
- **범위 철거(드래그)는 넣지 않는다** — 오조작 시 피해가 크다. 한 번에 하나씩
