using UnityEngine;

// 상태머신 — 순수 C# 클래스. 소유 Entity가 매 프레임 Tick()으로 구동한다.
// 각 상태가 소유 Entity의 컴포넌트(순수 C#)에 접근하는 통로 역할을 겸한다.
public class StateMachineComponent
{
    public Entity Owner { get; }

    public Transform Transform => Owner.transform;
    public MovementComponent Movement => Owner.Movement;
    public CombatComponent Combat => Owner.Combat;
    public SensorComponent Sensor => Owner.Sensor;

    private IEntityState currentState;
    public IEntityState CurrentState => currentState;

    public StateMachineComponent(Entity owner)
    {
        Owner = owner;
    }

    public void SetState(IEntityState newState)
    {
        currentState?.Exit(this);
        currentState = newState;
        currentState?.Enter(this);
    }

    public void Tick()
    {
        currentState?.Update(this);
    }
}
