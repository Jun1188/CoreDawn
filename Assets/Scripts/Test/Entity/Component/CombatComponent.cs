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
