using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using CoreDawn.Inputs;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Sim;
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

        /// <summary>소지품 전체 — 앞 HotbarSize칸이 핫바 줄, 나머지가 가방 격자. 같은 그릇이다.</summary>
        protected ItemContainer Main => PlayerInventoryHolder.Instance?.MainContainer;
        protected int HotbarSize => PlayerInventoryHolder.Instance?.HotbarSize ?? 0;

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

            var main = Main;
            if (main != null) main.Changed += OnContainerChanged;
        }

        protected void UnbindCommon()
        {
            Root?.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            screenRoot?.UnregisterCallback<PointerDownEvent>(OnScrimPointerDown);

            tooltip?.Dispose();
            tooltip = null;

            var main = Main;
            if (main != null) main.Changed -= OnContainerChanged;

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
                if (Main != null) BuildRows(grid, Main, Columns, from: HotbarSize);   // 가방 = 핫바 뒤 칸들
            }

            if (hotbarRow != null)
            {
                hotbarRow.Clear();
                var hot = Main; int hotbarSize = HotbarSize;
                if (hot != null)
                {
                    int active = HotbarController.Instance != null ? HotbarController.Instance.CurrentHotbarIndex : -1;
                    for (int i = 0; i < hotbarSize; i++)   // 핫바 = 같은 그릇의 앞 칸들
                    {
                        var slot = MakeSlot(hot, i, keyLabel: (i + 1).ToString());
                        if (i == active) slot.AddToClassList("ui-slot--active");
                        if (i == hotbarSize - 1) slot.AddToClassList("ui-slot--last");
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
        /// <param name="from">이 인덱스부터 그린다 — 플레이어 소지품은 핫바 칸(앞)을 건너뛴다.</param>
        protected void BuildRows(VisualElement parent, ItemContainer c, int columns, int from = 0)
        {
            VisualElement row = null;
            for (int i = from; i < c.SlotCount; i++)
            {
                if ((i - from) % columns == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList("inv-grid__row");
                    parent.Add(row);
                }
                var slot = MakeSlot(c, i, keyLabel: null);
                if ((i - from) % columns == columns - 1 || i == c.SlotCount - 1) slot.AddToClassList("ui-slot--last");
                row.Add(slot);
            }
        }

        protected VisualElement MakeSlot(ItemContainer container, int index, string keyLabel)
        {
            var stack = container.PeekAt(index);
            bool empty = stack.IsEmpty;

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
            if (carried.IsEmpty)
            {
                var picked = container.TakeAt(index);
                if (!picked.IsEmpty) carried = picked;
                return;
            }
            var target = container.PeekAt(index);
            if (target.IsEmpty)
            {
                // 빈 칸에 놓기 — 그릇 상한만큼만 들어가고 나머지는 손에 남는다
                int fit = Mathf.Min(container.CapFor(carried.item), carried.amount);
                if (fit >= carried.amount)
                {
                    if (container.TryPutAt(index, carried)) carried = ItemStack.Empty;
                }
                else if (fit > 0 && container.TryPutAt(index, new ItemStack(carried.item, fit)))
                {
                    carried = carried.With(carried.amount - fit);
                }
            }
            else if (target.item == carried.item)
            {
                // 같은 아이템 — 합치기(자리만큼)
                int add = Mathf.Min(container.RoomAt(index, target.item), carried.amount);
                container.SetAt(index, target.With(target.amount + add));
                carried = carried.With(carried.amount - add);
            }
            else
            {
                // 다른 아이템 — 교환
                if (container.TryExchangeAt(index, carried, out var prev)) carried = prev;
            }
        }

        void RightClick(ItemContainer container, int index)
        {
            var target = container.PeekAt(index);
            if (carried.IsEmpty)
            {
                // 빈손 우클릭 — 절반 집기
                if (target.IsEmpty) return;
                int take = target.amount - target.amount / 2;   // 절반 (홀수면 큰 쪽)
                carried = new ItemStack(target.item, take);
                container.SetAt(index, target.With(target.amount - take));
                return;
            }
            // 들고 있을 때 우클릭 — 한 개 놓기
            if (target.IsEmpty)
            {
                if (container.TryPutAt(index, new ItemStack(carried.item, 1))) carried = carried.With(carried.amount - 1);
            }
            else if (target.item == carried.item && container.RoomAt(index, target.item) > 0)
            {
                container.SetAt(index, target.With(target.amount + 1));
                carried = carried.With(carried.amount - 1);
            }
        }

        /// <summary>Shift+클릭 목적지 — 화면마다 다르다. 기본은 가방↔핫바.</summary>
        protected virtual void QuickMove(ItemContainer src, int index)
        {
            var stack = src.PeekAt(index);
            if (stack.IsEmpty) return;
            var main = Main;
            if (src != main || main == null) return;
            // 같은 그릇의 다른 구간으로 — 핫바 칸이면 가방 구간으로, 가방 칸이면 핫바 구간으로
            int hot = HotbarSize;
            bool fromHotbar = index < hot;
            var rest = fromHotbar ? MoveStack(stack, main, hot, main.SlotCount) : MoveStack(stack, main, 0, hot);
            src.SetAt(index, rest);   // 남은 몫(없으면 빈 슬롯)
        }

        /// <summary>기존 스택부터 채우고 남으면 빈 슬롯에 — uGUI 쪽과 같은 순서.</summary>
        /// <summary>
        /// src를 dst의 [from, to) 구간에 최대한 넣고(같은 스택부터, 그다음 빈 칸) 남은 몫을 돌려준다 — 값이므로 호출자가 남은 몫을 슬롯에 다시 놓는다.
        /// 구간을 안 주면 그릇 전체(앞 칸=핫바부터).
        /// </summary>
        protected static ItemStack MoveStack(ItemStack src, ItemContainer dst, int from = 0, int to = -1)
        {
            int end = to < 0 ? dst.SlotCount : Mathf.Min(to, dst.SlotCount);
            for (int i = from; i < end && !src.IsEmpty; i++)
            {
                var t = dst.PeekAt(i);
                if (t.IsEmpty || t.item != src.item || dst.RoomAt(i, src.item) <= 0) continue;
                int add = Mathf.Min(dst.RoomAt(i, src.item), src.amount);
                dst.SetAt(i, t.With(t.amount + add));
                src = src.With(src.amount - add);
            }
            for (int i = from; i < end && !src.IsEmpty; i++)
            {
                if (!dst.PeekAt(i).IsEmpty) continue;
                int add = Mathf.Min(dst.CapFor(src.item), src.amount);
                if (dst.TryPutAt(i, new ItemStack(src.item, add))) src = src.With(src.amount - add);
            }
            return src;
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
            if (carried.IsEmpty) return;

            if (e.button == 0)
            {
                DropToWorld(carried.item, carried.amount);
                carried = ItemStack.Empty;
            }
            else if (e.button == 1)
            {
                DropToWorld(carried.item, 1);
                carried = carried.With(carried.amount - 1);
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
            bool has = !carried.IsEmpty;
            carry.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            if (!has) return;

            UIItemIcon.Apply(carryIcon, carried.item);
            carryCount.text = carried.amount > 1 ? carried.amount.ToString() : "";
        }

        /// <summary>들고 있던 스택을 가방으로 되돌린다. 가방이 가득이면 바닥에 떨어뜨린다.</summary>
        void ReturnCarried()
        {
            if (carried.IsEmpty) { carried = ItemStack.Empty; return; }

            var holder = PlayerInventoryHolder.Instance;
            if (holder == null || !holder.AddItemToPlayer(carried.item, carried.amount))
                DropToWorld(carried.item, carried.amount);

            carried = ItemStack.Empty;
            RefreshCarry();
        }

        protected static void DropToWorld(ItemDef item, int amount)
        {
            // 떨굴 위치는 플레이어가 정한다 — 구 uGUI 매니저를 경유하지 않는다
            var pc = PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.playerController : null;
            if (pc == null) return;   // 떨굴 위치가 없다 — 이 경로는 플레이어 없는 씬뿐

            Vector3 pos = pc.transform.position + pc.playerCamera.forward * 1.5f + Vector3.up * 0.5f;
            DroppedItem.Spawn(item, amount, pos, pc.playerCamera.forward);
        }

        // ───────────────────── 잡동사니 ─────────────────────

        protected static string DisplayNameOf(ItemDef item) =>
            item == null ? "" : string.IsNullOrEmpty(item.DisplayName) ? item.Id : item.DisplayName;

        protected static void Show(VisualElement e, bool on)
        {
            if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
