using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// SCR-09 설비 — 레시피와 가동 상태.
///
/// 제련로·제작기·조립기·제조기가 이 화면 하나를 쓴다 — 데이터상 전부 같은 SO이고
/// 입력 슬롯 수(1·1·2·4)만 다르므로 IN 칸이 그 수만큼 늘어날 뿐이다.
/// 입력은 세로로 쌓다가 넷이 되면 2×2로 접고, 입력이 하나면 남는 폭을 진행바가 쓴다.
///
/// 시작/중지는 상태줄 오른쪽 끝 — 멈춰도 진행률과 버퍼는 보존되므로
/// "잠깐 자원을 아끼려고 세워둔다"가 안전한 조작이 된다.
/// </summary>
[DefaultExecutionOrder(100)]
public class MachinePanelView : UITKPopup
{
    static MachinePanelView cached;

    AssemblerBehavior target;
    string search = "";

    VisualElement recipes, detail, inSlots, outSlots, ioArrow, machineBar, machineFill, statusBadge;
    VisualElement yieldIcon;
    Label machineName, yieldName, yieldSub, yieldTime, machinePct, machineEta, statusText, statusNote, recipesEmpty;
    Button btnClose, btnToggle;
    TextField searchField;

    MachineState shownState = (MachineState)(-1);
    bool shownPaused;
    UITooltip tooltip;

    // ───────────────────────── 열기 ─────────────────────────

    /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false — 설비 상호작용이 조용히 무시된다.</summary>
    public static bool TryOpen(AssemblerBehavior machine)
    {
        if (machine == null) return false;

        if (cached == null)
            cached = FindFirstObjectByType<MachinePanelView>(FindObjectsInactive.Include);
        if (cached == null) return false;

        if (cached.isActiveAndEnabled)
        {
            cached.Retarget(machine);
            return true;
        }

        cached.target = machine;      // OnEnable → Bind 전에 넣어야 한다
        cached.gameObject.SetActive(true);
        return true;
    }

    void Retarget(AssemblerBehavior machine)
    {
        UnhookContainers();
        target = machine;
        HookContainers();
        search = "";
        if (searchField != null) searchField.SetValueWithoutNotify("");
        RebuildAll();
    }

    // ───────────────────── UITKPopup 계약 ─────────────────────

    protected override void Bind()
    {
        var r = Root;
        recipes      = r.Q("recipes");
        recipesEmpty = r.Q<Label>("recipes-empty");
        detail       = r.Q("detail");
        inSlots      = r.Q("in-slots");
        outSlots     = r.Q("out-slots");
        ioArrow      = r.Q("io-arrow");
        machineBar   = r.Q("machine-bar");
        machineFill  = r.Q("machine-fill");
        statusBadge  = r.Q("status");
        yieldIcon    = r.Q("yield-icon");

        machineName = r.Q<Label>("machine-name");
        yieldName   = r.Q<Label>("yield-name");
        yieldSub    = r.Q<Label>("yield-sub");
        yieldTime   = r.Q<Label>("yield-time");
        machinePct  = r.Q<Label>("machine-pct");
        machineEta  = r.Q<Label>("machine-eta");
        statusText  = r.Q<Label>("status-text");
        statusNote  = r.Q<Label>("status-note");

        btnClose  = r.Q<Button>("btn-close");
        btnToggle = r.Q<Button>("btn-toggle");
        searchField = r.Q<TextField>("machine-search");

        btnClose.clicked += Close;
        btnToggle.clicked += TogglePaused;
        searchField.RegisterValueChangedCallback(OnSearchChanged);

        // 돋보기 — USS에 SVG가 없어 요소로 넣는다. Bind가 다시 돌아도 하나만
        var searchBox = r.Q("machine-search-box");
        if (searchBox != null && searchBox.Q<SearchGlyph>() == null)
            searchBox.Insert(0, new SearchGlyph());

        tooltip = new UITooltip(r);

        RecipeRewardUnlockService.RecipeUnlocked += OnRecipeRewardUnlocked;

        HookContainers();
        shownState = (MachineState)(-1);
        RebuildAll();
    }

    protected override void Unbind()
    {
        if (btnClose != null) btnClose.clicked -= Close;
        if (btnToggle != null) btnToggle.clicked -= TogglePaused;
        if (searchField != null) searchField.UnregisterValueChangedCallback(OnSearchChanged);
        RecipeRewardUnlockService.RecipeUnlocked -= OnRecipeRewardUnlocked;

        tooltip?.Dispose();
        tooltip = null;

        UnhookContainers();
        target = null;
    }

    void HookContainers()
    {
        var b = target?.Building;
        if (b == null) return;
        b.Input.Changed  += RebuildSlots;
        b.Output.Changed += RebuildSlots;
    }

    void UnhookContainers()
    {
        var b = target?.Building;
        if (b == null) return;
        b.Input.Changed  -= RebuildSlots;
        b.Output.Changed -= RebuildSlots;
    }

    void OnSearchChanged(ChangeEvent<string> e)
    {
        search = e.newValue ?? "";
        RebuildRecipes();
    }

    void OnRecipeRewardUnlocked(RecipeDataSO _) => RebuildRecipes();

    void TogglePaused()
    {
        if (target == null) return;
        target.SetPaused(!target.Paused);
        RefreshRunning(force: true);
    }

    void RebuildAll()
    {
        if (target == null) return;
        machineName.text = string.IsNullOrEmpty(target.Data.displayName) ? target.Data.name : target.Data.displayName;
        RebuildRecipes();
        RebuildSlots();
        RefreshDetailHead();
        RefreshRunning(force: true);
    }

    // ── 진행·상태는 매 프레임 변하는 값 — 폴링하되 바뀐 것만 다시 만든다 ──

    void Update()
    {
        if (target == null) return;
        RefreshRunning(force: false);
    }

    // ───────────────────── 레시피 목록 ─────────────────────

    void RebuildRecipes()
    {
        if (recipes == null || target == null) return;
        recipes.Clear();
        tooltip?.Hide();   // 호버 중이던 행이 교체되면 Leave가 안 온다

        var list = target.GetUnlockedRecipes()
            .OrderBy(r => UIItemOrder.TierOf(PrimaryOutput(r)))
            .ThenBy(r => PrimaryOutput(r) != null ? (int)PrimaryOutput(r).line : int.MaxValue)
            .ThenBy(r => PrimaryOutput(r) != null ? (int)PrimaryOutput(r).type : int.MaxValue)
            .ThenBy(r => DisplayNameOf(PrimaryOutput(r)), System.StringComparer.Ordinal)
            .ToList();

        int shown = 0;
        foreach (var r in list)
        {
            string name = DisplayNameOf(PrimaryOutput(r));
            if (search.Length > 0 && name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            recipes.Add(MakeRecipeRow(r, name));
            shown++;
        }

        Show(recipesEmpty, shown == 0);
        if (recipesEmpty != null && shown == 0)
            recipesEmpty.text = search.Length > 0 ? "검색 결과가 없습니다" : "돌릴 수 있는 레시피가 없습니다";
    }

    VisualElement MakeRecipeRow(RecipeDataSO r, string name)
    {
        var row = new VisualElement();
        row.AddToClassList("ui-row");
        row.AddToClassList("ui-row--compact");
        if (r == target.CurrentRecipe) row.AddToClassList("ui-row--selected");

        var ic = new VisualElement();
        ic.AddToClassList("ui-slot__icon");
        var item = PrimaryOutput(r);
        if (item != null) ic.style.backgroundColor = UIFlowColors.Of(item.line);
        row.Add(ic);

        var nm = new Label(name);
        nm.AddToClassList("ui-row__name");
        row.Add(nm);

        var meta = new Label($"{r.craftTime:0.0}s");
        meta.AddToClassList("ui-row__meta");
        row.Add(meta);

        tooltip?.AttachRecipe(row, r);

        var captured = r;
        row.RegisterCallback<ClickEvent>(_ =>
        {
            // 진행 중이던 조합은 심이 취소하고 재료를 되돌린다 (AssemblerBehavior.SetRecipe)
            target.SetRecipe(captured);
            RebuildAll();
        });
        return row;
    }

    static ItemDataSO PrimaryOutput(RecipeDataSO r) =>
        r != null && r.outputs != null && r.outputs.Length > 0 ? r.outputs[0].item : null;

    // ───────────────────── 상세 — 산출 머리글 ─────────────────────

    void RefreshDetailHead()
    {
        var recipe = target.CurrentRecipe;
        bool has = recipe != null;
        detail.style.visibility = has ? Visibility.Visible : Visibility.Hidden;
        if (!has) return;

        var item = PrimaryOutput(recipe);
        int per = recipe.outputs != null && recipe.outputs.Length > 0 ? recipe.outputs[0].amount : 1;

        yieldIcon.style.backgroundColor = item != null ? UIFlowColors.Of(item.line) : UIFlowColors.Muted;
        yieldName.text = DisplayNameOf(item);
        if (item != null) yieldName.style.color = UIFlowColors.Of(item.line);

        // N개/분 — 벨트 처리량과 비교해 라인을 몇 개 물릴지 계산하는 값이라 항상 띄운다
        float perMinute = recipe.craftTime > 0f ? 60f / recipe.craftTime * per : 0f;
        yieldSub.text = $"회당 {per}개 · {Mathf.RoundToInt(perMinute)}개 / 분";
        yieldTime.text = $"◷ {recipe.craftTime:0.0}s";
    }

    // ───────────────────── IO 슬롯 ─────────────────────

    void RebuildSlots()
    {
        var b = target?.Building;
        if (b == null || inSlots == null) return;

        // 입력: 세로로 쌓다가 넷이면 2×2. 입력이 하나면 남는 폭을 진행바가 쓴다
        ToggleClass(inSlots, "ui-io__slots--grid", b.Input.SlotCount >= 4);
        ToggleClass(ioArrow, "ui-io__arrow--wide", b.Input.SlotCount <= 1);

        tooltip?.Hide();   // 슬롯 교체 중 Leave 유실 방어
        FillSlots(inSlots, b.Input);
        FillSlots(outSlots, b.Output);
    }

    void FillSlots(VisualElement parent, ItemContainer c)
    {
        parent.Clear();
        for (int i = 0; i < c.SlotCount; i++)
        {
            var stack = c.PeekAt(i);
            bool empty = stack == null || stack.item == null || stack.amount <= 0;

            var slot = new VisualElement();
            slot.AddToClassList("ui-slot");
            if (empty) slot.AddToClassList("ui-slot--empty");
            if (i == c.SlotCount - 1) slot.AddToClassList("ui-slot--last");

            var icon = new VisualElement();
            icon.AddToClassList("ui-slot__icon");
            if (!empty) icon.style.backgroundColor = UIFlowColors.Of(stack.item.line);
            slot.Add(icon);

            if (!empty)
            {
                var n = new Label(stack.amount.ToString());
                n.AddToClassList("ui-slot__n");
                slot.Add(n);

                tooltip?.AttachItem(slot, stack.item);
            }

            parent.Add(slot);
        }
    }

    // ───────────────────── 진행·상태 ─────────────────────

    void RefreshRunning(bool force)
    {
        if (target == null || target.CurrentRecipe == null) return;

        // 진행바·잔여는 매 프레임, 상태 배지·버튼은 바뀔 때만
        SetBarFill(machineFill, target.Progress);
        machinePct.text = $"{Mathf.RoundToInt(target.Progress * 100f)}%";

        var state = target.State;
        machineEta.text = state switch
        {
            MachineState.Running => $"{target.RemainingTime:0.0}s",
            MachineState.Stopped => "멈춤",
            _                    => "대기",
        };

        if (!force && state == shownState && target.Paused == shownPaused) return;
        shownState = state;
        shownPaused = target.Paused;

        ToggleClass(machineBar, "ui-machine--paused", target.Paused);

        foreach (var cls in new[] { "ui-status--run", "ui-status--wait", "ui-status--block", "ui-status--stop" })
            statusBadge.RemoveFromClassList(cls);
        (string badgeClass, string text) = state switch
        {
            MachineState.Running      => ("ui-status--run", "가동 중"),
            MachineState.WaitingInput => ("ui-status--wait", "재료 대기"),
            MachineState.OutputBlocked=> ("ui-status--block", "출력 막힘"),
            _                         => ("ui-status--stop", "중지됨"),
        };
        statusBadge.AddToClassList(badgeClass);
        statusText.text = text;

        // 참고문 — 가동 중엔 소비량, 중지 중엔 보존 안내. 나머지는 상태 배지가 이미 말한다
        statusNote.text = state switch
        {
            MachineState.Running => ConsumptionText(target.CurrentRecipe),
            MachineState.Stopped => "진행은 그대로 보존된다",
            _                    => "",
        };

        btnToggle.text = target.Paused ? "▶ 시작" : "■ 중지";
        ToggleClass(btnToggle, "ui-btn--primary", target.Paused);
    }

    static string ConsumptionText(RecipeDataSO r)
    {
        if (r?.inputs == null || r.inputs.Length == 0) return "";
        return string.Join(" · ", r.inputs
            .Where(i => i.item != null)
            .Select(i => $"{DisplayNameOf(i.item)} {i.amount}")) + " 소비";
    }

    // ───────────────────── 잡동사니 ─────────────────────

    static string DisplayNameOf(ItemDataSO item) =>
        item == null ? "" : string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;

    static void Show(VisualElement e, bool on)
    {
        if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
