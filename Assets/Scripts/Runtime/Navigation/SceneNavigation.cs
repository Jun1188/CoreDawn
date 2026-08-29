using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Navigation
{
    /// <summary>
    /// 씬의 길찾기(GridManager·FlowFieldManager·PathRequest·GroundSampler)를 심 계약(<see cref="INavigation"/>)으로 감싼다.
    ///
    /// 심 모듈(이동·두뇌)은 이것만 보고, 싱글턴은 여기서만 만진다. 격자·플로우필드가 심 내부로 들어오는 5단계에
    /// 이 클래스는 사라지고 구현만 바뀐다. 상태가 없으므로 인스턴스는 하나로 충분하다.
    /// </summary>
    public sealed class SceneNavigation : INavigation
    {
        public bool IsReady => GridManager.Instance != null && GridManager.Instance.Costs.IsReady;

        public bool IsWalkable(Vector3 world)
        {
            var grid = GridManager.Instance;
            if (grid == null) return true;   // 격자 없는 씬(테스트)은 어디든 걷는다 — 구 MovementComponent와 같은 규약
            var node = grid.NodeFromWorldPoint(world);
            return node != null && grid.IsWalkable(node);
        }

        public float TerrainSpeedAt(Vector3 world)
        {
            var grid = GridManager.Instance;
            if (grid == null) return 1f;
            var node = grid.NodeFromWorldPoint(world);
            return node != null ? grid.TerrainSpeed(node.gridCoord) : 1f;
        }

        public float GroundHeightAt(Vector3 world) => GroundSampler.HeightAt(world);

        public bool HasFlowField => FlowFieldManager.Instance != null && FlowFieldManager.Instance.HasField;

        public Vector3 FlowDirectionAt(Vector3 world)
            => FlowFieldManager.Instance != null ? FlowFieldManager.Instance.GetDirection(world) : Vector3.zero;

        public Entity FindBreachTarget(Vector3 from, float range)
        {
            var ff = FlowFieldManager.Instance;
            var building = ff != null ? ff.FindBreachTarget(from, range) : null;
            return building?.Owner;
        }

        public void FindPath(Vector3 from, Vector3 to, bool ignoreBuildings, Action<List<Vector3>> onDone)
        {
            if (onDone == null) return;
            PathRequest.Find(from, to, ignoreBuildings, nodes =>
            {
                if (nodes == null) { onDone(null); return; }
                var points = new List<Vector3>(nodes.Count);
                foreach (var n in nodes) points.Add(n.worldPosition);
                onDone(points);
            });
        }

        public void FindBlockingBuilding(Vector3 from, Vector3 to, Action<Entity> onDone)
        {
            if (onDone == null) return;
            PathRequest.FindBlockingBuilding(from, to, b => onDone(b?.Owner));
        }
    }
}
