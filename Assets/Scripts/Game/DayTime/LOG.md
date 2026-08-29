# DayTime 시스템 작업 로그

> 낮/밤 주기 시스템의 구조 결정과 변경 이력. 최신 항목이 아래.

---

## 2026-07-10 — DayTime 시스템 도입

- **기획 맥락**: 낮(건설/자동화) ↔ 밤(웨이브 디펜스) 코어 루프. 로그라이크 보상은 웨이브 클리어 시
- **구조** (Factory와 같은 심/뷰 분리 원칙):
  - `DayCycle` — plain C# 심 코어. 페이즈(낮/밤)·일수·진행도·`NormalizedTimeOfDay`(0=일출, 0.25=정오, 0.5=일몰, 0.75=자정)·`DayStarted`/`NightStarted` 이벤트. 페이즈 경계를 넘긴 시간은 다음 페이즈로 이월
  - `TimeManager` — 시간 전용 Mono 드라이버(싱글톤). 낮/밤 길이 설정, 매 프레임 `Advance`, 전환 로그·HUD 갱신. **GameManager에 넣지 않고 별도 매니저로 분리** (팀 결정)
  - `DayNightLightingView` — 해 각도/색/강도/앰비언트 연출. `Reset()`이 기본 연출값 자동 세팅
  - `GameManager` — 시간 로직을 덜어내고 세이브/로드 훅만. 아침 자동 저장은 `DayStarted` 구독
  - `DayCycleDebugHUD` (Test/DayTime) — 상태 표시 + 배속 x5 + 밤 조기 종료 버튼
- **확정 결정**:
  - 밤 종료 = 고정 시간(nightDuration) + **`EndNightEarly()` API** — 웨이브 매니저가 적 전멸 시 호출하면 즉시 아침. 낮에 호출하면 무시
  - 밤 건설 제한은 **미연동** — `TimeManager.IsBuildingAllowed`만 준비 (연동 시점은 추후 결정)
  - `DayPhase` enum은 top-level (구 GameManager 중첩 enum에서 이동, SystemUIManager 참조 갱신)
- **확장 지점** (시간 시스템은 소비자를 모름):
  - 웨이브 스포너 → `TimeManager.Instance.Cycle.NightStarted += day => ...`
  - 로그라이크 보상/상점 → `DayStarted` 구독
  - 조명 외 연출(스카이박스, 사운드) → `NormalizedTimeOfDay` 참조

---

## 2026-07-10 — 조명 자정 시작 버그 수정

- **증상**: 게임 시작(1일차 낮)인데 화면이 자정처럼 어두움
- **원인**: 해 각도 공식이 `t*360 - 90` — 일출(t=0)에 X=-90°, 즉 해가 지평선 아래에서 위로 쏘는 자정 자세였음
- **수정**: `X = t*360` — 일출 0°(지평선), 정오 90°(머리 위), 일몰 180°, 자정 270°