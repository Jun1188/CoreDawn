using UnityEngine;

/// <summary>회복 — 즉시 효과. 회복량 = flat + ctx.Power × powerScale.</summary>
[CreateAssetMenu(fileName = "Effect_Heal", menuName = "Combat/Effect/Heal")]
public class HealEffectSO : EffectSO
{
    [Tooltip("고정 회복량.")]
    public float flat = 10f;

    [Tooltip("시전측 기본 수치(Power)에 곱할 배율. 0 = 고정량만.")]
    public float powerScale = 0f;

    public override void Apply(Entity target, in EffectContext ctx)
    {
        float amount = flat + ctx.Power * powerScale;
        if (amount > 0f) target.Health.Heal(amount);
    }
}
