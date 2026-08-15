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

    private ItemContainer watched;

    private void Start()
    {
        if (InputManager.Instance != null) InputManager.Instance.Register(this);

        // 핫바 내용이 바뀌면(인벤 조작·제작·줍기·드롭 무엇이든) 장착을 스스로 맞춘다.
        // 바꾸는 쪽이 "장착도 갱신해 달라"고 부르던 호출들(구 RefreshAllGameUIs)이 전부 사라진다 —
        // 호출을 한 군데라도 빠뜨리면 손에 든 무기와 핫바가 어긋나던 문제도 함께 사라진다.
        watched = PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.HotbarContainer : null;
        if (watched != null) watched.Changed += EquipFromActiveSlot;
        EquipFromActiveSlot();
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null) InputManager.Instance.Unregister(this);
        if (watched != null) { watched.Changed -= EquipFromActiveSlot; watched = null; }
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
        container.Touch();   // Changed → 위 구독이 장착을, HUD가 표시를 스스로 맞춘다
        if (slot.amount <= 0) container.TakeAt(currentHotbarIndex);
    }
}