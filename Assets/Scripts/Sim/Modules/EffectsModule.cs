using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 효과 — 심 모듈. 명중한 효과 목록을 이 엔티티에 적용하고(즉시: 피해·회복·넉백), 지속 효과의 진행(남은 시간·틱)을
    /// 갖는다. 피해·효과·사망이 여기서 심 안에 끝난다 — 뷰는 명중을 감지해 넘기고 결과를 그릴 뿐이다.
    /// (구 EffectController: 뷰가 소유하고 Time.deltaTime으로 돌리던 것을 그대로 옮겼다. 동작 변경 0.)
    ///
    /// 받는 피해 배율은 <see cref="IDamageInterceptor"/>로 <see cref="HealthModule.Damage"/> 안에서 곱한다 —
    /// 출처가 무엇이든(투사체·근접·DoT·환경) 한 번 걸러진다. 뷰가 곱해서 넘기던 옛 구조는 뷰를 거치지 않는 피해를 놓친다.
    ///
    /// 채널 집계 — 값은 정의(공유)가 아니라 활성 인스턴스의 Value에서 읽는다:
    ///   이동 속도: 가장 강한 감속(&lt;1 최솟값) × 가장 강한 가속(&gt;1 최댓값) — 같은 극끼리는 안 쌓인다
    ///   주는·받는 피해: 서로 다른 효과의 곱 — 출처가 다른 버프는 함께 작용하고, 같은 효과의 자기 중첩은 Stacks=false가 막는다
    ///
    /// 틱은 <see cref="EffectSystem"/>이 돌린다. Health가 있는 엔티티는 전부 이 모듈을 갖는다(공장·몬스터·플레이어·뷰 우선 개체).
    /// </summary>
    public sealed class EffectsModule : EntityModule, IDamageInterceptor
    {
        // 지속 효과 1건의 진행 상태
        sealed class Active
        {
            public EffectSpec Spec;
            public float Value;
            public float Interval;
            public Entity Source;
            public float Remaining;
            public float TickTimer;
        }

        readonly List<Active> active = new List<Active>();

        /// <summary>활성 이동 속도 배율 — Movement가 읽는다.</summary>
        public float MoveSpeedMultiplier { get; private set; } = 1f;

        /// <summary>받는 피해 배율 — <see cref="Intercept"/>가 Health.Damage 안에서 곱한다.</summary>
        public float IncomingDamageMultiplier { get; private set; } = 1f;

        // 공격 버프가 하나라도 걸려 있는가 — BakeOutgoing의 무할당 지름길용
        bool hasAttackModifiers;

        /// <summary>지속 효과 시작/종료 시 발화 — 상태 아이콘 UI 등 표시용.</summary>
        public event Action Changed;

        public int ActiveCount => active.Count;

        bool OwnerAlive => Owner != null && Owner.IsAlive;

        // ── 적용 ────────────────────────────────────────────────────

        /// <summary>
        /// 효과 목록을 일괄 적용한다. 시전측 배율은 이미 구워져 있어야 한다(<see cref="BakeOutgoing"/>).
        /// </summary>
        /// <param name="source">시전자. 출처 없는 피해(환경·웨이브 버프)면 null.</param>
        /// <param name="hitPoint">명중 지점 — 방사형 넉백의 기준.</param>
        /// <param name="hitDirection">공격이 날아온 방향. 모르면(폭발·오라) 기본값 — 넉백이 스스로 대체 규칙을 쓴다.</param>
        public void Apply(IReadOnlyList<Effect> effects, Entity source, Vector3 hitPoint, Vector3 hitDirection = default)
        {
            if (effects == null || !OwnerAlive) return;
            for (int i = 0; i < effects.Count; i++)
                Apply(effects[i], source, hitPoint, hitDirection);
        }

        public void Apply(in Effect effect, Entity source, Vector3 hitPoint, Vector3 hitDirection = default)
        {
            var spec = effect.Spec;
            if (spec == null || !OwnerAlive) return;

            switch (spec.Kind)
            {
                case EffectKind.Damage:
                    if (effect.Value > 0f) Owner.Health?.Damage(effect.Value, source);
                    break;
                case EffectKind.Heal:
                    if (effect.Value > 0f) Owner.Health?.Heal(effect.Value);
                    break;
                case EffectKind.Knockback:
                    ApplyKnockback(spec, effect.Value, source, hitPoint, hitDirection);
                    break;
                default:
                    Add(effect, source);   // 지속 효과 — 진행은 Tick이
                    break;
            }
        }

        void ApplyKnockback(EffectSpec spec, float distance, Entity source, Vector3 hitPoint, Vector3 hitDirection)
        {
            var movement = Owner.Get<MovementModule>();   // 이동이 없는 개체(건물)는 밀리지 않는다
            if (movement == null || distance <= 0f) return;

            // 방향 — 날아온 방향(총알·근접) → 명중점에서 바깥(폭발·오라, 또는 방향을 모를 때) → 시전자에서 바깥
            Vector3 dir = Vector3.zero;
            if (!spec.RadialKnockback && hitDirection.sqrMagnitude > 0.0001f) dir = hitDirection;
            else
            {
                Vector3 fromPoint = Owner.Position - hitPoint;
                fromPoint.y = 0f;
                if (fromPoint.sqrMagnitude > 0.0001f) dir = fromPoint;
                else if (source != null) dir = Owner.Position - source.Position;
            }
            if (dir.sqrMagnitude < 0.0001f) return;   // 방향을 알 수 없으면 포기
            movement.AddKnockback(dir, distance);     // 정규화·수평 투영은 Movement가 한다
        }

        void Add(in Effect effect, Entity source)
        {
            var spec = effect.Spec;
            if (!spec.Stacks)
            {
                foreach (var e in active)
                {
                    if (!ReferenceEquals(e.Spec, spec)) continue;
                    e.Remaining = effect.Lifetime;   // 갱신한 쪽의 출처·크기·시간으로 교체 (같은 정의를 다른 값으로 재적용)
                    e.Value = effect.Value;
                    e.Interval = effect.Interval;
                    e.Source = source;
                    Recompute();
                    return;                          // 시작 통지 없음
                }
            }

            active.Add(new Active
            {
                Spec = spec,
                Value = effect.Value,
                Interval = effect.Interval,
                Source = source,
                Remaining = effect.Lifetime,
                TickTimer = effect.Interval,   // 첫 틱은 한 간격 뒤 — 적용 순간의 즉발은 즉시 효과의 몫
            });
            Recompute();
            Changed?.Invoke();
        }

        // ── 나가는 공격 베이크 ──────────────────────────────────────

        /// <summary>
        /// 이 엔티티의 공격 버프(AttackModifier)가 증폭하는 항목의 Value를 곱해 최종 공격 목록을 만든다.
        /// 공격/발사 시점에 한 번 호출. 무엇이 증폭되는지는 버프 정의의 Affects(데이터)가 정한다 —
        /// 배율이 감속·넉백 같은 비대상 항목까지 뭉개지 않는다. 버프가 없으면 원본을 그대로 돌려준다(할당 없음).
        /// </summary>
        public Effect[] BakeOutgoing(Effect[] effects)
        {
            if (effects == null || effects.Length == 0 || !hasAttackModifiers) return effects;

            var baked = new Effect[effects.Length];
            for (int i = 0; i < effects.Length; i++)
                baked[i] = effects[i].WithValue(effects[i].Value * AttackMultiplierFor(effects[i].Spec));
            return baked;
        }

        /// <summary>해당 효과 정의에 걸리는 공격 배율 — 그 정의를 Affects에 담은 활성 버프들의 곱.</summary>
        public float AttackMultiplierFor(EffectSpec spec)
        {
            float multiplier = 1f;
            foreach (var e in active)
                if (e.Spec.Kind == EffectKind.AttackModifier && e.Spec.DoesAffect(spec))
                    multiplier *= e.Value;
            return multiplier < 0f ? 0f : multiplier;
        }

        /// <summary>해당 정의의 지속 효과가 걸려 있는가 (상태 아이콘·중복 검사용).</summary>
        public bool Has(EffectSpec spec)
        {
            foreach (var e in active)
                if (ReferenceEquals(e.Spec, spec)) return true;
            return false;
        }

        // ── 진행 ────────────────────────────────────────────────────

        /// <summary>지속 효과 진행 — EffectSystem이 매 틱 호출한다.</summary>
        public void Tick(float dt)
        {
            if (active.Count == 0) return;
            if (!OwnerAlive) { Clear(); return; }   // DoT가 시체를 계속 때리지 않게

            bool ended = false;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var e = active[i];
                e.Remaining -= dt;

                float interval = e.Interval;
                if (interval > 0f)
                {
                    // 엡실론 없이 0 비교하면 dt 누적 오차(1e-8대)로 만료 순간의 마지막 틱이
                    // 유실된다 — "2초/0.5초 = 4틱"이 3틱이 되는 것. 검증에서 실측된 사례.
                    const float eps = 1e-4f;
                    e.TickTimer -= dt;
                    while (e.TickTimer <= eps && OwnerAlive)
                    {
                        OnTick(e);
                        e.TickTimer += interval;
                    }
                }

                if (e.Remaining <= 0f)
                {
                    active.RemoveAt(i);
                    ended = true;
                }
            }

            if (ended)
            {
                Recompute();
                Changed?.Invoke();
            }
        }

        void OnTick(Active e)
        {
            switch (e.Spec.Kind)
            {
                case EffectKind.DamageOverTime:
                    if (e.Value > 0f) Owner.Health?.Damage(e.Value, e.Source);   // 받는 배율은 Intercept가 곱한다
                    break;
            }
        }

        /// <summary>모든 지속 효과 즉시 종료 — 사망 시 EffectSystem이 호출한다.</summary>
        public void Clear()
        {
            if (active.Count == 0) return;
            active.Clear();
            Recompute();
            Changed?.Invoke();
        }

        // 활성 효과가 바뀔 때만 집계 — 매 틱 순회하지 않는다.
        // 공격 배율은 효과 정의별(Affects)이라 스칼라로 집계할 수 없다 — AttackMultiplierFor가 조회 시 계산하고,
        // 여기서는 존재 여부(hasAttackModifiers)만 갱신한다.
        void Recompute()
        {
            float slow = 1f, haste = 1f, incoming = 1f;
            hasAttackModifiers = false;
            foreach (var e in active)
            {
                float v = e.Value;
                switch (e.Spec.Kind)
                {
                    case EffectKind.MoveSpeed:
                        if (v < slow) slow = v;        // 가장 강한 감속
                        else if (v > haste) haste = v; // 가장 강한 가속
                        break;
                    case EffectKind.AttackModifier:
                        hasAttackModifiers = true;
                        break;
                    case EffectKind.IncomingDamage:
                        incoming *= v;
                        break;
                }
            }
            MoveSpeedMultiplier = slow * haste < 0f ? 0f : slow * haste;
            IncomingDamageMultiplier = incoming < 0f ? 0f : incoming;
        }

        // ── 받는 피해 ───────────────────────────────────────────────

        /// <summary>받는 피해 배율(방어 디버프·웨이브 버프) — Health.Damage의 인터셉터 체인에서 곱한다.</summary>
        public float Intercept(float amount, Entity source) => amount * IncomingDamageMultiplier;

        protected internal override void OnAttach() => Owner.Health?.AddInterceptor(this);   // Health가 뒤에 붙으면 Health가 훑어 등록한다

        protected internal override void OnDetach()
        {
            Owner.Health?.RemoveInterceptor(this);
            active.Clear();
            Recompute();
        }
    }
}
