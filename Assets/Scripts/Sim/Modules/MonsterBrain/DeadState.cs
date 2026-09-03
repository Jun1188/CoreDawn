using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 죽은 상태 — 시체가 corpseSeconds 만큼 남았다가 심이 제거한다(뷰는 Entity.Removed 로 사라진다).
    /// 제거 시점을 뷰가 정하던 것(view.deathDelay → 뷰 파괴 → Despawn)을 심으로 옮긴 자리(2026-09-04).
    /// </summary>
    public sealed class DeadState : IEntityState
    {
        float elapsed;

        public void Enter(MonsterBrainModule b) => b.Movement?.StopMoving();

        public void Update(MonsterBrainModule b, float dt)
        {
            elapsed += dt;
            if (elapsed >= b.CorpseSeconds) b.System.Despawn(b.Owner);
        }

        public void Exit(MonsterBrainModule b) { }
    }
}
