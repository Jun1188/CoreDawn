using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 길찾기 요청의 창구 — 호출부는 월드 좌표로 묻고 <see cref="Node"/> 목록으로 받는다.
///
/// 계산은 워커에서 돌므로 답은 <b>다음 프레임 이후</b>에 온다. 그래서 콜백을 받는데,
/// 콜백이 도착할 즈음엔 요청한 쪽이 이미 다른 상태일 수 있다 — 그 확인은 호출부의 몫이다.
/// (셀↔월드 변환과 Node 조회는 Unity 데이터라 여기, 즉 메인 스레드에서 한다.)
/// </summary>
public static class PathRequest
{
    static PathRequestQueue Queue
    {
        get
        {
            if (PathRequestQueue.Instance == null)
            {
                // 씬 배선을 요구하지 않는다 — 필요한 순간에 스스로 선다(길찾기가 없는 씬엔 서지도 않는다)
                new GameObject(nameof(PathRequestQueue)).AddComponent<PathRequestQueue>();
            }
            return PathRequestQueue.Instance;
        }
    }

    /// <summary>
    /// 경로를 청한다. 결과는 메인 스레드 콜백으로 — 못 찾으면 null, 이미 도착이면 빈 목록.
    /// 격자가 아직 없으면 그 자리에서 null을 돌려준다(동기 시절과 같은 실패 규약).
    /// </summary>
    public static void Find(Vector3 startPos, Vector3 targetPos, bool ignoreBuildings,
                            Action<List<Node>> onDone)
    {
        if (onDone == null) return;

        var grid = GridManager.Instance;
        if (grid == null || !grid.Costs.IsReady) { onDone(null); return; }

        Node startNode = grid.NodeFromWorldPoint(startPos);
        Node goalNode = grid.NodeFromWorldPoint(targetPos);
        if (startNode == null || goalNode == null) { onDone(null); return; }

        Queue.Request(startNode.gridCoord, goalNode.gridCoord, ignoreBuildings,
                      cells => onDone(ToNodes(grid, cells)));
    }

    /// <summary>
    /// 길이 완전히 막혔을 때, 건물을 없는 셈 친 이상 경로 위에서 처음 만나는 건물을 찾는다.
    /// "무엇을 부수면 길이 열리는가"에 대한 답이다. 없으면 null(지형이 막은 것이라 부숴도 소용없다).
    /// </summary>
    public static void FindBlockingBuilding(Vector3 startPos, Vector3 targetPos, Action<Building> onDone)
    {
        if (onDone == null) return;

        Find(startPos, targetPos, ignoreBuildings: true, path =>
        {
            var boot = FactoryBootstrap.Instance;
            if (path == null || boot == null || boot.Sim == null) { onDone(null); return; }

            foreach (var node in path)
            {
                var blocker = boot.Sim.Grid.GetAt(node.gridCoord);
                if (blocker != null && !blocker.IsRemoved) { onDone(blocker); return; }
            }
            onDone(null);
        });
    }

    static List<Node> ToNodes(GridManager grid, List<Vector2Int> cells)
    {
        if (cells == null) return null;

        var nodes = new List<Node>(cells.Count);
        foreach (var cell in cells)
        {
            var node = grid.GetNode(cell);
            if (node != null) nodes.Add(node);
        }
        return nodes;
    }
}
