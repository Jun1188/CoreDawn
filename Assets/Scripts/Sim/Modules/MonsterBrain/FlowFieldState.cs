using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 기본 길찾기 — 플로우필드(벡터 필드)를 따라 목표(코어/타워)로 전진한다. 런타임 A*보다 훨씬 가볍다.
    /// 진격 경로 위의 건물이 사거리에 들어오면 공격으로 전환한다 — 사거리 안이라고 아무 건물이나 치지 않는다.
    /// </summary>
    public sealed class FlowFieldState : IEntityState
    {
        public void Enter(MonsterBrainModule b) { }

        public void Update(MonsterBrainModule b, float dt)
        {
            var nav = b.Nav;
            if (nav == null || !nav.HasFlowField)
            {
                b.SetState(new IdleState());
                return;
            }

            if (b.Attack != null)
            {
                var building = nav.FindBreachTarget(b.Owner.Position, b.Attack.Range);
                if (MonsterBrainModule.IsValidTarget(building))
                {
                    b.SetState(new AttackState(building));
                    return;
                }
            }

            // 방향은 매 틱 갱신 — 필드가 재계산돼도 자연스럽게 새 방향을 따른다
            b.Movement?.SetDirection(nav.FlowDirectionAt(b.Owner.Position));
        }

        public void Exit(MonsterBrainModule b) => b.Movement?.StopMoving();
    }
}
