using System;
using System.Collections.Generic;
using Newtonsoft.Json;

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
    /// 효과 정의 — json effects 섹션 항목. 종류·중첩 규칙·틱 간격·기본 지속 시간만 갖고, 크기·시간은 적용(<see cref="Effect"/>)이 든다.
    /// 정의당 하나라 "같은 효과인가"(재적용 갱신·AttackModifier의 대상)는 참조 동일성으로 판정한다.
    /// </summary>
    public sealed class EffectSpec : Def
    {
        [JsonProperty("type")] public EffectKind Kind;

        /// <summary>기본 지속 시간(초). 0 이하 = 영구(대상이 죽을 때까지). 적용이 duration을 주면 그것이 우선.</summary>
        [JsonProperty("duration")] public float Duration;

        /// <summary>"Refresh" = 재적용 시 남은 시간·크기 갱신, "Stack" = 별개 인스턴스로 중첩.</summary>
        [JsonProperty("stacking")] public string Stacking = "Refresh";

        /// <summary>기본 틱 간격(초). 0 이하 = 틱 없음.</summary>
        [JsonProperty("tickInterval")] public float TickInterval;

        /// <summary>넉백 방향 — "Directional" = 공격이 날아온 방향, "Radial" = 명중점에서 바깥.</summary>
        [JsonProperty("knockbackMode")] public string KnockbackMode = "Directional";

        /// <summary>AttackModifier가 증폭(또는 약화)하는 효과 id들.</summary>
        [JsonProperty("affects")] public List<string> AffectIds = new List<string>();

        [JsonIgnore] public bool Stacks => Stacking == "Stack";
        [JsonIgnore] public bool RadialKnockback => KnockbackMode == "Radial";
        [JsonIgnore] public bool IsInstant => Kind == EffectKind.Damage || Kind == EffectKind.Heal || Kind == EffectKind.Knockback;

        /// <summary>남은 시간의 시작값. 영구 효과는 무한대 — 큰 수로 흉내 내면 저장·표시에 새어 나온다.</summary>
        [JsonIgnore] public float Lifetime => Duration > 0f ? Duration : float.PositiveInfinity;

        EffectSpec[] affects = Array.Empty<EffectSpec>();

        [JsonIgnore] public IReadOnlyList<EffectSpec> Affects => affects;

        public EffectSpec() { }

        /// <summary>코드·테스트·에셋 브리지용 — json을 거치지 않고 정의를 만든다.</summary>
        public EffectSpec(string id, EffectKind kind, float duration = 0f, bool stacks = false,
                          float tickInterval = 0f, bool radialKnockback = false)
        {
            Id = string.IsNullOrEmpty(id) ? kind.ToString() : id;
            Kind = kind;
            Duration = duration;
            Stacking = stacks ? "Stack" : "Refresh";
            TickInterval = tickInterval;
            KnockbackMode = radialKnockback ? "Radial" : "Directional";
        }

        /// <summary>AttackModifier 전용. 생성 뒤에 따로 채우는 이유: 버프가 버프를 가리킬 수 있어(순환) 먼저 등록하고 나중에 잇는다.</summary>
        public void SetAffects(EffectSpec[] targets) => affects = targets ?? Array.Empty<EffectSpec>();

        public bool DoesAffect(EffectSpec other)
        {
            if (other == null) return false;
            for (int i = 0; i < affects.Length; i++)
                if (ReferenceEquals(affects[i], other)) return true;
            return false;
        }

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            var list = new List<EffectSpec>(AffectIds.Count);
            foreach (var id in AffectIds)
            {
                var t = db.ResolveEffect(id, errors, Id);
                if (t != null) list.Add(t);
            }
            affects = list.ToArray();
        }
    }
}
