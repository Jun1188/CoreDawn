using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using CoreDawn.Factory;
using CoreDawn.Inputs;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Data;
using InputEvent = CoreDawn.Inputs.InputEvent;

namespace CoreDawn.UI
{
    /// <summary>
    /// 아이템 화면 공통 뼈대 — 인벤토리(SCR-04)와 보관소(SCR-08)가 같이 쓴다.
    ///
    /// 문서: "같은 뼈대를 쓴다 — 아래 두 단(소지품·핫바)이 자리까지 그대로라 창이 바뀌어도
    /// 손이 헤매지 않는다." 화면이 같으니 코드도 같은 곳에 있어야 어긋나지 않는다.
    ///
    /// 여기 있는 것: 소지품·핫바 격자, 캐리지(들고 있는 스택)와 슬롯 조작
    /// (좌 집기/놓기/합치기/교환 · 우 절반/한 개 · Shift 빠른 이동), 창 밖 던지기,
    /// 닫힐 때 캐리지 회수, I/E로 닫기.
    /// 파생이 정하는 것: 위 단(제작/보관소 칸)과 Shift 이동의 목적지.
    /// </summary>
    public abstract class PlayerItemPanelView : UITKPopup
    {
        protected ItemStack carried;
        VisualElement carry, carryIcon;
        Label carryCount;

        protected VisualElement screenRoot, grid, hotbarRow;
        protected UITooltip tooltip;

        protected ItemContainer Main   => PlayerInventoryHolder.Instance?.MainContainer;
        protected ItemContainer Hotbar => PlayerInventoryHolder.Instance?.HotbarContainer;

        protected const int Columns = 9;   // 문서 SCR-04 — 소지품 9열

        // ───────────────── UITKPopup 계약의 공통 절반 ─────────────────
        // 파생의 Bind/Unbind가 반드시 이 둘을 호출한다.

        protected void BindCommon()
        {
            var r = Root;
            grid      = r.Q("grid");
            hotbarRow = r.Q("hotbar");

            // 캐리지 — 포인터를 따라다니는 스택. 패널 위 어디서든 보여야 하므로 루트에 단다
            BuildCarry(r);
            r.RegisterCallback<PointerMoveEvent>(OnPointerMove);

            tooltip = new UITooltip(r);

            // 창 밖(스크림)에 놓으면 월드로 던진다 — 마인크래프트 문법.
            // 좌클릭 전부 · 우클릭 한 개. 슬롯 클릭은 StopPropagation이라 여기 오지 않는다
            screenRoot = r.Q("screen-root");
            screenRoot?.RegisterCallback<PointerDownEvent>(OnScrimPointerDown);

            var main = Main; var hot = Hotbar;
            if (main != null) main.Changed += OnContainerChanged;
            if (hot  != null) hot.Changed  += OnContainerChanged;
        }

        protected void UnbindCommon()
        {
            Root?.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            screenRoot?.UnregisterCallback<PointerDownEvent>(OnScrimPointerDown);

            tooltip?.Dispose();
            tooltip = null;

            var main = Main; var hot = Hotbar;
            if (main != null) main.Changed -= OnContainerChanged;
            if (hot  != null) hot.Changed  -= OnContainerChanged;

            ReturnCarried();   // 닫는 경로가 무엇이든 (ESC·I·E·씬 전환) 들고 있던 것을 잃지 않는다
        }

        /// <summary>구독 중인 컨테이너가 바뀌었다 — 파생이 자기 격자·표시를 다시 그린다.</summary>
        protected abstract void OnContainerChanged();

        public override bool OnInput(in InputEvent e)
        {
            // 연 키로 다시 닫는 대칭 조작 — uGUI InventoryPopup과 같은 계약
            if (e.Phase == InputActionPhase.Performed &&
                (e.Id == InputActionId.ToggleInventory || e.Id == InputActionId.Interact))
            {
                Close();
                return true;
            }
            return base.OnInput(e);   // Cancel(ESC) 닫기 + 모달 삼킴
        }

        // ───────────────────── 슬롯 격자 ─────────────────────

        /// <summary>소지품(9열)과 핫바를 다시 그린다 — 파생의 OnContainerChanged가 부른다.</summary>
        protected void RebuildPlayerGrids()
        {
            // 호버 중이던 슬롯이 통째로 교체되면 Leave가 안 오므로 여기서 닫는다
            tooltip?.Hide();

            if (grid != null)
            {
                grid.Clear();
                if (Main != null) BuildRows(grid, Main, Columns);
            }

            if (hotbarRow != null)
            {
                hotbarRow.Clear();
                var hot = Hotbar;
                if (hot != null)
                {
                    int active = HotbarController.Instance != null ? HotbarController.Instance.CurrentHotbarIndex : -1;
                    for (int i = 0; i < hot.SlotCount; i++)
                    {
                        var slot = MakeSlot(hot, i, keyLabel: (i + 1).ToString());
                        if (i == active) slot.AddToClassList("ui-slot--active");
                        if (i == hot.SlotCount - 1) slot.AddToClassList("ui-slot--last");
                        hotbarRow.Add(slot);
                    }
                }
            }
        }

        /// <summary>
        /// 컨테이너를 columns칸씩 행으로 잘라 채운다.
        /// flex-wrap에 맡기지 않는 이유는 분배기 격자와 같다 — 반올림 오차로 줄바꿈이
        /// 계산과 어긋나는 일이 구조적으로 없다.
        /// </summary>
        protected void BuildRows(VisualElement parent, ItemContainer c, int columns)
        {
            VisualElement row = null;
            for (int i = 0; i < c.SlotCount; i++)
            {
                if (i % columns == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList("inv-grid__row");
                    parent.Add(row);
                }
                var slot = MakeSlot(c, i, keyLabel: null);
                if (i % columns == columns - 1 || i == c.SlotCount - 1) slot.AddToClassList("ui-slot--last");
                row.Add(slot);
            }
        }

        protected VisualElement MakeSlot(ItemContainer container, int index, string keyLabel)
        {
            var stack = container.PeekAt(index);
            bool empty = stack == null || stack.item == null || stack.amount <= 0;

            var slot = new VisualElement();
            slot.AddToClassList("ui-slot");
            if (empty) slot.AddToClassList("ui-slot--empty");

            if (keyLabel != null)
            {
                var key = new Label(keyLabel);
                key.AddToClassList("ui-slot__key");
                slot.Add(key);
            }

            var icon = new VisualElement();
            icon.AddToClassList("ui-slot__icon");
            if (!empty) UIItemIcon.Apply(icon, stack.item);
            slot.Add(icon);

            if (!empty)
            {
                // 1개는 숫자를 붙이지 않는다 — 아이콘만으로 하나임이 읽히고,
                // 온 화면에 깔린 "1"은 정보가 아니라 잡음이다 (uGUI ItemSocket과 같은 규칙)
                if (stack.amount > 1)
                {
                    var n = new Label(stack.amount.ToString());
                    n.AddToClassList("ui-slot__n");
                    slot.Add(n);
                }

                tooltip?.AttachItem(slot, stack.item);
            }

            var c = container; var i = index;
            slot.RegisterCallback<PointerDownEvent>(e => OnSlotPointerDown(e, c, i));
            return slot;
        }

        // ───────────────── 슬롯 조작 — 캐리지 방식 ─────────────────
        // uGUI InventoryManager와 같은 문법: 좌클릭 집기/놓기/합치기/교환,
        // 우클릭 절반 집기/한 개 놓기, Shift+클릭 빠른 이동.
        // 규칙(스택 상한 등)은 전부 ItemContainer가 지킨다 — 여기서는 옮기기만 한다.

        void OnSlotPointerDown(PointerDownEvent e, ItemContainer container, int index)
        {
            e.StopPropagation();

            if (e.button == 0)
            {
                if (e.shiftKey) QuickMove(container, index);
                else LeftClick(container, index);
            }
            else if (e.button == 1)
            {
                RightClick(container, index);
            }

            MoveCarry(e.position);
            RefreshCarry();
        }

        void LeftClick(ItemContainer container, int index)
        {
            if (carried == null || carried.item == null)
            {
                var picked = container.TakeAt(index);
                if (picked != null && picked.item != null) carried = picked;
                return;
            }

            var target = container.PeekAt(index);
            if (target == null || target.item == null)
            {
                // 상한(포탑 탄약함처럼 좁은 버퍼)을 넘으면 통째로 거절하지 않고 들어갈 만큼만 —
                // 클릭이 아무 반응 없이 씹히는 것보다 "20개만 들어갔다"가 읽힌다
                int fit = Mathf.Min(container.CapFor(carried.item), carried.amount);
                if (fit >= carried.amount)
                {
                    if (container.TryPutAt(index, carried)) carried = null;
                }
                else if (fit > 0 && container.TryPutAt(index, new ItemStack(carried.item, fit)))
                {
                    carried.amount -= fit;
                }
            }
            else if (target.item == carried.item)
            {
                int add = Mathf.Min(container.RoomAt(index, target.item), carried.amount);
                target.amount += add;
                carried.amount -= add;
                container.Touch();
                if (carried.amount <= 0) carried = null;
            }
            else
            {
                if (container.TryExchangeAt(index, carried, out var prev)) carried = prev;
            }
        }

        void RightClick(ItemContainer container, int index)
        {
            var target = container.PeekAt(index);

            if (carried == null || carried.item == null)
            {
                if (target == null || target.item == null || target.amount <= 0) return;

                int take = target.amount - target.amount / 2;   // 절반 (홀수면 큰 쪽)
                carried = new ItemStack(target.item, take);
                target.amount -= take;
                container.Touch();
                if (target.amount <= 0) container.TakeAt(index);
                return;
            }

            if (target == null || target.item == null)
            {
                if (container.TryPutAt(index, new ItemStack(carried.item, 1))) carried.amount--;
            }
            else if (target.item == carried.item && container.RoomAt(index, target.item) > 0)
            {
                target.amount++;
                carried.amount--;
                container.Touch();
            }

            if (carried.amount <= 0) carried = null;
        }

        /// <summary>Shift+클릭 목적지 — 화면마다 다르다. 기본은 가방↔핫바.</summary>
        protected virtual void QuickMove(ItemContainer src, int index)
        {
            var stack = src.PeekAt(index);
            if (stack == null || stack.item == null || stack.amount <= 0) return;

            var dst = src == Main ? Hotbar : Main;
            if (dst == null) return;

            MoveStack(stack, dst);

            src.Touch();
            if (stack.amount <= 0) src.TakeAt(index);
        }

        /// <summary>기존 스택부터 채우고 남으면 빈 슬롯에 — uGUI 쪽과 같은 순서.</summary>
        protected static void MoveStack(ItemStack src, ItemContainer dst)
        {
            for (int i = 0; i < dst.SlotCount && src.amount > 0; i++)
            {
                var t = dst.PeekAt(i);
                if (t == null || t.item != src.item || dst.RoomAt(i, src.item) <= 0) continue;
                int add = Mathf.Min(dst.RoomAt(i, src.item), src.amount);
                t.amount += add;
                src.amount -= add;
                dst.Touch();
            }
            for (int i = 0; i < dst.SlotCount && src.amount > 0; i++)
            {
                var t = dst.PeekAt(i);
                if (t != null && t.item != null) continue;
                int add = Mathf.Min(dst.CapFor(src.item), src.amount);
                if (dst.TryPutAt(i, new ItemStack(src.item, add))) src.amount -= add;
            }
        }

        // ───────────────────── 캐리지 표시 ─────────────────────

        void BuildCarry(VisualElement root)
        {
            if (carry != null) carry.RemoveFromHierarchy();

            carry = new VisualElement { pickingMode = PickingMode.Ignore };
            carry.AddToClassList("inv-carry");

            carryIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            carryIcon.AddToClassList("ui-slot__icon");
            carry.Add(carryIcon);

            carryCount = new Label { pickingMode = PickingMode.Ignore };
            carryCount.AddToClassList("ui-slot__n");
            carry.Add(carryCount);

            root.Add(carry);
            RefreshCarry();
        }

        void OnPointerMove(PointerMoveEvent e) => MoveCarry(e.position);

        /// <summary>창 밖에 놓으면 던진다 — 마인크래프트 문법. 좌클릭 전부, 우클릭 한 개.</summary>
        void OnScrimPointerDown(PointerDownEvent e)
        {
            if (e.target != screenRoot) return;   // 패널 안쪽 클릭은 각자의 몫
            if (carried == null || carried.item == null || carried.amount <= 0) return;

            if (e.button == 0)
            {
                DropToWorld(carried.item, carried.amount);
                carried = null;
            }
            else if (e.button == 1)
            {
                DropToWorld(carried.item, 1);
                carried.amount--;
                if (carried.amount <= 0) carried = null;
            }
            else return;

            RefreshCarry();
        }

        void MoveCarry(Vector2 panelPos)
        {
            if (carry == null) return;
            carry.style.left = panelPos.x - 33f;
            carry.style.top  = panelPos.y - 33f;
        }

        protected void RefreshCarry()
        {
            if (carry == null) return;
            bool has = carried != null && carried.item != null && carried.amount > 0;
            carry.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (!has) return;

            UIItemIcon.Apply(carryIcon, carried.item);
            carryCount.text = carried.amount > 1 ? carried.amount.ToString() : "";
        }

        /// <summary>들고 있던 스택을 가방으로 되돌린다. 가방이 가득이면 바닥에 떨어뜨린다.</summary>
        void ReturnCarried()
        {
            if (carried == null || carried.item == null || carried.amount <= 0) { carried = null; return; }

            var holder = PlayerInventoryHolder.Instance;
            if (holder == null || !holder.AddItemToPlayer(carried.item, carried.amount))
                DropToWorld(carried.item, carried.amount);

            carried = null;
            RefreshCarry();
        }

        protected static void DropToWorld(ItemDataSO item, int amount)
        {
            // 떨굴 위치는 플레이어가 정한다 — 구 uGUI 매니저를 경유하지 않는다
            var pc = PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.playerController : null;
            if (pc == null) return;   // 떨굴 위치가 없다 — 이 경로는 플레이어 없는 씬뿐

            Vector3 pos = pc.transform.position + pc.playerCamera.forward * 1.5f + Vector3.up * 0.5f;
            DroppedItem.Spawn(item, amount, pos, pc.playerCamera.forward);
        }

        // ───────────────────── 잡동사니 ─────────────────────

        protected static string DisplayNameOf(ItemDataSO item) =>
            item == null ? "" : string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;

        protected static void Show(VisualElement e, bool on)
        {
            if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
