using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>대기 — 플로우필드가 준비되면 기본 이동(FlowFieldState)으로 전환한다. 플레이어 감지는 플레이어 센서 콜백이 담당한다.</summary>
    public sealed class IdleState : IEntityState
    {
        public void Enter(MonsterBrainModule b) => b.Movement?.StopMoving();

        public void Update(MonsterBrainModule b, float dt)
        {
            if (b.Nav != null && b.Nav.HasFlowField) b.SetState(new FlowFieldState());
        }

        public void Exit(MonsterBrainModule b) { }
    }
}
