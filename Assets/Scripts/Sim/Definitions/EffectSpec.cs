using System;
using System.Collections.Generic;

namespace CoreDawn.Sim
{
    /// <summary>효과의 종류 — 심이 아는 전부. 새 종류는 여기와 <see cref="EffectsModule"/>의 Apply/Tick/Recompute 분기에 함께 추가한다.</summary>
    public enum EffectKind
    {
        /// <summary>즉시 피해 (Value = 피해량).</summary>
        Damage,
        /// <summary>즉시 회복 (Value = 회복량).</summary>
        Heal,
        /// <summary>즉시 밀기 (Value = 거리 m).</summary>
        Knockback,
        /// <summary>지속 피해 (Value = 틱당 피해).</summary>
        DamageOverTime,
        /// <summary>지속 이동 속도 배율 (&lt;1 감속, &gt;1 가속).</summary>
        MoveSpeed,
        /// <summary>지속 공격 배율 — <see cref="EffectSpec.Affects"/>에 적힌 효과의 Value를 곱한다.</summary>
        AttackModifier,
        /// <summary>지속 받는 피해 배율.</summary>
        IncomingDamage,
    }

    /// <summary>
    /// 효과 정의 — 심이 읽는 순수 데이터. 에셋(EffectSO)에서 한 번 변환되고 같은 에셋은 같은 인스턴스다:
    /// "같은 효과인가"(재적용 갱신·AttackModifier의 대상)를 참조 동일성으로 판정한다. 심은 에셋을 모른다.
    /// 크기는 여기 없다 — <see cref="Effect"/>가 정의와 크기를 묶는다.
    /// </summary>
    public sealed class EffectSpec
    {
        public readonly string Id;
        public readonly EffectKind Kind;

        /// <summary>지속 시간(초). 0 이하 = 영구(대상이 죽을 때까지). 즉시 효과는 무시한다.</summary>
        public readonly float Duration;

        /// <summary>true = 별개 인스턴스로 중첩, false = 재적용 시 남은 시간과 크기를 갱신(Refresh).</summary>
        public readonly bool Stacks;

        /// <summary>틱 간격(초). 0 이하 = 틱 없음(시작/종료만 있는 상태 효과).</summary>
        public readonly float TickInterval;

        /// <summary>넉백 방향 — true면 명중점에서 바깥(폭발·오라), false면 공격이 날아온 방향(총알·근접).</summary>
        public readonly bool RadialKnockback;

        EffectSpec[] affects = Array.Empty<EffectSpec>();

        public EffectSpec(string id, EffectKind kind, float duration = 0f, bool stacks = false,
                          float tickInterval = 0f, bool radialKnockback = false)
        {
            Id = string.IsNullOrEmpty(id) ? kind.ToString() : id;
            Kind = kind;
            Duration = duration;
            Stacks = stacks;
            TickInterval = tickInterval;
            RadialKnockback = radialKnockback;
        }

        public bool IsInstant => Kind == EffectKind.Damage || Kind == EffectKind.Heal || Kind == EffectKind.Knockback;

        /// <summary>남은 시간의 시작값. 영구 효과는 무한대 — 큰 수로 흉내 내면 저장·표시에 새어 나온다.</summary>
        public float Lifetime => Duration > 0f ? Duration : float.PositiveInfinity;

        /// <summary>AttackModifier가 증폭(또는 약화)하는 효과 정의들. 비어 있으면 아무것도 건드리지 않는 버프.</summary>
        public IReadOnlyList<EffectSpec> Affects => affects;

        /// <summary>
        /// AttackModifier 전용. 생성 뒤에 따로 채우는 이유: 버프가 버프를 가리킬 수 있어(순환) 변환기가
        /// 정의를 먼저 등록하고 나중에 잇는다.
        /// </summary>
        public void SetAffects(EffectSpec[] targets) => affects = targets ?? Array.Empty<EffectSpec>();

        public bool DoesAffect(EffectSpec other)
        {
            if (other == null) return false;
            for (int i = 0; i < affects.Length; i++)
                if (ReferenceEquals(affects[i], other)) return true;
            return false;
        }

        public override string ToString() => Id;
    }
}
