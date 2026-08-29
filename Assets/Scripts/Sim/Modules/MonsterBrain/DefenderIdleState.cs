using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>방어자 전용 대기 상태 (FlowFieldState로 넘어가지 않음).</summary>
    public sealed class DefenderIdleState : IEntityState
    {
        public void Enter(MonsterBrainModule b) => b.Movement?.StopMoving();
        public void Update(MonsterBrainModule b, float dt) { }
        public void Exit(MonsterBrainModule b) { }
    }
}
