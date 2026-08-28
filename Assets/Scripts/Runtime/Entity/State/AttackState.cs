using UnityEngine;

namespace CoreDawn.Entities
{
    public class AttackState : IEntityState
    {
        // 추적 복귀 히스테리시스 — 진입(사거리)과 이탈 기준이 같으면 사거리 경계에서
        // Attack↔Chase가 매 프레임 진동해 이동이 서다 가다를 반복한다(애니메이션 끊김의 주범).
        // 이탈은 사거리보다 이만큼 여유를 두고 판정한다.
        private const float ExitRangeBuffer = 1.15f;

        private Entity target;

        public Entity Target => target;

        public AttackState(Entity target)
        {
            this.target = target;
        }

        public void Enter(StateMachineComponent stateMachine)
        {
            stateMachine.Movement?.StopMoving();
            TryAttackTarget(stateMachine);
        }

        public void Update(StateMachineComponent stateMachine)
        {
            if (!target.IsValidTarget())
            {
                stateMachine.SetState(new IdleState());
                return;
            }

            float distance = target.DistanceTo(stateMachine.Transform.position);
            if (stateMachine.Combat == null || distance > stateMachine.Combat.AttackRange * ExitRangeBuffer)
            {
                // 같은 타겟을 유지한 채 재추적
                stateMachine.SetState(new ChaseState(target));
                return;
            }

            // 사거리 안에 있는 동안은 쿨다운이 돌 때마다 계속 공격
            if (stateMachine.Combat.CanAttack())
            {
                TryAttackTarget(stateMachine);
            }
        }

        public void Exit(StateMachineComponent stateMachine)
        {
        }

        private void TryAttackTarget(StateMachineComponent stateMachine)
        {
            if (!target.IsValidTarget() || stateMachine.Combat == null) return;

            // y축 회전만 적용해 타겟을 바라본다
            Vector3 lookPoint = target.GetPosition();
            lookPoint.y = stateMachine.Transform.position.y;
            stateMachine.Transform.LookAt(lookPoint);

            stateMachine.Combat.TryAttack(target);
        }
    }
}
