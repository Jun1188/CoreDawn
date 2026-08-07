# FPS / Player Controller — 작업 로그

## 2026-08-07 — PlayerController 대개편 (이동 FSM + 카메라/무기 정식 연결)

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
