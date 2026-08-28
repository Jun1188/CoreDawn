using System;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 공격 — 심 모듈. 사거리·쿨다운과 "지금 누구를 때린다"는 결정만 갖는다.
    /// (이름이 Combat이 아닌 이유: CoreDawn.Combat 네임스페이스와 단순명이 겹친다 — AGENTS.md 네임스페이스 규칙)
    ///
    /// 명중 효과(피해·넉백·DoT)의 적용은 4단계까지 뷰의 효과 시스템에 남아 있다 — 그래서 이 모듈은 결정을
    /// <see cref="AttackRequested"/>로 알리고, 뷰(MonsterView)가 대상 뷰를 찾아 효과를 건다.
    /// 4단계에서 효과가 심으로 오면 이벤트가 곧 적용이 된다.
    /// </summary>
    public sealed class Attack : EntityModule
    {
        public float Range { get; private set; }
        public float Cooldown { get; private set; }

        float lastAttackTime = float.NegativeInfinity;

        /// <summary>때리기로 했다 — (대상). 쿨다운은 여기서 이미 소비됐다.</summary>
        public event Action<Entity> AttackRequested;

        public Attack(float range, float cooldown) => Configure(range, cooldown);

        public void Configure(float range, float cooldown)
        {
            Range = Math.Max(0f, range);
            Cooldown = Math.Max(0.01f, cooldown);
        }

        public bool CanAttack(float now) => now >= lastAttackTime + Cooldown;

        /// <summary>살아 있는 대상에게 쿨다운이 찼을 때만. 성공하면 쿨다운을 소비하고 AttackRequested를 쏜다.</summary>
        public bool TryAttack(Entity target, float now)
        {
            if (target == null || !target.IsAlive || !CanAttack(now)) return false;
            lastAttackTime = now;
            AttackRequested?.Invoke(target);
            return true;
        }
    }
}
