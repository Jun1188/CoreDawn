using System.Collections.Generic;
using UnityEngine;

// 플레이어 엔티티 — 조작/건설(PlayerController, PlacementSystem 등)은 기존 시스템이
// 담당하고, 여기서는 엔티티 측면(HP, 몬스터 감지 콜백)만 다룬다.
//
// 길찾기 최적화의 핵심: 몬스터 N마리가 각자 플레이어를 스캔하는 대신,
// 플레이어 하나가 센서 범위의 몬스터를 찾아 OnDetectedByPlayer / OnLostByPlayer로
// 알려준다. 감지된 몬스터만 런타임 A*를 쓰고 나머지는 플로우필드를 탄다.
public class Player : Entity
{
    [SerializeField] private SensorComponent sensor = new SensorComponent();

    [Tooltip("근접 자동 반격 — 사거리 안의 몬스터를 쿨다운마다 공격한다. 원거리는 총기(Bullet 피격)가 담당.")]
    [SerializeField] private CombatComponent combat = new CombatComponent();

    public override SensorComponent Sensor => sensor;
    public override CombatComponent Combat => combat;

    private readonly List<Entity> scanBuffer = new List<Entity>();
    private readonly HashSet<Monster> detected = new HashSet<Monster>();
    private readonly List<Monster> removeBuffer = new List<Monster>();

    protected override void Awake()
    {
        base.Awake();
        sensor.Initialize(this);
        combat.Initialize(this);
    }

    protected override void Update()
    {
        base.Update();
        if (IsDead) return;
        ScanForMonsters();
        MeleeAttack();
    }

    // 사거리 안까지 접근한 몬스터에 대한 근접 자동 반격
    private void MeleeAttack()
    {
        if (!combat.CanAttack()) return;
        Entity target = sensor.GetClosestTarget(combat.AttackRange);
        if (target.IsValidTarget())
        {
            combat.TryAttack(target);
        }
    }

    // ── 총기 시스템 통합 (get 전용 조회) ──
    // 현재 장착 무기의 GunData.damage를 읽는다. Monster의 Bullet 피격 처리가 사용.
    private static WeaponManager cachedWeaponManager;

    public static float GetCurrentBulletDamage()
    {
        if (cachedWeaponManager == null)
            cachedWeaponManager = FindFirstObjectByType<WeaponManager>();

        var weapon = cachedWeaponManager != null ? cachedWeaponManager.CurrentWeapon : null;
        if (weapon != null && weapon.gunData != null) return weapon.gunData.damage;
        return 20f; // 무기 시스템이 없는 테스트 씬용 기본값
    }

    private void ScanForMonsters()
    {
        if (!sensor.TryScan(scanBuffer)) return; // scanInterval 주기로만 실제 스캔

        // 새로 들어온 몬스터 → 감지 콜백
        foreach (var entity in scanBuffer)
        {
            if (entity is Monster monster && detected.Add(monster))
            {
                monster.OnDetectedByPlayer(this);
            }
        }

        // 범위를 벗어났거나 죽은 몬스터 → 해제 콜백
        removeBuffer.Clear();
        foreach (var monster in detected)
        {
            if (monster == null || monster.IsDead || !scanBuffer.Contains(monster))
            {
                removeBuffer.Add(monster);
            }
        }
        foreach (var monster in removeBuffer)
        {
            detected.Remove(monster);
            if (monster != null) monster.OnLostByPlayer();
        }
    }

    private void OnDisable()
    {
        // 플레이어가 비활성화되면(사망 등) 추적 중이던 몬스터를 모두 해제
        foreach (var monster in detected)
        {
            if (monster != null) monster.OnLostByPlayer();
        }
        detected.Clear();
    }
}
