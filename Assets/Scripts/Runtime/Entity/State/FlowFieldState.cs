using UnityEngine;
using CoreDawn.Navigation;

namespace CoreDawn.Entities
{
    // 기본 길찾기 — 플로우필드(벡터 필드)를 따라 목표(코어/타워)로 전진한다.
    // 런타임 A*보다 훨씬 가볍다: 몬스터는 자기 셀의 방향 벡터만 샘플링하면 된다.
    // 사거리에 건물이 들어오면 공격으로 전환하고, 플레이어 추적(런타임 A*)은
    // Player 센서 콜백이 ChaseState로 전환시키므로 여기서는 신경 쓰지 않는다.
    public class FlowFieldState : IEntityState
    {
        public void Enter(StateMachineComponent stateMachine) { }

        public void Update(StateMachineComponent stateMachine)
        {
            var manager = FlowFieldManager.Instance;
            if (manager == null || !manager.HasField)
            {
                stateMachine.SetState(new IdleState());
                return;
            }

            // 진격 경로 위의 건물(뚫어야 할 것·도착한 목표)이 사거리에 들어오면 공격.
            // 사거리 안이라고 아무 건물이나 치지 않는다 — 그러면 코어로 가다 길가의 나무·설비마다 멈춘다.
            if (stateMachine.Combat != null)
            {
                var building = manager.FindBreachTarget(
                    stateMachine.Transform.position, stateMachine.Combat.AttackRange);
                if (building.IsValidTarget())
                {
                    stateMachine.SetState(new AttackState(building));
                    return;
                }
            }

            // 방향은 매 프레임 갱신 — 필드가 재계산돼도 자연스럽게 새 방향을 따른다
            Vector3 direction = manager.GetDirection(stateMachine.Transform.position);
            stateMachine.Movement?.SetDirection(direction);
        }

        public void Exit(StateMachineComponent stateMachine)
        {
            stateMachine.Movement?.StopMoving();
        }
    }
}
