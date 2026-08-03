using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 빌드 메뉴 — BuildingDatabase를 읽어 카테고리별 버튼을 자동 생성하는 파이프라인 팝업.
/// SO를 만들기만 하면 (스캐너가 DB에 등록 →) 여기 버튼이 저절로 생긴다. 수동 연결 없음.
///
/// UI는 런타임에 코드로 조립한다 — 씬/프리팹 저작 불필요. UI 담당이 다듬고 싶으면
/// 같은 이름의 패널을 프리팹으로 만들어 교체하면 된다 (표면: Open/Toggle/Close).
/// B키로 열고 닫기(UI 맵의 ToggleBuild), ESC 닫기·모달은 UIPopup 공통.
/// </summary>
public class BuildMenuPopup : UIPopup
{
    private static BuildMenuPopup instance;

    private PlacementSystem placement;

    protected override void OnEnable()
    {
        base.OnEnable();   // 리시버 등록 + UI 맵 Push
        Cursor.lockState = CursorLockMode.None;   // 메뉴 조작용 커서 해제 (PausePopup 패턴)
        Cursor.visible = true;
    }

    protected override void OnDisable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        base.OnDisable();
    }

    /// <summary>메뉴 토글 — 최초 호출 시 Canvas 아래에 패널을 생성한다 (BuildController가 호출).</summary>
    public static void Toggle(PlacementSystem placement)
    {
        if (instance == null) instance = CreatePanel(placement);
        else if (!instance.gameObject.activeSelf) instance.Rebuild(); // DB 변경 대응 — 열 때마다 재구성

        instance.gameObject.SetActive(!instance.gameObject.activeSelf);
    }

    public override bool OnInput(in InputEvent e)
    {
        // B(UI 맵의 ToggleBuild)로도 닫기 — 연 키로 다시 닫는 대칭 조작
        if (e.Phase == InputActionPhase.Performed && e.Id == InputActionId.ToggleBuild)
        {
            Close();
            return true;
        }
        return base.OnInput(e);   // Cancel(ESC) 닫기 + 모달 삼킴
    }

    // ───────────────────────── 런타임 UI 조립 ─────────────────────────

    private static BuildMenuPopup CreatePanel(PlacementSystem placement)
    {
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[BuildMenu] 씬에 Canvas가 없습니다.");
            return null;
        }

        var panelGO = new GameObject("BuildMenu_Panel(Auto)", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(canvas.transform, false);

        var rt = (RectTransform)panelGO.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(640, 520);

        panelGO.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.95f);

        var layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 8;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        // 비활성 상태에서 팝업 부착 — 부착 즉시 OnEnable(UI 맵 Push)이 발화하는 것을 방지
        panelGO.SetActive(false);
        var popup = panelGO.AddComponent<BuildMenuPopup>();
        popup.placement = placement;
        popup.Rebuild();
        return popup;
    }

    /// <summary>데이터베이스 기준으로 버튼 전체 재구성 — 열 때마다 호출 (SO 추가/삭제 반영).</summary>
    private void Rebuild()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        MakeLabel(transform, "건설 메뉴", 22, FontStyles.Bold);

        var db = placement != null ? placement.Database : BuildingDatabaseSO.LoadDefault();
        if (db == null)
        {
            MakeLabel(transform, "BuildingDatabase 없음 (Resources 확인)", 16, FontStyles.Italic);
            return;
        }

        foreach (var (category, items) in db.GroupedByCategory())
        {
            MakeLabel(transform, BuildingCategoryNames.Korean(category), 17, FontStyles.Bold);

            var grid = new GameObject("Grid", typeof(RectTransform)).AddComponent<GridLayoutGroup>();
            grid.transform.SetParent(transform, false);
            grid.cellSize = new Vector2(96, 108);
            grid.spacing = new Vector2(8, 8);

            foreach (var so in items)
            {
                if (so.hideFromBuildMenu) continue;
                if (GameManager.Instance != null && !GameManager.Instance.IsTierUnlocked(so.requiredCoreTier)) continue;
                MakeButton(grid.transform, so);
            }
        }
    }

    private void MakeButton(Transform parent, BuildingDataSO so)
    {
        // 재료가 모자라면 옅게 — 왜 못 짓는지는 이름 아래 줄이 말해준다
        bool affordable = BuildCost.CanAfford(so);

        var go = new GameObject(so.name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = affordable
            ? new Color(0.22f, 0.22f, 0.26f, 1f)
            : new Color(0.22f, 0.22f, 0.26f, 0.45f);

        // 아이콘 (SO에 지정돼 있을 때만)
        if (so.icon != null)
        {
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(go.transform, false);
            var irt = (RectTransform)icon.transform;
            irt.anchorMin = new Vector2(0.1f, 0.25f);
            irt.anchorMax = new Vector2(0.9f, 0.95f);
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            var img = icon.GetComponent<Image>();
            img.sprite = so.icon;
            img.preserveAspect = true;
        }

        // 이름 (하단) — 못 짓는 이유가 있으면 이름 대신 그것을 말한다
        string label = string.IsNullOrEmpty(so.displayName) ? so.name : so.displayName;
        if (!affordable && BuildCost.TryGetMissing(so, out var missing, out int shortBy))
        {
            string mName = string.IsNullOrEmpty(missing.displayName) ? missing.name : missing.displayName;
            label += $"\n<size=11>{mName} {shortBy} 부족</size>";
        }

        var text = MakeLabel(go.transform, label, 13, FontStyles.Normal);
        var trt = (RectTransform)text.transform;
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 0.28f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        text.alignment = TextAlignmentOptions.Center;

        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (placement != null) placement.SelectBuilding(so);
            Close();   // 선택 즉시 배치 모드로
        });
    }

    private static TextMeshProUGUI MakeLabel(Transform parent, string text, float size, FontStyles style)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        return tmp;
    }
}
