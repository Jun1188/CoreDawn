using System;
using System.Collections.Generic;

/// <summary>
/// 엔티티 하나의 활성 지속 효과 목록 — 순수 C#. 소유 Entity가 Update에서 Tick(dt)으로 구동한다
/// (HealthComponent·MovementComponent와 같은 패턴). 즉시 효과는 여길 거치지 않고
/// EffectSO.Apply에서 끝난다.
/// </summary>
public class EffectController
{
    // 지속 효과 1건의 진행 상태 — SO(공유 에셋)는 상태를 못 가지므로 여기서 든다
    private class ActiveEffect
    {
        public DurationEffectSO def;
        public EffectContext ctx;
        public float remaining;
        public float tickTimer;
    }

    private readonly Entity owner;
    private readonly List<ActiveEffect> active = new List<ActiveEffect>();

    /// <summary>활성 이동 속도 배율 — 감속 효과 중 가장 강한 것. Entity가 Movement에 밀어 넣는다.</summary>
    public float MoveSpeedMultiplier { get; private set; } = 1f;

    /// <summary>주는 피해 배율 — 활성 스탯 효과의 곱. 시전 측이 Power 계산에 곱한다.</summary>
    public float AttackMultiplier { get; private set; } = 1f;

    /// <summary>받는 피해 배율 — 활성 스탯 효과의 곱. Entity.ReceiveDamage가 곱한다.</summary>
    public float IncomingDamageMultiplier { get; private set; } = 1f;

    /// <summary>지속 효과 시작/종료 시 발화 — 상태 아이콘 UI 등 표시용.</summary>
    public event Action Changed;

    public EffectController(Entity owner) => this.owner = owner;

    // ── 적용 진입점 ──────────────────────────────────────────────

    /// <summary>
    /// 효과 목록을 일괄 적용한다. 목록이 비어 있으면 Power를 그대로 피해로 넣는
    /// 기본 피해(DamageEffectSO.Default)로 처리한다 — 구 TakeDamage(float) 경로.
    /// </summary>
    public void ApplyAll(IReadOnlyList<EffectSO> effects, in EffectContext ctx)
    {
        if (owner.IsDead) return;

        if (effects == null || effects.Count == 0)
        {
            DamageEffectSO.Default.Apply(owner, ctx);
            return;
        }
        for (int i = 0; i < effects.Count; i++)
            effects[i]?.Apply(owner, ctx);
    }

    /// <summary>지속 효과 등록 — DurationEffectSO.Apply가 호출한다. 직접 부르지 말 것.</summary>
    internal void Add(DurationEffectSO def, in EffectContext ctx)
    {
        if (def == null || owner.IsDead) return;

        if (def.stacking == EffectStacking.Refresh)
        {
            foreach (var e in active)
            {
                if (e.def != def) continue;
                e.remaining = def.duration;
                e.ctx = ctx; // 갱신한 쪽의 출처·수치로 교체
                return;      // OnStart 재호출 없음 — 시간만 연장
            }
        }

        var entry = new ActiveEffect
        {
            def = def,
            ctx = ctx,
            remaining = def.duration,
            tickTimer = def.TickInterval, // 첫 틱은 한 간격 뒤 — 적용 순간의 즉발은 즉시 효과의 몫
        };
        active.Add(entry);
        def.OnStart(owner, ctx);
        Recompute();
        Changed?.Invoke();
    }

    public void Tick(float dt)
    {
        if (active.Count == 0) return;
        if (owner.IsDead) { Clear(); return; } // DoT가 시체를 계속 때리지 않게

        bool ended = false;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var e = active[i];
            e.remaining -= dt;

            float interval = e.def.TickInterval;
            if (interval > 0f)
            {
                // 엡실론 없이 0 비교하면 dt 누적 오차(1e-8대)로 만료 순간의 마지막 틱이
                // 유실된다 — "2초/0.5초 = 4틱"이 3틱이 되는 것. 검증에서 실측된 사례.
                const float eps = 1e-4f;
                e.tickTimer -= dt;
                while (e.tickTimer <= eps && !owner.IsDead)
                {
                    e.def.OnTick(owner, e.ctx);
                    e.tickTimer += interval;
                }
            }

            if (e.remaining <= 0f)
            {
                active.RemoveAt(i);
                e.def.OnEnd(owner);
                ended = true;
            }
        }

        if (ended)
        {
            Recompute();
            Changed?.Invoke();
        }
    }

    /// <summary>모든 지속 효과 즉시 종료 — 사망 시 Entity가 호출한다.</summary>
    public void Clear()
    {
        if (active.Count == 0) return;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var e = active[i];
            active.RemoveAt(i);
            e.def.OnEnd(owner);
        }
        Recompute();
        Changed?.Invoke();
    }

    /// <summary>해당 정의의 지속 효과가 걸려 있는가 (상태 아이콘·중복 검사용).</summary>
    public bool Has(EffectSO def)
    {
        foreach (var e in active)
            if (e.def == def) return true;
        return false;
    }

    // 활성 효과가 바뀔 때만 집계 — 매 프레임 순회하지 않는다.
    // 이동 속도: 기준값 1에서 최솟값 — 감속(<1)만 표현. 가속(>1)을 넣으려면 재논의.
    // 스탯 배율: 곱 — 서로 다른 출처의 버프는 함께 작용하고, 같은 효과의 자기 중첩은
    //            stacking=Refresh가 막는다 (Stack으로 열면 곱으로 쌓인다).
    private void Recompute()
    {
        float speed = 1f, attack = 1f, incoming = 1f;
        foreach (var e in active)
        {
            if (e.def is IMoveSpeedModifier m && m.SpeedMultiplier < speed)
                speed = m.SpeedMultiplier;
            if (e.def is IStatModifier s)
            {
                attack *= s.AttackMultiplier;
                incoming *= s.IncomingDamageMultiplier;
            }
        }
        MoveSpeedMultiplier = speed < 0f ? 0f : speed;
        AttackMultiplier = attack < 0f ? 0f : attack;
        IncomingDamageMultiplier = incoming < 0f ? 0f : incoming;
    }
}
