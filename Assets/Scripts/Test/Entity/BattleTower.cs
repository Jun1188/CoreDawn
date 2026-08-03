using UnityEngine;

// 공격 가능한 건물(타워) — BuildingEntity의 HP/사망/레지스트리 위에 자동 공격을 더한다.
// 구 canAttack 분기를 상속으로 대체: 타워는 이 클래스, 비전투 건물은 BuildingEntity 그대로.
//
// 공격 방식: 전용 sensor로 사거리 내 몬스터를 감지하면 Bullet을 발사한다.
// 데미지는 플레이어 총과 동일하게 Bullet이 명중 시 전달(Bullet.TryApplyDamage)하며,
// 여기서는 쿨다운 소비(MarkAttackPerformed)만 한다.
// bulletPrefab을 비워두면 구 방식(즉시 데미지 TryAttack)으로 폴백한다.
public class BattleTower : BuildingEntity
{
    [Header("Tower Combat")]
    [SerializeField] private CombatComponent combat = new CombatComponent();
    [SerializeField] private SensorComponent sensor = new SensorComponent();

    [Header("Bullet")]
    [Tooltip("발사할 총알 프리팹(Bullet 컴포넌트 필요). 비우면 즉시 데미지 방식으로 폴백.")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float bulletSpeed = 25f;
    [SerializeField] private float bulletLifetime = 3f;
    [Tooltip("총구 위치 오프셋(로컬 기준 높이). 타워 콜라이더 밖에서 발사되도록 조정.")]
    [SerializeField] private float muzzleHeight = 1.2f;

    private int monsterMask;

    public override CombatComponent Combat => combat;
    public override SensorComponent Sensor => sensor;

    protected override void Awake()
    {
        base.Awake();
        sensor.Initialize(this);
        monsterMask = LayerMask.GetMask("Monster");
    }

    // 심에 연결된 타워의 보급 담당 (TowerDataSO 건물일 때만). 씬에 직접 놓인
    // 구 타워는 null이고, 그때는 예전처럼 탄약 없이 무한 사격한다.
    private TowerBehavior supply;
    private bool statsApplied;

    /// <summary>
    /// 배치된 뒤 한 번, SO의 사거리·연사를 전투 컴포넌트에 주입한다.
    /// Sim은 PlacementBridge가 배치 시점에 꽂아주므로 Awake에서는 아직 없다.
    /// </summary>
    private void ApplyDataStats()
    {
        if (statsApplied || Sim?.Data is not TowerDataSO data) return;
        statsApplied = true;

        supply = Sim.Behavior as TowerBehavior;
        combat.Configure(data.range, data.fireRate > 0f ? 1f / data.fireRate : 1f);
        sensor.SetDetectionRange(data.range);
    }

    protected override void Update()
    {
        base.Update();
        if (IsDead) return;

        ApplyDataStats();

        // 공격하지 않는 건물(인펜스·감속 필드)은 여기서 끝
        if (Sim?.Data is TowerDataSO td && td.IsPassive) return;

        // 쿨다운이 준비됐을 때만 스캔해 OverlapSphere 낭비를 줄인다
        if (!combat.CanAttack()) return;

        // 탄약이 떨어졌으면 사격하지 않는다. 벨트가 채워주면 다음 프레임에 재개된다.
        if (supply != null && !supply.HasAmmo) return;

        Entity target = sensor.GetClosestTarget(combat.AttackRange);
        if (!target.IsValidTarget()) return;

        // 쏘기 직전에 한 발 소비 — 피해량은 장전된 탄약이 정한다
        float damage = combat.AttackDamage;
        if (supply != null && !supply.TryConsumeRound(out damage)) return;

        if (bulletPrefab != null)
        {
            FireBullet(target, damage);
            combat.MarkAttackPerformed(); // 데미지는 총알이 전달, 여긴 쿨다운만 소비
        }
        else
        {
            combat.TryAttack(target); // 폴백: 즉시 데미지 (구 TestTrace 씬 호환)
        }
    }

    private void FireBullet(Entity target, float damage)
    {
        // 조준점: 대상 콜라이더 중심 (없으면 트랜스폼 위치)
        var targetCol = target.GetComponentInChildren<Collider>();
        Vector3 aimPoint = targetCol != null ? targetCol.bounds.center : target.GetPosition();

        Vector3 muzzle = transform.position + Vector3.up * muzzleHeight;
        Vector3 dir = aimPoint - muzzle;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        var go = Instantiate(bulletPrefab, muzzle + dir * 0.6f, Quaternion.LookRotation(dir));

        // 자기 자신 콜라이더와의 즉시 충돌 방지
        var bulletCol = go.GetComponentInChildren<Collider>();
        if (bulletCol != null)
        {
            foreach (var ownCol in GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(bulletCol, ownCol);
        }

        var bullet = go.GetComponent<Bullet>();
        if (bullet != null)
            bullet.Setup(bulletSpeed, bulletLifetime, damage, monsterMask);
        else
            Debug.LogWarning($"[BattleTower] bulletPrefab에 Bullet 컴포넌트가 없습니다: {bulletPrefab.name}", this);
    }
}
