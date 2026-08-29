using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Sim;
using CoreDawn.Factory;

namespace CoreDawn.Factory
{
    public interface IBuildingBehavior
    {
        /// <summary>
        /// FactorySim이 이 건물이 깨어 있는 틱에 호출.
        /// (MarkDirty로 등록됐거나 ScheduleWake 예약 시각이 됐을 때)
        /// </summary>
        void Tick(float dt);

        /// <summary>
        /// BuildingGraph.OnPlaced() 완료 후 1회 호출.
        /// 이 시점에서는 InputConnections / OutputConnections가 모두 확정되어 있다.
        /// 자원 조회, 레시피 결정 등 연결 기반 초기화에 사용.
        /// </summary>
        void OnAfterPlaced();
    }
}
