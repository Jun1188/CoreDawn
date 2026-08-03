using System;
using UnityEngine;

// 공격 — 순수 C# 클래스. 쿨다운 관리와 데미지 전달만 담당한다.
[Serializable]
public class CombatComponent
{
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackRange = 1.5f;
    [SerializeField] private float attackCooldown = 2f;

    private float lastAttackTime = float.MinValue;

    public float AttackDamage => attackDamage;
    public float AttackRange => attackRange;
    public float AttackCooldown => attackCooldown;

    public event Action OnAttackAction;

    /// <summary>
    /// 데이터(SO)에서 전투 수치를 주입한다 — 인스펙터 값을 덮어쓴다.
    /// 타워처럼 BuildingDataSO가 사거리·연사를 정의하는 경우에 쓴다.
    /// damage는 발당 피해가 탄약에서 오는 경우 그대로 두면 되므로 선택 인자다.
    /// </summary>
    public void Configure(float range, float cooldown, float? damage = null)
    {
        attackRange = Mathf.Max(0f, range);
        attackCooldown = Mathf.Max(0.01f, cooldown);
        if (damage.HasValue) attackDamage = damage.Value;
    }

    public bool CanAttack() => Time.time >= lastAttackTime + attackCooldown;

    // 데미지를 직접 주지 않는 공격(투사체 발사 등)이 쿨다운만 소비할 때 사용.
    // 데미지는 발사체(Bullet)가 명중 시 전달한다.
    public void MarkAttackPerformed()
    {
        lastAttackTime = Time.time;
        OnAttackAction?.Invoke();
    }

    public void TryAttack(Entity target)
    {
        if (!target.IsValidTarget()) return;

        if (CanAttack())
        {
            lastAttackTime = Time.time;

            // 데미지 전달
            target.TakeDamage(attackDamage);

            // 이벤트 발생 (애니메이션, 사운드 등에서 구독)
            OnAttackAction?.Invoke();
        }
    }
}
