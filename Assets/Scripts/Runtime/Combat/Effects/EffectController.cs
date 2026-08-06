using System;
using System.Collections.Generic;

/// <summary>
/// 엔티티 하나의 활성 지속 효과 목록 — 순수 C#. 소유 Entity가 Update에서 Tick(dt)으로 구동한다
/// (HealthComponent·MovementComponent와 같은 패턴). 즉시 효과는 여길 거치지 않고
/// EffectSO.Apply에서 끝난다.
///
/// 채널 집계 — 값은 정의(공유 에셋)가 아니라 활성 인스턴스의 ctx.Value에서 읽는다:
///   이동 속도: 가장 강한 감속(&lt;1 최솟값) × 가장 강한 가속(&gt;1 최댓값) — 같은 극끼리는 안 쌓인다
///   주는·받는 피해: 서로 다른 효과의 곱 — 출처가 다른 버프는 함께 작용하고,
///                   같은 효과의 자기 중첩은 stacking=Refresh가 막는다
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

    /// <summary>활성 이동 속도 배율 — Entity가 Movement에 밀어 넣는다.</summary>
    public float MoveSpeedMultiplier { get; private set; } = 1f;

    /// <summary>받는 피해 배율 — Entity.ReceiveDamage가 곱한다.</summary>
    public float IncomingDamageMultiplier { get; private set; } = 1f;

    // 공격 버프가 하나라도 걸려 있는가 — BakeOutgoing의 무할당 지름길용
    private bool hasAttackModifiers;

    /// <summary>지속 효과 시작/종료 시 발화 — 상태 아이콘 UI 등 표시용.</summary>
    public event Action Changed;

    public EffectController(Entity owner) => this.owner = owner;

    // ── 적용 진입점 ──────────────────────────────────────────────

    /// <summary>효과 항목 목록을 일괄 적용한다. 시전측 배율은 이미 베이크돼 있어야 한다(BakeOutgoing).</summary>
    public void ApplyAll(IReadOnlyList<EffectEntry> entries, Entity source, UnityEngine.Vector3 hitPoint)
    {
        if (owner.IsDead || entries == null) return;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            entry.effect?.Apply(owner, new EffectContext(source, entry.value, hitPoint));
        }
    }

    // ── 나가는 공격 베이크 ──────────────────────────────────────

    /// <summary>
    /// 이 엔티티의 공격 버프(AttackModifier)가 증폭하는 항목의 value를 곱해
    /// 최종 공격 목록을 만든다. 공격/발사 시점에 한 번 호출 — 탄이 날아가는 동안
    /// 버프가 끝나도 발사 때 배율이 유지된다.
    /// 무엇이 증폭되는지는 버프 에셋의 affects 목록(데이터)이 정한다 —
    /// 배율이 감속·넉백 같은 비대상 항목까지 뭉개는 일이 없다.
    /// 버프가 없으면 원본을 그대로 돌려준다 (할당 없음).
    /// </summary>
    public EffectEntry[] BakeOutgoing(EffectEntry[] entries)
    {
        if (entries == null || entries.Length == 0 || !hasAttackModifiers) return entries;

        var baked = new EffectEntry[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            baked[i] = new EffectEntry(entry.effect, entry.value * AttackMultiplierFor(entry.effect));
        }
        return baked;
    }

    /// <summary>해당 효과 정의에 걸리는 공격 배율 — 그 정의를 affects에 담은 활성 버프들의 곱.</summary>
    public float AttackMultiplierFor(EffectSO effect)
    {
        float multiplier = 1f;
        foreach (var e in active)
            if (e.def is AttackModifierEffectSO buff && buff.Affects(effect))
                multiplier *= e.ctx.Value;
        return multiplier < 0f ? 0f : multiplier;
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
                e.ctx = ctx;  // 갱신한 쪽의 출처·크기로 교체
                Recompute();  // 크기가 바뀌었을 수 있다 (같은 에셋을 다른 value로 재적용)
                return;       // OnStart 재호출 없음 — 시간만 연장
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
    // 공격 배율은 효과 정의별(affects)이라 스칼라로 집계할 수 없다 — AttackMultiplierFor가
    // 조회 시 계산하고, 여기서는 존재 여부(hasAttackModifiers)만 갱신한다.
    private void Recompute()
    {
        float slow = 1f, haste = 1f, incoming = 1f;
        hasAttackModifiers = false;
        foreach (var e in active)
        {
            float v = e.ctx.Value;
            switch (e.def)
            {
                case MoveSpeedEffectSO:
                    if (v < slow) slow = v;        // 가장 강한 감속
                    else if (v > haste) haste = v; // 가장 강한 가속
                    break;
                case AttackModifierEffectSO:
                    hasAttackModifiers = true;
                    break;
                case IncomingDamageEffectSO:
                    incoming *= v;
                    break;
            }
        }
        MoveSpeedMultiplier = slow * haste < 0f ? 0f : slow * haste;
        IncomingDamageMultiplier = incoming < 0f ? 0f : incoming;
    }
}
