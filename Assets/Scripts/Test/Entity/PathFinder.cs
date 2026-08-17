using System.Collections.Generic;
using UnityEngine;

public static class PathFinder
{
    // A* 알고리즘을 수행하고 결과 경로(List<Node>)를 반환
    // ignoreBuildings: true면 정적 장애물만 검사 (FindBlockingBuilding의 이상 경로 계산용)
    public static List<Node> FindPath(Vector3 startPos, Vector3 targetPos, bool ignoreBuildings = false)
    {
        if (GridManager.Instance == null)
        {
            Debug.LogWarning("GridManager.Instance is null. Please ensure a GridManager exists in the scene.");
            return null;
        }

        Node startNode = GridManager.Instance.NodeFromWorldPoint(startPos);
        Node targetNode = GridManager.Instance.NodeFromWorldPoint(targetPos);
        if (startNode == null || targetNode == null) return null;

        // 목표가 건물/장애물 위라면(예: 건물 자체가 공격 대상) 주변의 가장 가까운 walkable 노드로 보정
        if (!GridManager.Instance.IsWalkable(targetNode, ignoreBuildings))
        {
            targetNode = FindNearestWalkable(targetNode, ignoreBuildings);
            if (targetNode == null) return null;
        }

        Dictionary<Node, pathNode> pathNodes = new Dictionary<Node, pathNode>();
        pathNode startPathNode = new pathNode(startNode) { gCost = 0 };
        pathNodes.Add(startNode, startPathNode);
        List<pathNode> openSet = new List<pathNode>();
        HashSet<Node> closedSet = new HashSet<Node>();
        openSet.Add(startPathNode);
        while (openSet.Count > 0)
        {
            pathNode currentPathNode = openSet[0];
            for (int i = 1; i < openSet.Count; i++)
            {
                if (openSet[i].FCost < currentPathNode.FCost ||
                   (openSet[i].FCost == currentPathNode.FCost && openSet[i].hCost < currentPathNode.hCost))
                {
                    currentPathNode = openSet[i];
                }
            }
            openSet.Remove(currentPathNode);
            closedSet.Add(currentPathNode.targetNode);
            // 목적지에 도착하면 경로를 역추적하여 리스트로 반환
            if (currentPathNode.targetNode == targetNode)
            {
                return RetracePath(startPathNode, currentPathNode);
            }
            foreach (Node neighbour in GridManager.Instance.GetNeighbours(currentPathNode.targetNode))
            {
                if (!GridManager.Instance.IsWalkable(neighbour, ignoreBuildings) || closedSet.Contains(neighbour)) continue;

                // 대각선 이동은 양 옆 직교 셀이 모두 열려 있어야 허용 (건물 모서리 끼임 방지)
                Vector2Int d = neighbour.gridCoord - currentPathNode.targetNode.gridCoord;
                if (d.x != 0 && d.y != 0)
                {
                    if (!GridManager.Instance.IsWalkable(currentPathNode.targetNode.gridCoord + new Vector2Int(d.x, 0), ignoreBuildings)) continue;
                    if (!GridManager.Instance.IsWalkable(currentPathNode.targetNode.gridCoord + new Vector2Int(0, d.y), ignoreBuildings)) continue;
                }

                if (!pathNodes.TryGetValue(neighbour, out pathNode neighbourPathNode))
                {
                    neighbourPathNode = new pathNode(neighbour);
                    pathNodes.Add(neighbour, neighbourPathNode);
                }
                // 이동 비용에 지형을 싣는다 — 거리만 재면 추적이 강을 첨벙 건너간다.
                // 위 IsWalkable과 같은 파라미터를 넘겨 판정과 비용의 기준을 일치시킨다.
                int enter = GridManager.Instance.EnterCost(neighbour.gridCoord, ignoreBuildings);
                if (enter >= TileRules.Blocked) continue;
                int stepCost = GetDistance(currentPathNode.targetNode, neighbour) * enter / TileRules.BaseCost;
                int newCostToNeighbour = currentPathNode.gCost + stepCost;
                if (newCostToNeighbour < neighbourPathNode.gCost || !openSet.Contains(neighbourPathNode))
                {
                    neighbourPathNode.gCost = newCostToNeighbour;
                    neighbourPathNode.hCost = GetDistance(neighbour, targetNode);
                    neighbourPathNode.parent = currentPathNode;
                    if (!openSet.Contains(neighbourPathNode))
                    {
                        openSet.Add(neighbourPathNode);
                    }
                }
            }
        }

        // 길을 찾지 못한 경우
        return null;
    }

    // 길이 완전히 막혔을 때, 건물을 무시한 이상적 경로를 구해
    // 그 경로상에서 처음 만나는 건물(부숴야 할 대상)을 반환.
    // 점유 조회는 심의 GridIndex(FactoryBootstrap.Sim.Grid)를 사용한다.
    public static Building FindBlockingBuilding(Vector3 startPos, Vector3 targetPos)
    {
        var boot = FactoryBootstrap.Instance;
        if (boot == null || boot.Sim == null) return null;

        List<Node> idealPath = FindPath(startPos, targetPos, ignoreBuildings: true);
        if (idealPath == null) return null; // 지형 자체가 막힘 → 부숴도 소용없음

        foreach (Node node in idealPath)
        {
            Building blocker = boot.Sim.Grid.GetAt(node.gridCoord);
            if (blocker != null && !blocker.IsRemoved) return blocker;
        }
        return null;
    }

    // 중심 노드 주변을 링 단위로 넓혀가며 가장 가까운 walkable 노드를 찾는다
    private static Node FindNearestWalkable(Node center, bool ignoreBuildings, int maxRadius = 3)
    {
        for (int r = 1; r <= maxRadius; r++)
        {
            Node best = null;
            float bestDist = float.MaxValue;
            for (int x = -r; x <= r; x++)
            {
                for (int y = -r; y <= r; y++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != r) continue; // 링 테두리만 검사
                    Vector2Int cell = center.gridCoord + new Vector2Int(x, y);
                    if (!GridManager.Instance.IsWalkable(cell, ignoreBuildings)) continue;

                    float dist = new Vector2(x, y).sqrMagnitude;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = GridManager.Instance.GetNode(cell);
                    }
                }
            }
            if (best != null) return best;
        }
        return null;
    }

    // 경로 역추적 메서드
    private static List<Node> RetracePath(pathNode startNode, pathNode endNode)
    {
        List<Node> path = new List<Node>();
        pathNode currentNode = endNode;
        while (currentNode != startNode)
        {
            path.Add(currentNode.targetNode);
            currentNode = currentNode.parent;
        }
        path.Reverse();
        return path;
    }
    // 거리 계산 메서드
    private static int GetDistance(Node nodeA, Node nodeB)
    {
        int dstX = Mathf.Abs(nodeA.gridCoord.x - nodeB.gridCoord.x);
        int dstY = Mathf.Abs(nodeA.gridCoord.y - nodeB.gridCoord.y);
        if (dstX > dstY) return 14 * dstY + 10 * (dstX - dstY);
        return 14 * dstX + 10 * (dstY - dstX);
    }
}
