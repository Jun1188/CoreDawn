using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// SCR-03 건설 메뉴 — UITK 이관 2단계.
///
/// 기존 BuildMenuPopup이 런타임에 new GameObject로 조립하던 화면을 UXML로 옮긴 것.
/// 조립부가 사라지고 바인딩만 남는다 (문서 SCR-03 주석).
///
/// 설계와 맞춘 동작 두 가지:
///   - 목록에는 이름만. 입력 슬롯·건설 비용 같은 수치는 커서 옆 툴팁으로 뺀다.
///   - 잠긴 건물을 숨기지 않고 해금 조건과 함께 보여준다. 다음 테크 목표가
///     자연스럽게 노출되는 편이 게이트를 향한 동기로 강하게 작동한다.
///     (기존 구현은 IsTierUnlocked가 false면 목록에서 아예 뺐다)
/// </summary>
[DefaultExecutionOrder(100)]
public class BuildMenuView : UITKPopup
{
    static BuildMenuView cached;

    PlacementSystem placement;
    BuildingCategory category = BuildingCategory.Production;
    BuildingDataSO selected;

    VisualElement tabs, grid, hints, actionsTooltip;
    Label emptyNote, tipName, tipType, tipDesc;
    VisualElement tipStats, tipCost, tipSepStats, tipSepCost;
    Button btnClose, btnPlace;

    readonly List<Button> tabButtons = new();

    // ───────────────────────── 열기 ─────────────────────────

    /// <summary>씬에 이 패널이 있으면 토글하고 true. 없으면 false — 호출부가 기존 uGUI로 넘어간다.</summary>
    public static bool TryToggle(PlacementSystem placement)
    {
        if (cached == null)
            cached = FindFirstObjectByType<BuildMenuView>(FindObjectsInactive.Include);
        if (cached == null) return false;

        if (cached.isActiveAndEnabled)
        {
            cached.Close();
            return true;
        }

        cached.placement = placement;
        cached.gameObject.SetActive(true);
        return true;
    }

    public override bool OnInput(in InputEvent e)
    {
        // B로도 닫기 — 연 키로 다시 닫는 대칭 조작 (기존 BuildMenuPopup과 동일)
        if (e.Phase == InputActionPhase.Performed && e.Id == InputActionId.ToggleBuild)
        {
            Close();
            return true;
        }
        return base.OnInput(e);
    }

    // ───────────────────── UITKPopup 계약 ─────────────────────

    protected override void Bind()
    {
        var r = Root;
        tabs = r.Q("tabs");
        grid = r.Q("grid");
        hints = r.Q("hints");
        emptyNote = r.Q<Label>("empty-note");

        btnClose = r.Q<Button>("btn-close");
        btnPlace = r.Q<Button>("btn-place");

        actionsTooltip = r.Q("tooltip");
        tipName = r.Q<Label>("tip-name");
        tipType = r.Q<Label>("tip-type");
        tipDesc = r.Q<Label>("tip-desc");
        tipStats = r.Q("tip-stats");
        tipCost = r.Q("tip-cost");
        tipSepStats = r.Q("tip-sep-stats");
        tipSepCost = r.Q("tip-sep-cost");

        btnClose.clicked += Close;
        btnPlace.clicked += StartPlacing;

        selected = null;
        HideTooltip();
        RebuildTabs();
        RebuildGrid();
    }

    protected override void Unbind()
    {
        if (btnClose != null) btnClose.clicked -= Close;
        if (btnPlace != null) btnPlace.clicked -= StartPlacing;
        selected = null;
    }

    // ───────────────────────── 목록 ─────────────────────────

    BuildingDatabaseSO Database =>
        placement != null && placement.Database != null ? placement.Database : BuildingDatabaseSO.LoadDefault();

    /// <summary>탭은 DB에 실제로 항목이 있는 카테고리만 만든다 — 빈 탭을 두지 않는다.</summary>
    void RebuildTabs()
    {
        tabs.Clear();
        tabButtons.Clear();

        var db = Database;
        if (db == null) return;

        bool categoryStillExists = false;

        foreach (var (cat, items) in db.GroupedByCategory())
        {
            if (!HasAnyVisible(items)) continue;
            if (cat == category) categoryStillExists = true;

            var c = cat;
            var btn = new Button(() => { category = c; RebuildTabs(); RebuildGrid(); })
            {
                text = BuildingCategoryNames.Korean(cat),
            };
            btn.AddToClassList("ui-tab");
            ToggleClass(btn, "ui-tab--active", cat == category);
            tabs.Add(btn);
            tabButtons.Add(btn);
        }

        // 선택 중이던 카테고리가 사라졌으면 첫 탭으로
        if (!categoryStillExists && tabButtons.Count > 0)
        {
            foreach (var (cat, items) in db.GroupedByCategory())
            {
                if (!HasAnyVisible(items)) continue;
                category = cat;
                break;
            }
            RebuildTabs();
        }
    }

    static bool HasAnyVisible(List<BuildingDataSO> items)
    {
        foreach (var so in items)
            if (so != null && !so.hideFromBuildMenu) return true;
        return false;
    }

    void RebuildGrid()
    {
        grid.Clear();
        selected = null;

        var db = Database;
        int shown = 0;

        if (db != null)
        {
            foreach (var (cat, items) in db.GroupedByCategory())
            {
                if (cat != category) continue;

                foreach (var so in items)
                {
                    if (so == null || so.hideFromBuildMenu) continue;
                    grid.Add(MakeCell(so));
                    shown++;
                }
            }
        }

        Show(emptyNote, shown == 0);
        RefreshActions();
    }

    VisualElement MakeCell(BuildingDataSO so)
    {
        bool unlocked = IsUnlocked(so);

        var cell = new VisualElement();
        cell.AddToClassList("build-cell");

        var row = new VisualElement();
        row.AddToClassList("ui-row");
        ToggleClass(row, "ui-row--locked", !unlocked);

        var icon = new VisualElement();
        icon.AddToClassList("ui-slot__icon");
        icon.AddToClassList("ui-slot__icon--xs");
        if (so.icon != null) icon.style.backgroundImage = new StyleBackground(so.icon);
        row.Add(icon);

        var name = new Label(DisplayNameOf(so));
        name.AddToClassList("ui-row__name");
        row.Add(name);

        // 잠긴 항목만 예외로 해금 조건을 줄에 남긴다 — 고르기 전에 알아야 하는 정보다
        if (!unlocked)
        {
            var meta = new Label($"게이트 {so.requiredCoreTier}");
            meta.AddToClassList("ui-row__meta");
            row.Add(meta);
        }

        row.RegisterCallback<PointerEnterEvent>(_ => ShowTooltip(so));
        row.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
        row.RegisterCallback<PointerMoveEvent>(e => MoveTooltip(e.position));

        if (unlocked)
            row.RegisterCallback<ClickEvent>(_ => Select(so, row));

        cell.Add(row);
        return cell;
    }

    void Select(BuildingDataSO so, VisualElement row)
    {
        selected = so;

        foreach (var cell in grid.Children())
            foreach (var child in cell.Children())
                ToggleClass(child, "ui-row--selected", child == row);

        RefreshActions();
    }

    static bool IsUnlocked(BuildingDataSO so) =>
        GameManager.Instance == null || GameManager.Instance.IsTierUnlocked(so.requiredCoreTier);

    // ───────────────────── 하단 힌트·버튼 ─────────────────────

    /// <summary>
    /// 조작 힌트는 선택한 건물에 따라 바뀐다 — 벨트를 고르면 T(모양 변경)가 나타나고,
    /// 아무것도 안 골랐으면 배치 관련 힌트를 보여줄 이유가 없다.
    /// </summary>
    void RefreshActions()
    {
        hints.Clear();

        if (selected != null)
        {
            hints.Add(KeyHint("R", "회전"));
            if (selected is BeltDataSO) hints.Add(KeyHint("T", "모양 변경"));
            hints.Add(KeyHint("LMB", "배치"));
            hints.Add(KeyHint("RMB", "취소"));
        }

        btnPlace.SetEnabled(selected != null);
        btnPlace.text = selected != null ? "배치 시작" : "건물을 고르세요";
    }

    static VisualElement KeyHint(string cap, string label)
    {
        var e = new VisualElement();
        e.AddToClassList("ui-key");

        var k = new Label(cap);
        k.AddToClassList("ui-key__cap");
        e.Add(k);
        e.Add(new Label(label));
        return e;
    }

    void StartPlacing()
    {
        if (selected == null || placement == null) return;
        placement.SelectBuilding(selected);
        Close();   // 선택 즉시 배치 모드로 — 기존 동작 유지
    }

    // ───────────────────────── 툴팁 ─────────────────────────

    void ShowTooltip(BuildingDataSO so)
    {
        tipName.text = DisplayNameOf(so);
        tipType.text = $"TIER {so.requiredCoreTier} · {BuildingCategoryNames.Korean(so.category)}";

        tipDesc.text = so.description ?? "";
        Show(tipDesc, !string.IsNullOrEmpty(so.description));

        // 구분선 위는 정체성, 아래는 수치 — 순서를 섞지 않으면 눈이 위치를 학습한다
        tipStats.Clear();
        tipStats.Add(TooltipStat("크기", $"{so.size.x} × {so.size.y}"));

        // 라벨과 칩을 같은 줄에 흘린다
        tipCost.Clear();
        var costLabel = new Label("건설 비용");
        costLabel.AddToClassList("build-tooltip__cost-label");
        tipCost.Add(costLabel);

        int chips = 0;
        foreach (var e in BuildCostDummyData.For(so))
        {
            var chip = new VisualElement();
            chip.AddToClassList("ui-chip");
            var suffix = UIItemPalette.SuffixOf(e.Line);
            if (suffix != null) chip.AddToClassList("ui-chip--" + suffix);

            chip.Add(new Label(e.Name));
            var n = new Label(e.Amount.ToString());
            n.AddToClassList("ui-chip__n");
            chip.Add(n);
            tipCost.Add(chip);
            chips++;
        }

        Show(tipSepStats, true);
        Show(tipCost, chips > 0);
        Show(tipSepCost, chips > 0);

        actionsTooltip.RemoveFromClassList("ui-hidden");
    }

    static VisualElement TooltipStat(string label, string value)
    {
        var row = new VisualElement();
        row.AddToClassList("ui-tooltip__stat");
        row.Add(new Label(label));

        var v = new Label(value);
        v.AddToClassList("ui-tooltip__stat-value");
        row.Add(v);
        return row;
    }

    void HideTooltip() => actionsTooltip?.AddToClassList("ui-hidden");

    /// <summary>커서 우하단 12px에 붙이고, 화면 밖으로 나가면 반대편으로 뒤집는다.</summary>
    void MoveTooltip(Vector2 pointer)
    {
        if (actionsTooltip == null || Root == null) return;

        const float Gap = 12f;
        float w = actionsTooltip.resolvedStyle.width;
        float h = actionsTooltip.resolvedStyle.height;
        float rootW = Root.resolvedStyle.width;
        float rootH = Root.resolvedStyle.height;

        float x = pointer.x + Gap;
        float y = pointer.y + Gap;

        if (!float.IsNaN(w) && x + w > rootW) x = pointer.x - Gap - w;
        if (!float.IsNaN(h) && y + h > rootH) y = pointer.y - Gap - h;

        actionsTooltip.style.left = Mathf.Max(0f, x);
        actionsTooltip.style.top = Mathf.Max(0f, y);
    }

    // ───────────────────────── 잡동사니 ─────────────────────────

    static string DisplayNameOf(BuildingDataSO so) =>
        so == null ? "" : string.IsNullOrEmpty(so.displayName) ? so.name : so.displayName;

    static void Show(VisualElement e, bool on)
    {
        if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
