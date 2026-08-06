# Combat 시스템 작업 로그

> Claude Code와 진행한 구조 검토·수정 기록. 최신 항목이 아래.

---

## 2026-08-06 — 공격을 효과(Effect)로 갈아엎기 — 효과 시스템 도입

### 배경
기존 데미지 경로는 세 갈래(총알 `Bullet.TryApplyDamage`, 근접 `CombatComponent.TryAttack`,
타워 폴백) 전부 `Entity.TakeDamage(float)` 하나로 수렴했다. "누가·무엇으로 때렸는지"가
전달되지 않아 속성 피해·지속 피해·감속·저항 같은 걸 얹을 자리가 없었다.
→ **피해도 효과의 하나**로 만들고, 공격은 효과 목록을 명중 대상에게 적용하는 것으로 재정의.

### 설계 (사용자 확정)
- **코드 로직 + SO 파라미터**: 효과 종류는 C# 클래스, 수치는 SO 에셋 (AmmoItemSO 패턴).
- **범위**: 즉시(피해·회복) + 지속(DoT·감속)까지. 넉백·스탯 버프는 다음 기회.
- **수치 전달**: 발당 피해는 시전측이 정하므로(`GunData.damage`, 탄약 피해 × 포탑 배율,
  `attackDamage`) `EffectContext.Power`로 실어 보내고, 효과 SO는 배율·가산만 정의한다.
  SO는 공유 에셋이라 시전자별 수치를 담을 수 없다는 게 핵심 제약.
- **호환**: 효과 목록이 비면 `DamageEffectSO.Default`(Power 그대로)로 처리 —
  기존 프리팹·데이터가 에셋 배선 없이 그대로 동작한다. `TakeDamage(float)`는
  효과 파이프라인으로 위임하는 호환 셔틀로 유지.

### 새 파일 — `Runtime/Combat/` (SO 정의는 Factory처럼 `SO/` 폴더로 분리)
| 파일 | 역할 |
|---|---|
| `Effects/EffectContext.cs` | Source·Power·HitPoint를 담는 readonly struct |
| `Effects/EffectController.cs` | 엔티티당 활성 효과 목록 (순수 C#, Entity.Update가 Tick 구동). 감속 최솟값 집계 → `MoveSpeedMultiplier` |
| `Effects/IMoveSpeedModifier.cs` | 감속류가 구현 — 겹치면 곱 대신 **최솟값**(수치 폭주 방지) |
| `SO/EffectSO.cs` | 즉시 효과 베이스 (`Apply`) |
| `SO/DurationEffectSO.cs` | 지속 효과 베이스 — Apply가 EffectController에 등록. duration·stacking(Refresh/Stack)·OnStart/OnTick/OnEnd |
| `SO/DamageEffectSO.cs` | flat + Power×scale 피해. `Default` = 효과 미지정 폴백 |
| `SO/HealEffectSO.cs` | 회복 |
| `SO/DamageOverTimeEffectSO.cs` | 틱 피해 (간격·틱당 피해·Power 배율) |
| `SO/SlowEffectSO.cs` | 감속 (배율 0~1) |

### 연결 지점
- `Entity`: `Effects`(지연 생성) + `ApplyEffects(효과들, ctx)` 단일 진입점. Update에서
  `Effects.Tick` + `Movement.SpeedMultiplier` 주입. 사망 시 `Effects.Clear`(시체 DoT 방지).
- `MovementComponent`: `SpeedMultiplier` 프로퍼티 — 경로/방향 이동·분리 조향 모두
  `moveSpeed × 배율`로 통일.
- `CombatComponent`: `attackEffects` 직렬화 + `Initialize(owner)` (Monster/Player/BattleTower
  Awake에서 주입 — Movement.Initialize와 같은 패턴).
- `Bullet.Setup(..., effects, source)`: 피해 0이어도 효과탄이면 유효하도록 판정 수정.
- `GunData.attackEffects` → `ProjectileGun`이 발사 시 탑재. 발사자는 `WeaponBase.OwnerEntity`
  (Player가 런타임 부착이라 찾을 때까지 지연 재시도).
- `TowerDataSO.attackEffects` → `BattleTower.FireBullet`이 탄에 탑재.

### 감속 필드 타워 가동 (첫 실사용처)
- 데이터만 있고(배율 0 = IsPassive) 감속 로직이 없던 `SlowFieldTower`를 **펄스형 오라**로:
  `BattleTower.TickAura` — fireRate 주기(5초)마다 범위 내 모든 몬스터에게 효과 적용,
  펄스 1회 = 에너지 셀 1개. **범위가 비면 연료를 태우지 않는다.**
- `TowerBehavior.TryConsumeRound`의 IsPassive 가드 제거 — 감속 필드도 연료는 소비해야 한다.
  발사 여부는 BattleTower가 IsPassive로 가르므로 역할 충돌 없음.
- 에셋: `Assets/Data/Effects/Effect_SlowField.asset` (배율 0.5, 지속 6초 — 펄스 5초보다
  길게 잡아 필드 안에서 감속이 끊기지 않게). GameDataImporter는 attackEffects를 건드리지
  않으므로 재임포트에도 배선이 살아남는다.

### 함정 — 만료 순간의 마지막 틱 유실
dt 누적 부동소수 오차(1e-8대)로 `tickTimer`가 0에 아주 미세하게 못 미쳐,
"2초/0.5초 = 4틱"이 3틱이 되는 케이스가 검증에서 실측됐다.
→ 틱 판정에 엡실론(1e-4) 적용. 밸런스 수치가 "N틱"으로 설계되는 이상 결정적이어야 한다.

### 검증
- 헤드리스 14/14: 순수 피해 폴백·TakeDamage 호환·피해 배율·DoT 4틱·만료 제거·
  Refresh(피해 2배 아님)/Stack(2배) 중첩·감속 단일/최솟값/만료 복귀/Refresh 연장·
  사망 정리·시체 적용 거부.
- 플레이 실측(PlayLoopTest): 몬스터 소환 후 감속 적용 → controller 0.5 즉시,
  다음 Update에 Movement 0.5 반영. 게임 코드 예외 0건.

### 남은 것 / 알아둘 것
- **플레이어는 감속 대상이 아니다** — Player.Movement가 null(FPS 이동은 PlayerController).
  플레이어 감속이 필요해지면 PlayerController가 `Effects.MoveSpeedMultiplier`를 읽으면 된다.
- 가속(배율 >1)은 현재 집계(기준 1에서 최솟값)로는 표현 불가 — 넣을 때 집계 방식 재논의.
- 효과 상태 아이콘 UI는 `EffectController.Changed` 이벤트에 붙이면 된다 (아직 미구현).
- GameData.json에는 아직 효과 항목이 없다 — 효과를 json으로 정의하려면 임포터 확장 필요.

---

## 2026-08-06 — 넉백·스탯 버프 + SC2식 군중이동(CrowdSystem)

### 군중이동 갈아엎기 — `Test/Entity/Manager/CrowdSystem.cs`
구 `MovementComponent.ApplySeparation`(개체별 OverlapSphere + 힘 기반 + 속도 클램프)을
버리고, SC2식 **위치 기반 겹침 해소**를 중앙 한 패스로:
- **힘이 아니라 위치 보정** — 겹친 양(반지름 합 − 거리)을 그대로 되돌린다.
  한 패스에 딱 붙어 정지 (실측: 거리 0.4 → 정확히 0.800).
- **비대칭 분배** — 이동 중 3 : 정지 1 가중치. 움직이는 쪽은 겹침의 1/4만 밀리고
  정지한 쪽이 3/4 비켜준다 (실측 1:3 정확).
- **속도 클램프 없음** — 겹침 해소는 이동과 별개 레이어. 완전 겹침도 한 패스에 풀린다.
- 구동: `Monster.OnEnable/OnDisable` 등록부(BuildingEntity.All 패턴) → 첫 등록 때
  러너 생성 → 모든 이동이 끝난 `LateUpdate`에 Solve. 보정은 모아서 마지막에 일괄 적용
  (순회 순서 편향 제거). 시체·비활성 제외, 워커빌리티 필터 유지.
- **플레이어는 군중 밖** — 플레이어는 몬스터를 밀지 않는다(사용자 확정). 몬스터가
  플레이어를 미는 건 기존 PhysX 접촉(kinematic 콜라이더 vs 플레이어 dynamic RB) 그대로.
- 물리 정리 실측: Monster.prefab·스폰 경로 모두 이미 kinematic이라 프리팹 수정 불요.
  씬에 남은 dynamic 잔재(MainScene `tracer (1)` 등 구 추적 테스트)는 별도 정리 대상.

### 넉백 — `SO/KnockbackEffectSO.cs` + MovementComponent
- 즉시 효과: HitPoint→대상 방향(근접처럼 방향이 0이면 시전자 위치로 폴백)으로
  `AddKnockback(방향, 총거리)`.
- MovementComponent에 넉백 임펄스 레이어: 지수 감쇠, 감속·이동속도 제한과 무관,
  벽(비워커블 셀)에 닿으면 소멸. 밀린 결과의 겹침은 같은 프레임 CrowdSystem이 해소.
- **함정**: `pos += v·dt`(explicit Euler)로 적분하면 60fps에서 총거리가 ~7% 과잉
  (감쇠 전 속도로 한 스텝을 다 가는 오차 — 프레임레이트 의존). 한 프레임 변위를
  지수 감쇠의 정확한 적분값 `v·(1−e^(−λdt))/λ`로 바꿔 총거리가 dt와 무관하게
  지정 거리와 일치 (실측 2m 지정 → 1.989).

### 스탯 버프 — `SO/StatModifierEffectSO.cs` + `Effects/IStatModifier.cs`
- 지속 효과: 주는 피해 배율·받는 피해 배율. 집계는 **곱**(서로 다른 출처는 함께 작용,
  자기 중첩은 stacking=Refresh가 차단) — 감속의 최솟값 집계와 다른 이유를 인터페이스에 기록.
- 적용 지점: 공격 배율은 시전 측 Power 계산(CombatComponent·ProjectileGun 발사 시점·
  BattleTower), 받는 피해 배율은 신설 **`Entity.ReceiveDamage`** — 피해의 단일 수렴점.
  DamageEffectSO·DoT가 Health.TakeDamage 대신 이걸 호출한다 (구 TakeDamage 경로도
  Default 피해 효과를 거치므로 방어 배율이 일괄 적용된다).

### 검증
- 헤드리스 12/12: 대칭 분리 정확 0.8 · 몫 50:50 · 이동 우선권 1:3 · 완전 겹침 해소 ·
  시체 제외 · 넉백 총거리(2m→1.989, 1m→0.988) · 공격 배율 · 버프 곱(1.5×2=3) ·
  방어 0.8 적용 · 만료 복원 · Movement 없는 대상 무시.
- 플레이 실측(PlayLoopTest): 겹쳐 소환한 3마리가 군중 시스템으로 분리, 동행 쌍 거리
  정확히 0.800 유지. 게임 코드 예외 0건.
- **스모크 함정 2가지**: ① 그리드 밖 좌표에 소환하면 워커빌리티 필터가 보정을 다
  버려서 "분리 안 됨"으로 오인 — 그리드 안 걷기 가능 셀에서 실측할 것.
  ② PlayLoopTest 중앙은 타워 사거리라 소환 몬스터가 몇 초 만에 사살됨 — 실측용은
  SetMaxHealth로 고체력을 줄 것.

---

## 2026-08-07 — 총·타워 발사 통합(ProjectileSystem) · 총기 상속 제거

### 상속 제거 — `Gun` 단일 클래스
구 `WeaponBase`(abstract) ← `ProjectileGun`/`RaycastGun` 3층을 `Runtime/FPS/Gun.cs` 하나로.
투사체냐 히트스캔이냐는 클래스가 아니라 **데이터(`GunData.fireMode`)**가 정한다 —
서브클래스 간 차이가 ExecuteFire 몸통뿐이었으니 "다른 행동"이 아니라 "다른 수치"였던 것.
- **프리팹 이관 요령**: 통합 클래스가 `ProjectileGun.cs`의 파일을 이름만 바꿔
  **GUID를 승계**하고(스크립트 바인딩은 이름이 아니라 GUID), RaycastGun 컴포넌트는
  Player.prefab·씬 3개의 YAML에서 GUID만 치환해 흡수했다(직렬화 필드가 전부 base 소유라
  호환). 컴포넌트 fileID가 유지되므로 WeaponManager.weapons 배열 참조도 그대로 산다.
  검증: Gun 2개(Rifle=Projectile, LaserGun=Hitscan), weapons 끊김 0, 빠진 스크립트 0.
- 구 RaycastGun을 쓰던 `Test_Gun 1.asset`은 `fireMode = Hitscan`으로 설정 (동작 보존).

### 발사 공용화 — `Runtime/Combat/ProjectileSystem.cs`
총(Gun)과 공격 타워(BattleTower)가 같은 코드로 쏜다. 히트스캔은 속도 무한의 발사일 뿐:
- `ProjectileShot`(스펙 struct) — 속도·수명·사거리·위력·대상 마스크·효과·발사자.
  위력은 발사 시점 확정(탄이 나는 동안 버프가 끝나도 유지).
- `Fire`(투사체) / `Hitscan`(즉시 판정) / `TryClosestHit`·`ApplyHit`(공용 판정·명중 처리).
- **풀은 프리팹당 전역 공유** — 총기별 전용 풀(중복 인스턴스)과 타워의 발사마다
  Instantiate/Destroy(GC)를 모두 대체. 풀 인스턴스는 DontDestroyOnLoad 루트 아래
  (씬 전환이 풀 안 오브젝트를 파괴해 죽은 참조를 남기지 않게).
  실측: 타워 ~280발 사격에 풀 인스턴스 3개.

### Bullet — 스윕 이동으로 전환 (터널링 제거)
매 프레임 "직전→다음 위치" 스피어캐스트. 트리거/충돌 이벤트 의존 제거 —
빠른 탄(속도 50 = 60fps에서 프레임당 0.83m)이 얇은 콜라이더를 건너뛰는 터널링이
원천 차단되고, 명중 처리는 히트스캔과 같은 `ApplyHit`를 탄다.
- 발사자 무시는 스윕 필터(`IsChildOf(Source.root)`)가 처리 — 타워가 발사마다 돌리던
  자기 콜라이더 IgnoreCollision 순회 삭제.
- 트리거는 스윕에서 무시(`QueryTriggerInteraction.Ignore`) — 총알(트리거)끼리
  서로 맞고 소멸하는 일이 없다.
- Invoke 예약 수명 → 타이머(풀 재사용 시 취소 관리 불요), 거리 판정 sqrMagnitude.
- **알려진 한계**: 스피어캐스트는 시작 시점에 이미 겹친 콜라이더를 못 본다 —
  총구에 밀착한 대상은 첫 프레임에 관통될 수 있다(총구 오프셋으로 실질적으론 희귀).

### 검증 (PlayLoopTest 실측)
- 히트스캔: 위력 30 지정 → 체력 정확히 −30.
- 타워: 새 경로로 자연 사격 — 소환 표적 체력이 지속 감소(수천 누적), 게임 코드 예외 0.
- 풀: 수백 발에 인스턴스 3개 유지 (재사용 확인).

### 알아둘 것
- 타워 히트스캔(레이저 타워)은 `ProjectileSystem.Hitscan` 호출만 하면 된다 —
  TowerDataSO에 fireMode를 내리는 건 다음 작업.
- 총알 프리팹의 콜라이더는 이제 판정에 쓰이지 않는다(스윕 반경 정보원으로만).
- Player.cs의 `GetCurrentBulletDamage`는 구 우회 경로 잔재 — Bullet이 위력을 직접
  들고 다니므로 이제 참조처가 없어지면 삭제 후보.
