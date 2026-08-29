using System;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 공격 — 심 모듈. 사거리·쿨다운·명중 효과를 갖고, "지금 누구를 때린다"를 여기서 끝낸다:
    /// 직접 공격(근접)은 <see cref="TryAttack"/>이 대상의 <see cref="EffectsModule"/>에 효과를 바로 건다.
    /// 투사체·오라처럼 효과를 전달 계층에 위임하는 공격은 <see cref="MarkPerformed"/>로 쿨다운만 소비한다 —
    /// 그 효과는 명중 시 뷰가 심에 넘긴다(EntityView.ApplyEffects → Effects).
    /// 언제 때릴지(사거리 판정·표적 선택)는 두뇌(MonsterBrain)나 뷰(타워)가 정한다 — 이 모듈은 규칙만.
    /// (이름이 Combat이 아닌 이유: CoreDawn.Combat 네임스페이스와 단순명이 겹친다 — AGENTS.md 네임스페이스 규칙)
    /// </summary>
    public sealed class AttackModule : EntityModule
    {
        public float Range { get; private set; }
        public float Cooldown { get; private set; }

        /// <summary>명중 시 무슨 일이 일어나는가 — 직접 공격이 대상에게 건다. 비어 있으면 "때렸다"는 사실(쿨다운·연출)만 남는다.</summary>
        public Effect[] Effects { get; private set; } = Array.Empty<Effect>();

        float lastAttackTime = float.NegativeInfinity;

        /// <summary>공격이 일어났다 — (대상, null 가능). 연출(애니메이션·소리)용. 효과는 이미 심에서 적용됐거나 전달 계층이 맡는다.</summary>
        public event Action<Entity> Attacked;

        public AttackModule(float range, float cooldown, Effect[] effects = null)
        {
            Configure(range, cooldown);
            SetEffects(effects);
        }

        public void Configure(float range, float cooldown)
        {
            Range = Math.Max(0f, range);
            Cooldown = Math.Max(0.01f, cooldown);
        }

        public void SetEffects(Effect[] effects) => Effects = effects ?? Array.Empty<Effect>();

        public bool CanAttack(float now) => now >= lastAttackTime + Cooldown;

        /// <summary>
        /// 직접 공격 — 쿨다운을 소비하고 대상에게 효과를 건다. 사거리 판정은 호출자의 몫.
        /// 공격 버프는 항목별로 구워서(Affects 대상만) 나간다. 근접은 명중점이 곧 대상 위치라 방향이 나오지 않는다 —
        /// 때린 쪽에서 대상 쪽으로가 곧 "날아온 방향"이다(넉백이 쓴다).
        /// </summary>
        public bool TryAttack(Entity target, float now)
        {
            if (target == null || !target.IsAlive || !CanAttack(now)) return false;
            lastAttackTime = now;

            var effects = Effects;
            var mine = Owner?.Get<EffectsModule>();
            if (mine != null) effects = mine.BakeOutgoing(effects);
            Vector3 dir = Owner != null ? target.Position - Owner.Position : Vector3.zero;
            target.Get<EffectsModule>()?.Apply(effects, Owner, target.Position, dir);

            Attacked?.Invoke(target);
            return true;
        }

        /// <summary>위임 공격(투사체·오라) — 효과는 전달 계층이 명중 시 적용하고, 여기선 쿨다운만 소비한다.</summary>
        public void MarkPerformed(float now, Entity target = null)
        {
            lastAttackTime = now;
            Attacked?.Invoke(target);
        }
    }
}
