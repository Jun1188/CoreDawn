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
using CoreDawn.Sound;

namespace CoreDawn.ResourceNodes
{
    // ================================================================
    //  ResourceNode.cs
    //  광맥 — "이 칸 아래에 이 자원이 묻혀 있고, 일정 주기로 재고가 쌓인다"
    //
    //  포함:
    //    ResourceNode         — 씬에 놓는 광맥 하나 (자원 + 풋프린트 + 생산/재고)
    //    ResourceNodeRegistry — 셀 → 광맥 O(1) 조회, FactorySystem 훅 주입, 배치 판정
    //    ResourceNodeRuntime  — 생산 구동 + 잘못 놓인 채굴기 철거를 도는 내부 러너
    //
    //  공장(FactorySystem)과의 접점은 두 델리게이트뿐이다:
    //    ① GetResourceAt        — 채굴기가 무엇을 캘지 (없으면 null → 채굴 안 함)
    //    ② TryExtractResourceAt — 채굴 1회마다 광맥 재고를 실제로 꺼내감 (없으면 대기)
    //  ②가 아직 심에 없는 빌드에서도 ①만으로 동작한다(재고 무시, 무한 채굴).
    //
    //  플레이어와의 접점은 하나다: ResourceNode가 IHoldInteractable이라 E를 누르고 있으면
    //  손으로 캘 수 있다(느리다). 채굴기와 같은 재고에서 꺼내므로 규칙이 갈리지 않는다.
    //
    //  시간축: 생산은 FactorySystem.Now(심 클럭)를 따른다 — 일시정지·배속이 공장과 같이 맞는다.
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
    public class ResourceNode : MonoBehaviour, IHoldInteractable, IPingable
    {
        // ── 핑 대상 (IPingable) — 광맥은 Entity도 Interactable도 아니라 직접 구현한다
        public string PingLabel =>
            resource != null ? (string.IsNullOrEmpty(resource.displayName) ? resource.name : resource.displayName) : name;
        public GameObject PingRoot => gameObject;
        public bool CanBePinged => isActiveAndEnabled;

        [Header("자원")]
        [Tooltip("이 광맥에서 채굴되는 아이템. 채굴기가 이 아이템을 그대로 생산한다.")]
        [SerializeField] private ItemDataSO resource;

        [Header("크기 — 타일 단위")]
        [Tooltip("광맥이 덮는 칸 수 (가로, 세로). 오브젝트 위치가 풋프린트 중앙이다.")]
        [SerializeField] private Vector2Int size = Vector2Int.one;

        [Header("채굴")]
        [Tooltip("이 광맥에서 1개를 캐는 기준 시간(초). 배율 1인 채굴기 기준.\n" +
                 "재고 생성(productionInterval)과는 별개 축이다 — 이건 채굴 1회에 걸리는 시간이고, " +
                 "저건 재고가 차오르는 속도다. 둘 다 있으면 \"빨리 캘 순 있지만 재고가 금방 바닥나는 광맥\"도 표현된다.")]
        [SerializeField] private float extractInterval = 1f;

        [Header("생산")]
        [Tooltip("몇 초마다 생산하는가. 심(FactorySystem)의 시간 기준이라 일시정지·배속을 따른다.")]
        [SerializeField] private float productionInterval = 1f;
        [Tooltip("한 주기에 몇 개를 생산하는가.")]
        [SerializeField] private int amountPerCycle = 1;
        [Tooltip("쌓아둘 수 있는 재고 상한. 가득 차면 생산이 멈추고, 꺼내가면 재개한다.")]
        [SerializeField] private int maxStock = 20;
        [Tooltip("플레이 시작 시 미리 쌓여 있는 재고.")]
        [SerializeField] private int initialStock = 0;

        [Header("손 채굴 (E 홀드)")]
        [Tooltip("끄면 이 광맥은 채굴기로만 캘 수 있다 (조준해도 프롬프트가 뜨지 않는다).")]
        [SerializeField] private bool allowManualMining = true;
        [Tooltip("손으로 1회 캐는 데 걸리는 시간(초). 채굴기(extractInterval)보다 느리게 두는 것이 기본 의도다 —\n" +
                 "손 채굴은 채굴기를 지을 재료를 마련하는 수단이지, 채굴기를 대체하는 수단이 아니다.")]
        [SerializeField] private float manualExtractSeconds = 3f;
        [Tooltip("손으로 1회 캘 때 나오는 개수.")]
        [SerializeField] private int manualYield = 1;

        [Header("상태 (읽기 전용 — 플레이 중 관찰용)")]
        [Tooltip("현재 쌓여 있는 재고. 인스펙터에서 실시간으로 늘어나는 것을 볼 수 있다.")]
        [SerializeField] private int currentStock;

        [Header("에디터")]
        [Tooltip("켜면 인스펙터 수정 시 오브젝트를 셀 중앙에 자동 정렬한다.")]
        [SerializeField] private bool snapToGrid = true;
        [SerializeField] private Color gizmoColor = new Color(1f, 0.75f, 0.1f, 0.35f);

        /// <summary>다음 생산 시각(심 클럭). 음수면 아직 초기화 전.</summary>
        private float nextProduceAt = -1f;

        /// <summary>
        /// 맵 데이터로 세울 때 쓴다. 인스펙터 전용(private) 필드라 밖에서 못 넣으므로 통로를 낸다 —
        /// 광맥은 씬에 손으로 놓는 것이 아니라 맵이 정하는 것이기 때문이다.
        /// 0 이하는 "프리팹 값을 그대로 둔다"는 뜻이다.
        /// </summary>
        public void Configure(ItemDataSO item, int footprint, float extractSeconds, int stockCap)
        {
            if (item != null) resource = item;
            if (footprint > 0) size = new Vector2Int(footprint, footprint);
            if (extractSeconds > 0f) extractInterval = extractSeconds;
            if (stockCap > 0) maxStock = stockCap;
        }

        // ── 공개 조회 ────────────────────────────────────────────────

        /// <summary>이 광맥에서 나오는 아이템. 비어 있으면 채굴 불가 광맥으로 취급한다.</summary>
        public ItemDataSO Resource => resource;

        /// <summary>현재 재고.</summary>
        public int CurrentStock => currentStock;

        /// <summary>다음 생산 시각(심 클럭 기준 절대값). 세이브가 그대로 보존해야 주기가 어긋나지 않는다.</summary>
        public float NextProduceAt => nextProduceAt;

        /// <summary>재고 상한.</summary>
        public int MaxStock => Mathf.Max(1, maxStock);

        /// <summary>재고가 가득 차 생산이 멈춘 상태인가.</summary>
        public bool IsFull => currentStock >= MaxStock;

        /// <summary>한 주기의 길이(초). 0 이하 입력은 방어적으로 클램프한다.</summary>
        public float ProductionInterval => Mathf.Max(0.01f, productionInterval);

        /// <summary>1개를 캐는 기준 시간(초) — 배율 1인 채굴기 기준.</summary>
        public float ExtractInterval => Mathf.Max(0.01f, extractInterval);

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

        /// <summary>
        /// 세이브 복원 전용 — 재고와 다음 생산 시각, 누적 채굴량을 저장된 값으로 되돌린다.
        ///
        /// nextProduceAt은 심 클럭 기준 절대 시각이라 FactorySystem.Now를 되돌린 뒤에 넣어야 한다.
        /// (OnEnable이 -1로 리셋해 두므로, 복원하지 않으면 첫 Accrue에서 주기가 통째로 미뤄진다)
        /// </summary>
        public void RestoreState(int stock, float nextAt, int totalExtracted)
        {
            currentStock = Mathf.Clamp(stock, 0, MaxStock);
            nextProduceAt = nextAt;
            TotalExtracted = Mathf.Max(0, totalExtracted);
        }

        // ── 손 채굴 (IHoldInteractable) ───────────────────────────────
        //
        //  채굴기와 같은 재고에서 꺼낸다 — 광맥에 묻힌 양은 하나뿐이라는 규칙을 손이라고 비켜갈 수 없다.
        //  덕분에 "채굴기가 붙어 있는 광맥은 손으로 캘 것이 남지 않는다"가 저절로 성립한다.
        //  다른 것은 속도뿐이다: manualExtractSeconds가 채굴기의 extractInterval보다 느리다.

        /// <summary>손으로 1회 캐는 데 걸리는 시간(초).</summary>
        public float ManualExtractSeconds => Mathf.Max(0.1f, manualExtractSeconds);

        /// <summary>손 채굴이 열려 있는 광맥인가 (자원이 꽂혀 있어야 한다).</summary>
        public bool ManualMiningEnabled => allowManualMining && resource != null;

        string IInteractable.Prompt
        {
            get
            {
                if (!ManualMiningEnabled) return null;

                string what = string.IsNullOrEmpty(resource.displayName) ? resource.name : resource.displayName;

                // 재고가 비었어도 프롬프트는 남긴다 — 숨기면 "여기 캘 수 있는 곳이 맞나"부터 흔들린다.
                // 대신 왜 진행되지 않는지를 문장이 말한다.
                return currentStock > 0 ? $"{what} 손으로 캐기 (누르고 있기)"
                                        : $"{what} — 재고가 차는 중";
            }
        }

        /// <summary>탭 상호작용은 없다 — 손 채굴은 전부 홀드로만 진행한다.</summary>
        void IInteractable.Interact(PlayerController player) { }

        float IHoldInteractable.HoldSeconds => ManualExtractSeconds;

        string IHoldInteractable.HoldLabel => "채굴";

        /// <summary>재고가 있어야 진행한다. 비면 링이 그 자리에 멈추고, 다음 생산 주기에 저절로 이어진다.</summary>
        bool IHoldInteractable.CanHold => ManualMiningEnabled && currentStock > 0;

        /// <summary>
        /// 한 회차를 채웠다 — 재고에서 꺼내 플레이어에게 준다.
        /// 인벤토리가 가득 차면 바닥에 떨어뜨린다. 여기서 그냥 삼키면 캔 것이 증발한다.
        /// </summary>
        void IHoldInteractable.OnHoldComplete(PlayerController player)
        {
            int want = Mathf.Max(1, manualYield);
            if (!TryExtract(want, out int taken)) return;

            // silent: 이 프레임에 바로 아래에서 Mine음을 낸다 — 획득음까지 겹치면 같은 AudioSource에서
            // 두 파형이 합산돼 찢어진 소리가 난다. 채굴 완료의 신호는 Mine음 하나로 충분하다.
            var holder = PlayerInventoryHolder.Instance;
            bool stored = holder != null && holder.AddItemToPlayer(resource, taken, silent: true);

            if (!stored) DropAtHand(resource, taken, player);

            // 한 덩이가 떨어져 나올 때마다 나는 소리 — 링이 한 바퀴 돈 것을 눈으로 좇지 않아도 알 수 있다.
            SoundManager.Instance?.PlayCommonSFX(CommonSFX.Mine);
        }

        /// <summary>
        /// 광맥 위가 아니라 플레이어 쪽 옆면에 떨군다 — 광맥 콜라이더 안에서 태어나면
        /// 물리가 밀어내며 튀고, 슬래브 위에 놓으면 조준선에서 광맥과 겹쳐 줍기가 어려워진다.
        /// </summary>
        void DropAtHand(ItemDataSO item, int amount, PlayerController player)
        {
            Vector3 top = transform.position + Vector3.up * 0.8f;

            Vector3 toPlayer = player != null ? player.transform.position - top : Vector3.zero;
            toPlayer.y = 0f;
            Vector3 dir = toPlayer.sqrMagnitude > 1e-4f ? toPlayer.normalized : Vector3.forward;

            float reach = Mathf.Max(Size.x, Size.y) * 0.5f * ResourceNodeRegistry.Grid.CellSize + 0.4f;
            DroppedItem.Spawn(item, amount, top + dir * reach, dir * 0.3f + Vector3.up * 0.4f);
        }

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

            manualExtractSeconds = Mathf.Max(0.1f, manualExtractSeconds);
            manualYield          = Mathf.Max(1, manualYield);

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

}
