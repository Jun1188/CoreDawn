using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    /// <summary>지속 피해 — Value = 틱당 피해. 받는 배율은 틱마다 심 Health 안에서 곱한다.</summary>
    [CreateAssetMenu(fileName = "Effect_DoT", menuName = "Combat/Effect/Damage Over Time")]
    public class DamageOverTimeEffectSO : DurationEffectSO
    {
        [Header("틱")]
        [Tooltip("틱 간격(초).")]
        public float tickInterval = 0.5f;

        public override EffectKind Kind => EffectKind.DamageOverTime;

        public override float TickInterval => Mathf.Max(0.05f, tickInterval);
    }
}
