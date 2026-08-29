namespace CoreDawn.Sim
{
    /// <summary>
    /// 효과 하나 — "무엇을(<see cref="Spec"/>) 얼마나(<see cref="Value"/>) 얼마 동안(<see cref="Duration"/>)".
    /// 공격·탄·오라·웨이브 버프의 내용은 이 목록이 전부다: 피해도 항목의 하나({Damage, 10}).
    /// 시전측 배율(공격 버프·발사기 배율)은 보내기 전에 Value에 구워진다 — 탄이 날아가는 동안 버프가 끝나도 발사 때 값이 유지된다.
    /// Duration·TickInterval이 0이면 정의의 기본값.
    /// </summary>
    public readonly struct Effect
    {
        public readonly EffectSpec Spec;
        public readonly float Value;
        public readonly float Duration;
        public readonly float TickInterval;

        public Effect(EffectSpec spec, float value, float duration = 0f, float tickInterval = 0f)
        {
            Spec = spec;
            Value = value;
            Duration = duration;
            TickInterval = tickInterval;
        }

        public Effect WithValue(float value) => new Effect(Spec, value, Duration, TickInterval);

        /// <summary>실제 지속 시간 — 적용 값이 있으면 그것, 없으면 정의 기본(영구면 무한대).</summary>
        public float Lifetime => Duration > 0f ? Duration : (Spec != null ? Spec.Lifetime : 0f);

        /// <summary>실제 틱 간격 — 적용 값이 있으면 그것, 없으면 정의 기본.</summary>
        public float Interval => TickInterval > 0f ? TickInterval : (Spec != null ? Spec.TickInterval : 0f);

        public override string ToString() => Spec != null ? Spec.Id + "=" + Value : "(none)";
    }
}
