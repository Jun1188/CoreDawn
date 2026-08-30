using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Factory;
using CoreDawn.Inventories;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// SCR-08 보관소.
    ///
    /// 인벤토리 창과 같은 뼈대(PlayerItemPanelView) — 아래 두 단(소지품·핫바)이 자리까지
    /// 그대로라 창이 바뀌어도 손이 헤매지 않는다. 위에 보관소 칸이 얹힐 뿐이다.
    ///
    ///   정렬      보관소에만 — 자동으로 들어오는 물건이 쌓이는 곳이라 흐트러지지만,
    ///             인벤토리는 플레이어가 배치한 순서에 의미가 있어 함부로 섞으면 안 된다
    ///   자동 넣기  보관소에 이미 있는 종류만 올려 보낸다 — 전부 비우면 탄약과 무기까지
    ///             딸려 들어가 밤에 빈손이 된다
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class StoragePanelView : PlayerItemPanelView
    {
        static StoragePanelView cached;

        ItemContainer storage;

        VisualElement storeGrid;
        Button btnClose, btnSort, btnStack;

        const int StoreColumns = 6;   // 문서 SCR-08 — 인벤토리(9열)보다 좁게, 폭만으로 구분된다

        // ───────────────────────── 열기 ─────────────────────────

        /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false — 호출부가 기존 uGUI로 넘어간다.</summary>
        public static bool TryOpen(ItemContainer container)
        {
            if (container == null || PlayerInventoryHolder.Instance == null) return false;

            if (cached == null)
                cached = FindFirstObjectByType<StoragePanelView>(FindObjectsInactive.Include);
            if (cached == null) return false;

            if (cached.isActiveAndEnabled)
            {
                cached.Retarget(container);
                return true;
            }

            cached.storage = container;      // OnEnable → Bind 전에 넣어야 한다
            cached.gameObject.SetActive(true);
            return true;
        }

        void Retarget(ItemContainer container)
        {
            if (storage != null) storage.Changed -= OnContainerChanged;
            storage = container;
            if (storage != null) storage.Changed += OnContainerChanged;
            RebuildAll();
        }

        // ───────────────────── UITKPopup 계약 ─────────────────────

        protected override void Bind()
        {
            var r = Root;
            storeGrid = r.Q("store-grid");

            btnClose = r.Q<Button>("btn-close");
            btnSort  = r.Q<Button>("btn-sort");
            btnStack = r.Q<Button>("btn-stack");

            btnClose.clicked += Close;
            btnSort.clicked  += SortStorage;
            btnStack.clicked += AutoStack;

            BindCommon();
            if (storage != null) storage.Changed += OnContainerChanged;

            RebuildAll();
        }

        protected override void Unbind()
        {
            if (btnClose != null) btnClose.clicked -= Close;
            if (btnSort  != null) btnSort.clicked  -= SortStorage;
            if (btnStack != null) btnStack.clicked -= AutoStack;

            if (storage != null) storage.Changed -= OnContainerChanged;
            storage = null;

            UnbindCommon();
        }

        protected override void OnContainerChanged() => RebuildAll();

        void RebuildAll()
        {
            if (storeGrid != null)
            {
                storeGrid.Clear();
                // 벨트가 넣는 곳과 같은 컨테이너를 그대로 본다 — 별도 동기화 없음
                if (storage != null) BuildRows(storeGrid, storage, StoreColumns);
            }
            RebuildPlayerGrids();
        }

        /// <summary>Shift+클릭 — 보관소↔플레이어를 오간다. 플레이어 쪽은 앞 칸(핫바)부터.</summary>
        protected override void QuickMove(ItemContainer src, int index)
        {
            if (storage == null) { base.QuickMove(src, index); return; }
            var stack = src.PeekAt(index);
            if (stack.IsEmpty) return;
            if (src == storage)
            {
                stack = MoveStack(stack, Main);   // 앞 칸(핫바)부터 찬다
            }
            else
            {
                stack = MoveStack(stack, storage);
            }
            src.SetAt(index, stack);   // 남은 몫(없으면 빈 슬롯)
        }

        // ───────────────────── 정렬 · 자동 넣기 ─────────────────────

        /// <summary>
        /// 보관소를 티어 → 계통 → 용도 → 이름 순으로 다시 쌓는다 (아이템 목록들과 같은 기준).
        /// 전부 꺼냈다가 정렬 순서로 다시 넣는다 — 같은 내용물이 같은 컨테이너로 돌아가므로
        /// TryAdd는 실패할 수 없다 (합쳐지면 슬롯은 오히려 준다).
        /// </summary>
        void SortStorage()
        {
            if (storage == null) return;

            var entries = storage.Snapshot();
            if (entries.Count == 0) return;

            var order = UIItemOrder.Sorted(entries.Select(e => (ItemDataSO)e.item)).ToList();

            for (int i = 0; i < storage.SlotCount; i++) storage.TakeAt(i);
            foreach (var item in order)
                storage.TryAdd(item, entries.First(e => e.item == (ItemDef)item).n);

        }

        /// <summary>보관소에 이미 있는 종류만 소지품에서 올려 보낸다. 넘치는 만큼은 남긴다.</summary>
        void AutoStack()
        {
            if (storage == null) return;

            foreach (var (item, _) in storage.Snapshot())
            {
                PushKind(Main, item);
            }

        }

        void PushKind(ItemContainer src, ItemDataSO item)
        {
            if (src == null) return;
            int n = Mathf.Min(src.CountOf(item), storage.RoomFor(item));
            if (n <= 0) return;

            src.TryConsume(item, n);
            storage.TryAdd(item, n);
        }
    }
}
