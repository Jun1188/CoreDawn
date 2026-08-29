using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>지속 공격 배율 — affects에 적힌 효과의 Value를 곱한다(발사·공격 시점에 베이크). 심 정의의 Affects로 변환된다.</summary>
    [CreateAssetMenu(fileName = "Effect_AttackModifier", menuName = "Combat/Effect/Attack Modifier")]
    public class AttackModifierEffectSO : DurationEffectSO
    {
        [Tooltip("증폭(또는 약화)할 효과 정의들. 비우면 아무것도 건드리지 않는 버프가 된다 — 명시적으로 채울 것.")]
        public EffectSO[] affects;

        public override EffectKind Kind => EffectKind.AttackModifier;

        public bool Affects(EffectSO effect)
        {
            if (affects == null || effect == null) return false;
            foreach (var e in affects)
                if (e == effect) return true;
            return false;
        }
    }
}
