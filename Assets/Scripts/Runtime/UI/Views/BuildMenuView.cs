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

    VisualElement tabs, grid, hints, detMeta, detCost;
    Label emptyNote, detName, detType, detDesc, detSize, detCostLabel, detEmpty;
    Button btnClose;

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

        detName = r.Q<Label>("det-name");
        detType = r.Q<Label>("det-type");
        detDesc = r.Q<Label>("det-desc");
        detSize = r.Q<Label>("det-size");
        detCostLabel = r.Q<Label>("det-cost-label");
        detCost = r.Q("det-cost");
        detMeta = r.Q("det-meta");
        detEmpty = r.Q<Label>("det-empty");

        btnClose.clicked += Close;

        ClearDetails();
        RebuildTabs();
        RebuildGrid();
    }

    protected override void Unbind()
    {
        if (btnClose != null) btnClose.clicked -= Close;
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
        ClearDetails();
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

        row.RegisterCallback<PointerEnterEvent>(_ => ShowDetails(so, unlocked));
        row.RegisterCallback<PointerLeaveEvent>(_ => ClearDetails());

        // 고르는 즉시 배치 모드로 — 한 번 더 누르게 하지 않는다
        if (unlocked)
            row.RegisterCallback<ClickEvent>(_ => StartPlacing(so));

        cell.Add(row);
        return cell;
    }

    void StartPlacing(BuildingDataSO so)
    {
        if (so == null || placement == null) return;
        placement.SelectBuilding(so);
        Close();
    }

    static bool IsUnlocked(BuildingDataSO so) =>
        GameManager.Instance == null || GameManager.Instance.IsTierUnlocked(so.requiredCoreTier);

    // ───────────────────── 하단 힌트·버튼 ─────────────────────

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

    // ───────────────────── 하단 상세 (hover) ─────────────────────

    /// <summary>커서를 올린 건물의 정보를 하단 고정 자리에 채운다.</summary>
    void ShowDetails(BuildingDataSO so, bool unlocked)
    {
        Show(detEmpty, false);
        Show(detName, true);
        Show(detType, true);
        Show(detMeta, true);

        detName.text = DisplayNameOf(so);
        detType.text = unlocked
            ? $"TIER {so.requiredCoreTier} · {BuildingCategoryNames.Korean(so.category)}"
            : $"TIER {so.requiredCoreTier} · {BuildingCategoryNames.Korean(so.category)} · 게이트 {so.requiredCoreTier} 필요";

        detDesc.text = so.description ?? "";
        Show(detDesc, !string.IsNullOrEmpty(so.description));

        detSize.text = $"크기 {so.size.x} × {so.size.y}";

        detCost.Clear();
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
            detCost.Add(chip);
            chips++;
        }
        Show(detCostLabel, chips > 0);

        // 조작 힌트는 이 건물에 실제로 쓰이는 것만 — 벨트에서만 T(모양 변경)가 뜬다
        hints.Clear();
        if (unlocked)
        {
            hints.Add(KeyHint("R", "회전"));
            if (so is BeltDataSO) hints.Add(KeyHint("T", "모양 변경"));
            hints.Add(KeyHint("LMB", "배치"));
            hints.Add(KeyHint("RMB", "취소"));
        }
    }

    void ClearDetails()
    {
        Show(detEmpty, true);
        Show(detName, false);
        Show(detType, false);
        Show(detDesc, false);
        Show(detMeta, false);
        hints?.Clear();
    }

    // ───────────────────────── 잡동사니 ─────────────────────────

    static string DisplayNameOf(BuildingDataSO so) =>
        so == null ? "" : string.IsNullOrEmpty(so.displayName) ? so.name : so.displayName;

    static void Show(VisualElement e, bool on)
    {
        if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
