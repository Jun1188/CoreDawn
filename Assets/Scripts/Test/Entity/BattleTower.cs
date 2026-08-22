using UnityEngine;

// 공격 가능한 건물(타워) — BuildingEntity의 HP/사망/레지스트리 위에 자동 공격을 더한다.
// 구 canAttack 분기를 상속으로 대체: 타워는 이 클래스, 비전투 건물은 BuildingEntity 그대로.
//
// 역할 분담 (총과 같은 문법):
//   탄약   = 효과 + 탄도(탄속·중력·폭발·외형) — 소비한 AmmoModuleSO가 발사의 내용 전부
//   타워   = 각도(조준·곡사) · 배율(damageMultiplier) · 소비 시점 · 펄스 주기
//   전달   = ProjectileSystem — fireMode(Projectile/Hitscan/Aura)는 TowerDataSO(데이터)가 정한다
//   연출   = TowerVisualController — 포탑 회전·반동·사운드. 판정은 모른다.
public class BattleTower : BuildingEntity
{
    [Header("Tower Combat")]
    [SerializeField] private CombatComponent combat = new CombatComponent();
    [SerializeField] private SensorComponent sensor = new SensorComponent();

    [Tooltip("심 없이 씬에 직접 놓인 타워용 데이터 폴백 — 심 배치 타워는 Sim.Data가 우선한다.")]
    [SerializeField] private TowerDataSO fallbackData;

    private int monsterMask;

    public override CombatComponent Combat => combat;
    public override SensorComponent Sensor => sensor;

    protected override void Awake()
    {
        base.Awake();
        sensor.Initialize(this);
        combat.Initialize(this);
        monsterMask = LayerMask.GetMask("Monster");
        visual = GetComponent<TowerVisualController>();
    }

    // 심에 연결된 타워의 보급 담당 (TowerDataSO 건물일 때만). 씬에 직접 놓인
    // 구 타워는 null이고, 그때는 예전처럼 탄약 없이 무한 사격한다.
    private TowerBehavior supply;
    private bool statsApplied;

    // ── 연출·조준 ──────────────────────────────────────────────
    private TowerVisualController visual;

    /// <summary>등장 연출 길이 — 이 동안은 조준도 사격도 하지 않는다.</summary>
    private const float DeployDuration = 0.45f;
    private float deployUntil;

    /// <summary>
    /// 이미 잡은 목표를 놓는 사거리에 주는 여유(m) — 잡을 때보다 이만큼 더 버틴다.
    /// 몬스터 콜라이더 반지름과 같은 값이다: 경계가 흐릿한 이유가 바로 그 반지름이기 때문이다.
    /// 사거리 끝에서 목표가 붙었다 떨어졌다 하며 포탑이 떠는 것을 막는다.
    /// </summary>
    private const float TargetKeepMargin = 0.5f;

    private TowerState state = (TowerState)(-1);
    /// <summary>지금 무엇을 하고 있는가 — 연출과 로직이 공유하는 단일 진실.</summary>
    public TowerState State => state;

    // 조준 미리보기용 탄 — 곡사 여부(중력)만 알면 되므로 실제 소비 없이 defaultAmmo를 참고한다.
    private AmmoModuleSO previewRound;

    // 목표 캐시 — 매 프레임 OverlapSphere를 돌리지 않기 위한 것.
    // 붙잡은 목표는 죽거나 사거리를 벗어날 때까지 유지한다: 느린 포탑이 매 프레임
    // 더 가까운 적으로 갈아타면 영원히 조준을 못 끝낸다.
    private Entity target;
    private Collider targetCollider;
    private float nextScanTime;

    /// <summary>이 타워의 데이터 — 심 배치면 Sim.Data, 씬 배치면 인스펙터 폴백.
    /// BuildingEntity.Data(BuildingDataSO)를 타워 전용 타입으로 좁혀 가린다(의도된 가림).</summary>
    private new TowerDataSO Data => Sim?.Data as TowerDataSO ?? fallbackData;

    // 데이터의 minRange(타일)를 월드 미터로 환산해 둔 값 — 거리 비교가 전부 미터라서.
    private float minRangeWorld;

    /// <summary>
    /// 타일 → 미터 환산 계수. 데이터의 사거리는 그리드 칸 수(타일) 단위인데 물리 쿼리는
    /// 미터로 돈다 — 칸 크기를 곱하지 않으면 칸이 1m가 아닌 맵에서 사거리가 통째로 어긋난다.
    /// 칸 크기의 소유자는 배치 시스템(맵에서 주입받는다). 없는 씬(테스트)은 1칸 = 1m.
    /// </summary>
    private static float TileSize()
    {
        var placement = FindFirstObjectByType<PlacementSystem>();
        return placement != null ? placement.CellSize : 1f;
    }

    /// <summary>
    /// 배치된 뒤 한 번, SO의 사거리·연사를 전투 컴포넌트에 주입한다.
    /// Sim은 PlacementBridge가 배치 시점에 꽂아주므로 Awake에서는 아직 없다.
    /// </summary>
    private void ApplyDataStats()
    {
        var data = Data;
        if (statsApplied || data == null) return;
        statsApplied = true;

        supply = Sim?.Behavior as TowerBehavior;
        float tile = TileSize();
        combat.Configure(data.range * tile, data.fireRate > 0f ? 1f / data.fireRate : 1f);
        sensor.SetDetectionRange(data.range * tile);
        minRangeWorld = data.minRange * tile;

        previewRound = data.defaultAmmo != null ? data.defaultAmmo.GetModule<AmmoModuleSO>() : null;

        deployUntil = Time.time + DeployDuration;
        if (visual != null) visual.PlayDeploy();
    }

    private void SetState(TowerState next)
    {
        if (state == next) return;
        state = next;
        if (visual != null) visual.OnStateChanged(next);
    }

    protected override void Update()
    {
        base.Update();
        if (IsDead) { SetState(TowerState.Destroyed); return; }

        ApplyDataStats();

        var data = Data;
        if (data == null)
        {
            // 데이터가 아예 없는 구 씬 타워 — 근접 즉시 공격 폴백 (combat이 효과 정의)
            if (!combat.CanAttack()) return;
            Entity t = sensor.GetClosestTarget(combat.AttackRange);
            if (t.IsValidTarget()) combat.TryAttack(t);
            return;
        }

        // 비전투 구조물(인펜스) — 몸으로 막을 뿐 아무것도 전달하지 않는다
        if (data.fireMode == FireMode.None) { SetState(TowerState.Inert); return; }

        // 등장 연출 중에는 아무것도 하지 않는다 — 땅에서 솟는 중에 쏘면 우스꽝스럽다
        if (Time.time < deployUntil) { SetState(TowerState.Deploying); return; }

        // 보급 상태를 목표보다 먼저 본다. 벨트가 끊겼다는 사실이 목표 유무보다 급한 정보다 —
        // 플레이어는 이 연출/소리를 보고 공장 배관을 고치러 간다.
        //
        // 여기서 AimIdle을 부르지 않는 것은 의도다: 탄이 없는 포탑은 훑을 이유가 없고,
        // 포신이 처진 채(Tower_Starved) 멈춰 있어야 "죽어 있다"가 한눈에 보인다.
        // 다만 그 대가로 yaw와 idlePhase가 그 자리에 얼어붙으므로, HasAmmo가 빠르게
        // 깜빡이면 스캔이 끊겼다 이어졌다 하며 떨린다. 지금은 탄 소비가 발사 시에만
        // 일어나(TowerBehavior.TryConsumeRound) 깜빡일 수 없다 — 그 전제가 깨지면
        // 여기에 짧은 유예를 두어야 한다.
        if (supply != null && !supply.HasAmmo)
        {
            target = null;
            targetCollider = null;
            SetState(TowerState.Starved);
            return;
        }

        if (data.fireMode == FireMode.Aura)
        {
            TickAura(data);
            return;
        }

        // ── 포탑 계열 (Projectile / Hitscan) ───────────────────

        AcquireTarget(data);

        // 조준 원점은 포탑 회전과 무관하게 고정한다 — 총구를 기준으로 각을 풀면
        // 조준이 총구를 옮기고 총구가 다시 조준을 바꾸는 되먹임이 생긴다.
        // 실제 발사각은 FireAt에서 진짜 총구 기준으로 다시 계산한다.
        Vector3 aimOrigin = transform.position + Vector3.up * data.muzzleHeight;

        if (!target.IsValidTarget())
        {
            if (visual != null) visual.AimIdle(data.turnSpeed);
            SetState(TowerState.Idle);
            return;
        }

        Vector3 aimPoint = AimPointOf(target);
        bool aligned = true;
        if (visual != null)
        {
            Vector3 aimDir = AimDirection(aimOrigin, aimPoint, data);
            aligned = visual.AimTowards(aimDir, data.turnSpeed, data.aimTolerance);
        }

        SetState(aligned ? TowerState.Firing : TowerState.Aiming);

        // 조준이 끝나기 전에는 여기서 멈춘다. 탄 소비(NextRound)보다 반드시 앞이어야 한다 —
        // 뒤에 두면 포탑이 도는 동안 매 프레임 한 발씩 사라진다.
        if (!aligned) return;
        if (!combat.CanAttack()) return;

        // 쏘기 직전에 한 발 소비 — 효과·탄도는 소비한 탄약이 정의하고, 타워는 각도·배율만.
        if (!NextRound(data, out AmmoModuleSO round))
        {
            SetState(TowerState.Starved);
            return;
        }

        if (round == null || (data.fireMode == FireMode.Projectile && round.bulletPrefab == null))
        {
            combat.TryAttack(target); // 폴백: 즉시 적용 (탄 정의 없음 — 효과는 combat이 정의)
            return;
        }

        var effects = ProjectileSystem.ScaleDamage(round.attackEffects, data.damageMultiplier);
        effects = Effects.BakeOutgoing(effects); // 타워도 버프 대상 (아직 거는 곳은 없지만 규칙 통일)

        FireAt(target, data, round, effects);
        combat.MarkAttackPerformed(); // 효과는 전달 계층이 적용, 여긴 쿨다운만 소비
    }

    /// <summary>
    /// 목표 확보 — 붙잡은 목표가 아직 유효하면 물리 질의 없이 그대로 쓴다.
    /// 재탐색은 센서의 스캔 주기로 제한한다: <see cref="SensorComponent.GetClosestTarget"/>은
    /// 호출할 때마다 즉시 OverlapSphere를 돌리므로 매 프레임 부르면 안 된다.
    /// </summary>
    private void AcquireTarget(TowerDataSO data)
    {
        if (target.IsValidTarget())
        {
            // 놓는 사거리를 잡는 사거리보다 조금 넓게 둔다(히스테리시스).
            // 경계에 딱 걸친 몬스터는 걸음마다 안팎을 오가는데, 두 기준이 같으면 그때마다
            // 목표를 놓쳤다 다시 잡으며 포탑이 대기 스캔과 조준 사이를 홱홱 오간다.
            // 여유를 준 만큼은 "쫓아가다 놓치기 직전" 구간이 되어 조준이 이어진다.
            float d = Vector3.Distance(transform.position, target.GetPosition());
            if (d <= combat.AttackRange + TargetKeepMargin && d >= minRangeWorld) return; // 유지
        }

        target = null;
        targetCollider = null;

        if (Time.time < nextScanTime) return;
        nextScanTime = Time.time + Mathf.Max(0.05f, sensor.ScanInterval);

        target = sensor.GetClosestTarget(combat.AttackRange, minRangeWorld);
        // 콜라이더는 목표를 잡을 때 한 번만 찾는다 — 조준은 매 프레임이라 여기서 캐시해야 한다
        targetCollider = target != null ? target.GetComponentInChildren<Collider>() : null;
    }

    /// <summary>조준점 — 대상 콜라이더 중심(없으면 트랜스폼 위치).</summary>
    private Vector3 AimPointOf(Entity t)
        => targetCollider != null ? targetCollider.bounds.center : t.GetPosition();

    /// <summary>
    /// 포탑이 향해야 할 방향. 중력탄을 쏘는 발사기는 목표가 아니라 <b>발사각</b>을 봐야 한다 —
    /// 박격포가 표적을 똑바로 겨누면 포탄은 발밑에 떨어진다.
    /// 탄종은 발사 순간에야 확정되므로 여기서는 defaultAmmo로 곡사 여부만 미리 본다.
    /// </summary>
    private Vector3 AimDirection(Vector3 origin, Vector3 aimPoint, TowerDataSO data)
    {
        if (data.fireMode == FireMode.Projectile && previewRound != null && previewRound.gravity > 0f)
            return ProjectileSystem.BallisticAim(origin, aimPoint, previewRound.speed,
                                                 previewRound.gravity, data.preferHighArc);

        Vector3 dir = aimPoint - origin;
        return dir.sqrMagnitude < 0.0001f ? transform.forward : dir.normalized;
    }

    /// <summary>
    /// 이번 발사의 탄을 확보한다. 보급(심)이 있으면 탄약량 확인과 실제 한 발 소비가
    /// 모두 성공해야 true다. 없으면(씬 직접 배치) defaultAmmo를 가정해 무한 사격한다.
    /// 씬 직접 배치 타워의 round가 null인 경우만 기존 근접 폴백을 허용한다.
    /// </summary>
    private bool NextRound(TowerDataSO data, out AmmoModuleSO round)
    {
        if (supply != null)
        {
            round = null;
            return supply.HasAmmo && supply.TryConsumeRound(out round);
        }

        round = data.defaultAmmo != null ? data.defaultAmmo.GetModule<AmmoModuleSO>() : null;
        return true;
    }

    // 오라 — 쏘는 대신 쿨다운(fireRate 주기)마다 반경 전원에게 펄스한다.
    // 효과는 소비한 연료(에너지 셀 등)가 정의한다 — 연료를 바꾸면 오라가 바뀐다.
    // 범위가 비어 있으면 연료를 태우지 않는다.
    private void TickAura(TowerDataSO data)
    {
        // 쿨다운 중에는 굳이 반경을 세지 않는다 — 펄스형이라 조준할 것도 없다
        if (!combat.CanAttack()) { SetState(TowerState.Idle); return; }

        if (ProjectileSystem.CountTargets(transform.position, combat.AttackRange, monsterMask) == 0)
        {
            SetState(TowerState.Idle);
            return;
        }

        if (!NextRound(data, out AmmoModuleSO round))
        {
            SetState(TowerState.Starved);
            return;
        }

        EffectEntry[] effects = round != null ? round.attackEffects : combat.AttackEffects; // 구 씬 오라 폴백
        if (effects == null || effects.Length == 0) { SetState(TowerState.Idle); return; }

        effects = ProjectileSystem.ScaleDamage(effects, data.damageMultiplier);
        effects = Effects.BakeOutgoing(effects);

        ProjectileSystem.Fire(transform.position, transform.forward,
            new ProjectileShot(0f, 0f, combat.AttackRange, effects, monsterMask, this,
                               mode: FireMode.Aura));
        combat.MarkAttackPerformed();

        SetState(TowerState.Firing);
        if (visual != null) visual.OnShotFired();
    }

    /// <summary>탄이 타워 모델 안에서 태어나지 않게 밀어내는 거리 — 진짜 총구가 있으면 필요 없다.</summary>
    private const float MuzzlePushout = 0.6f;

    private void FireAt(Entity target, TowerDataSO data, AmmoModuleSO round, EffectEntry[] effects)
    {
        Vector3 aimPoint = AimPointOf(target);

        // 총구 — 리그에 진짜 총구가 있으면 포신 끝에서, 없으면 예전처럼 높이로 대신한다.
        // 다총신 타워는 여기서 배럴이 한 칸 넘어간다.
        Transform muzzleTf = null;
        bool hasMuzzle = visual != null && visual.TryTakeMuzzle(out muzzleTf);
        Vector3 muzzle = hasMuzzle ? muzzleTf.position
                                   : transform.position + Vector3.up * data.muzzleHeight;

        // 발사기의 일은 각도다 — 중력탄(유탄)은 탄도해로 조준각을 풀고, 직선탄은 그냥 겨눈다
        Vector3 dir;
        if (data.fireMode == FireMode.Projectile && round.gravity > 0f)
        {
            dir = ProjectileSystem.BallisticAim(muzzle, aimPoint, round.speed, round.gravity, data.preferHighArc);
        }
        else
        {
            dir = aimPoint - muzzle;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
        }

        var shot = new ProjectileShot(round.speed, round.lifetime, combat.AttackRange + 2f,
                                      effects, monsterMask, this, round.gravity, round.explosionRadius,
                                      data.fireMode, round.bulletPrefab, round.pierce, null,
                                      round.hitEffectPrefab);

        // 진짜 총구에서 쏠 때는 밀어내지 않는다 — 이미 포신 끝이라 한 번 더 밀면 탄이 허공에서 태어난다.
        // 자기 명중은 Bullet이 발사자 루트를 걸러내므로 어느 쪽이든 안전하다.
        Vector3 origin = hasMuzzle ? muzzle : muzzle + dir * MuzzlePushout;

        // 총구 화염 — 같은 탄이면 총과 타워가 같은 연출을 쓴다 (탄약이 연출의 주인)
        ProjectileSystem.PlayEffect(round.muzzleFlashPrefab, origin, Quaternion.LookRotation(dir));

        // 전달은 총(Gun)과 같은 단일 진입점 — 방식 분기는 ProjectileSystem이 한다.
        ProjectileSystem.Fire(origin, dir, shot);

        if (visual != null) visual.OnShotFired();
    }
}
