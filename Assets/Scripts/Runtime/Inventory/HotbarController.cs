using UnityEngine;
using UnityEngine.InputSystem;

public class HotbarController : MonoBehaviour, IInputReceiver
{
    public static HotbarController Instance { get; private set; }
    
    // ★ HotbarContainer 참조로 변경
    public int hotbarSlotCount => PlayerInventoryHolder.Instance != null && PlayerInventoryHolder.Instance.HotbarContainer != null 
        ? PlayerInventoryHolder.Instance.HotbarContainer.SlotCount 
        : 9;
        
    [SerializeField] private PlayerController player;

    private int currentHotbarIndex;
    public int CurrentHotbarIndex => currentHotbarIndex;

    /// <summary>
    /// 세이브 복원 전용 — 선택 칸을 되돌리고 그 칸에 맞는 무기를 다시 장착시킨다.
    /// (장착 상태는 핫바 선택에서 유도되므로 무기 쪽을 따로 저장할 필요가 없다)
    /// </summary>
    public void RestoreSelection(int index)
    {
        currentHotbarIndex = Mathf.Max(0, index);

        var holder = PlayerInventoryHolder.Instance;
        if (holder != null && InventoryManager.Instance != null)
            InventoryManager.Instance.CheckWeaponEquip(holder.HotbarContainer, currentHotbarIndex);

        if (HotbarUI.Instance != null) HotbarUI.Instance.RefreshHotbar();
    }

    public int Priority => InputPriority.HudWidget;
    public bool IsInputActive => isActiveAndEnabled;

    private void Awake()
    {
        Instance = this;
        if (player == null) player = FindFirstObjectByType<PlayerController>();
    }

    private void Start()
    {
        if (InputManager.Instance != null) InputManager.Instance.Register(this);
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null) InputManager.Instance.Unregister(this);
    }

    public bool OnInput(in InputEvent e)
    {
        if (e.Phase != InputActionPhase.Performed) return false;

        var holder = PlayerInventoryHolder.Instance;
        int slotCount = holder != null && holder.HotbarContainer != null ? holder.HotbarContainer.SlotCount : 9;

        switch (e.Id)
        {
            case InputActionId.Hotbar:
                string key = e.Context.control.name;
                if (int.TryParse(key[^1..], out int digit) && digit >= 1 && digit <= slotCount)
                {
                    Select(digit - 1);
                    return true;
                }
                return false;

            case InputActionId.HotbarScroll:
                float scroll = e.Read<float>();
                if (scroll == 0f) return false;
                int next = currentHotbarIndex + (scroll > 0 ? -1 : 1);
                if (next < 0) next = slotCount - 1;
                if (next >= slotCount) next = 0;
                Select(next);
                return true;

            case InputActionId.QuickDrop:
                DropActiveItem();
                return true;
        }
        return false;
    }

    private void Select(int index)
    {
        if (index == currentHotbarIndex) return;
        currentHotbarIndex = index;

        if (HotbarUI.Instance != null) HotbarUI.Instance.RefreshHotbar();

        var holder = PlayerInventoryHolder.Instance;
        if (InventoryManager.Instance != null && holder != null)
            InventoryManager.Instance.CheckWeaponEquip(holder.HotbarContainer, currentHotbarIndex);
    }

    private void DropActiveItem()
    {
        var holder = PlayerInventoryHolder.Instance;
        if (player == null || holder == null || holder.HotbarContainer == null) return;

        var container = holder.HotbarContainer;
        if (container.SlotCount <= currentHotbarIndex) return;

        ItemStack slot = container.PeekAt(currentHotbarIndex);
        if (slot == null || slot.item == null || slot.amount <= 0) return;

        Vector3 spawnPos = player.transform.position + player.playerCamera.forward * 1.5f + Vector3.up * 0.5f;
        DroppedItem.Spawn(slot.item, 1, spawnPos, player.playerCamera.forward);

        slot.amount--;
        container.Touch();
        if (slot.amount <= 0) container.TakeAt(currentHotbarIndex);

        if (InventoryManager.Instance != null) InventoryManager.Instance.RefreshAllGameUIs();
    }
}