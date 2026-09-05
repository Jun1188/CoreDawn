using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>몬스터 두뇌의 상태 — 매 틱 두뇌가 Update를 부른다. 구 IEntityState(뷰 상태기)의 심 판.</summary>
    public interface IEntityState
    {
        void Enter(MonsterBrainModule brain);
        void Update(MonsterBrainModule brain, float dt);
        void Exit(MonsterBrainModule brain);
    }
}
