using System;
using UnityEngine;

// 체력/사망 상태 — 순수 C# 클래스. 소유 Entity의 인스펙터에 인라인으로 직렬화된다.
// 사망 후 오브젝트 처리(비활성화/파괴)는 Entity.HandleDeath가 담당한다.
[Serializable]
public class HealthComponent
{
    [SerializeField] private float maxHealth = 100f;

    private float currentHealth;

    public bool IsDead { get; private set; }

    public event Action<float, float> OnHealthChanged;
    public event Action OnDeath;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;

    public void Initialize()
    {
        currentHealth = maxHealth;
        IsDead = false;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(float damageAmount)
    {
        if (IsDead) return;

        currentHealth = Mathf.Clamp(currentHealth - damageAmount, 0, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // 런타임 부착 엔티티(EnsurePlayerEntity 등)의 최대 체력 조정용 — 인스펙터를 못 쓰는 경우 사용
    public void SetMaxHealth(float newMaxHealth, bool refill = true)
    {
        maxHealth = Mathf.Max(1f, newMaxHealth);
        if (refill && !IsDead) currentHealth = maxHealth;
        else currentHealth = Mathf.Min(currentHealth, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void Heal(float healAmount)
    {
        if (IsDead) return;

        currentHealth = Mathf.Clamp(currentHealth + healAmount, 0, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    // 즉시 사망 — Entity.Die()가 호출 (강제 처치/치트/스크립트 연출용)
    public void Kill()
    {
        if (IsDead) return;
        currentHealth = 0;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        Die();
    }

    private void Die()
    {
        IsDead = true;
        OnDeath?.Invoke();
    }
}
