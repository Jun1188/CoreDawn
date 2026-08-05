using UnityEngine;

[RequireComponent(typeof(InventoryUI))]
public class HotbarUI : MonoBehaviour
{
    public static HotbarUI Instance { get; private set; }

    private InventoryUI inventoryUI;
    [SerializeField] private Transform hotbarGridParent;

    [Header("Visual Colors")]
    public Color activeBorderColor = Color.yellow;
    public Color defaultBorderColor = new Color(0.1f, 0.1f, 0.1f, 1f);

    private ItemContainer bound;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        inventoryUI = GetComponent<InventoryUI>();
    }

    private void Start()
    {
        if (PlayerInventoryHolder.Instance != null)
            Bind(PlayerInventoryHolder.Instance.HotbarContainer);
        RefreshHotbar();
    }

    private void OnDestroy()
    {
        if (bound != null) bound.Changed -= RefreshHotbar;
    }

    public void Bind(ItemContainer container)
    {
        // 컨테이너 변경을 직접 구독한다 — 누가 바꿨든(제작 소비, 월드 줍기, 건설 비용 차감)
        // HUD가 즉시 따라온다. RefreshAllGameUIs를 불러 주는 경로에만 기대면
        // 그걸 빠뜨린 경로에서 마우스를 움직일 때까지 낡은 수량이 보인다.
        if (bound != null) bound.Changed -= RefreshHotbar;
        bound = container;
        if (bound != null) bound.Changed += RefreshHotbar;

        if (inventoryUI != null) inventoryUI.Bind(container);
    }
    public void RefreshHotbar()
    {
        if (inventoryUI != null) inventoryUI.RefreshUI();

        var container = PlayerInventoryHolder.Instance?.HotbarContainer;
        if (container == null || hotbarGridParent == null) return;

        int activeIndex = HotbarController.Instance != null ? HotbarController.Instance.CurrentHotbarIndex : 0;

        for (int i = 0; i < hotbarGridParent.childCount; i++)
        {
            Transform child = hotbarGridParent.GetChild(i);
            Transform border = child.Find("Border") ?? child.Find("Frame");

            if (border != null && border.TryGetComponent<UnityEngine.UI.Image>(out var borderImg))
            {
                if (i == activeIndex)
                {
                    borderImg.color = activeBorderColor;
                    child.localScale = Vector3.one * 1.1f;
                }
                else
                {
                    borderImg.color = defaultBorderColor;
                    child.localScale = Vector3.one * 1.0f;
                }
            }
        }
    }
}