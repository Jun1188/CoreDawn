using System.Collections.Generic;
using UnityEngine;

// ================================================================
//  ResourceNode.cs
//  광맥 — "이 칸 아래에 이 자원이 묻혀 있고, 일정 주기로 재고가 쌓인다"
//
//  포함:
//    ResourceNode         — 씬에 놓는 광맥 하나 (자원 + 풋프린트 + 생산/재고)
//    ResourceNodeRegistry — 셀 → 광맥 O(1) 조회, FactorySim 훅 주입, 배치 판정
//    ResourceNodeRuntime  — 생산 구동 + 잘못 놓인 채굴기 철거를 도는 내부 러너
//
//  기존 시스템과의 접점은 FactorySim의 두 델리게이트뿐이다:
//    ① GetResourceAt        — 채굴기가 무엇을 캘지 (없으면 null → 채굴 안 함)
//    ② TryExtractResourceAt — 채굴 1회마다 광맥 재고를 실제로 꺼내감 (없으면 대기)
//  ②가 아직 심에 없는 빌드에서도 ①만으로 동작한다(재고 무시, 무한 채굴).
//
//  시간축: 생산은 FactorySim.Now(심 클럭)를 따른다 — 일시정지·배속이 공장과 같이 맞는다.
//  심이 없는 씬에서는 Time.time으로 자동 폴백한다.
//
//  좌표계: 심/PlacementSystem과 동일 (cellSize / gridOrigin).
//  씬에 ResourceNode가 하나도 없으면 주입 자체를 하지 않으므로,
//  기존 씬(FactoryTest 등)의 동작은 그대로다.
// ================================================================

/// <summary>
/// 광맥 한 덩어리. 오브젝트 위치가 풋프린트의 중앙, size가 차지하는 타일 수다.
/// productionInterval마다 amountPerCycle개씩 내부 재고에 쌓고, 채굴기가
/// <see cref="TryExtract"/>로 그 재고를 꺼내간다 (= 생산 속도가 채굴 속도의 상한).
/// </summary>
[DisallowMultipleComponent]
public class ResourceNode : MonoBehaviour
{
    [Header("자원")]
    [Tooltip("이 광맥에서 채굴되는 아이템. 채굴기가 이 아이템을 그대로 생산한다.")]
    [SerializeField] private ItemDataSO resource;

    [Header("크기 — 타일 단위")]
    [Tooltip("광맥이 덮는 칸 수 (가로, 세로). 오브젝트 위치가 풋프린트 중앙이다.")]
    [SerializeField] private Vector2Int size = Vector2Int.one;

    [Header("생산")]
    [Tooltip("몇 초마다 생산하는가. 심(FactorySim)의 시간 기준이라 일시정지·배속을 따른다.")]
    [SerializeField] private float productionInterval = 1f;
    [Tooltip("한 주기에 몇 개를 생산하는가.")]
    [SerializeField] private int amountPerCycle = 1;
    [Tooltip("쌓아둘 수 있는 재고 상한. 가득 차면 생산이 멈추고, 꺼내가면 재개한다.")]
    [SerializeField] private int maxStock = 20;
    [Tooltip("플레이 시작 시 미리 쌓여 있는 재고.")]
    [SerializeField] private int initialStock = 0;

    [Header("상태 (읽기 전용 — 플레이 중 관찰용)")]
    [Tooltip("현재 쌓여 있는 재고. 인스펙터에서 실시간으로 늘어나는 것을 볼 수 있다.")]
    [SerializeField] private int currentStock;

    [Header("에디터")]
    [Tooltip("켜면 인스펙터 수정 시 오브젝트를 셀 중앙에 자동 정렬한다.")]
    [SerializeField] private bool snapToGrid = true;
    [SerializeField] private Color gizmoColor = new Color(1f, 0.75f, 0.1f, 0.35f);

    /// <summary>다음 생산 시각(심 클럭). 음수면 아직 초기화 전.</summary>
    private float nextProduceAt = -1f;

    // ── 공개 조회 ────────────────────────────────────────────────

    /// <summary>이 광맥에서 나오는 아이템. 비어 있으면 채굴 불가 광맥으로 취급한다.</summary>
    public ItemDataSO Resource => resource;

    /// <summary>현재 재고.</summary>
    public int CurrentStock => currentStock;

    /// <summary>재고 상한.</summary>
    public int MaxStock => Mathf.Max(1, maxStock);

    /// <summary>재고가 가득 차 생산이 멈춘 상태인가.</summary>
    public bool IsFull => currentStock >= MaxStock;

    /// <summary>한 주기의 길이(초). 0 이하 입력은 방어적으로 클램프한다.</summary>
    public float ProductionInterval => Mathf.Max(0.01f, productionInterval);

    /// <summary>풋프린트 크기(타일). 항상 1 이상.</summary>
    public Vector2Int Size => new(Mathf.Max(1, size.x), Mathf.Max(1, size.y));

    /// <summary>풋프린트의 왼쪽 아래 셀 — 현재 transform 위치에서 매번 계산한다.</summary>
    public Vector2Int Origin => ResourceNodeRegistry.CellOf(transform.position, Size);

    /// <summary>레지스트리에 실제로 등록해 둔 셀 목록 (이동 후에도 정확히 해제하기 위한 원본).</summary>
    internal readonly List<Vector2Int> ClaimedCells = new();

    /// <summary>풋프린트가 덮는 셀 열거 (현재 위치 기준).</summary>
    public IEnumerable<Vector2Int> Cells
    {
        get
        {
            Vector2Int o = Origin, s = Size;
            for (int x = 0; x < s.x; x++)
                for (int y = 0; y < s.y; y++)
                    yield return o + new Vector2Int(x, y);
        }
    }

    public bool Covers(Vector2Int cell)
    {
        Vector2Int d = cell - Origin, s = Size;
        return d.x >= 0 && d.y >= 0 && d.x < s.x && d.y < s.y;
    }

    // ── 생산 ────────────────────────────────────────────────────

    /// <summary>
    /// 심 클럭 now 기준으로 밀린 생산 주기를 정산한다 (러너가 매 프레임 호출).
    /// Update가 아니라 now를 받는 이유: 심의 시간축(일시정지/배속)을 그대로 따르기 위함.
    /// </summary>
    internal void Accrue(float now)
    {
        if (resource == null) return;

        float interval = ProductionInterval;

        // 첫 정산이거나 클럭이 갈아끼워졌을 때(심 생성 전 Time.time → 심의 Now) 재동기화
        if (nextProduceAt < 0f || now < nextProduceAt - interval)
        {
            nextProduceAt = now + interval;
            return;
        }

        int step = Mathf.Max(1, amountPerCycle);
        int guard = 0;   // 프레임 스파이크로 수천 주기가 밀려도 한 프레임을 잡아먹지 않게

        while (now >= nextProduceAt && guard++ < 256)
        {
            if (currentStock >= MaxStock)
            {
                // 재고가 꽉 참 → 생산 정지. 꺼내가면 다음 주기부터 다시 쌓인다.
                nextProduceAt = now + interval;
                return;
            }

            currentStock = Mathf.Min(currentStock + step, MaxStock);
            nextProduceAt += interval;
        }

        if (guard >= 256) nextProduceAt = now + interval;   // 밀린 빚은 버린다
    }

    /// <summary>
    /// 재고를 꺼내간다 (채굴기의 창구).
    /// 요청량보다 재고가 적으면 있는 만큼만 준다. 재고가 0이면 false.
    /// </summary>
    public bool TryExtract(int amount, out int taken)
    {
        taken = Mathf.Min(Mathf.Max(0, amount), currentStock);
        if (taken <= 0) { taken = 0; return false; }

        currentStock   -= taken;
        TotalExtracted += taken;
        return true;
    }

    /// <summary>
    /// 이 광맥에서 지금까지 실제로 캐 간 총량. 재고는 생산이 채굴보다 빠르면 늘 상한에 붙어 있어
    /// "도는지 멈췄는지"를 구분해주지 못한다 — 채굴 진행 여부는 이 값의 증가로 판단한다.
    /// </summary>
    public int TotalExtracted { get; private set; }

    /// <summary>재고를 꺼내가고 꺼낸 개수만 돌려주는 간편형 (0 = 재고 없음).</summary>
    public int Extract(int amount) => TryExtract(amount, out int taken) ? taken : 0;

    // ── 생명주기 ─────────────────────────────────────────────────

    void OnEnable()
    {
        currentStock  = Mathf.Clamp(initialStock, 0, MaxStock);
        nextProduceAt = -1f;                       // 첫 Accrue에서 현재 클럭으로 맞춘다
        ResourceNodeRegistry.Register(this);
    }

    void OnDisable() => ResourceNodeRegistry.Unregister(this);

    // FactoryBootstrap보다 먼저 OnEnable이 돌았을 경우를 대비한 재시도 (주입은 멱등).
    void Start() => ResourceNodeRegistry.EnsureSimHook();

    /// <summary>런타임에 광맥을 옮기거나 크기를 바꿨을 때 점유 셀을 다시 등록한다.</summary>
    public void Refresh()
    {
        ResourceNodeRegistry.Unregister(this);
        ResourceNodeRegistry.Register(this);
    }

    // ── 에디터 편의 ──────────────────────────────────────────────

    /// <summary>오브젝트를 현재 풋프린트의 셀 중앙으로 정렬한다 (높이는 유지).</summary>
    [ContextMenu("그리드에 정렬")]
    public void AlignToGrid()
    {
        Vector3 p = ResourceNodeRegistry.Grid.GetFootprintCenter(Origin, Size);
        p.y = transform.position.y;
        if ((transform.position - p).sqrMagnitude > 1e-6f) transform.position = p;
    }

    void OnValidate()
    {
        size.x             = Mathf.Max(1, size.x);
        size.y             = Mathf.Max(1, size.y);
        productionInterval = Mathf.Max(0.01f, productionInterval);
        amountPerCycle     = Mathf.Max(1, amountPerCycle);
        maxStock           = Mathf.Max(1, maxStock);
        initialStock       = Mathf.Clamp(initialStock, 0, maxStock);

        if (Application.isPlaying || !gameObject.scene.IsValid()) return;

        currentStock = initialStock;             // 플레이 전에는 초기 재고를 그대로 보여준다
        ResourceNodeRegistry.InvalidateGrid();   // PlacementSystem 설정이 바뀌었을 수 있다
        if (snapToGrid) AlignToGrid();
    }

    void OnDrawGizmos()
    {
        var grid = ResourceNodeRegistry.Grid;
        Vector2Int o = Origin, s = Size;

        // 자원이 비어 있는 광맥은 빨갛게 — 인스펙터 연결 누락을 씬에서 바로 보이게
        Color fill = resource != null ? gizmoColor : new Color(1f, 0.2f, 0.2f, 0.35f);
        Vector3 cube = new Vector3(grid.CellSize * 0.96f, 0.02f, grid.CellSize * 0.96f);

        for (int x = 0; x < s.x; x++)
            for (int y = 0; y < s.y; y++)
            {
                Vector3 c = grid.GridToWorldCenter(o + new Vector2Int(x, y));
                c.y = transform.position.y + 0.02f;

                Gizmos.color = fill;
                Gizmos.DrawCube(c, cube);
                Gizmos.color = new Color(fill.r, fill.g, fill.b, 1f);
                Gizmos.DrawWireCube(c, cube);
            }
    }
}

// ─── 레지스트리 ────────────────────────────────────────────────

/// <summary>
/// 씬의 모든 광맥을 셀 단위로 색인하고, 그 정보를 세 소비자에게 공급한다:
///   ① FactorySim.GetResourceAt        — 채굴기가 무엇을 캘지 (없으면 채굴 안 함)
///   ② FactorySim.TryExtractResourceAt — 채굴 1회당 광맥 재고 차감 (없으면 대기)
///   ③ CanPlace(...)                   — 채굴기를 여기 지어도 되는지 (배치 UI/시스템용)
/// </summary>
public static class ResourceNodeRegistry
{
    static readonly Dictionary<Vector2Int, ResourceNode> _byCell = new();
    static readonly List<ResourceNode> _nodes = new();
    static readonly Queue<Vector2Int> _rejects = new();
    static readonly GridSystem _fallbackGrid = new(1f, Vector3.zero);

    static GridSystem _grid;
    static FactorySim _hookedSim;
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
            return boot != null && boot.Sim != null ? boot.Sim.Now : Time.time;
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
    static readonly System.Func<Vector2Int, ItemDataSO> _resolveHook = ResolveMinerTarget;
    static readonly System.Func<Vector2Int, int, int>   _extractHook = ExtractForMiner;

    /// <summary>
    /// 씬의 심(FactoryBootstrap)에 자원 소켓을 연결한다. 멱등.
    /// 다른 컴포넌트(FactoryTest의 "전 좌표 철광석" 같은 디버그 오버라이드)가 소켓을 덮어썼으면
    /// 되찾아온다 — 씬에 광맥이 있으면 광맥이 기준이 되어야 실행 순서에 흔들리지 않는다.
    /// </summary>
    public static void EnsureSimHook()
    {
        var boot = FactoryBootstrap.Instance;
        if (boot == null || boot.Sim == null) return;

        var sim = boot.Sim;
        if (ReferenceEquals(_hookedSim, sim) &&
            ReferenceEquals(sim.GetResourceAt, _resolveHook) &&
            ReferenceEquals(sim.TryExtractResourceAt, _extractHook)) return;

        HookSim(sim);
        _hookedSim = sim;
    }

    /// <summary>임의의 심에 자원 소켓 두 개를 연결한다 (헤드리스 테스트/보조 심용).</summary>
    public static void HookSim(FactorySim sim)
    {
        if (sim == null) return;
        sim.GetResourceAt        = _resolveHook;
        sim.TryExtractResourceAt = _extractHook;
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
    //    배치 도중(FactorySim.Place 내부)에 철거하면 뷰가 아직 등록되기 전이라
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
            if (boot == null || boot.Sim == null) continue;

            var b = boot.Sim.Grid.GetAt(cell);
            if (b == null || b.IsRemoved || b.Data is not MinerDataSO) continue;
            if (ResourceAt(b.Origin) != null) continue;   // 그 사이 광맥이 생겼으면 통과

            Debug.LogWarning($"[ResourceNode] '{b.Data.displayName}'는 광맥 위에만 설치할 수 있습니다 " +
                             $"(셀 {cell}). 설치를 취소했습니다.");
            PlacementBridge.Remove(b);
        }
    }
}

/// <summary>광맥 생산 구동 + 철거 대기열 처리 전용 러너. 레지스트리가 직접 만든다(씬 배선 없음).</summary>
[AddComponentMenu("")]
internal class ResourceNodeRuntime : MonoBehaviour
{
    void Update()     => ResourceNodeRegistry.TickProduction();
    void LateUpdate() => ResourceNodeRegistry.ProcessRejections();
}
