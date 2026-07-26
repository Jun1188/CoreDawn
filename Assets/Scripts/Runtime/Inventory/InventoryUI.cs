using UnityEngine;

public class InventoryUI : MonoBehaviour
{
    [Header("UI Prefab & Parent")]
    public GameObject itemSocketPrefab;
    public Transform slotGridParent;

    public ItemContainer TargetContainer { get; private set; }
    private ItemSocket[] uiSlots;

    public void Bind(ItemContainer container)
    {
        TargetContainer = container;
        InitUISlots();
        RefreshUI();
    }

    private void OnEnable()
    {
        if (TargetContainer != null) RefreshUI();
    }

    public void InitUISlots()
    {
        if (TargetContainer == null || slotGridParent == null) return;

        foreach (Transform child in slotGridParent) Destroy(child.gameObject);

        int count = TargetContainer.SlotCount;
        uiSlots = new ItemSocket[count];

        for (int i = 0; i < count; i++)
        {
            GameObject go = Instantiate(itemSocketPrefab, slotGridParent);
            InventorySlotUI slotLink = go.GetComponent<InventorySlotUI>() ?? go.AddComponent<InventorySlotUI>();
            slotLink.Init(i, this);
            uiSlots[i] = go.GetComponent<ItemSocket>();
        }
    }

    public void RefreshUI()
    {
        if (TargetContainer == null || uiSlots == null) return;

        if (uiSlots.Length != TargetContainer.SlotCount)
        {
            InitUISlots();
            return;
        }

        for (int i = 0; i < uiSlots.Length; i++)
        {
            ItemStack itemStack = TargetContainer.PeekAt(i);
            if (itemStack != null && itemStack.item != null && itemStack.amount > 0)
            {
                uiSlots[i].SetItem(itemStack.item, itemStack.amount);
            }
            else
            {
                uiSlots[i].ClearSlot();
            }
        }
    }

    public void OnSlotLeftClicked(int clickedIndex)
    {
        if (TargetContainer != null)
            InventoryManager.Instance?.HandleSlotLeftClick(TargetContainer, clickedIndex, this);
    }

    public void OnSlotRightClicked(int clickedIndex)
    {
        if (TargetContainer != null)
            InventoryManager.Instance?.HandleSlotRightClick(TargetContainer, clickedIndex, this);
    }
}