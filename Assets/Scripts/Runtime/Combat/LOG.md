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

---

## 2026-08-07 — 공격 정의를 EffectEntry 목록으로 완전 통일

### 원칙 (사용자 확정 — "모두 동일한 effect라는 데이터 형태의 취급")
bare 피해 필드(`GunData.damage`·`AmmoItemSO.damage`·`CombatComponent.attackDamage`)와
숨은 폴백(`DamageEffectSO.Default`)을 전부 없애고, **공격 = `EffectEntry { effect, value }` 목록**:

| 어디 | 무엇 |
|---|---|
| 클래스(EffectSO 하위) | **채널(코드)** — 무슨 일이 일어나는가 |
| 에셋 | **정체성** — 중첩 키(Refresh)·지속시간 같은 형태. 감속/가속처럼 상반 용도는 에셋을 나눠야 재적용이 서로를 덮지 않는다 |
| entry.value | **극성과 세기** — 피해량·거리(m)·배율. 해석은 효과가 한 가지 방식으로 고정 |
| 시전측 배율 | **선별적** — 무차별 곱이면 배율 1.5 타워의 감속탄이 감속 0.5→0.75로 약해지는 버그. 공격 버프는 에셋의 `affects` 목록(데이터)에 든 효과만, 포탑 damageMultiplier는 이름값대로 피해형(Damage·DoT)만 |

- `EffectContext.Power` → **Value** (의미: 그 항목의 크기).
- 효과 정의에서 크기 노브 제거: Damage/Heal(flat·powerScale 삭제 → Value 그대로),
  Knockback(distance 삭제 → Value=거리), DoT(damagePerTick 삭제 → Value=틱당 피해).
- **상반 쌍은 채널 하나 + value 극성**: `SlowEffectSO` → `MoveSpeedEffectSO`(0.5=감속,
  1.3=가속), `StatModifierEffectSO`(배율 2개짜리 — 원칙 위반) 분해·삭제 →
  `AttackModifierEffectSO` + `IncomingDamageEffectSO`. Damage↔Heal은 코드가 달라 통합 제외.
- 집계는 정의가 아니라 **활성 인스턴스의 ctx.Value**에서: 이동속도 = 최강 감속(min<1) ×
  최강 가속(max>1) — 같은 극은 안 쌓임. 피해 배율 2채널은 곱. 같은 에셋 Refresh 재적용은
  value도 갱신(집계 재계산 포함).

### 시전측 전환
- `GunData.attackEffects`(entry 목록) — 반동 연출·툴팁용 `BaseDamage`(피해 항목 합) 헬퍼.
- `AmmoItemSO.attackEffects` — 탄약이 명중 효과 전부를 정의. `TowerBehavior.TryConsumeRound`가
  피해값 대신 **효과 목록**을 반환하고, 타워는 damageMultiplier를 ValueScale로 곱한다.
  크리스탈탄에 감속을 붙이려면 탄약 목록에 {MoveSpeed계 에셋, 0.5}만 추가.
- `CombatComponent.attackEffects` — 몬스터·타워 폴백 근접. 런타임 부착 플레이어는
  `BattleManager.EnsurePlayerEntity`가 `Resources/Effect_Damage`(RecipeDatabase 패턴)로 주입.
- `TowerDataSO.attackEffects` → **auraEffects**로 개명 — 오라(감속 필드) 전용임을 명시.
  발사 타워의 명중 효과는 탄약이 정의한다.
- `ProjectileShot`: Power 제거 → Effects(entry 목록). 배율은 스칼라로 실려 가지 않는다 —
  **발사/공격 시점에 항목별로 굽는다**(`EffectController.BakeOutgoing` + 타워 `ScaleDamage`).
  탄이 날아가는 동안 버프가 끝나도 발사 때 배율이 유지되는 건 그대로.
- 공격 버프(`AttackModifierEffectSO`)는 **affects 목록**으로 대상을 선언 — "피해만 강화",
  "화상만 강화", "넉백 강화" 같은 변종이 코드 수정 없이 에셋으로 만들어진다. 그래서
  공격 배율은 스칼라 집계가 불가능해졌고 `AttackMultiplierFor(effect)` 조회로 바뀌었다.
  빈 affects = 아무것도 증폭하지 않음 (명시적 — 숨은 기본값 없음).

### 마이그레이션 (에디터 스크립트 자동 변환)
- `Assets/Resources/Effect_Damage.asset` 공용 피해 에셋 1개 — 전 시전자가 공유.
- 총 2종·탄약 5종·프리팹 9종(combat) 변환, SlowFieldTower.auraEffects = {SlowField, 0.5}.
- 임포터: json `damage` → Damage 항목 변환(수동 배선한 부가 효과는 보존). json 스키마 무변경.
- **사고·교훈**: 마이그레이션의 YAML 파싱이 경로 문제로 전부 폴백(10)됐는데, 대상 값
  대부분이 우연히 10이라 조용히 지나갈 뻔했다 — 유일하게 25였던 BattleTower.prefab으로
  발각, git 히스토리로 복원. 변환 스크립트는 "파싱 실패 = 에러"로 짜야지 폴백으로 뭉개면 안 된다.

### 검증
- 헤드리스 12/12: 피해 value·시전측 배율(30×1.5=45)·복합 목록(피해+회복)·감속 최강만·
  감속×가속(0.5×1.4=0.7)·만료 복원·같은 에셋 Refresh value 갱신(감속→가속 뒤집기)·
  공격/받는 피해 채널·DoT 4틱·넉백 value=거리·TakeDamage 호환.
- 플레이(통제 조건): 무공급 타워 프리팹이 4m 표적을 새 경로로 사살. 예외 0.
- **스모크 함정 추가**: PlayLoopTest의 씬 배치 타워 4기는 밤 웨이브에 파괴될 수 있다 —
  "타워가 안 쏜다"로 보이면 먼저 타워 생존부터 확인할 것.

---

## 2026-08-11 — 효과·총을 json(GameData)으로 — 임포터 확장

### 결정 (사용자 확정)
"json을 쓰는 이유가 임포터"이므로 **총 데이터는 json이 전부 소유**한다 (건물들과 같은 문화).
안전장치: **json에 적힌 필드만 덮는다** — 생략 필드는 에셋 값 유지. 0이 정당한 감각
튜닝 필드(반동·킥백·탄퍼짐)는 음수를 생략 신호로 쓰고, bool(isAutomatic)은 생략 판별이
불가능하므로 항상 명시한다(DTO 주석에 박음). 예외 2개: bulletPrefab·enemyLayer는
에셋/씬 참조라 json 밖 — 인스펙터 소관.

### 스키마 (Assets/Data/Import/*.json — 파일 분할 가능, GameDataCombat.json 초안 추가)
- **effects 섹션**: `kind`(→클래스 매핑: Damage/Heal/Knockback/DamageOverTime/MoveSpeed/
  AttackModifier/IncomingDamage) + 형태 필드만(duration·stacking·tickInterval·affects).
  EffectSO를 GameDataSO 상속으로 바꿔 id 부여("Effect:이름"). affects(효과→효과 참조)는
  전 파일 임포트 후 2차 해석. kind 불일치는 자동 재생성하지 않음(참조 파괴 방지 — 수동 정리).
- **guns 섹션**: GunData를 GameDataSO 상속으로("Gun:이름"), gunName 삭제 → displayName
  (HUD·WeaponManager 수정). 전투 정합 + 감각 튜닝 전부.
- **items 확장**: 탄약 `attackEffects`(정식 — 구 damage 숏컷은 attackEffects 없을 때만 변환),
  무기 `type: "Weapon"` → WeaponItemSO 생성/재생성 + `gun` 참조로 GunData 자동 배선.
- 임포트 순서: **효과 → 총 → 아이템** → 레시피 → 건물 (참조 방향 역순).

### 마이그레이션·검증
- 기존 에셋 id 부여: Effect:Damage(Resources — 런타임 부착 플레이어용 위치 유지),
  Effect:SlowField, Gun:Rifle(Test_Gun), Gun:LaserGun(Test_Gun 1). 무기 아이템은
  기존 id(Item:Test_Weapon 계열) 재사용.
- 검증: 1차 임포트 왕복 무손실(값 일치·gun 참조 유지·CrystalAmmo 배선 보존) →
  2차 재임포트 아이덤포턴스(신규 에러 0) → 플레이 장착·발사 정상(HUD displayName 표기).
- 크리스탈탄 감속 예시는 이제 json 한 줄: 탄약 항목에
  `"attackEffects": [{"effect":"Effect:Damage","value":12},{"effect":"Effect:SlowField","value":0.5}]`.

---

## 2026-08-11 — 아이템 상속 제거 — 역할은 모듈(ItemModuleSO) 조합으로

### 원칙 (사용자 확정 — "item에 상속구조가 있어선 안돼")
구 `ItemDataSO ← AmmoItemSO/WeaponItemSO` 상속을 폐기. 모든 아이템은 평평한
ItemDataSO 하나이고, 역할은 `modules` 목록의 모듈이 정의한다:

| | 구 (상속) | 신 (조합) |
|---|---|---|
| 탄약 | AmmoItemSO.attackEffects | `AmmoModuleSO` (attackEffects) |
| 무기 | WeaponItemSO.gunData | `WeaponModuleSO` (gun) |
| 판정 | `item is AmmoItemSO` | `item.GetModule<AmmoModuleSO>()` / TryGetModule |

- 상속의 문제였던 것: 역할 추가 = 서브클래스 추가, 겸직 불가(탄약이자 투척 무기 같은 것),
  타입 승격 시 같은 id 재생성(참조 복구 의존). 모듈은 셋 다 해소.
- 모듈은 아이템 에셋의 **서브에셋**(AddObjectToAsset) — 파일 하나 = 아이템 + 모듈들.
  `ItemType` enum은 분류·UI 축으로 유지하되 코드 판정은 모듈 존재로.
- 소비처 전환: TowerBehavior(TryConsumeRound)·InventoryManager(핫바 장착)·ItemTooltipUI.
- 임포터: 타입 승격 재생성 로직 삭제 → `EnsureModule<T>`가 json 필드
  (attackEffects·gun) 존재 시 모듈을 만들어 배선. json 스키마는 무변화.

### 마이그레이션·함정
- 에셋 7종(탄약 5 + 무기 2): 데이터 → 모듈 서브에셋 생성 → 본체 `m_Script`를
  ItemDataSO로 스와프(guid 보존 — 레시피·ammoFilter·인벤 참조 전부 무사).
- **함정**: 스와프+SaveAssets를 한 배치에서 여러 에셋에 돌리면 앞 에셋의 리임포트가
  뒤 에셋의 매니지드 래퍼를 죽인다("destroyed but still trying to access") —
  호출당 1개 처리 + 처리 전 `ImportAsset(ForceUpdate)`로 캐시를 새로 잡아 해결.
- **함정 2**: 클래스 스와프 직후엔 `FindAssets("t:타입")` 검색 인덱스가 낡아 있다 —
  임포터의 byId 구축이 원본을 놓쳐 같은 id의 에셋을 중복 생성했다. 스와프한 에셋들을
  `ImportAsset(ForceUpdate)`로 재인덱싱해 해결. 마이그레이션 후 첫 임포트 전에 필수.
- 검증: 7/7 모듈 값 무결(탄약 [Damage,10]·무기 gun id), 서브클래스 삭제 후 컴파일 클린,
  재임포트 아이덤포턴스(중복 재발 없음), 플레이에서 모듈 경로 장착·발사 정상.
