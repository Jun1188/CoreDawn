using UnityEngine;
using CoreDawn.Entities;

namespace CoreDawn.Combat
{
    /// <summary>회복 — 즉시 효과. 회복량 = ctx.Value (시전측 EffectEntry가 정한다).</summary>
    [CreateAssetMenu(fileName = "Effect_Heal", menuName = "Combat/Effect/Heal")]
    public class HealEffectSO : EffectSO
    {
        public override void Apply(EntityView target, in EffectContext ctx)
        {
            if (ctx.Value > 0f) target.Health.Heal(ctx.Value);
        }
    }
}
