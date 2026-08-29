using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    /// <summary>즉시 피해 — Value = 피해량. 받는 배율·보호막·아군 무시는 심 Health의 인터셉터가 거른다.</summary>
    [CreateAssetMenu(fileName = "Effect_Damage", menuName = "Combat/Effect/Damage")]
    public class DamageEffectSO : EffectSO
    {
        public override EffectKind Kind => EffectKind.Damage;
    }
}
