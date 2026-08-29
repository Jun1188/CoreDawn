using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    /// <summary>지속 받는 피해 배율 — Value &gt; 1 방어 디버프, &lt; 1 방어 버프. 심 Health의 인터셉터에서 곱한다.</summary>
    [CreateAssetMenu(fileName = "Effect_IncomingDamage", menuName = "Combat/Effect/Incoming Damage")]
    public class IncomingDamageEffectSO : DurationEffectSO
    {
        public override EffectKind Kind => EffectKind.IncomingDamage;
    }
}
