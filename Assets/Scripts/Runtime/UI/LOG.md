# UI 작업 로그

> UI Toolkit(UITK) 이관 기록. 최신 항목이 아래.
> 디자인 근거는 `UI_디자인시스템_레퍼런스.html` — 수치·간격은 전부 여기서 온다.

---

## 2026-08-03 — 이관 1단계: 디자인 시스템 + 코어 패널 (SCR-01)

### 왜 UITK인가

기존 UI는 전부 uGUI 런타임 코드 조립(BuildMenuPopup 등) — 씬 저작이 0이라 팀 병합에는
안전했지만, 스타일이 코드에 흩어져 있어 "간격 8px"을 바꾸려면 여러 파일을 뒤져야 했다.
UITK는 **구조(UXML)·스타일(USS)·동작(C#)이 분리**되고 스타일이 파일 하나에 모인다.

### USS 3층 구조

| 파일 | 역할 |
|---|---|
| `tokens.uss` | 색·간격·글꼴 크기 등 원시값 변수만. **여기 말고는 리터럴 색을 쓰지 않는다** |
| `components.uss` | 버튼·칩·게이지 등 재사용 조각 |
| `screens.uss` | 화면별 레이아웃 |

### USS 실측 제약 (추측 금지)

`StylePropertyUtil.s_NameToId`를 리플렉션으로 열어 **실제 지원 프로퍼티 99개**를 확인했다.
문서에 없어서 헤매기 쉬운 것들:

- **없음**: `gap` · `box-shadow` · `line-height` · `grid-*` · `z-index` · `outline` ·
  `background`/`border` 축약형 · `:last-child` · `:focus-within` · `align-items: baseline`
- **이름이 다름**: `font-weight` → `-unity-font-style`, `text-align` → `-unity-text-align`
- **`border-radius`가 x/y 반지름을 독립적으로 클램프** — 납작한 요소에 999px(알약)을 주면
  가로/세로가 따로 잘려 **타원**이 된다. 진행 바는 `height/2`를 직접 써야 함 (tokens.uss에 주석 박아둠)

### 구성 요소

- **`UITKPopup`** — 기존 `UIPopup` 파이프라인(열림 중 사격·건설 차단, ESC 닫기)의 UITK판.
  uGUI 팝업과 같은 표면이라 호출부는 어느 쪽인지 몰라도 됨
- **`CorePanelView`** — 코어 상태 화면. `TryOpen`이 **이미 열려 있으면 `Retarget()`으로 재바인딩** —
  `SetActive(true)`는 no-op이라 `OnEnable`이 안 불려서, 두 번째 코어를 열면 첫 번째가 보이던 버그
- **`RadarScope`** — `Painter2D` 벡터 드로잉. 데이터에 따라 매 프레임 달라지므로 SVG는 부적합
  (SVG는 임포트 시점에 구워짐. 6.3의 네이티브 SVG 지원은 확인했으나 정적 아이콘용)
- **`UIItemPalette`** — 아이템 계통색을 한 곳에서 결정 (`ItemLine` 소비자)
- **`CoreInfoDummyData`** — 아직 없는 시스템(티어·레이더 신호 등)의 자리를 더미로 채움.
  실제 시스템이 붙을 때 이 파일만 지우면 됨

---

## 2026-08-03 — 폰트 3종 도입 + 기존 uGUI 한글 렌더링 복구

- **`Assets/Art/Fonts/`** — ChakraPetch-SemiBold(표제) · IBMPlexSansKR-Regular(본문) ·
  IBMPlexMono-Medium(수치). 전부 SIL OFL, 라이선스 원문 동봉
- **UITK는 기본 테마에서 한글이 나온다** (초기 판단 정정). 문제는 **기존 uGUI/TMP 쪽**이었음 —
  `LiberationSans` 하나뿐이라 한글 10자 중 10자 미표시(두부)
- **수정**: IBMPlexSansKR을 `AtlasPopulationMode.Dynamic`으로 SDF 에셋화 →
  `TMP_Settings.m_fallbackFontAssets`에 전역 폴백 등록. **미리 베이크할 글자 목록이 필요 없다** —
  런타임에 쓰인 글자만 아틀라스에 들어감 (`characterTable`이 0 → 10으로 느는 것 확인)
- 부수: `MainScene` 재직렬화(SystemUIManager 미직렬화 필드),
  `RecipeSocket` 출력 슬롯 인덱스 오류 수정 — 작업 중 발견

---

## 2026-08-03 — 이관 2단계: 건설 메뉴 (SCR-03)

- `BuildMenuPanel.uxml` + `BuildMenuView` — `BuildingDatabaseSO`를 읽어 카테고리별로 채우는
  구조는 uGUI판(GridSystem/LOG.md 2026-07-24)과 동일. 데이터 원천은 그대로 두고 표현만 교체
- `BuildController`가 UITK 패널을 열도록 연동
- 건물 SO 7종에 `displayName`/`description` 채움 — 메뉴가 파일명을 그대로 보여주고 있었음
- `BuildCostDummyData` — `buildCost` 필드가 아직 없던 시점의 자리 채우기
  (**현재는 실데이터로 교체됨** — GridSystem/LOG.md 2026-08-04 참조)

---

## 2026-08-04 — 패널 크기 고정 + 수치를 레퍼런스에 맞춤

- **패널이 내용에 따라 커지면 안 된다** — 크기는 고정, 넘치면 `ScrollView`.
  코어·건설 패널 양쪽 적용. 품목이 2개만 보이던 것도 이 문제(높이가 내용에 눌림)
- 코어 패널의 숫자 표기·구분선 위치·글자 크기를 레퍼런스 수치로 교정, 티어 4단계 더미 추가
- **진행 바가 타원으로 보이던 것** — 위의 `border-radius` 클램프 함정. `height/2`(4·7·10px)로 교체
- 건설 상세 영역: 레퍼런스와 어긋난 비율 교정 (`min-width: 210px`, 재료 칩을 한 줄로 흘림,
  슬롯 표기 제거, 하단 여백 확보)

---

## 2026-08-04 — 레이더 완성 + 건설 메뉴를 클릭 즉시 배치로

### 레이더 (`RadarScope`)

- **점선 원의 점 크기가 반지름마다 달라지던 것** — 대시를 각도로 지정했기 때문.
  **픽셀 호 길이**로 지정하고 반지름별로 각도 환산하도록 변경
- **NO SIGNAL이 코어 점과 겹침** — 레퍼런스의 비율(-0.13R)은 폰트 크기가 고정인데 R만 줄면
  깨진다. 코어 링 반지름 + 텍스트 높이 절반을 바닥값으로 하는 여유를 둠
- **진입점에서 뻗어나가는 지시선이 없고 점 위치도 어긋남** — 코어 쪽 점선 화살표만 그리고
  라벨로 가는 꺾인 선을 빠뜨렸음. 더미 거리도 0.95였으나 레퍼런스는 바깥 링(1.0).
  `EntryLeader()`가 꺾임점·꼬리를 한 번 계산해 **그리기와 라벨 배치가 같은 값**을 쓰게 함
- **레이더 꺼짐 상태에서 INCOMING·DESTROYED NESTS는 `???`** + "항법 계통 오프라인" 표기
- 아래 여백이 남아 레이더를 확대
- 가동 중 상태를 눈으로 확인할 수 있도록 더미 데이터 토글 제공

### 건설 메뉴

- **"배치 시작" 버튼 제거 — 행 클릭이 곧 배치 시작**. 한 번 더 누르게 할 이유가 없음
- **떠다니는 툴팁 → 하단 고정 상세 영역** (없어진 버튼 자리를 넓혀 사용).
  커서를 따라다니는 툴팁은 목록을 훑을 때 시선이 튄다

---

## 남은 이관 (예정)

| | 화면 |
|---|---|
| 2단계 잔여 | 분배기 필터 (Factory/LOG.md 2026-08-01의 uGUI판) |
| 3단계 | 인벤토리 · 핫바 · 제작 |
| 4단계 | HUD |

### 알려진 TODO

- **SCR-01b 수리 확인 모달** — 티어 상승을 보류시키는 게임플레이 변경이 선행돼야 함
- 레거시 입력 잔재: `ItemTooltipUI.cs:29`, `InventoryManager.cs:133`의 `Input.mousePosition`
- 건물 SO의 `icon` 필드가 전부 비어 있음 — 메뉴가 아이콘 없이 렌더됨
