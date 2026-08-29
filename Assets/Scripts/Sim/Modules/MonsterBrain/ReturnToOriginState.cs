using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>원위치 복귀. 일회성 방어 몬스터는 도착 후 소멸하고, 보스는 비선공 대기로 남는다.</summary>
    public sealed class ReturnToOriginState : IEntityState
    {
        readonly Vector3 origin;
        readonly bool despawnOnArrival;

        public ReturnToOriginState(Vector3 origin, bool despawnOnArrival = true)
        {
            this.origin = origin;
            this.despawnOnArrival = despawnOnArrival;
        }

        public void Enter(MonsterBrainModule b)
        {
            // 경로가 올 때까지는 둥지 쪽으로 곧장 걷는다 — 워커의 답을 기다리며 멈춰 서 있으면 복귀가 한 박자 늦어 보인다.
            b.Movement?.SetDirection((origin - b.Owner.Position).normalized);

            b.Nav?.FindPath(b.Owner.Position, origin, false, path =>
            {
                if (b == null || !b.Owner.IsAlive || !ReferenceEquals(b.CurrentState, this)) return;
                if (path != null && path.Count > 0) b.Movement?.StartMoving(path);
            });
        }

        public void Update(MonsterBrainModule b, float dt)
        {
            Vector3 pos = b.Owner.Position;
            float dist = Vector3.Distance(pos, origin);

            // 경로가 없거나 끝났는데 아직 집이 아니면, 남은 거리는 격자 판정을 거치지 않고 곧장 좁힌다.
            // 격자 경로의 마지막 노드는 칸 중심이라 자리와 몇 m 어긋나고, 자리가 걸을 수 없는 칸 위면 플로우필드 이동이
            // 진입을 거부한다 — 그대로 두면 도착 판정(1.5m)에 영영 못 닿아 무적 샌드백이 된다. 복귀만큼은 반드시 끝나야 한다.
            if (dist >= 1.5f && (b.Movement == null || !b.Movement.HasPath))
            {
                b.Movement?.StopMoving();

                float speed = b.Movement != null ? b.Movement.MoveSpeed : 3f;
                Vector3 target = new Vector3(origin.x, pos.y, origin.z);
                b.Owner.Position = Vector3.MoveTowards(pos, target, speed * dt);
                b.Movement?.FaceImmediately(target - pos);

                dist = Vector3.Distance(b.Owner.Position, origin);
            }

            if (dist < 1.5f)
            {
                b.Movement?.StopMoving();
                if (despawnOnArrival)
                {
                    b.System.Despawn(b.Owner);
                }
                else
                {
                    // 도착 — 복귀 재생의 마지막 한 뼘을 채운다(롤 캠프 리셋과 같은 마무리).
                    b.OnReturnedHome();
                    b.SetState(new DefenderIdleState());
                }
            }
        }

        public void Exit(MonsterBrainModule b) => b.Movement?.StopMoving();
    }
}
