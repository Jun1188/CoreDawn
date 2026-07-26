using UnityEngine;
using UnityEngine.EventSystems;

public class InventorySlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public int slotIndex { get; private set; } 
    private InventoryUI inventoryUI;

    public void Init(int index, InventoryUI ui)
    {
        slotIndex = index;
        inventoryUI = ui;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (inventoryUI == null) return;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            inventoryUI.OnSlotLeftClicked(slotIndex);
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            inventoryUI.OnSlotRightClicked(slotIndex);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        RefreshTooltipContext();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (ItemTooltipUI.Instance != null)
        {
            ItemTooltipUI.Instance.HideTooltip();
        }
    }

    private void RefreshTooltipContext()
    {
        // ★ TargetContainer 및 PeekAt으로 수정
        if (inventoryUI == null || inventoryUI.TargetContainer == null || ItemTooltipUI.Instance == null) return;

        ItemStack currentStack = inventoryUI.TargetContainer.PeekAt(slotIndex);
        if (currentStack != null && currentStack.item != null && currentStack.amount > 0)
        {
            ItemTooltipUI.Instance.ShowTooltip(currentStack.item);
        }
        else
        {
            ItemTooltipUI.Instance.HideTooltip();
        }
    }

    private void OnDisable()
    {
        if (ItemTooltipUI.Instance != null) ItemTooltipUI.Instance.HideTooltip();
    }
}