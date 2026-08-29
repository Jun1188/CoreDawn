using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    /// <summary>지속 이동 속도 배율 — Value &lt; 1 감속, &gt; 1 가속. 심 Movement가 Effects에서 읽는다.</summary>
    [CreateAssetMenu(fileName = "Effect_MoveSpeed", menuName = "Combat/Effect/Move Speed")]
    public class MoveSpeedEffectSO : DurationEffectSO
    {
        public override EffectKind Kind => EffectKind.MoveSpeed;
    }
}
