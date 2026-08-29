using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    public sealed class DeadState : IEntityState
    {
        public void Enter(MonsterBrainModule b) => b.Movement?.StopMoving();
        public void Update(MonsterBrainModule b, float dt) { }
        public void Exit(MonsterBrainModule b) { }
    }
}
