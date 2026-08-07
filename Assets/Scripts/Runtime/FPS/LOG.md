# FPS(무기) 시스템 작업 로그

> Claude Code와 진행한 구조 검토·수정 기록. 최신 항목이 아래.

---

## 2026-08-07 — 총기 코드 Runtime 이관 + 구조 정리

### 배치
`Test/WeaponAdvanced/` 전체를 `Runtime/FPS/Weapon/`으로 (메타 동반 이동 — GUID 보존이라
프리팹·씬 참조 무손상). Gun·GunData·WeaponController도 같은 폴더로 모아
무기 관련 코드가 한 곳에 있다. `Runtime/FPS/` 루트에는 PlayerController만 남음.

### 외부 접근 — 접근 규칙을 세움
정리 전에는 상태 소유가 뒤집혀 있었다: 조준 상태(isAiming·targetFov)를 **연출 모듈**
(WeaponADS)이 public 필드로 소유하고, 입력(WeaponController)·Gun·킥백이
`weaponManager.adsModule.isAiming` 같은 사슬로 직접 읽고 썼다.

| 계층 | 규칙 |
|---|---|
| `WeaponManager` | **허브** — 장착/교체·조준 상태(IsAiming)의 원본. 외부는 공개 API만: `CurrentWeapon`·`EquipWeapon`·`UnequipWeapon`·`SetAiming` |
| 연출 모듈 4종 | 전부 `[SerializeField] private` — 밖에서 모듈 필드를 찌르는 경로 차단. 필드명 유지로 프리팹 직렬화 보존 |
| `Gun` | 게임플레이만(탄창·연사·탄퍼짐·발사). **연출을 모른다** — `Fired` 이벤트만 쏘고, 매니저가 셰이크·리코일·킥백으로 팬아웃(전부 null 가드). 구 `ApplyRecoil`의 싱글턴 직접 호출·매니저 역참조 삭제 |
| `WeaponADS` | `SetAiming`/`SetupWeapon(sight, zoomFov)`로 받기만 함. `new Camera camera` 멤버 가림 → `targetCamera`(FormerlySerializedAs) + null 가드 |

`Gun.isReloading`(public 가변) → `IsReloading { get; private set; }`.
`Gun.weaponManager` 역참조 필드 삭제 (이벤트 구독으로 대체).

### 데이터 구조
- **죽은 필드 삭제**: `GunData.maxRange`(참조 0 — `range`와 중복), `weaponType`+`WeaponType` enum(참조 0).
- **`zoomFOV`를 Gun(컴포넌트) → GunData(데이터)로** — 총의 성격 수치는 전부 SO에.
  두 프리팹 값이 기본값(50)과 같아 마이그레이션 불요.
- GunData를 역할별 헤더(발사/탄창/조준/반동/킥백/탄퍼짐)로 재편, fireRate가
  "초당 발수"가 아니라 **발사 간격(초)**임을 툴팁에 명시.
- `WeaponMotionManager`: 인터페이스 배열의 무의미한 `[SerializeField]` 제거(직렬화 불가),
  null 가드 (과거 콘솔에 실제 NRE 기록 있음).
- `CameraShakeManager`: 중복 싱글턴 시 `Destroy(gameObject)` → `Destroy(this)` —
  카메라 리그에 붙는 컴포넌트라 오브젝트째 지우면 카메라가 날아간다. 죽은 주석 블록 제거.

### 검증
PlayLoopTest 실측: `EquipWeapon` → `TryFire`(발사 성공, 탄 30→29, 팬아웃 예외 0) →
`SetAiming` 토글 정상. 19시대 예외 0건.

### 남은 것
- weapon·ammo·effect의 json 임포트 스키마 — effects 섹션 + guns 섹션 설계안 제시됨,
  총 json 소유 범위(전투 정합만 vs 전부) 결정 대기.
- Test_Gun.asset의 `gunName`이 비어 있음 (HUD 탄약 표기에 빈 이름 노출) — 데이터 정리 대상.
