using UnityEngine;
using System.Collections.Generic;

// 런타임 A* 추적 — 플레이어 센서에 감지된 몬스터만 사용하는 무거운 길찾기.
// 평상시 이동은 FlowFieldState가 담당한다. 추적 포기는 거리 판정 대신
// Player의 해제 콜백(Monster.OnLostByPlayer)이 담당한다.
public class ChaseState : IEntityState
{
    private Entity target;
    private float pathUpdateInterval = 0.5f;
    private float lastPathUpdateTime;
    private StateMachineComponent owner;

    public Entity Target => target;

    public ChaseState(Entity target)
    {
        this.target = target;
    }

    public void Enter(StateMachineComponent stateMachine)
    {
        owner = stateMachine;
        if (stateMachine.Movement != null)
        {
            stateMachine.Movement.OnPathBlocked += HandlePathBlocked;
        }
        UpdatePath(stateMachine);
    }

    public void Update(StateMachineComponent stateMachine)
    {
        if (!target.IsValidTarget())
        {
            stateMachine.SetState(new IdleState());
            return;
        }

        float distance = target.DistanceTo(stateMachine.Transform.position);

        if (stateMachine.Combat != null && distance <= stateMachine.Combat.AttackRange)
        {
            stateMachine.SetState(new AttackState(target));
            return;
        }

        if (Time.time >= lastPathUpdateTime + pathUpdateInterval)
        {
            UpdatePath(stateMachine);
        }
    }

    public void Exit(StateMachineComponent stateMachine)
    {
        if (stateMachine.Movement != null)
        {
            stateMachine.Movement.OnPathBlocked -= HandlePathBlocked;
            stateMachine.Movement.StopMoving();
        }
    }

    private void HandlePathBlocked()
    {
        if (owner != null) UpdatePath(owner);
    }

    private void UpdatePath(StateMachineComponent stateMachine)
    {
        if (!target.IsValidTarget()) return;

        lastPathUpdateTime = Time.time;
        List<Node> path = PathFinder.FindPath(stateMachine.Transform.position, target.GetPosition());

        if (path != null)
        {
            if (path.Count > 0)
            {
                stateMachine.Movement?.StartMoving(path);
            }
            else
            {
                // 시작 셀과 목표(보정) 셀이 같으면 빈 경로가 반환된다 — 막힘이 아니라 이미 도착.
                // 사거리 진입 여부는 Update의 거리 판정에 맡긴다
                stateMachine.Movement?.StopMoving();
            }
            return;
        }

        // 길이 완전히 막힘 → 경로를 막는 건물을 새 타겟으로 삼아 부순다.
        // 심(POCO) 건물을 뷰 GameObject의 Building 엔티티로 변환해 타겟으로 쓴다
        Building blocker = PathFinder.FindBlockingBuilding(stateMachine.Transform.position, target.GetPosition());
        BuildingEntity buildingEntity = BuildingEntity.GetOrAttach(blocker);

        if (buildingEntity != null && !ReferenceEquals(buildingEntity, target))
        {
            stateMachine.SetState(new ChaseState(buildingEntity));
            return;
        }

        // 부술 건물조차 없으면(지형 막힘 등) Chase에 머물며 pathUpdateInterval 주기로 재시도.
        // Idle로 보내면 Idle이 다음 프레임 바로 플로우필드로 전환해 상태 진동이 생길 수 있다
        stateMachine.Movement?.StopMoving();
    }
}
