namespace CoreDawn.Sim
{
    /// <summary>
    /// 효과 하나 — "무엇을(<see cref="Spec"/>) 얼마나(<see cref="Value"/>)". 공격·탄·오라·웨이브 버프의 내용은
    /// 이 목록이 전부다: 피해도 항목의 하나({Damage, 10}). 크기의 해석은 종류가 한다(피해량·거리·배율).
    /// 시전측 배율(공격 버프·발사기 배율)은 보내기 전에 Value에 구워진다 — 탄이 날아가는 동안 버프가 끝나도 발사 때 값이 유지된다.
    /// </summary>
    public readonly struct Effect
    {
        public readonly EffectSpec Spec;
        public readonly float Value;

        public Effect(EffectSpec spec, float value)
        {
            Spec = spec;
            Value = value;
        }

        public Effect WithValue(float value) => new Effect(Spec, value);

        public override string ToString() => Spec != null ? Spec.Id + "=" + Value : "(none)";
    }
}
