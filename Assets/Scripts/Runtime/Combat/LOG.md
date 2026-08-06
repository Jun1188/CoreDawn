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
