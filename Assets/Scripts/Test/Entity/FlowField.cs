using System.Collections.Generic;
using UnityEngine;

// 플로우필드(벡터 필드) — 순수 C# 클래스.
// 목표(코어/타워) 셀들을 시드로 한 다중 시작점 다익스트라로 통합 비용 필드를 만들고,
// 각 셀이 비용이 가장 낮아지는 이웃(목표 방향)을 가리키게 한다.
// 비용 모델(직교 10/대각 14, 모서리 끼임 방지)은 기존 A*(PathFinder)와 동일하다.
//
// 런타임 A*는 몬스터 수만큼 반복 계산되지만, 플로우필드는 1회 계산으로
// 모든 몬스터가 자기 셀의 방향만 샘플링하면 되므로 대량 웨이브에 적합하다.
//
// <b>워커 스레드에서 돈다</b> — 그래서 Unity API를 일절 부르지 않는다. 지형·건물 비용은
// 메인 스레드가 미리 떠 온 배열(CostSnapshot)로 받고, 여기서는 배열 산술만 한다.
// 자료구조도 2차원이 아니라 1차원 배열이다: 인덱스 계산이 곧 접근이라 캐시에 유리하다.
public class FlowField
{
    /// <summary>
    /// 메인 스레드가 떠 주는 지형·건물 비용. 워커는 이것만 보고 계산한다.
    /// <see cref="GridManager.CaptureCostSnapshot"/>이 채운다.
    /// </summary>
    public class CostSnapshot
    {
        public Vector2Int Size;
        public int[] EnterCost;   // 진격 비용 (건물 통과 허용) — Blocked 이상이면 못 감
        public bool[] Walkable;   // 보행 가능 (건물은 막힘) — 대각 모서리 판정용

        public void Resize(Vector2Int size)
        {
            int count = size.x * size.y;
            if (EnterCost == null || EnterCost.Length != count)
            {
                EnterCost = new int[count];
                Walkable = new bool[count];
            }
            Size = size;
        }
    }

    private int[] integration;    // 각 셀 → 목표까지의 누적 비용
    private int[] next;           // 각 셀이 향할 다음 셀 (인덱스, -1이면 없음)
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

    /// <summary>
    /// 필드를 다시 계산한다. <b>워커 스레드에서 호출해도 안전하다</b> — Unity API를 부르지 않는다.
    /// 계산이 끝날 때까지 이 인스턴스를 읽는 쪽이 없어야 한다(FlowFieldManager가 더블 버퍼로 보장).
    /// </summary>
    public void Rebuild(CostSnapshot snapshot, List<Goal> goals)
    {
        if (snapshot == null || snapshot.EnterCost == null || goals == null || goals.Count == 0)
        {
            HasField = false;
            return;
        }

        size = snapshot.Size;
        int count = size.x * size.y;

        if (integration == null || integration.Length != count)
        {
            integration = new int[count];
            next = new int[count];
        }

        for (int i = 0; i < count; i++)
        {
            integration[i] = int.MaxValue;
            next[i] = -1;
        }

        // 목표 셀(건물 자리라 unwalkable이어도 시드는 허용)에서 바깥으로 다익스트라 확장
        var open = new MinHeap(64 + goals.Count);
        foreach (var goal in goals)
        {
            if (!InBounds(goal.cell)) continue;

            int gi = Index(goal.cell);
            if (goal.seedCost < integration[gi])
            {
                integration[gi] = goal.seedCost;
                open.Push(gi, goal.seedCost);
            }
        }

        var enterCost = snapshot.EnterCost;
        var walkable = snapshot.Walkable;

        while (open.Count > 0)
        {
            open.Pop(out int index, out int cost);
            if (cost > integration[index]) continue; // 더 싼 값으로 갱신된 낡은 항목

            int cx = index % size.x;
            int cy = index / size.x;

            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx;
                if (nx < 0 || nx >= size.x) continue;

                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int ny = cy + dy;
                    if (ny < 0 || ny >= size.y) continue;

                    int ni = ny * size.x + nx;

                    // 진격 비용: 지면 10 · 강 30 · 건물 +HP비례 · 절벽 ∞.
                    // 건물은 "비싼 길"이라 경로가 사라지지 않는다 — 목표가 아닌 건물(벨트)로
                    // 막아도 몬스터가 굳지 않고 가장 얇은 곳을 뚫는다.
                    int enter = enterCost[ni];
                    if (enter >= TileRules.Blocked) continue;

                    // 대각은 양 옆이 물리적으로 열려 있어야 한다 — 절벽이든 건물이든 모서리는 못 스친다.
                    // 직교로는 건물을 뚫고 갈 수 있지만(비용), 대각으로 두 건물 사이 틈을
                    // 공짜로 빠져나가는 것은 물리와 맞지 않는다.
                    if (dx != 0 && dy != 0)
                    {
                        if (!walkable[cy * size.x + nx]) continue;
                        if (!walkable[ny * size.x + cx]) continue;
                    }

                    // 대각은 √2배 — 정수 유지를 위해 14/10으로 근사한다
                    int step = (dx != 0 && dy != 0) ? enter * 14 / 10 : enter;
                    int newCost = cost + step;
                    if (newCost >= integration[ni]) continue;

                    integration[ni] = newCost;
                    next[ni] = index; // 확장해 온 방향의 반대 = 목표로 가는 방향
                    open.Push(ni, newCost);
                }
            }
        }

        HasField = true;
    }

    // 해당 셀에서 목표 쪽 다음 셀. 목표 셀 자체이거나 도달 불가능한 셀이면 false.
    public bool TryGetNext(Vector2Int cell, out Vector2Int nextCell)
    {
        nextCell = default;
        if (!HasField || !InBounds(cell)) return false;

        int n = next[Index(cell)];
        if (n < 0) return false;

        nextCell = new Vector2Int(n % size.x, n / size.x);
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

        int value = integration[Index(cell)];
        if (value == int.MaxValue) return false;

        cost = value;
        return true;
    }

    private int Index(Vector2Int c) => c.y * size.x + c.x;

    private bool InBounds(Vector2Int c) => c.x >= 0 && c.x < size.x && c.y >= 0 && c.y < size.y;

    // 다익스트라용 간단한 이진 최소 힙 — 셀을 1차원 인덱스로 다룬다
    private class MinHeap
    {
        private int[] cells;
        private int[] costs;
        private int count;

        public MinHeap(int capacity)
        {
            capacity = Mathf.Max(16, capacity);
            cells = new int[capacity];
            costs = new int[capacity];
        }

        public int Count => count;

        public void Push(int cell, int cost)
        {
            if (count == cells.Length)
            {
                System.Array.Resize(ref cells, count * 2);
                System.Array.Resize(ref costs, count * 2);
            }

            int i = count++;
            cells[i] = cell;
            costs[i] = cost;

            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (costs[parent] <= costs[i]) break;
                Swap(parent, i);
                i = parent;
            }
        }

        public void Pop(out int cell, out int cost)
        {
            cell = cells[0];
            cost = costs[0];

            count--;
            cells[0] = cells[count];
            costs[0] = costs[count];

            int i = 0;
            while (true)
            {
                int left = i * 2 + 1, right = left + 1, smallest = i;
                if (left < count && costs[left] < costs[smallest]) smallest = left;
                if (right < count && costs[right] < costs[smallest]) smallest = right;
                if (smallest == i) break;
                Swap(smallest, i);
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
