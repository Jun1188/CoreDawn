using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>런타임 A* 추적 — 플레이어 센서에 감지된 몬스터만 쓰는 무거운 길찾기. 추적 포기는 OnLost가 담당한다.</summary>
    public sealed class ChaseState : IEntityState
    {
        readonly Entity target;
        const float PathUpdateInterval = 0.5f;
        float lastPathUpdateTime;
        MonsterBrainModule owner;

        public Entity Target => target;

        public ChaseState(Entity target) => this.target = target;

        public void Enter(MonsterBrainModule b)
        {
            owner = b;
            if (b.Movement != null) b.Movement.OnPathBlocked += HandlePathBlocked;
            UpdatePath(b);
        }

        public void Update(MonsterBrainModule b, float dt)
        {
            if (!MonsterBrainModule.IsValidTarget(target))
            {
                b.SetState(new IdleState());
                return;
            }

            float distance = MonsterBrainModule.DistanceTo(target, b.Owner.Position);
            if (b.Attack != null && distance <= b.Attack.Range)
            {
                b.SetState(new AttackState(target));
                return;
            }

            if (b.Now >= lastPathUpdateTime + PathUpdateInterval) UpdatePath(b);
        }

        public void Exit(MonsterBrainModule b)
        {
            if (b.Movement != null)
            {
                b.Movement.OnPathBlocked -= HandlePathBlocked;
                b.Movement.StopMoving();
            }
        }

        void HandlePathBlocked()
        {
            if (owner != null) UpdatePath(owner);
        }

        void UpdatePath(MonsterBrainModule b)
        {
            if (!MonsterBrainModule.IsValidTarget(target) || b.Nav == null) return;

            lastPathUpdateTime = b.Now;

            // 계산은 워커에서 돈다 — 답은 다음 프레임 이후에 온다. 그 사이 상태가 갈렸으면 남의 경로를 들이밀지 않는다.
            b.Nav.FindPath(b.Owner.Position, target.Position, false, path =>
            {
                if (!IsStillCurrent(b)) return;

                if (path != null)
                {
                    if (path.Count > 0) b.Movement?.StartMoving(path);
                    else b.Movement?.StopMoving();   // 시작 셀과 목표 셀이 같으면 빈 경로 — 이미 도착. 사거리 진입은 Update의 거리 판정에
                    return;
                }

                HandleFullyBlocked(b);
            });
        }

        /// <summary>콜백이 도착했을 때 아직 이 상태가 돌고 있는가 — 늦게 온 답을 거르는 유일한 관문.</summary>
        bool IsStillCurrent(MonsterBrainModule b)
            => owner == b && b != null && b.Owner.IsAlive && ReferenceEquals(b.CurrentState, this) && MonsterBrainModule.IsValidTarget(target);

        /// <summary>길이 완전히 막혔다 → 경로를 막는 건물을 새 타겟으로 삼아 부순다.</summary>
        void HandleFullyBlocked(MonsterBrainModule b)
        {
            b.Nav.FindBlockingBuilding(b.Owner.Position, target.Position, blocker =>
            {
                if (!IsStillCurrent(b)) return;

                if (MonsterBrainModule.IsValidTarget(blocker) && !ReferenceEquals(blocker, target))
                {
                    b.SetState(new ChaseState(blocker));
                    return;
                }

                // 부술 건물조차 없으면(지형 막힘 등) Chase에 머물며 주기로 재시도 — Idle로 보내면 상태 진동이 생긴다
                b.Movement?.StopMoving();
            });
        }
    }
}
