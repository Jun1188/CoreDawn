using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// SCR-04 인벤토리 · 수동 제작.
///
/// 위에서 아래로 제작 → 소지품 → 핫바. 패널이 화면 하단에 붙어 핫바가 HUD 핫바와
/// 같은 자리에 온다 — I로 열고 닫아도 손이 기억하는 위치가 유지된다.
///
/// 수동 제작은 기존 uGUI(제작 입력 슬롯에 재료를 옮겨 담는 방식)와 다르다:
///   - 재료를 옮겨 담지 않는다. 가방·핫바의 보유량을 그대로 세고 그대로 쓴다
///   - 해금된 레시피는 전부 목록에 오른다 — "잠김" 줄이 없다 (문서 SCR-04)
///   - 버튼을 누르고 있는 동안 계속 만든다. 소비는 한 개가 완성되는 순간에만
///     일어난다 — 중간에 떼도 잃는 것이 없고, 완성 직전 재료를 빼돌려도
///     완성 시점 검사에 걸려 무에서 만들어지지 않는다
/// </summary>
[DefaultExecutionOrder(100)]
public class InventoryPanelView : UITKPopup
{
    static InventoryPanelView cached;

    // ── 제작 상태 ──
    RecipeDataSO selected;
    bool holding;
    float progress;          // 현재 1회분 경과 시간(초)
    string search = "";

    // ── 마우스 캐리지 — 들고 있는 스택 ──
    ItemStack carried;
    VisualElement carry, carryIcon;
    Label carryCount;

    // ── 요소 참조 ──
    VisualElement screenRoot;
    VisualElement recipes, mats, grid, hotbarRow, detail;
    VisualElement yieldIcon, craftBtnFill;
    Label yieldName, yieldPer, yieldTime, craftBtnText, craftBtnTime, recipesEmpty;
    Button btnClose, btnCraft;
    TextField searchField;

    ItemContainer Main   => PlayerInventoryHolder.Instance?.MainContainer;
    ItemContainer Hotbar => PlayerInventoryHolder.Instance?.HotbarContainer;

    // ───────────────────────── 열기 ─────────────────────────

    /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false — 호출부가 기존 uGUI로 넘어간다.</summary>
    public static bool TryOpen()
    {
        if (PlayerInventoryHolder.Instance == null) return false;

        if (cached == null)
            cached = FindFirstObjectByType<InventoryPanelView>(FindObjectsInactive.Include);
        if (cached == null) return false;

        if (!cached.isActiveAndEnabled)
            cached.gameObject.SetActive(true);
        return true;
    }

    // ───────────────────── UITKPopup 계약 ─────────────────────

    protected override void Bind()
    {
        var r = Root;
        recipes      = r.Q("recipes");
        recipesEmpty = r.Q<Label>("recipes-empty");
        mats         = r.Q("mats");
        grid         = r.Q("grid");
        hotbarRow    = r.Q("hotbar");
        detail       = r.Q("detail");

        yieldIcon    = r.Q("yield-icon");
        yieldName    = r.Q<Label>("yield-name");
        yieldPer     = r.Q<Label>("yield-per");
        yieldTime    = r.Q<Label>("yield-time");
        craftBtnFill = r.Q("craft-btn-fill");
        craftBtnText = r.Q<Label>("craft-btn-text");
        craftBtnTime = r.Q<Label>("craft-btn-time");

        btnClose    = r.Q<Button>("btn-close");
        btnCraft    = r.Q<Button>("btn-craft");
        searchField = r.Q<TextField>("recipe-search");

        btnClose.clicked += Close;
        searchField.RegisterValueChangedCallback(OnSearchChanged);

        // 돋보기 — USS에 SVG가 없어 요소로 넣는다. Bind가 다시 돌아도 하나만
        var searchBox = r.Q("recipe-search-box");
        if (searchBox != null && searchBox.Q<SearchGlyph>() == null)
            searchBox.Insert(0, new SearchGlyph());

        // 홀드 — Button의 Clickable이 포인터를 캡처하므로 Up/CaptureOut이 버튼으로 온다.
        // CaptureOut까지 받아야 창이 닫히거나 포커스를 뺏겨도 홀드가 풀린다
        btnCraft.RegisterCallback<PointerDownEvent>(OnCraftDown, TrickleDown.TrickleDown);
        btnCraft.RegisterCallback<PointerUpEvent>(OnCraftUp);
        btnCraft.RegisterCallback<PointerCaptureOutEvent>(OnCraftCaptureOut);

        // 캐리지 — 포인터를 따라다니는 스택. 패널 위 어디서든 보여야 하므로 루트에 단다
        BuildCarry(r);
        r.RegisterCallback<PointerMoveEvent>(OnPointerMove);

        // 창 밖(스크림)에 놓으면 월드로 던진다 — 마인크래프트 문법.
        // 좌클릭 전부 · 우클릭 한 개. 슬롯 클릭은 StopPropagation이라 여기 오지 않는다
        screenRoot = r.Q("screen-root");
        screenRoot?.RegisterCallback<PointerDownEvent>(OnScrimPointerDown);

        var main = Main; var hot = Hotbar;
        if (main != null) main.Changed += OnContainerChanged;
        if (hot  != null) hot.Changed  += OnContainerChanged;
        if (GameManager.Instance != null) GameManager.Instance.TierUnlocked += OnTierUnlocked;

        holding = false;
        progress = 0f;

        RebuildRecipes();
        RebuildGrids();
        RefreshDetail();
    }

    protected override void Unbind()
    {
        if (btnClose != null) btnClose.clicked -= Close;
        if (searchField != null) searchField.UnregisterValueChangedCallback(OnSearchChanged);
        if (btnCraft != null)
        {
            btnCraft.UnregisterCallback<PointerDownEvent>(OnCraftDown, TrickleDown.TrickleDown);
            btnCraft.UnregisterCallback<PointerUpEvent>(OnCraftUp);
            btnCraft.UnregisterCallback<PointerCaptureOutEvent>(OnCraftCaptureOut);
        }
        Root?.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        screenRoot?.UnregisterCallback<PointerDownEvent>(OnScrimPointerDown);

        var main = Main; var hot = Hotbar;
        if (main != null) main.Changed -= OnContainerChanged;
        if (hot  != null) hot.Changed  -= OnContainerChanged;
        if (GameManager.Instance != null) GameManager.Instance.TierUnlocked -= OnTierUnlocked;

        holding = false;
        ReturnCarried();   // 닫는 경로가 무엇이든 (ESC·I·E·씬 전환) 들고 있던 것을 잃지 않는다
    }

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

    void OnContainerChanged()
    {
        RebuildGrids();
        RefreshDetail();   // 보유량이 바뀌면 "필요/보유"와 버튼 상태도 함께
    }

    void OnTierUnlocked(int _) => RebuildRecipes();

    void OnSearchChanged(ChangeEvent<string> e)
    {
        search = e.newValue ?? "";
        RebuildRecipes();
    }

    // ───────────────────── 제작 — 홀드 루프 ─────────────────────

    void OnCraftDown(PointerDownEvent e)
    {
        if (e.button != 0) return;
        holding = true;
    }

    void OnCraftUp(PointerUpEvent e)          { holding = false; }
    void OnCraftCaptureOut(PointerCaptureOutEvent e) { holding = false; }

    void Update()
    {
        if (!holding || selected == null || !CanCraftOnce(selected))
        {
            // 떼면 그 자리에서 멈춘다 — 소비 전이므로 잃는 것이 없다
            if (progress > 0f) { progress = 0f; RefreshProgress(); }
            return;
        }

        progress += Time.deltaTime;
        if (progress >= selected.craftTime)
        {
            CraftOnce(selected);
            progress = 0f;   // 계속 누르고 있으면 다음 1회가 바로 시작된다
        }
        RefreshProgress();
    }

    /// <summary>재료가 가방+핫바에 전부 있는가 — 진행 중에도 매 프레임 이것으로 판단한다.</summary>
    bool CanCraftOnce(RecipeDataSO r)
    {
        if (r == null || r.inputs == null || Main == null) return false;
        foreach (var input in r.inputs)
        {
            if (input.item == null) continue;
            if (CountAll(input.item) < input.amount) return false;
        }
        return true;
    }

    void CraftOnce(RecipeDataSO r)
    {
        foreach (var input in r.inputs)
            if (input.item != null) ConsumeAll(input.item, input.amount);

        var holder = PlayerInventoryHolder.Instance;
        foreach (var output in r.outputs)
        {
            if (output.item == null || output.amount <= 0) continue;

            // 가방이 가득이면 바닥에 떨어뜨린다 — 소비는 이미 일어났으므로 잃게 두면 안 된다
            if (!holder.AddItemToPlayer(output.item, output.amount))
                DropToWorld(output.item, output.amount);
        }

        InventoryManager.Instance?.RefreshAllGameUIs();   // uGUI 핫바 HUD·무기 장착 동기화
    }

    int CountAll(ItemDataSO item) =>
        (Main?.CountOf(item) ?? 0) + (Hotbar?.CountOf(item) ?? 0);

    /// <summary>가방부터 소비하고 모자란 만큼 핫바에서 — 핫바의 무기·탄약 배치를 지킨다.</summary>
    void ConsumeAll(ItemDataSO item, int n)
    {
        int fromMain = Mathf.Min(n, Main.CountOf(item));
        if (fromMain > 0) Main.TryConsume(item, fromMain);
        if (n - fromMain > 0) Hotbar.TryConsume(item, n - fromMain);
    }

    // ───────────────────── 레시피 목록 ─────────────────────

    /// <summary>해금된 레시피 전부 — 해금되면 전부 수동 제작이 가능하므로 "잠김" 줄이 없다.</summary>
    List<RecipeDataSO> UnlockedRecipes()
    {
        var db = RecipeDatabaseSO.LoadDefault();
        if (db == null || db.recipes == null) return new List<RecipeDataSO>();

        return SortRecipes(db.recipes.Where(RecipeDatabaseSO.IsUnlocked));
    }

    /// <summary>목록 순서는 아이템 목록과 같은 기준 — 대표 산출물의 티어 → 계통 → 용도 → 이름.</summary>
    static List<RecipeDataSO> SortRecipes(IEnumerable<RecipeDataSO> list) =>
        list.OrderBy(r => UIItemOrder.TierOf(PrimaryOutput(r)))
            .ThenBy(r => PrimaryOutput(r) != null ? (int)PrimaryOutput(r).line : int.MaxValue)
            .ThenBy(r => PrimaryOutput(r) != null ? (int)PrimaryOutput(r).type : int.MaxValue)
            .ThenBy(r => DisplayNameOf(PrimaryOutput(r)), System.StringComparer.Ordinal)
            .ToList();

    static ItemDataSO PrimaryOutput(RecipeDataSO r) =>
        r != null && r.outputs != null && r.outputs.Length > 0 ? r.outputs[0].item : null;

    void RebuildRecipes()
    {
        if (recipes == null) return;
        recipes.Clear();

        var list = UnlockedRecipes();

        // 선택이 목록에서 사라졌으면(검색이 아니라 데이터가 바뀐 경우) 첫 항목으로
        if (selected != null && !list.Contains(selected)) selected = null;

        int shown = 0;
        foreach (var r in list)
        {
            string name = DisplayNameOf(PrimaryOutput(r));
            if (search.Length > 0 && name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (selected == null) selected = r;
            recipes.Add(MakeRecipeRow(r, name));
            shown++;
        }

        Show(recipesEmpty, shown == 0);
        if (recipesEmpty != null && shown == 0)
            recipesEmpty.text = search.Length > 0 ? "검색 결과가 없습니다" : "해금된 레시피가 없습니다";

        RefreshDetail();
    }

    VisualElement MakeRecipeRow(RecipeDataSO r, string name)
    {
        var row = new VisualElement();
        row.AddToClassList("ui-row");
        row.AddToClassList("ui-row--compact");
        if (r == selected) row.AddToClassList("ui-row--selected");

        var ic = new VisualElement();
        ic.AddToClassList("ui-slot__icon");
        ic.AddToClassList("ui-slot__icon--xs");
        var item = PrimaryOutput(r);
        if (item != null) ic.style.backgroundColor = UIFlowColors.Of(item.line);
        row.Add(ic);

        var nm = new Label(name);
        nm.AddToClassList("ui-row__name");
        row.Add(nm);

        // 회당 시간 — 목록에서 레시피끼리 비교하는 용도 (문서: 시간이 나오는 세 자리 중 첫째)
        var meta = new Label($"{r.craftTime:0.0}s");
        meta.AddToClassList("ui-row__meta");
        row.Add(meta);

        var captured = r;
        row.RegisterCallback<ClickEvent>(_ =>
        {
            selected = captured;
            progress = 0f;
            RebuildRecipes();
        });
        return row;
    }

    // ───────────────────── 선택 레시피 상세 ─────────────────────

    void RefreshDetail()
    {
        if (detail == null) return;

        bool has = selected != null;
        detail.style.visibility = has ? Visibility.Visible : Visibility.Hidden;
        if (!has) return;

        var item = PrimaryOutput(selected);
        int per = selected.outputs != null && selected.outputs.Length > 0 ? selected.outputs[0].amount : 1;

        yieldIcon.style.backgroundColor = item != null ? UIFlowColors.Of(item.line) : UIFlowColors.Muted;
        yieldName.text = DisplayNameOf(item);
        if (item != null) yieldName.style.color = UIFlowColors.Of(item.line);
        yieldPer.text  = $"회당 {per}개 산출";
        yieldTime.text = $"◷ {selected.craftTime:0.0}s";   // 시간 둘째 자리 — 상세 배지

        RebuildMats();
        RefreshProgress();
    }

    void RebuildMats()
    {
        mats.Clear();
        if (selected?.inputs == null) return;

        foreach (var input in selected.inputs)
        {
            if (input.item == null) continue;

            int have = CountAll(input.item);

            var row = new VisualElement();
            row.AddToClassList("ui-mat");
            // 계통색 띠는 부족해도 그대로 — 어느 라인의 재료인지는 여전히 유효한 정보다
            var lineClass = UIItemPalette.MatClass(input.item);
            if (lineClass != null) row.AddToClassList(lineClass);

            var nm = new Label(DisplayNameOf(input.item));
            nm.AddToClassList("ui-mat__name");
            row.Add(nm);

            var need = new Label(input.amount.ToString());
            need.AddToClassList("ui-mat__n");
            row.Add(need);

            // 부족하면 보유 수만 붉어진다 (문서: 부족한 재료는 보유 수를 붉게)
            var have_ = new Label($"/ {have}");
            have_.AddToClassList("inv-mat__dim");
            if (have < input.amount) have_.AddToClassList("inv-mat__dim--short");
            row.Add(have_);

            mats.Add(row);
        }
    }

    /// <summary>진행 표시는 버튼 하나에만 — 채움과 남은 시간이 한 덩어리로 읽힌다 (문서 SCR-04).</summary>
    void RefreshProgress()
    {
        if (selected == null) return;

        bool crafting = holding && progress > 0f;
        float t = Mathf.Clamp01(selected.craftTime > 0f ? progress / selected.craftTime : 1f);

        SetBarFill(craftBtnFill, t);   // 버튼 안이 차오르면 한 개 완성

        // 진행 중에는 현재 1회분의 잔여, 아니면 회당 시간
        craftBtnTime.text = crafting
            ? $"{Mathf.Max(0f, selected.craftTime - progress):0.0}s"
            : $"{selected.craftTime:0.0}s";

        bool can = CanCraftOnce(selected);
        btnCraft.SetEnabled(can);
        ToggleClass(btnCraft, "ui-btn--disabled", !can);
        craftBtnText.text = can ? "누르고 있는 동안 제작" : MissingLabel();
    }

    /// <summary>버튼 라벨이 무엇이 없는지 말한다 — "재료 부족"만으로는 다시 재료 줄을 읽어야 한다.</summary>
    string MissingLabel()
    {
        string first = null;
        int count = 0;

        if (selected?.inputs != null)
            foreach (var input in selected.inputs)
            {
                if (input.item == null || CountAll(input.item) >= input.amount) continue;
                count++;
                first ??= DisplayNameOf(input.item);
            }

        return count switch
        {
            0 => "재료 부족",                       // 도달 불가 방어 — can=false인데 목록상 부족이 없는 경우
            1 => $"{first} 부족",
            _ => $"{first} 외 {count - 1}종 부족",
        };
    }

    // ───────────────────── 슬롯 격자 ─────────────────────

    const int Columns = 9;   // 문서 SCR-04 — 9열. 가방 18칸(기본)이면 2줄

    void RebuildGrids()
    {
        if (grid == null) return;

        grid.Clear();
        var main = Main;
        if (main != null)
        {
            // flex-wrap에 맡기지 않고 9칸씩 행을 직접 만든다 — 분배기 격자와 같은 이유
            // (반올림 오차로 줄바꿈이 계산과 어긋나는 일이 구조적으로 없다)
            VisualElement row = null;
            for (int i = 0; i < main.SlotCount; i++)
            {
                if (i % Columns == 0)
                {
                    row = new VisualElement();
                    row.AddToClassList("inv-grid__row");
                    grid.Add(row);
                }
                var slot = MakeSlot(main, i, keyLabel: null);
                if (i % Columns == Columns - 1) slot.AddToClassList("ui-slot--last");
                row.Add(slot);
            }
        }

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

    VisualElement MakeSlot(ItemContainer container, int index, string keyLabel)
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
        if (!empty) icon.style.backgroundColor = UIFlowColors.Of(stack.item.line);
        slot.Add(icon);

        if (!empty)
        {
            var n = new Label(stack.amount.ToString());
            n.AddToClassList("ui-slot__n");
            slot.Add(n);
        }

        var c = container; var i = index;
        slot.RegisterCallback<PointerDownEvent>(e => OnSlotPointerDown(e, c, i));
        return slot;
    }

    // ───────────────── 슬롯 조작 — 캐리지 방식 ─────────────────
    // uGUI InventoryManager와 같은 문법: 좌클릭 집기/놓기/합치기/교환,
    // 우클릭 절반 집기/한 개 놓기, Shift+클릭 가방↔핫바 빠른 이동.
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
        InventoryManager.Instance?.RefreshAllGameUIs();   // uGUI 핫바 HUD·무기 장착 동기화
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
            if (container.TryPutAt(index, carried)) carried = null;
        }
        else if (target.item == carried.item)
        {
            int add = Mathf.Min(target.maxStackSize - target.amount, carried.amount);
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
        else if (target.item == carried.item && target.amount < target.maxStackSize)
        {
            target.amount++;
            carried.amount--;
            container.Touch();
        }

        if (carried.amount <= 0) carried = null;
    }

    /// <summary>Shift+클릭 — 가방↔핫바 반대편으로 보낸다 (SCR-04에는 상자가 없다).</summary>
    void QuickMove(ItemContainer src, int index)
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
    static void MoveStack(ItemStack src, ItemContainer dst)
    {
        for (int i = 0; i < dst.SlotCount && src.amount > 0; i++)
        {
            var t = dst.PeekAt(i);
            if (t == null || t.item != src.item || t.amount >= t.maxStackSize) continue;
            int add = Mathf.Min(t.maxStackSize - t.amount, src.amount);
            t.amount += add;
            src.amount -= add;
            dst.Touch();
        }
        for (int i = 0; i < dst.SlotCount && src.amount > 0; i++)
        {
            var t = dst.PeekAt(i);
            if (t != null && t.item != null) continue;
            int add = Mathf.Min(src.maxStackSize, src.amount);
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
        InventoryManager.Instance?.RefreshAllGameUIs();
    }

    void MoveCarry(Vector2 panelPos)
    {
        if (carry == null) return;
        carry.style.left = panelPos.x - 22f;
        carry.style.top  = panelPos.y - 22f;
    }

    void RefreshCarry()
    {
        if (carry == null) return;
        bool has = carried != null && carried.item != null && carried.amount > 0;
        carry.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
        if (!has) return;

        carryIcon.style.backgroundColor = UIFlowColors.Of(carried.item.line);
        carryCount.text = carried.amount.ToString();
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
        InventoryManager.Instance?.RefreshAllGameUIs();
    }

    static void DropToWorld(ItemDataSO item, int amount)
    {
        var pc = InventoryManager.Instance != null ? InventoryManager.Instance.playerController : null;
        if (pc == null) return;   // 떨굴 위치가 없다 — 이 경로는 플레이어 없는 씬뿐

        Vector3 pos = pc.transform.position + pc.playerCamera.forward * 1.5f + Vector3.up * 0.5f;
        DroppedItem.Spawn(item, amount, pos, pc.playerCamera.forward);
    }

    // ───────────────────── 잡동사니 ─────────────────────

    static string DisplayNameOf(ItemDataSO item) =>
        item == null ? "" : string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;

    static void Show(VisualElement e, bool on)
    {
        if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
