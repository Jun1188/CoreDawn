using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Worlds;

namespace CoreDawn.Navigation
{
    /// <summary>
    /// A* 길찾기 — 셀 인덱스와 배열만 쓰는 순수 계산이라 <b>워커 스레드에서 돈다</b>.
    ///
    /// 예전 구현은 노드마다 <c>new</c>를 하고(pathNode·이웃 List·Dictionary·HashSet), 열린 목록을
    /// 선형 스캔했다. 격자를 484²로 세분화한 뒤로는 그 비용이 그대로 드러나 몬스터 12마리가
    /// 프레임당 5만 번을 할당하며 100ms를 먹었다. 지금은 이렇게 바꿨다:
    ///   - 비용·통행 판정은 <see cref="CostField"/>(공유 배열) — GridManager를 매 칸 부르지 않는다
    ///   - 열린 목록은 이진 힙, 방문 표시는 세대(generation) 도장 — 매 탐색마다 배열을 지우지 않는다
    ///   - 작업 배열은 인스턴스가 재사용한다 — 탐색 한 번의 할당은 결과 경로뿐이다
    ///
    /// 인스턴스 하나는 한 번에 한 탐색만 한다(작업 배열을 공유하므로). 동시 탐색이 필요하면
    /// 인스턴스를 나눠 쓴다 — <see cref="PathRequestQueue"/>가 그 규칙을 지킨다.
    /// </summary>
    public class PathFinder
    {
        // 작업 배열 — 격자 크기가 바뀔 때만 다시 잡는다
        int[] gCost;
        int[] parent;
        int[] stamp;        // 이 셀을 언제 건드렸나 (generation 도장)
        bool[] closed;
        int generation;
        Vector2Int size;

        MinHeap open = new MinHeap(256);

        /// <summary>
        /// 시작 칸에서 목표 칸까지의 경로(칸 목록). 못 찾으면 null, 이미 도착이면 빈 목록.
        /// <paramref name="ignoreBuildings"/>가 true면 건물을 없는 셈 치고 이상 경로를 구한다
        /// (막혔을 때 "무엇이 막았나"를 찾기 위한 용도).
        /// </summary>
        public List<Vector2Int> FindPath(CostField costs, Vector2Int start, Vector2Int goal,
                                         bool ignoreBuildings = false)
        {
            if (costs == null || !costs.IsReady) return null;
            if (!costs.InBounds(start) || !costs.InBounds(goal)) return null;

            EnsureBuffers(costs.Size);

            // 목표가 건물·장애물 위면(건물 자체가 공격 대상) 주변의 설 수 있는 칸으로 보정
            if (!Passable(costs, goal, ignoreBuildings))
            {
                if (!TryFindNearestPassable(costs, goal, ignoreBuildings, out goal)) return null;
            }

            if (start == goal) return new List<Vector2Int>();

            generation++;
            open.Clear();

            int startIndex = costs.Index(start);
            int goalIndex = costs.Index(goal);

            Touch(startIndex);
            gCost[startIndex] = 0;
            parent[startIndex] = -1;
            open.Push(startIndex, Heuristic(start, goal));

            int width = size.x;

            while (open.Count > 0)
            {
                open.Pop(out int current, out _);
                if (closed[current] && stamp[current] == generation) continue;

                Touch(current);
                closed[current] = true;

                if (current == goalIndex) return Retrace(current, startIndex);

                int cx = current % width;
                int cy = current / width;

                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx;
                    if (nx < 0 || nx >= width) continue;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int ny = cy + dy;
                        if (ny < 0 || ny >= size.y) continue;

                        int ni = ny * width + nx;
                        if (closed[ni] && stamp[ni] == generation) continue;

                        int enter = costs.EnterCost[ni];
                        if (!PassableAt(costs, ni, enter, ignoreBuildings)) continue;

                        // 대각선은 양 옆 직교 칸이 모두 열려 있어야 허용 (건물·절벽 모서리 끼임 방지)
                        if (dx != 0 && dy != 0)
                        {
                            if (!PassableAt(costs, cy * width + nx, costs.EnterCost[cy * width + nx], ignoreBuildings)) continue;
                            if (!PassableAt(costs, ny * width + cx, costs.EnterCost[ny * width + cx], ignoreBuildings)) continue;
                        }

                        // 이동 비용에 지형을 싣는다 — 거리만 재면 추적이 강을 첨벙 건너간다
                        int step = (dx != 0 && dy != 0) ? 14 : 10;
                        int tentative = gCost[current] + step * enter / TileRules.BaseCost;

                        bool seen = stamp[ni] == generation;
                        if (seen && tentative >= gCost[ni]) continue;

                        Touch(ni);
                        gCost[ni] = tentative;
                        parent[ni] = current;
                        open.Push(ni, tentative + Heuristic(new Vector2Int(nx, ny), goal));
                    }
                }
            }

            return null;   // 길이 완전히 막힘
        }

        /// <summary>이 칸을 이번 탐색에서 처음 만졌으면 초기화한다 — 매번 배열 전체를 지우지 않으려는 장치.</summary>
        void Touch(int index)
        {
            if (stamp[index] == generation) return;
            stamp[index] = generation;
            gCost[index] = int.MaxValue;
            parent[index] = -1;
            closed[index] = false;
        }

        void EnsureBuffers(Vector2Int fieldSize)
        {
            int count = fieldSize.x * fieldSize.y;
            if (gCost == null || gCost.Length != count)
            {
                gCost = new int[count];
                parent = new int[count];
                stamp = new int[count];
                closed = new bool[count];
                generation = 0;
            }
            size = fieldSize;
        }

        static bool Passable(CostField costs, Vector2Int cell, bool ignoreBuildings)
        {
            if (!costs.InBounds(cell)) return false;
            int i = costs.Index(cell);
            return PassableAt(costs, i, costs.EnterCost[i], ignoreBuildings);
        }

        /// <summary>
        /// 걸어서 지날 수 있는 칸인가.
        /// 평소에는 건물을 막힌 것으로 본다(실제로 걸어갈 수 있는 길이어야 하므로).
        /// ignoreBuildings면 지형만 본다 — 건물을 없앤다면 어디로 갔을지를 묻는 것이다.
        /// </summary>
        static bool PassableAt(CostField costs, int index, int enter, bool ignoreBuildings)
        {
            if (enter >= TileRules.Blocked) return false;
            return ignoreBuildings || costs.Walkable[index];
        }

        bool TryFindNearestPassable(CostField costs, Vector2Int center, bool ignoreBuildings,
                                    out Vector2Int found, int maxRadius = 3)
        {
            found = center;

            for (int r = 1; r <= maxRadius; r++)
            {
                int bestDist = int.MaxValue;
                bool any = false;

                for (int x = -r; x <= r; x++)
                {
                    for (int y = -r; y <= r; y++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != r) continue; // 링 테두리만 검사

                        var cell = new Vector2Int(center.x + x, center.y + y);
                        if (!Passable(costs, cell, ignoreBuildings)) continue;

                        int dist = x * x + y * y;
                        if (dist < bestDist) { bestDist = dist; found = cell; any = true; }
                    }
                }
                if (any) return true;
            }
            return false;
        }

        List<Vector2Int> Retrace(int goalIndex, int startIndex)
        {
            var path = new List<Vector2Int>();
            int width = size.x;

            for (int at = goalIndex; at != startIndex && at >= 0; at = parent[at])
                path.Add(new Vector2Int(at % width, at / width));

            path.Reverse();
            return path;
        }

        static int Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            return dx > dy ? 14 * dy + 10 * (dx - dy) : 14 * dx + 10 * (dy - dx);
        }

        // 이진 최소 힙 — 플로우필드와 같은 구조(셀 인덱스 + 비용)
        class MinHeap
        {
            int[] cells;
            int[] costs;
            int count;

            public MinHeap(int capacity)
            {
                capacity = Mathf.Max(16, capacity);
                cells = new int[capacity];
                costs = new int[capacity];
            }

            public int Count => count;
            public void Clear() => count = 0;

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

            void Swap(int a, int b)
            {
                (cells[a], cells[b]) = (cells[b], cells[a]);
                (costs[a], costs[b]) = (costs[b], costs[a]);
            }
        }
    }
}
