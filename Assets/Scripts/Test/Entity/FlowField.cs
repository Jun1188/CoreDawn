using System.Collections.Generic;
using UnityEngine;

// 플로우필드(벡터 필드) — 순수 C# 클래스.
// 목표(코어/타워) 셀들을 시드로 한 다중 시작점 다익스트라로 통합 비용 필드를 만들고,
// 각 셀이 비용이 가장 낮아지는 이웃(목표 방향)을 가리키게 한다.
// 비용 모델(직교 10/대각 14, 모서리 끼임 방지)은 기존 A*(PathFinder)와 동일하다.
//
// 런타임 A*는 몬스터 수만큼 반복 계산되지만, 플로우필드는 1회 계산으로
// 모든 몬스터가 자기 셀의 방향만 샘플링하면 되므로 대량 웨이브에 적합하다.
public class FlowField
{
    private int[,] integration;   // 각 셀 → 목표까지의 누적 비용
    private Vector2Int[,] next;   // 각 셀이 향할 다음 셀 (목표 방향)
    private bool[,] hasNext;
    private Vector2Int size;

    public bool HasField { get; private set; }

    public struct Goal
    {
        public Vector2Int cell;
        public int seedCost; // 코어(0)를 타워(양수)보다 우선하도록 시드 비용으로 가중치를 준다

        public Goal(Vector2Int cell, int seedCost)
        {
            this.cell = cell;
            this.seedCost = seedCost;
        }
    }

    public void Clear() => HasField = false;

    public void Rebuild(GridManager grid, List<Goal> goals)
    {
        if (grid == null || goals == null || goals.Count == 0)
        {
            HasField = false;
            return;
        }

        size = grid.gridSize;
        if (integration == null || integration.GetLength(0) != size.x || integration.GetLength(1) != size.y)
        {
            integration = new int[size.x, size.y];
            next = new Vector2Int[size.x, size.y];
            hasNext = new bool[size.x, size.y];
        }

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                integration[x, y] = int.MaxValue;
                hasNext[x, y] = false;
            }
        }

        // 목표 셀(건물 자리라 unwalkable이어도 시드는 허용)에서 바깥으로 다익스트라 확장
        var open = new MinHeap(64 + goals.Count);
        foreach (var goal in goals)
        {
            if (!InBounds(goal.cell)) continue;
            if (goal.seedCost < integration[goal.cell.x, goal.cell.y])
            {
                integration[goal.cell.x, goal.cell.y] = goal.seedCost;
                open.Push(goal.cell, goal.seedCost);
            }
        }

        while (open.Count > 0)
        {
            open.Pop(out Vector2Int cell, out int cost);
            if (cost > integration[cell.x, cell.y]) continue; // 더 싼 값으로 갱신된 낡은 항목

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Vector2Int n = new Vector2Int(cell.x + dx, cell.y + dy);
                    if (!InBounds(n)) continue;

                    // 진격 비용: 지면 10 · 강 30 · 건물 +HP비례 · 절벽 ∞.
                    // passBuildings=true — 건물은 "비싼 길"이라 경로가 사라지지 않는다.
                    // 목표가 아닌 건물(벨트)로 막아도 몬스터가 굳지 않고 가장 얇은 곳을 뚫는다.
                    int enter = grid.EnterCost(n, passBuildings: true);
                    if (enter >= TileRules.Blocked) continue;

                    // 대각은 양 옆이 물리적으로 열려 있어야 한다 — 절벽이든 건물이든 모서리는 못 스친다.
                    // 여기만 IsWalkable을 쓰는 이유: 직교로는 건물을 뚫고 갈 수 있지만(비용),
                    // 대각으로 두 건물 사이 틈을 공짜로 빠져나가는 것은 물리와 맞지 않는다.
                    if (dx != 0 && dy != 0)
                    {
                        if (!grid.IsWalkable(new Vector2Int(cell.x + dx, cell.y))) continue;
                        if (!grid.IsWalkable(new Vector2Int(cell.x, cell.y + dy))) continue;
                    }

                    // 대각은 √2배 — 정수 유지를 위해 14/10으로 근사한다
                    int step = (dx != 0 && dy != 0) ? enter * 14 / 10 : enter;
                    int newCost = cost + step;
                    if (newCost >= integration[n.x, n.y]) continue;

                    integration[n.x, n.y] = newCost;
                    next[n.x, n.y] = cell; // 확장해 온 방향의 반대 = 목표로 가는 방향
                    hasNext[n.x, n.y] = true;
                    open.Push(n, newCost);
                }
            }
        }

        HasField = true;
    }

    // 해당 셀에서 목표 쪽 다음 셀. 목표 셀 자체이거나 도달 불가능한 셀이면 false.
    public bool TryGetNext(Vector2Int cell, out Vector2Int nextCell)
    {
        nextCell = default;
        if (!HasField || !InBounds(cell) || !hasNext[cell.x, cell.y]) return false;
        nextCell = next[cell.x, cell.y];
        return true;
    }

    /// <summary>
    /// 이 칸에서 목표까지의 누적 비용. 도달할 수 없는 칸(막힘·필드 밖)이면 false.
    /// 읽기 전용 — 시각화·검증이 "어디가 얼마나 먼가"를 물을 때 쓴다.
    /// </summary>
    public bool TryGetCost(Vector2Int cell, out int cost)
    {
        cost = 0;
        if (!HasField || !InBounds(cell)) return false;

        int value = integration[cell.x, cell.y];
        if (value == int.MaxValue) return false;

        cost = value;
        return true;
    }

    private bool InBounds(Vector2Int c) => c.x >= 0 && c.x < size.x && c.y >= 0 && c.y < size.y;

    // 다익스트라용 간단한 이진 최소 힙
    private class MinHeap
    {
        private Vector2Int[] cells;
        private int[] costs;
        public int Count { get; private set; }

        public MinHeap(int capacity)
        {
            if (capacity < 16) capacity = 16;
            cells = new Vector2Int[capacity];
            costs = new int[capacity];
        }

        public void Push(Vector2Int cell, int cost)
        {
            if (Count == cells.Length)
            {
                System.Array.Resize(ref cells, Count * 2);
                System.Array.Resize(ref costs, Count * 2);
            }
            cells[Count] = cell;
            costs[Count] = cost;
            int i = Count++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (costs[parent] <= costs[i]) break;
                Swap(i, parent);
                i = parent;
            }
        }

        public void Pop(out Vector2Int cell, out int cost)
        {
            cell = cells[0];
            cost = costs[0];
            Count--;
            cells[0] = cells[Count];
            costs[0] = costs[Count];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, smallest = i;
                if (l < Count && costs[l] < costs[smallest]) smallest = l;
                if (r < Count && costs[r] < costs[smallest]) smallest = r;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            (cells[a], cells[b]) = (cells[b], cells[a]);
            (costs[a], costs[b]) = (costs[b], costs[a]);
        }
    }
}
