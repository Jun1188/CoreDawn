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

        EquipFromActiveSlot();
    }

    /// <summary>
    /// 활성 슬롯의 무기를 장착/해제한다 — 핫바 상태의 소유자인 여기가 장착 브리지의
    /// 유일한 집이다. (구 InventoryManager.CheckWeaponEquip에서 회수 — 화면 매니저가
    /// 게임플레이 로직을 들고 있으면 uGUI 없는 씬에서 장착이 통째로 죽는다.)
    /// </summary>
    public void EquipFromActiveSlot()
    {
        var holder = PlayerInventoryHolder.Instance;
        var weaponManager = player != null ? player.weaponManager : null;
        if (holder == null || holder.HotbarContainer == null || weaponManager == null) return;
        if (currentHotbarIndex < 0 || currentHotbarIndex >= holder.HotbarContainer.SlotCount) return;

        var slot = holder.HotbarContainer.PeekAt(currentHotbarIndex);
        if (slot != null && slot.item != null && slot.item.TryGetModule<WeaponModuleSO>(out var weaponModule))
            weaponManager.EquipWeapon(weaponModule.gun);
        else
            weaponManager.UnequipWeapon();
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