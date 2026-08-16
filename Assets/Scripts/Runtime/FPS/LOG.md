# FPS(무기·플레이어) 시스템 작업 로그

> 작업 기록. 최신 항목이 아래. (2026-08-07 두 항목은 별개 브랜치에서 같은 날 작업 후 병합됨)

---

## 2026-08-07 — PlayerController 대개편 (이동 FSM + 카메라/무기 정식 연결) [main #53]

### 문제

- 이동이 `rb.linearVelocity = target` 통짜 덮어쓰기 — 관성·가속이 존재하지 않음.
- 접지 판정이 `Mathf.Abs(rb.linearVelocity.y) < 0.001f` 꼼수. 경사·계단·낙하에서 전부 오판.
- 카메라/무기 모션 모듈이 플레이어와 **정식 경로 없이** 붙어 있었다:
  - `HandSway`가 구 입력 시스템(`Input.GetAxisRaw("Mouse X")`)을 직접 읽음 —
    게임 전체가 쓰는 입력 파이프라인(맵 스택·우선순위 라우팅)을 통째로 우회.
  - `HandSway`가 `Physics.Raycast`로 접지를 **따로** 판정 (컨트롤러와 별개의 진실).
  - `ProceduralRecoil`이 RecoilHolder의 localRotation을 **통째로 덮어씀** → 다른 카메라 모션을
    같은 노드에 얹을 자리가 없었다.
  - `WeaponADS`가 `camera.fieldOfView`를 직접 씀 → 속도/슬라이딩 FOV와 충돌.
- 앉기/슬라이딩 없음.

### 구조

```
Player            요(yaw) + 물리 + 이동 FSM      PlayerController
└ CameraHolder    피치 + 눈높이 + FOV            PlayerCameraRig
  └ RecoilHolder  절차적 오프셋 합성              CameraMotionManager
    │                └ ICameraMotionModule: CameraMovementTilt / CameraStepBob / ProceduralRecoil
    ├ Main Camera  충격 흔들림                   CameraShakeManager (기존 유지)
    └ Weapon_Holder 무기 모션 합성                WeaponMotionManager
                     └ IWeaponMotionModule: HandSway / WeaponStancePose / WeaponADS / WeaponKickback
```

**모든 연결은 `PlayerMotionState` 하나를 거친다.** 컨트롤러가 쓰고, 모듈은 읽기만 한다
(`IPlayerMotionProvider`로 조회 — 구체 타입 의존 없음).
게시 항목: 속도/로컬속도/로컬가속도, 시점 델타(+EMA), 접지·지면 법선·경사각, 자세와
`CrouchWeight`/`SlideWeight`, `AimWeight`, 그리고 **공용 보행 위상 `StrideCycle`**.
이산 사건(점프/착지/자세 전환/슬라이드 시작·종료/발걸음)은 이벤트로 나간다.

카메라 보브와 무기 보브가 같은 `StrideCycle`을 쓰므로 손과 시야가 절대 어긋나지 않는다.

### 이동 (PlayerController + IPlayerLocomotionState)

- 상태: `Grounded` / `Crouching` / `Sliding` / `Airborne`.
  입력 파이프라인 설계문서 §6의 `IPlayerState`를 실제로 구현한 것 —
  "상호배타적 상태가 없어 보류"였던 판단이 앉기/슬라이딩 도입으로 뒤집혔다.
- **운동량**: 가속/마찰을 `MoveTowards`로 적용. 진행 방향의 반대로 입력하면
  제동 배율(`counterStrafeBoost`)이 붙어 방향 전환이 즉각적이다.
- **공중**: 퀘이크식 공중 가속 — 이미 가진 속도는 건드리지 않고 부족한 방향만 채운다.
  스트레이프로 운동량이 유지·연장된다. 코요테 타임 + 점프 선입력 버퍼 + 가변 점프 높이.
- **접지**: 스피어캐스트 + 경사각 판정. 접지 중에는 중력을 끄고 속도를 지면 평면에 눕혀
  (속력 보존) 경사에서 저절로 미끄러지지 않는다. 급경사는 접지로 치지 않아 중력에 흘러내린다.
  흡착은 **발밑에 실제로 뜬 틈만큼만** — 평지에서 상시로 누르지 않아 카메라가 떨지 않는다.
- **자기 콜라이더 제외**: 접지 캐스트와 머리 위 공간 검사 모두 `NonAlloc` + `IsSelf` 필터.
  레이어 마스크로 거르면 다른 오브젝트까지 같이 사라지므로 그렇게 하지 않았다.
- **슬라이딩**: 진입 임펄스 → 마찰 감속, 내리막에서만 가속. 제한적 조향(각속도 상한),
  슬라이드 점프는 속도를 그대로 들고 뜬다. 종료 조건 = 저속 / 시간 초과 / 앉기 해제.
- **자세 기하**: 콜라이더 높이와 눈높이가 **DOTween 한 곡선**에서 유도된다(발바닥 고정).
  몸과 시야가 어긋날 수 없다.

### 연출

| 신호 | 카메라 | 무기 |
|---|---|---|
| 좌우 이동 / 시점 회전 | `CameraMovementTilt` 롤 | `HandSway` 롤 + 위치 스웨이 |
| 전후 가속 / 낙하 | 피치 + 위치 리드 | 가속 관성 오프셋 |
| 보행 | `CameraStepBob` 8자 보브 | `HandSway` 보브 (같은 위상) |
| 착지 / 점프 | DOTween 딥·킥 + FOV 펀치 | DOTween 드롭·상승 |
| 달리기 | 속도 비례 FOV | `WeaponStancePose` 총기 하강 포즈 |
| 슬라이딩 | 롤·피치·요 + FOV 펀치 | 크게 눕히고 뒤로 당김 |
| 사격 | `ProceduralRecoil` (속도 비례 증폭) | `WeaponKickback` |
| 조준(ADS) | FOV 전환 | 위 항목 전부 `AimWeight`로 억제 |

- `MotionSpring` — 감쇠 스프링 닫힌형 해석해를 공용 유틸로 승격(구 WeaponKickback 내부 코드).
  카메라 반동·발걸음 충격·무기 킥백이 같은 수식을 공유해 모션 톤이 하나로 맞는다.
  프레임률 독립 `Damp()`도 여기 있다.
- FOV 소유권을 `PlayerCameraRig`로 일원화. `WeaponADS`는 `AimWeight`/`AimFov`만 게시한다.
- 사격/조준은 `PlayerController.SuppressSprint()`를 호출 — "총을 내린 채 발사"가 불가능하다.

### 입력

- `InputActionId`에 `Sprint`(LeftShift, 게임패드 L3) / `Crouch`(LeftCtrl, C) 추가.
  `GameInput.inputactions`의 Gameplay 맵에도 같이 반영.
- 앉기는 홀드가 기본, `PlayerController.crouchIsToggle`로 토글 전환 가능.

### 씬/프리팹

`Assets/Prefabs/Player.prefab`에 반영 (MainScene 인스턴스로 전파 확인):
- CameraHolder → `PlayerCameraRig`
- RecoilHolder → `CameraMotionManager`, `CameraMovementTilt`, `CameraStepBob`
- Weapon_Holder → `WeaponStancePose`
- `groundMask` = Everything (자기 콜라이더는 코드에서 필터)

### 남은 것

- 발소리/슬라이드 SFX — `PlayerMotionState.Stepped` / `SlideStarted` 이벤트에 붙이면 된다.
- 벽 달리기·매달리기 같은 추가 이동은 `IPlayerLocomotionState` 구현체를 하나 더 붙이면 끝.
- 계단 오르기(step offset)는 미구현 — 낮은 턱은 경사로/램프로 처리 중.
- 애니메이션(3인칭 리그) 연동 시에도 `PlayerMotionState`를 그대로 읽으면 된다.

---

## 2026-08-07 — 총기 코드 Runtime 이관 + 구조 정리 [Feature_Damage]

### 배치
`Test/WeaponAdvanced/` 전체를 `Runtime/FPS/Weapon/`으로 (메타 동반 이동 — GUID 보존이라
프리팹·씬 참조 무손상). Gun·GunData·WeaponController도 같은 폴더로 모아
무기 관련 코드가 한 곳에 있다.

### 외부 접근 — 접근 규칙을 세움
정리 전에는 조준 상태(isAiming·targetFov)를 연출 모듈(WeaponADS)이 public 필드로 소유하고,
입력·Gun·킥백이 `weaponManager.adsModule.isAiming` 같은 사슬로 직접 읽고 썼다.

| 계층 | 규칙 |
|---|---|
| `WeaponManager` | **허브** — 장착/교체·조준 상태(IsAiming)의 원본. 외부는 공개 API만: `CurrentWeapon`·`EquipWeapon`·`UnequipWeapon`·`SetAiming` |
| 연출 모듈 | 전부 `[SerializeField] private` — 밖에서 모듈 필드를 찌르는 경로 차단 |
| `Gun` | 게임플레이만(탄창·연사·탄퍼짐·발사). **연출을 모른다** — `Fired` 이벤트만 쏘고, 매니저가 셰이크·리코일·킥백으로 팬아웃(전부 null 가드) |
| `WeaponADS` | `SetAiming`/`SetupWeapon(sight, zoomFov)`로 받기만 함 |

`Gun.isReloading`(public 가변) → `IsReloading { get; private set; }`.
`Gun.weaponManager` 역참조 필드 삭제 (이벤트 구독으로 대체).

### 데이터 구조
- 죽은 필드 삭제: `GunData.maxRange`(참조 0 — `range`와 중복), `weaponType`+`WeaponType` enum(참조 0).
- **`zoomFOV`를 Gun(컴포넌트) → GunData(데이터)로** — 총의 성격 수치는 전부 SO에.
- GunData를 역할별 헤더(발사/탄창/조준/반동/킥백/탄퍼짐)로 재편, fireRate가
  "초당 발수"가 아니라 **발사 간격(초)**임을 툴팁에 명시.

### 남은 것
- weapon·ammo·effect의 json 임포트 스키마 — effects 섹션 + guns 섹션 설계안 제시됨,
  총 json 소유 범위(전투 정합만 vs 전부) 결정 대기.
- Test_Gun.asset의 `gunName`이 비어 있음 (HUD 탄약 표기에 빈 이름 노출) — 데이터 정리 대상.

---

## 2026-08-07 — 위 두 작업의 병합 (main → Feature_Damage)

같은 날 두 브랜치가 같은 파일들을 고쳤다. 병합 원칙: **모듈 내부 = #53(모션 스테이트 기반이
더 새 아키텍처), 배치·접근 규칙 = Feature_Damage 유지.**

- 모듈 전부 `Runtime/FPS/Weapon/`에 정착 (WeaponStancePose 포함).
- `WeaponADS` = #53 내부(AimWeight/AimFov 게시, MotionSpring, IsAimAllowed, OnDisable 리셋)
  + 캡슐화 유지(`SetAiming`/`SetupWeapon(sight, zoomFov)`, 호환용 camera·defaultFov·targetFov
  필드 삭제 — FOV는 PlayerCameraRig 소유, 줌 FOV는 GunData.zoomFOV가 원본).
- `WeaponController` = 캡슐화 API 호출 + #53의 `SuppressSprint` 결합.
- `ProceduralRecoil`·`WeaponMotionManager`·`HandSway`·`WeaponKickback` = #53 버전.
  킥백의 `Fire(z, rot, isAiming)` 시그니처가 유지돼 매니저 팬아웃과 그대로 호환.
- `Monster` = #53(보스·둥지 방어자) + Feature_Damage(combat.Initialize, CrowdSystem 등록).
- PR #54(RecipeManager 재추가)는 팀 결정으로 **취소** — 스크립트 삭제, UITest는 병합 전 상태 유지.

---

## 2026-08-16 — 반동이 '탁탁' 끊긴 이유: 해석해는 정확해도 화면은 표본만 본다

반동은 감쇠 스프링 **닫힌형 해석해**라 수치적으로는 흠이 없다. 그런데도 발사할 때 카메라가
진동이 아니라 순간이동처럼 튀었다.

원인은 수학이 아니라 **표본화**다. 씬 설정이 13Hz였는데, 60fps로 그리면 한 주기가
**4.6프레임**이다. 중간이 없으니 곡선이 아니라 계단으로 보인다.

`MotionSpring.Visible(frequency, dt)` — 한 주기가 최소 8프레임에 걸치도록 진동수를 낮춘다.
상수를 고치지 않고 코드에서 거는 이유는 진동수가 **씬에 저장된 값**이라 씬마다 흩어져 있고,
무엇보다 이 상한이 **프레임률에 따라 달라져야** 하기 때문이다:

| 프레임률 | 13Hz 요청 → 실제 | 주기당 프레임 |
|---|---|---|
| 30fps | 3.8Hz | 8.0 |
| 60fps | 7.5Hz | 8.0 |
| 120fps | 13Hz (그대로) | 9.2 |

여유가 있는 기기에서는 원래의 날카로움이 그대로 살아난다. 임펄스 역산
(`SolveImpulseVelocity`)도 같은 진동수를 써야 "딱 그만큼 튄다"가 성립한다.

### 카메라 흔들림(CameraShakeManager)

펄린 노이즈를 트랜스폼에 그대로 꽂던 것을 스프링 추종(`followSharpness`)으로 한 겹 걸렀다.
감쇠도 선형에서 smoothstep으로 — 선형이면 끝까지 진폭이 남아 있다가 뚝 끊긴다.
발사 흔들림 자체도 12Hz → 7Hz.

### 흔들림은 Base 카메라에 걸어야 한다

카메라를 Base(Main, 월드) / Overlay(무기 레이어)로 쪼개면서 `CameraShakeManager`가
**Overlay Camera**에 딸려 내려가 있었다. Overlay는 무기 레이어(12)만 렌더하므로 그 결과가
이랬다:

| | 반동 (RecoilHolder) | 셰이크 (Overlay) |
|---|---|---|
| 월드 | 흔들림 | **안 흔들림** |
| 무기 | 흔들림 | 흔들림 |

같은 사건(발사)인데 하나는 화면 전체를, 하나는 무기만 흔들어 **총이 월드에 대해 미끄러졌다**.
Main Camera로 옮기면 Overlay가 그 자식이라 함께 흔들려 둘이 같은 양으로 움직인다.
덤으로 `AudioListener`가 Main·Overlay 양쪽에 있어 Unity가 경고하던 것도 정리했다
(흔들리는 쪽에 리스너가 있으면 3D 정위가 미세하게 떨린다).

배선은 `Assets/Prefabs/Player.prefab`에 있다 — 씬이 아니라 프리팹이라 한 번 고치면 전부 따라온다.
RecoilHolder(반동) 아래에 Main(셰이크)이 있으므로 **두 흔들림은 계층으로 누적**된다.
세면 `globalIntensityScale`로 줄인다.

검증: 셰이크 대상=Main Camera · AudioListener 1개 · Main이 흔들릴 때 Overlay 월드 좌표가
Main과 완전히 일치(함께 움직인다).

### 빈 탄창은 알아서 채운다

마지막 탄을 쏘고 나서 방아쇠를 한 번 더 당겨야 재장전이 시작되던 것을 없앴다.
`Update`에서 보는 이유는 시점 하나에 걸어서는 안 되기 때문이다 — `Start`나 장착 시점에
걸어봤더니 **초기화 중 무기가 껐다 켜지면서 `OnDisable`의 `StopAllCoroutines`가
재장전을 끊어버렸다**(실측: 예비 500발을 들고도 탄창이 0으로 남았고, 같은 함수를 직접
부르면 정상 장전됐다). 인벤토리에 탄이 없으면 `StartReload`가 조용히 물러나므로 헛돌지 않는다.

같은 이유로 `OnEnable`의 `IsReloading = false`가 중요하다 — 재장전 도중 무기를 바꾸면
코루틴만 끊기고 플래그는 남아, 그 총이 영영 '재장전 중'으로 굳어 쏘지 못한다.
