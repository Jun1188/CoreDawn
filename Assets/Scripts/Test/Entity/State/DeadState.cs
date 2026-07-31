using UnityEngine;

public class DeadState : IEntityState
{
    public void Enter(StateMachineComponent stateMachine)
    {
        stateMachine.Movement?.StopMoving();
    }

    public void Update(StateMachineComponent stateMachine) 
    {
    }

    public void Exit(StateMachineComponent stateMachine) 
    {
    }
}
