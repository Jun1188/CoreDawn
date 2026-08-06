using UnityEngine;

/// <summary>
/// 피해 — 즉시 효과. 피해량 = flat + ctx.Power × powerScale.
/// 효과를 하나도 지정하지 않은 공격은 <see cref="Default"/>(Power 그대로)로 처리되므로,
/// 기존 프리팹·데이터가 그대로 동작한다. 저항·크리티컬 같은 규칙이 생기면 여기 한 곳에 얹는다.
/// </summary>
[CreateAssetMenu(fileName = "Effect_Damage", menuName = "Combat/Effect/Damage")]
public class DamageEffectSO : EffectSO
{
    [Tooltip("고정 피해 (Power와 무관하게 가산).")]
    public float flat = 0f;

    [Tooltip("시전측 기본 수치(Power)에 곱할 배율.")]
    public float powerScale = 1f;

    public override void Apply(Entity target, in EffectContext ctx)
    {
        float amount = flat + ctx.Power * powerScale;
        if (amount > 0f) target.Health.TakeDamage(amount);
    }

    // 효과 미지정 공격의 폴백 — Power를 그대로 피해로 넣는 공유 런타임 인스턴스
    private static DamageEffectSO fallback;
    public static DamageEffectSO Default
    {
        get
        {
            if (fallback == null)
            {
                fallback = CreateInstance<DamageEffectSO>();
                fallback.name = "Damage (Default)";
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }
    }
}
