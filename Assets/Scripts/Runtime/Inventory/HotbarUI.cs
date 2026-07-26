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

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        inventoryUI = GetComponent<InventoryUI>();
    }

    private void Start()
    {
        if (PlayerInventoryHolder.Instance != null && inventoryUI != null)
        {
            inventoryUI.Bind(PlayerInventoryHolder.Instance.HotbarContainer);
        }
        RefreshHotbar();
    }
    public void Bind(ItemContainer container)
    {
        if (inventoryUI != null)
        {
            inventoryUI.Bind(container);
        }
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