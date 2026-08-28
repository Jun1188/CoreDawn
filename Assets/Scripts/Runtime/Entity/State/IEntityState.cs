public interface IEntityState
{
    void Enter(StateMachineComponent stateMachine);
    void Update(StateMachineComponent stateMachine);
    void Exit(StateMachineComponent stateMachine);
}
