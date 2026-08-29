using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>즉시 회복 — Value = 회복량.</summary>
    [CreateAssetMenu(fileName = "Effect_Heal", menuName = "Combat/Effect/Heal")]
    public class HealEffectSO : EffectSO
    {
        public override EffectKind Kind => EffectKind.Heal;
    }
}
