using UnityEngine;
using System.Collections.Generic;
using CoreDawn.Navigation;

namespace CoreDawn.Entities
{
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

            // 계산은 워커에서 돈다 — 답은 다음 프레임 이후에 온다.
            // 그 사이 상태가 갈렸으면(추적 포기·공격 전환) 남의 경로를 들이밀지 않는다.
            var sm = stateMachine;
            PathRequest.Find(sm.Transform.position, target.GetPosition(), false, path =>
            {
                if (!IsStillCurrent(sm)) return;

                if (path != null)
                {
                    if (path.Count > 0)
                    {
                        sm.Movement?.StartMoving(path);
                    }
                    else
                    {
                        // 시작 셀과 목표(보정) 셀이 같으면 빈 경로가 온다 — 막힘이 아니라 이미 도착.
                        // 사거리 진입 여부는 Update의 거리 판정에 맡긴다
                        sm.Movement?.StopMoving();
                    }
                    return;
                }

                HandleFullyBlocked(sm);
            });
        }

        /// <summary>콜백이 도착했을 때 아직 이 상태가 돌고 있는가 — 늦게 온 답을 거르는 유일한 관문.</summary>
        private bool IsStillCurrent(StateMachineComponent sm)
        {
            return owner == sm && sm != null && ReferenceEquals(sm.CurrentState, this)
                && target.IsValidTarget();
        }

        /// <summary>
        /// 길이 완전히 막혔다 → 경로를 막는 건물을 새 타겟으로 삼아 부순다.
        /// 심(POCO) 건물을 뷰 GameObject의 엔티티로 바꿔 타겟으로 쓴다.
        /// </summary>
        private void HandleFullyBlocked(StateMachineComponent sm)
        {
            PathRequest.FindBlockingBuilding(sm.Transform.position, target.GetPosition(), blocker =>
            {
                if (!IsStillCurrent(sm)) return;

                var buildingEntity = BuildingEntity.GetOrAttach(blocker);
                if (buildingEntity != null && !ReferenceEquals(buildingEntity, target))
                {
                    sm.SetState(new ChaseState(buildingEntity));
                    return;
                }

                // 부술 건물조차 없으면(지형 막힘 등) Chase에 머물며 pathUpdateInterval 주기로 재시도.
                // Idle로 보내면 Idle이 다음 프레임 바로 플로우필드로 전환해 상태 진동이 생길 수 있다
                sm.Movement?.StopMoving();
            });
        }
    }
}
