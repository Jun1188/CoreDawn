using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    public enum KnockbackMode
    {
        /// <summary>공격이 날아온 방향으로 민다 (총알·근접).</summary>
        Directional,
        /// <summary>명중점에서 바깥으로 민다 (폭발·오라).</summary>
        Radial,
    }

    /// <summary>즉시 밀기 — Value = 거리(m). 방향 규칙과 실제 밀기는 심(Effects → MovementModule.AddKnockback)이 한다.</summary>
    [CreateAssetMenu(fileName = "Effect_Knockback", menuName = "Combat/Effect/Knockback")]
    public class KnockbackEffectSO : EffectSO
    {
        [Tooltip("어느 쪽으로 미는가. Directional = 공격이 날아온 방향(총알·근접), " +
                 "Radial = 명중점에서 바깥(폭발·오라).")]
        public KnockbackMode mode = KnockbackMode.Directional;

        public override EffectKind Kind => EffectKind.Knockback;

        internal override EffectSpec BuildSpec()
            => new EffectSpec(SpecId, Kind, radialKnockback: mode == KnockbackMode.Radial);
    }
}
