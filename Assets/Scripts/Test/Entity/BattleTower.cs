using UnityEngine;

// 공격 가능한 건물(타워) — BuildingEntity의 HP/사망/레지스트리 위에 자동 공격을 더한다.
// 구 canAttack 분기를 상속으로 대체: 타워는 이 클래스, 비전투 건물은 BuildingEntity 그대로.
//
// 역할 분담 (총과 같은 문법):
//   탄약   = 효과 + 탄도(탄속·중력·폭발·외형) — 소비한 AmmoModuleSO가 발사의 내용 전부
//   타워   = 각도(조준·곡사) · 배율(damageMultiplier) · 소비 시점 · 펄스 주기
//   전달   = ProjectileSystem — fireMode(Projectile/Hitscan/Aura)는 TowerDataSO(데이터)가 정한다
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
    }

    // 심에 연결된 타워의 보급 담당 (TowerDataSO 건물일 때만). 씬에 직접 놓인
    // 구 타워는 null이고, 그때는 예전처럼 탄약 없이 무한 사격한다.
    private TowerBehavior supply;
    private bool statsApplied;

    /// <summary>이 타워의 데이터 — 심 배치면 Sim.Data, 씬 배치면 인스펙터 폴백.</summary>
    private TowerDataSO Data => Sim?.Data as TowerDataSO ?? fallbackData;

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
        combat.Configure(data.range, data.fireRate > 0f ? 1f / data.fireRate : 1f);
        sensor.SetDetectionRange(data.range);
    }

    protected override void Update()
    {
        base.Update();
        if (IsDead) return;

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
        if (data.fireMode == FireMode.None) return;

        // 쿨다운이 준비됐을 때만 스캔·펄스해 OverlapSphere 낭비를 줄인다
        if (!combat.CanAttack()) return;

        // 탄약/연료가 떨어졌으면 아무것도 하지 않는다. 벨트가 채워주면 다음 프레임에 재개된다.
        if (supply != null && !supply.HasAmmo) return;

        if (data.fireMode == FireMode.Aura)
        {
            TickAura(data);
            return;
        }

        // 최소 사거리(박격포의 사각)보다 가까운 적은 조준 대상에서 빠진다
        Entity target = sensor.GetClosestTarget(combat.AttackRange, data.minRange);
        if (!target.IsValidTarget()) return;

        // 쏘기 직전에 한 발 소비 — 효과·탄도는 소비한 탄약이 정의하고, 타워는 각도·배율만.
        AmmoModuleSO round = NextRound(data);
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
    /// 이번 발사의 탄 — 보급(심)이 있으면 실제로 한 발 소비하고, 없으면(씬 직접 배치)
    /// defaultAmmo를 가정해 무한 사격한다. 없으면 null — 근접 폴백.
    /// </summary>
    private AmmoModuleSO NextRound(TowerDataSO data)
    {
        if (supply != null) return supply.TryConsumeRound(out var round) ? round : null;
        return data.defaultAmmo != null ? data.defaultAmmo.GetModule<AmmoModuleSO>() : null;
    }

    // 오라 — 쏘는 대신 쿨다운(fireRate 주기)마다 반경 전원에게 펄스한다.
    // 효과는 소비한 연료(에너지 셀 등)가 정의한다 — 연료를 바꾸면 오라가 바뀐다.
    // 범위가 비어 있으면 연료를 태우지 않는다.
    private void TickAura(TowerDataSO data)
    {
        if (ProjectileSystem.CountTargets(transform.position, combat.AttackRange, monsterMask) == 0) return;

        AmmoModuleSO round = NextRound(data);
        EffectEntry[] effects = round != null ? round.attackEffects : combat.AttackEffects; // 구 씬 오라 폴백
        if (effects == null || effects.Length == 0) return;

        effects = ProjectileSystem.ScaleDamage(effects, data.damageMultiplier);
        effects = Effects.BakeOutgoing(effects);

        ProjectileSystem.Fire(transform.position, transform.forward,
            new ProjectileShot(0f, 0f, combat.AttackRange, effects, monsterMask, this,
                               mode: FireMode.Aura));
        combat.MarkAttackPerformed();
    }

    private void FireAt(Entity target, TowerDataSO data, AmmoModuleSO round, EffectEntry[] effects)
    {
        // 조준점: 대상 콜라이더 중심 (없으면 트랜스폼 위치)
        var targetCol = target.GetComponentInChildren<Collider>();
        Vector3 aimPoint = targetCol != null ? targetCol.bounds.center : target.GetPosition();

        Vector3 muzzle = transform.position + Vector3.up * data.muzzleHeight;

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
                                      data.fireMode, round.bulletPrefab, round.pierce);

        // 전달은 총(Gun)과 같은 단일 진입점 — 방식 분기는 ProjectileSystem이 한다.
        // 총구를 살짝 앞으로: 자기 명중은 필터가 걸러주지만 탄이 타워 모델 안에서 태어나지 않게.
        ProjectileSystem.Fire(muzzle + dir * 0.6f, dir, shot);
    }
}
