using System.Collections.Generic;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Pings;
using CoreDawn.Placement;
using CoreDawn.Data;

namespace CoreDawn.ResourceNodes
{
    /// <summary>
    /// 씬의 모든 광맥을 셀 단위로 색인하고, 그 정보를 세 소비자에게 공급한다:
    ///   ① FactorySystem.GetResourceAt        — 채굴기가 무엇을 캘지 (없으면 채굴 안 함)
    ///   ② FactorySystem.TryExtractResourceAt — 채굴 1회당 광맥 재고 차감 (없으면 대기)
    ///   ③ CanPlace(...)                   — 채굴기를 여기 지어도 되는지 (배치 UI/시스템용)
    /// </summary>
    public static class ResourceNodeRegistry
    {
        static readonly Dictionary<Vector2Int, ResourceNode> _byCell = new();
        static readonly List<ResourceNode> _nodes = new();
        static readonly Queue<Vector2Int> _rejects = new();
        static readonly GridSystem _fallbackGrid = new(1f, Vector3.zero);

        static GridSystem _grid;
        static FactorySystem _hookedSim;
        static ResourceNodeRuntime _runtime;

        /// <summary>
        /// 광맥 밖에 놓인 채굴기를 자동으로 철거할지. PlacementSystem 쪽에서
        /// CanPlace로 미리 막게 되면 꺼도 된다 (시나리오 테스트에서도 끌 수 있음).
        /// </summary>
        public static bool EnforceMinerPlacement = true;

        public static IReadOnlyList<ResourceNode> Nodes => _nodes;

        /// <summary>생산에 쓰는 시계 — 심이 있으면 심 시간(일시정지/배속 반영), 없으면 실시간.</summary>
        public static float Now
        {
            get
            {
                var boot = FactoryBootstrap.Instance;
                return boot != null && boot.Factory != null ? boot.Factory.Now : Time.time;
            }
        }

        // 도메인 리로드를 끈 에디터에서도 정적 상태가 새 플레이 세션으로 새지 않게.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _byCell.Clear();
            _nodes.Clear();
            _rejects.Clear();
            _grid = null;
            _hookedSim = null;
            _runtime = null;
            EnforceMinerPlacement = true;
        }

        // ── 좌표계 (심/PlacementSystem과 동일)

        /// <summary>PlacementSystem의 그리드 설정. 없으면 1칸=1유닛, 원점 0의 기본값.</summary>
        public static GridSystem Grid
        {
            get
            {
                if (_grid != null) return _grid;
                var placement = Object.FindFirstObjectByType<PlacementSystem>();
                _grid = placement != null
                    ? new GridSystem(placement.CellSize, placement.GridOrigin)
                    : _fallbackGrid;
                return _grid;
            }
        }

        /// <summary>PlacementSystem의 cellSize/gridOrigin이 바뀌었을 때 캐시를 버린다.</summary>
        public static void InvalidateGrid() => _grid = null;

        /// <summary>풋프린트 중앙 월드 좌표 → 왼쪽 아래 셀. (첫 셀 중앙을 찍어 경계 오차를 피한다)</summary>
        public static Vector2Int CellOf(Vector3 footprintCenter, Vector2Int size)
        {
            var grid = Grid;
            Vector3 firstCellCenter = footprintCenter
                - new Vector3(size.x - 1, 0f, size.y - 1) * 0.5f * grid.CellSize;
            return grid.WorldToGrid(firstCellCenter);
        }

        // ── 등록/해제 (ResourceNode.OnEnable/OnDisable)

        public static void Register(ResourceNode node)
        {
            if (node == null || _nodes.Contains(node)) return;

            _nodes.Add(node);
            node.ClaimedCells.Clear();

            foreach (var cell in node.Cells)
            {
                if (_byCell.TryGetValue(cell, out var other) && other != null && other != node)
                    Debug.LogWarning($"[ResourceNode] 셀 {cell}에서 '{other.name}'과 겹칩니다 — " +
                                     $"'{node.name}'이 덮어씁니다.", node);
                _byCell[cell] = node;
                node.ClaimedCells.Add(cell);
            }

            if (node.Resource == null)
                Debug.LogWarning($"[ResourceNode] '{node.name}'에 자원 아이템이 비어 있습니다. " +
                                 $"이 위의 채굴기는 아무것도 생산하지 않습니다.", node);

            EnsureRuntime();
            EnsureSimHook();
        }

        public static void Unregister(ResourceNode node)
        {
            if (node == null) return;
            _nodes.Remove(node);

            // 등록 시점의 셀로 지운다 — 그 사이 오브젝트가 움직였어도 정확히 해제된다
            foreach (var cell in node.ClaimedCells)
                if (_byCell.TryGetValue(cell, out var owner) && owner == node)
                    _byCell.Remove(cell);

            node.ClaimedCells.Clear();
        }

        // ── 조회

        public static ResourceNode NodeAt(Vector2Int cell)
            => _byCell.TryGetValue(cell, out var n) && n != null ? n : null;

        /// <summary>해당 칸에 묻힌 자원. 광맥이 없으면 null.</summary>
        public static ItemDataSO ResourceAt(Vector2Int cell) => NodeAt(cell)?.Resource;

        public static bool HasResourceAt(Vector2Int cell) => ResourceAt(cell) != null;

        // ── 생산 구동 (러너가 매 프레임 호출)

        internal static void TickProduction()
        {
            if (_nodes.Count > 0) EnsureSimHook();   // 늦게 생긴 심 연결 + 덮어쓰기 복구
            TickProduction(Now);
        }

        /// <summary>지정한 시각 기준으로 모든 광맥의 생산을 정산한다 (테스트·헤드리스 구동용).</summary>
        public static void TickProduction(float now)
        {
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                var node = _nodes[i];
                if (node == null) { _nodes.RemoveAt(i); continue; }
                node.Accrue(now);
            }
        }

        // ── 배치 규칙 (PlacementSystem / 배치 UI가 호출할 표면)

        /// <summary>이 건물을 이 자리에 지어도 되는가. 채굴기가 아닌 건물은 항상 true.</summary>
        public static bool CanPlace(BuildingDataSO so, Vector2Int origin, Vector2Int size)
            => CanPlace(so, origin, size, out _);

        /// <param name="reason">막힌 이유(한국어). 통과하면 null.</param>
        public static bool CanPlace(BuildingDataSO so, Vector2Int origin, Vector2Int size, out string reason)
        {
            reason = null;

            // 채굴기 — 반드시 광맥 위여야 한다 (그리고 한 광맥 안에 들어와야 한다)
            if (so is MinerDataSO) return CanPlaceMiner(origin, size, out _, out reason);

            // 그 외 건물 — 광맥을 덮으면 안 된다.
            // 광맥은 채굴기 자리다. 저장고·벨트 따위로 덮어버리면 그 광맥을 영영 못 쓰게 된다.
            int w = Mathf.Max(1, size.x), h = Mathf.Max(1, size.y);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    Vector2Int cell = origin + new Vector2Int(x, y);
                    if (NodeAt(cell) == null) continue;

                    reason = $"광맥 위에는 채굴기만 지을 수 있습니다 (셀 {cell}).";
                    return false;
                }

            return true;
        }

        /// <summary>
        /// 채굴기 배치 판정 — 풋프린트의 모든 칸이 같은 자원의 광맥 위여야 한다.
        /// </summary>
        public static bool CanPlaceMiner(Vector2Int origin, Vector2Int size,
                                         out ItemDataSO resource, out string reason)
        {
            resource = null;
            reason   = null;

            int w = Mathf.Max(1, size.x), h = Mathf.Max(1, size.y);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    Vector2Int cell = origin + new Vector2Int(x, y);
                    var item = ResourceAt(cell);

                    if (item == null)
                    {
                        reason = $"광맥 위가 아닙니다 (셀 {cell}).";
                        return false;
                    }
                    if (resource == null) resource = item;
                    else if (resource != item)
                    {
                        reason = $"서로 다른 광맥({resource.displayName} / {item.displayName})에 걸쳐 있습니다.";
                        return false;
                    }
                }

            return true;
        }

        // ── 심 주입 — 채굴기는 이 두 함수를 통해서만 광맥을 만난다

        // 소켓에 꽂는 델리게이트는 한 번만 만들어 둔다 — 매 프레임 동일성 비교에 쓰기 위함
        static readonly System.Func<Vector2Int, ItemDataSO> _resolveHook  = ResolveMinerTarget;
        static readonly System.Func<Vector2Int, int, int>   _extractHook  = ExtractForMiner;
        static readonly System.Func<Vector2Int, float>      _intervalHook = ExtractIntervalAt;

        /// <summary>이 칸을 덮는 광맥의 채굴 기준 시간. 광맥이 없으면 1초(기존 동작).</summary>
        static float ExtractIntervalAt(Vector2Int cell)
            => _byCell.TryGetValue(cell, out var node) && node != null ? node.ExtractInterval : 1f;

        /// <summary>
        /// 씬의 심(FactoryBootstrap)에 자원 소켓을 연결한다. 멱등.
        /// 다른 컴포넌트(FactoryTest의 "전 좌표 철광석" 같은 디버그 오버라이드)가 소켓을 덮어썼으면
        /// 되찾아온다 — 씬에 광맥이 있으면 광맥이 기준이 되어야 실행 순서에 흔들리지 않는다.
        /// </summary>
        public static void EnsureSimHook()
        {
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null) return;

            var sim = boot.Factory;
            if (ReferenceEquals(_hookedSim, sim) &&
                ReferenceEquals(sim.GetResourceAt, _resolveHook) &&
                ReferenceEquals(sim.TryExtractResourceAt, _extractHook) &&
                ReferenceEquals(sim.GetExtractIntervalAt, _intervalHook)) return;

            HookSim(sim);
            _hookedSim = sim;
        }

        /// <summary>임의의 심에 자원 소켓 두 개를 연결한다 (헤드리스 테스트/보조 심용).</summary>
        public static void HookSim(FactorySystem sim)
        {
            if (sim == null) return;
            sim.GetResourceAt        = _resolveHook;
            sim.TryExtractResourceAt = _extractHook;
            sim.GetExtractIntervalAt = _intervalHook;
        }

        /// <summary>
        /// 심이 채굴기 배치 직후(OnAfterPlaced) 한 번 호출하는 서비스.
        /// 광맥이 없으면 null을 돌려 채굴을 막고, 그 채굴기를 철거 대기열에 올린다.
        /// </summary>
        static ItemDataSO ResolveMinerTarget(Vector2Int origin)
        {
            var item = ResourceAt(origin);
            if (item == null && EnforceMinerPlacement) QueueReject(origin);
            return item;
        }

        /// <summary>
        /// 채굴 1회분을 광맥 재고에서 꺼낸다. 반환값 = 실제로 꺼낸 개수(0이면 재고 없음 → 채굴기 대기).
        /// 심은 ResourceNode를 몰라도 되게 (셀, 개수) → 개수 형태로만 노출한다.
        /// </summary>
        static int ExtractForMiner(Vector2Int cell, int amount)
        {
            var node = NodeAt(cell);
            return node != null ? node.Extract(amount) : 0;
        }

        // ── 잘못 놓인 채굴기 되돌리기
        //    배치 도중(FactorySystem.Place 내부)에 철거하면 뷰가 아직 등록되기 전이라
        //    GameObject가 고아로 남는다. 그래서 프레임 끝(LateUpdate)까지 미룬다.

        static void QueueReject(Vector2Int cell)
        {
            _rejects.Enqueue(cell);
            EnsureRuntime();
        }

        static void EnsureRuntime()
        {
            if (_runtime != null || !Application.isPlaying) return;
            var go = new GameObject("~ResourceNodeRuntime") { hideFlags = HideFlags.DontSave };
            _runtime = go.AddComponent<ResourceNodeRuntime>();
        }

        internal static void ProcessRejections()
        {
            while (_rejects.Count > 0)
            {
                Vector2Int cell = _rejects.Dequeue();

                var boot = FactoryBootstrap.Instance;
                if (boot == null || boot.Factory == null) continue;

                var b = boot.Factory.Grid.GetAt(cell);
                if (b == null || b.IsRemoved || b.Data is not MinerDataSO) continue;
                if (ResourceAt(b.Origin) != null) continue;   // 그 사이 광맥이 생겼으면 통과

                Debug.LogWarning($"[ResourceNode] '{b.Data.displayName}'는 광맥 위에만 설치할 수 있습니다 " +
                                 $"(셀 {cell}). 설치를 취소했습니다.");
                PlacementBridge.Remove(b);
            }
        }
    }
}
