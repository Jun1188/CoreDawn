// 대기 — 플로우필드가 준비되면 기본 이동(FlowFieldState)으로 전환한다.
// 플레이어 감지는 Player의 센서 콜백(Monster.OnDetectedByPlayer)이 담당하므로
// 여기서는 아무것도 스캔하지 않는다 (몬스터 개별 OverlapSphere 제거).
public class IdleState : IEntityState
{
    public void Enter(StateMachineComponent stateMachine)
    {
        stateMachine.Movement?.StopMoving();
    }

    public void Update(StateMachineComponent stateMachine)
    {
        if (FlowFieldManager.Instance != null && FlowFieldManager.Instance.HasField)
        {
            stateMachine.SetState(new FlowFieldState());
        }
    }

    public void Exit(StateMachineComponent stateMachine) { }
}
