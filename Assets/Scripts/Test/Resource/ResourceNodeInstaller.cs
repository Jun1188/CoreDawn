using System.Linq;
using UnityEngine;

/// <summary>
/// 광맥을 E 상호작용 대상으로 만든다 — 광맥을 보면 "[E] 채굴기 설치" 프롬프트가 뜨고,
/// 누르면 인벤토리의 채굴기 아이템을 1개 써서 그 자리에 세운다. 다시 누르면 회수해서 아이템으로 돌려받는다.
///
/// 설계 메모:
///  · 기존 코드를 건드리지 않는다. 발견(PlayerInteractionManager)·프롬프트·E키는 이미 있는 IInteractable
///    파이프라인을 그대로 타고, 설치/철거는 PlacementBridge 공개 API만 쓴다.
///  · 건물 높이는 마우스 배치와 같은 규약(표면 + PlacementSystem.SurfaceLift)을 쓴다 —
///    채굴기가 광맥 윗면에 올라앉는다.
///  · 어떤 채굴기 아이템이든 받는다: BuildingItemSO 중 building이 MinerDataSO면 대상.
///    (아이템 에셋을 새로 만들어도 이 스크립트는 그대로)
/// </summary>
[RequireComponent(typeof(ResourceNode))]
public class ResourceNodeInstaller : MonoBehaviour, IInteractable
{
    [Tooltip("이 광맥에 설치할 채굴기 아이템. 회수할 때 돌려줄 아이템이기도 하다 " +
             "(비워두면 인벤토리에 있는 채굴기 아이템을 찾아 쓰고, 회수는 못 한다).")]
    [SerializeField] private BuildingItemSO minerItem;

    ResourceNode _node;

    void Awake() => _node = GetComponent<ResourceNode>();

    // ─── 프롬프트 ──────────────────────────────────────────────

    public string Prompt
    {
        get
        {
            if (_node == null || _node.Resource == null) return null;

            if (MinerOnNode() != null) return BuildingAllowed ? "채굴기 회수" : "밤에는 회수할 수 없습니다";
            if (!BuildingAllowed)      return "밤에는 설치할 수 없습니다";

            return FindMinerItem() != null
                ? $"채굴기 설치 ({_node.Resource.displayName} 채굴)"
                : "채굴기 아이템이 필요합니다";
        }
    }

    /// <summary>낮에만 짓고 걷는다 — BuildController(빌드 메뉴)와 같은 규칙. TimeManager 없는 씬은 항상 허용.</summary>
    static bool BuildingAllowed =>
        TimeManager.Instance == null || TimeManager.Instance.IsBuildingAllowed;

    // ─── 상호작용 ──────────────────────────────────────────────

    public void Interact(PlayerController player)
    {
        if (!BuildingAllowed)
        {
            Debug.Log("[광맥] 밤에는 건설·철거를 할 수 없습니다. (아침까지 대기)");
            return;
        }

        var standing = MinerOnNode();
        if (standing != null) { Retrieve(standing); return; }

        var item = FindMinerItem();
        if (item == null)
        {
            Debug.Log("[광맥] 채굴기 아이템이 없습니다 — 상자에서 꺼내 오세요.");
            return;
        }

        Install(item);
    }

    void Install(BuildingItemSO item)
    {
        var container = ContainerHolding(item);
        if (container == null || !container.TryConsume(item, 1))
        {
            Debug.Log("[광맥] 채굴기 아이템을 꺼내지 못했습니다.");
            return;
        }

        Vector2Int cell = _node.Origin;
        Vector3 pos = ResourceNodeRegistry.Grid.GetFootprintCenter(cell, item.building.GetRotatedSize(0));
        pos.y = SurfaceTopAt(pos) + PlacementSystem.SurfaceLift(item.building, cell);

        var placed = PlacementBridge.Place(item.building, cell, pos);
        if (placed == null)
        {
            container.TryAdd(item, 1);   // 실패하면 아이템을 돌려준다 — 소비만 되고 사라지지 않게
            Debug.LogWarning("[광맥] 채굴기 설치에 실패해 아이템을 돌려놓았습니다.");
            return;
        }

        Refresh();
        Debug.Log($"[광맥] {_node.name} 셀 {cell}에 채굴기를 설치했습니다. (인벤토리 채굴기 {container.CountOf(item)}개 남음)");
    }

    void Retrieve(Building standing)
    {
        // 되돌려줄 아이템은 인스펙터에 꽂힌 것을 쓴다 — 빌드 메뉴로 지은 채굴기도 같은 아이템으로 회수된다
        var item = minerItem != null && minerItem.building == standing.Data ? minerItem : null;
        PlacementBridge.Remove(standing);

        if (item == null)
        {
            Debug.Log("[광맥] 채굴기를 철거했습니다. (되돌려줄 아이템 에셋이 없어 회수하지 못했습니다)");
            return;
        }

        var holder = PlayerInventoryHolder.Instance;
        bool taken = holder != null && holder.AddItemToPlayer(item, 1);

        if (!taken) PlacementBridge.DropAt(item, 1, transform.position + Vector3.up);

        Refresh();
        Debug.Log($"[광맥] 채굴기를 회수했습니다 — {(taken ? "인벤토리에 넣었습니다." : "가방이 가득 차 바닥에 떨어뜨렸습니다.")}");
    }

    static void Refresh()
    {
        if (InventoryManager.Instance != null) InventoryManager.Instance.RefreshAllGameUIs();
    }

    // ─── 조회 ─────────────────────────────────────────────────

    /// <summary>광맥 풋프린트 위에 서 있는 채굴기 (없으면 null).</summary>
    Building MinerOnNode()
    {
        var sim = FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.Sim : null;
        if (sim == null) return null;

        foreach (var cell in _node.Cells)
            if (sim.Grid.GetAt(cell) is { IsRemoved: false } b && b.Data is MinerDataSO)
                return b;

        return null;
    }

    /// <summary>플레이어가 들고 있는 채굴기 아이템 (핫바 우선, 없으면 가방).</summary>
    BuildingItemSO FindMinerItem()
    {
        var holder = PlayerInventoryHolder.Instance;
        if (holder == null) return null;

        return FindIn(holder.HotbarContainer) ?? FindIn(holder.MainContainer);

        static BuildingItemSO FindIn(ItemContainer c) =>
            c?.Snapshot()
              .Select(e => e.item as BuildingItemSO)
              .FirstOrDefault(b => b != null && b.building is MinerDataSO);
    }

    static ItemContainer ContainerHolding(ItemDataSO item)
    {
        var holder = PlayerInventoryHolder.Instance;
        if (holder == null) return null;

        if (holder.HotbarContainer != null && holder.HotbarContainer.CountOf(item) > 0) return holder.HotbarContainer;
        if (holder.MainContainer   != null && holder.MainContainer.CountOf(item)   > 0) return holder.MainContainer;
        return null;
    }

    /// <summary>표면 y — Ground 레이어를 위에서 훑는다 (광맥 슬래브도 Ground라 광맥 위면 그 윗면).</summary>
    static float SurfaceTopAt(Vector3 at)
    {
        int mask = LayerMask.GetMask("Ground");
        if (mask != 0 && Physics.Raycast(at + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, mask))
            return hit.point.y;
        return at.y;
    }
}
