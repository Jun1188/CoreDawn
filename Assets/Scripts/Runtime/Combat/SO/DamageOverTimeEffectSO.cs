using UnityEngine;

/// <summary>
/// 지속 피해(DoT) — duration 동안 tickInterval마다 피해를 준다 (화상·중독 등).
/// 틱당 피해 = damagePerTick + ctx.Power × powerScale.
/// </summary>
[CreateAssetMenu(fileName = "Effect_DoT", menuName = "Combat/Effect/Damage Over Time")]
public class DamageOverTimeEffectSO : DurationEffectSO
{
    [Header("틱 피해")]
    [Tooltip("틱 간격(초).")]
    public float tickInterval = 0.5f;

    [Tooltip("틱당 고정 피해.")]
    public float damagePerTick = 2f;

    [Tooltip("틱당 피해에 더할 시전측 수치(Power) 배율. 0 = 고정 피해만.")]
    public float powerScale = 0f;

    public override float TickInterval => Mathf.Max(0.05f, tickInterval);

    public override void OnTick(Entity target, in EffectContext ctx)
    {
        float amount = damagePerTick + ctx.Power * powerScale;
        if (amount > 0f) target.Health.TakeDamage(amount);
    }
}
