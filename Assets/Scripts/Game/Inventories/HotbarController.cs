using UnityEngine;
using UnityEngine.InputSystem;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Inputs;
using CoreDawn.Interaction;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Inventories
{
    [DefaultExecutionOrder(100)]
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

            EquipFromActiveSlot();
        }

        public int Priority => InputPriority.HudWidget;
        public bool IsInputActive => isActiveAndEnabled;

        private void Awake()
        {
            Instance = this;
            if (player == null) player = FindFirstObjectByType<PlayerController>();
        }

        private ItemContainer watched;

        private System.Collections.IEnumerator Start()
        {
            if (InputManager.Instance != null) InputManager.Instance.Register(this);

            // 핫바 내용이 바뀌면(인벤 조작·제작·줍기·드롭 무엇이든) 장착을 스스로 맞춘다.
            // 바꾸는 쪽이 "장착도 갱신해 달라"고 부르던 호출들(구 RefreshAllGameUIs)이 전부 사라진다 —
            // 호출을 한 군데라도 빠뜨리면 손에 든 무기와 핫바가 어긋나던 문제도 함께 사라진다.
            watched = PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.HotbarContainer : null;
            if (watched != null) watched.Changed += EquipFromActiveSlot;

            // 첫 장착은 한 프레임 미룬다. PlayerInventoryHolder는 Awake에 시작 핫바를 채우는데
            // WeaponManager는 Start에서 무기 모델을 전부 끄기 때문에, 지금 장착하면 그 뒤에
            // 꺼져 맨손으로 시작한다. 모든 Start가 끝난 다음에 걸어야 손에 남는다.
            yield return null;
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
            var so = !slot.IsEmpty ? ItemAssets.Of(slot.item) : null;
            if (so != null && so.TryGetModule<WeaponModuleSO>(out var weaponModule))
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
            if (slot.IsEmpty) return;

            Vector3 spawnPos = player.transform.position + player.playerCamera.forward * 1.5f + Vector3.up * 0.5f;
            DroppedItem.Spawn(slot.item, 1, spawnPos, player.playerCamera.forward);

            container.SetAt(currentHotbarIndex, slot.With(slot.amount - 1));   // Changed → 위 구독이 장착을, HUD가 표시를 스스로 맞춘다
        }
    }
}
