using UnityEngine;

public class AttackState : IEntityState
{
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
        if (stateMachine.Combat == null || distance > stateMachine.Combat.AttackRange)
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
