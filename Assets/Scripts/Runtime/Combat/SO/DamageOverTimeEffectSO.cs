using UnityEngine;
using CoreDawn.Entities;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 지속 피해(DoT) — duration 동안 tickInterval마다 ctx.Value만큼 피해 (화상·중독 등).
    /// 틱당 피해는 시전측 EffectEntry가, 간격·지속·중첩(형태)은 이 정의가 갖는다.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect_DoT", menuName = "Combat/Effect/Damage Over Time")]
    public class DamageOverTimeEffectSO : DurationEffectSO
    {
        [Header("틱")]
        [Tooltip("틱 간격(초).")]
        public float tickInterval = 0.5f;

        public override float TickInterval => Mathf.Max(0.05f, tickInterval);

        public override void OnTick(EntityView target, in EffectContext ctx)
        {
            if (ctx.Value > 0f) target.ReceiveDamage(ctx.Value); // 방어 배율은 수렴점에서 적용
        }
    }
}
