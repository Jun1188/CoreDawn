using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    [Header("Global Mouse Carriage")]
    public ItemSocket mouseCarriageSlot;
    private ItemStack mouseCarriageItem = null;

    [Header("Player References")]
    public PlayerController playerController;

    [Header("Core Deposit Panel")]
    [SerializeField] private GameObject coreRequirementPanel;
    [SerializeField] private CoreRequirementRowUI rowPrefab;
    [SerializeField] private Transform rowContainer;

    public bool IsScreenOpen { get; private set; }


    private ItemContainer boundContainer;
    private CoreBehavior boundCore;
    private int lastSeenVersion;
    private readonly List<CoreRequirementRowUI> spawnedRows = new();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (mouseCarriageSlot != null) mouseCarriageSlot.ClearSlot();
        CloseScreen();
    }

    public void OpenPlayerScreen() => OpenScreen(null);

    public void OpenContainerScreen(ItemContainer container)
    {
        if (container == null || playerController == null || IsScreenOpen) return;

        boundContainer = container;
        lastSeenVersion = container.Version;
        OpenScreen(container);
    }

    /// <summary>코어 전용 열기 — 일반 컨테이너 화면(드래그드롭)에 진행률 패널을 얹는다.</summary>
    public void OpenCoreScreen(CoreBehavior core)
    {
        if (core == null || playerController == null || IsScreenOpen) return;

        boundCore = core;
        OpenContainerScreen(core.Container);
        if (coreRequirementPanel != null) coreRequirementPanel.SetActive(true);
        RefreshCoreRows();
    }

    private void RefreshCoreRows()
    {
        if (boundCore == null || rowContainer == null || rowPrefab == null) return;

        var progress = boundCore.GetProgress();

        while (spawnedRows.Count < progress.Count)
        {
            var row = Instantiate(rowPrefab, rowContainer);
            spawnedRows.Add(row);
        }
        for (int i = 0; i < spawnedRows.Count; i++)
            spawnedRows[i].gameObject.SetActive(i < progress.Count);

        for (int i = 0; i < progress.Count; i++)
        {
            var (item, required, current) = progress[i];
            spawnedRows[i].Set(item, current, required);
        }
    }

    private void OpenScreen(ItemContainer container)
    {
        if (playerController == null || IsScreenOpen) return;
        IsScreenOpen = true;

        playerController.HaltMomentum();

        if (container != null && playerController.chestInventoryUI != null)
        {
            playerController.chestInventoryUI.Bind(container);
            playerController.chestInventoryUI.gameObject.SetActive(true);
            playerController.chestInventoryUI.RefreshUI();
        }

        if (playerController.inventoryUIPanel != null) playerController.inventoryUIPanel.SetActive(true);
        if (playerController.inventoryUI != null) playerController.inventoryUI.RefreshUI();
    }

    public void CloseScreen()
    {
        // 1. 조합 입력창에 올려둔 재료 가방으로 회수
        if (PlayerInventoryHolder.Instance != null)
            PlayerInventoryHolder.Instance.ReturnCraftingInputsToPlayer();

        // 2. 마우스로 들고 있던 아이템 바닥에 투척
        DropMouseCarriageItem();

        boundContainer = null;
        boundCore = null;
        if (coreRequirementPanel != null) coreRequirementPanel.SetActive(false);
        IsScreenOpen = false;

        if (playerController == null) return;
        if (playerController.inventoryUIPanel != null) playerController.inventoryUIPanel.SetActive(false);
        if (playerController.chestInventoryUI != null) playerController.chestInventoryUI.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (boundContainer != null && boundContainer.Version != lastSeenVersion)
        {
            lastSeenVersion = boundContainer.Version;
            if (playerController != null && playerController.chestInventoryUI != null)
                playerController.chestInventoryUI.RefreshUI();
            if (boundCore != null) RefreshCoreRows();
        }

        if (mouseCarriageItem != null && mouseCarriageItem.item != null && mouseCarriageItem.amount > 0)
        {
            mouseCarriageSlot.gameObject.SetActive(true);
            mouseCarriageSlot.transform.position = Input.mousePosition;
        }
        else
        {
            if (mouseCarriageSlot != null) mouseCarriageSlot.gameObject.SetActive(false);
        }
    }

    // ★ ItemContainer를 매개변수로 받도록 수정!
    public void HandleSlotLeftClick(ItemContainer container, int clickedIndex, InventoryUI uiSource)
    {
        if (container == null) return;

        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            HandleShiftClick(container, clickedIndex);
            return;
        }

        if (mouseCarriageItem == null || mouseCarriageItem.item == null)
        {
            var picked = container.TakeAt(clickedIndex);
            if (picked != null && picked.item != null) mouseCarriageItem = picked;
        }
        else
        {
            ItemStack targetSlot = container.PeekAt(clickedIndex);
            if (targetSlot == null || targetSlot.item == null)
            {
                if (container.TryPutAt(clickedIndex, mouseCarriageItem))
                    mouseCarriageItem = null;
            }
            else if (targetSlot.item == mouseCarriageItem.item)
            {
                int maxCanAdd = targetSlot.maxStackSize - targetSlot.amount;
                int toAdd = Mathf.Min(maxCanAdd, mouseCarriageItem.amount);
                targetSlot.amount += toAdd;
                mouseCarriageItem.amount -= toAdd;
                container.Touch();
                if (mouseCarriageItem.amount <= 0) mouseCarriageItem = null;
            }
            else
            {
                if (container.TryExchangeAt(clickedIndex, mouseCarriageItem, out var prev))
                    mouseCarriageItem = prev;
            }
        }

        UpdateMouseCarriageVisual();
        RefreshAllGameUIs();
    }

    // ★ ItemContainer를 매개변수로 받도록 수정!
    public void HandleSlotRightClick(ItemContainer container, int clickedIndex, InventoryUI uiSource)
    {
        if (container == null) return;

        ItemStack clickedBackendSlot = container.PeekAt(clickedIndex);

        if (mouseCarriageItem == null || mouseCarriageItem.item == null)
        {
            if (clickedBackendSlot != null && clickedBackendSlot.item != null && clickedBackendSlot.amount > 0)
            {
                int takeAmount = clickedBackendSlot.amount - (clickedBackendSlot.amount / 2);
                mouseCarriageItem = new ItemStack(clickedBackendSlot.item, takeAmount);
                clickedBackendSlot.amount -= takeAmount;
                container.Touch();

                if (clickedBackendSlot.amount <= 0)
                    container.TakeAt(clickedIndex);
            }
        }
        else
        {
            if (clickedBackendSlot == null || clickedBackendSlot.item == null)
            {
                if (container.TryPutAt(clickedIndex, new ItemStack(mouseCarriageItem.item, 1)))
                    mouseCarriageItem.amount--;
            }
            else if (clickedBackendSlot.item == mouseCarriageItem.item && clickedBackendSlot.amount < clickedBackendSlot.maxStackSize)
            {
                clickedBackendSlot.amount++;
                mouseCarriageItem.amount--;
                container.Touch();
            }

            if (mouseCarriageItem.amount <= 0) mouseCarriageItem = null;
        }

        UpdateMouseCarriageVisual();
        RefreshAllGameUIs();
    }

    private void HandleShiftClick(ItemContainer srcContainer, int clickedIndex)
    {
        var holder = PlayerInventoryHolder.Instance;
        if (holder == null || srcContainer == null) return;

        ItemStack srcSlot = srcContainer.PeekAt(clickedIndex);
        if (srcSlot == null || srcSlot.item == null || srcSlot.amount <= 0) return;

        bool isChestOpen = playerController != null && playerController.inventoryUIPanel != null && playerController.inventoryUIPanel.activeSelf && playerController.chestInventoryUI != null && playerController.chestInventoryUI.gameObject.activeSelf;
        ItemContainer chestContainer = isChestOpen ? playerController.chestInventoryUI.TargetContainer : null;

        if (srcContainer == holder.CraftingInputContainer || srcContainer == holder.CraftingOutputContainer)
        {
            TryMoveStackToContainer(srcSlot, holder.MainContainer);
            if (srcSlot.amount > 0) TryMoveStackToContainer(srcSlot, holder.HotbarContainer);
        }
        else if (isChestOpen && srcContainer == chestContainer)
        {
            TryMoveStackToContainer(srcSlot, holder.MainContainer);
            if (srcSlot.amount > 0) TryMoveStackToContainer(srcSlot, holder.HotbarContainer);
        }
        else if (srcContainer == holder.HotbarContainer)
        {
            if (isChestOpen) TryMoveStackToContainer(srcSlot, chestContainer);
            else TryMoveStackToContainer(srcSlot, holder.MainContainer);
        }
        else if (srcContainer == holder.MainContainer)
        {
            if (isChestOpen) TryMoveStackToContainer(srcSlot, chestContainer);
            else TryMoveStackToContainer(srcSlot, holder.HotbarContainer);
        }

        srcContainer.Touch();
        if (srcSlot.amount <= 0) srcContainer.TakeAt(clickedIndex);

        RefreshAllGameUIs();
    }

    private void TryMoveStackToContainer(ItemStack srcSlot, ItemContainer targetContainer)
    {
        if (targetContainer == null || srcSlot == null || srcSlot.amount <= 0) return;

        for (int i = 0; i < targetContainer.SlotCount; i++)
        {
            var targetSlot = targetContainer.PeekAt(i);
            if (targetSlot != null && targetSlot.item == srcSlot.item && targetSlot.amount < targetSlot.maxStackSize)
            {
                int add = Mathf.Min(targetSlot.maxStackSize - targetSlot.amount, srcSlot.amount);
                targetSlot.amount += add;
                srcSlot.amount -= add;
                targetContainer.Touch();
                if (srcSlot.amount <= 0) return;
            }
        }

        for (int i = 0; i < targetContainer.SlotCount; i++)
        {
            var targetSlot = targetContainer.PeekAt(i);
            if (targetSlot == null || targetSlot.item == null)
            {
                int add = Mathf.Min(srcSlot.maxStackSize, srcSlot.amount);
                if (targetContainer.TryPutAt(i, new ItemStack(srcSlot.item, add)))
                {
                    srcSlot.amount -= add;
                    if (srcSlot.amount <= 0) return;
                }
            }
        }
    }

    private void UpdateMouseCarriageVisual()
    {
        if (mouseCarriageSlot == null) return;
        if (mouseCarriageItem != null && mouseCarriageItem.item != null && mouseCarriageItem.amount > 0)
            mouseCarriageSlot.SetItem(mouseCarriageItem.item, mouseCarriageItem.amount);
        else
            mouseCarriageSlot.ClearSlot();
    }

    public void CheckWeaponEquip(ItemContainer hotbarContainer, int activeSlotIndex = -1)
    {
        if (playerController == null || playerController.weaponManager == null || hotbarContainer == null) return;

        if (activeSlotIndex == -1 && HotbarController.Instance != null)
            activeSlotIndex = HotbarController.Instance.CurrentHotbarIndex;

        if (hotbarContainer.SlotCount > activeSlotIndex && activeSlotIndex >= 0)
        {
            ItemStack activeSlot = hotbarContainer.PeekAt(activeSlotIndex);
            if (activeSlot != null && activeSlot.item is WeaponItemSO weaponItem)
                playerController.weaponManager.EquipWeapon(weaponItem.gunData);
            else
                playerController.weaponManager.UnequipWeapon();
        }
    }

    public void DropMouseCarriageItem()
    {
        if (mouseCarriageItem == null || mouseCarriageItem.item == null || mouseCarriageItem.amount <= 0) return;
        if (playerController == null) return;

        Vector3 spawnPos = playerController.transform.position + playerController.playerCamera.forward * 1.5f + Vector3.up * 0.5f;
        DroppedItem.Spawn(mouseCarriageItem.item, mouseCarriageItem.amount, spawnPos, playerController.playerCamera.forward);

        mouseCarriageItem = null;
        if (mouseCarriageSlot != null) mouseCarriageSlot.ClearSlot();

        RefreshAllGameUIs();
    }

    public void RefreshAllGameUIs()
    {
        var holder = PlayerInventoryHolder.Instance;
        if (holder != null) CheckWeaponEquip(holder.HotbarContainer);

        InventoryUI[] allActiveUIs = FindObjectsByType<InventoryUI>(FindObjectsSortMode.None);
        foreach (InventoryUI ui in allActiveUIs)
        {
            if (ui.gameObject.activeSelf) ui.RefreshUI();
        }

        if (HotbarUI.Instance != null) HotbarUI.Instance.RefreshHotbar();
    }
}