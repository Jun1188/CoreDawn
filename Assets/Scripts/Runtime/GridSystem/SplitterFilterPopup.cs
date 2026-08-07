using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 분배기 필터 설정 팝업 — E로 분배기를 조준·상호작용하면 열린다 (파이프라인 UIPopup).
///
/// 조작: 상단에서 출구 방향 탭 선택 → 아이템 그리드에서 클릭 토글.
///   - 선택 방향에 걸린 아이템: 초록 강조, 클릭하면 해제
///   - 다른 방향에 걸린 아이템: 노란 표시(방향 표기), 클릭하면 선택 방향으로 이동
///   - 무필터 아이템: 클릭하면 선택 방향에 추가
///
/// UI는 런타임 코드 조립(씬 저작 0) — BuildMenuPopup과 같은 방식. ESC/E로 닫기.
/// </summary>
public class SplitterFilterPopup : UIPopup
{
    private static SplitterFilterPopup instance;

    private SplitterBehavior target;
    private Direction selectedDir;
    private readonly List<Direction> outputDirs = new();

    private static string DirName(Direction d) => d switch
    {
        Direction.North => "북", Direction.East => "동",
        Direction.South => "남", Direction.West => "서",
        _ => d.ToString(),
    };

    /// <summary>분배기 필터 편집 열기 — 최초 호출 시 Canvas 아래에 패널 생성.</summary>
    public static void Open(SplitterBehavior behavior)
    {
        if (instance == null) instance = CreatePanel();
        if (instance == null) return;

        instance.target = behavior;

        // 출력 포트 방향 수집 (연결 여부 무관 — 미리 설정해둘 수 있게)
        instance.outputDirs.Clear();
        foreach (var p in behavior.Building.GetEffectivePorts())
            if (!p.IsInput) instance.outputDirs.Add(p.Direction);
        if (instance.outputDirs.Count > 0 && !instance.outputDirs.Contains(instance.selectedDir))
            instance.selectedDir = instance.outputDirs[0];

        instance.Rebuild();
        instance.gameObject.SetActive(true);
    }

    public override bool OnInput(in InputEvent e)
    {
        // E(UI 맵의 Interact)로도 닫기 — 연 키로 다시 닫는 대칭 조작 (InventoryPopup 패턴)
        if (e.Phase == InputActionPhase.Performed && e.Id == InputActionId.Interact)
        {
            Close();
            return true;
        }
        return base.OnInput(e);   // Cancel(ESC) 닫기 + 모달 삼킴
    }

    // ───────────────────── 런타임 UI 조립 ─────────────────────

    private static SplitterFilterPopup CreatePanel()
    {
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[SplitterFilter] 씬에 Canvas가 없습니다.");
            return null;
        }

        var panelGO = new GameObject("SplitterFilter_Panel(Auto)", typeof(RectTransform), typeof(Image));
        panelGO.transform.SetParent(canvas.transform, false);

        var rt = (RectTransform)panelGO.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(560, 460);

        panelGO.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.1f, 0.95f);

        var layout = panelGO.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 8;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        panelGO.SetActive(false);   // 비활성 상태에서 부착 — OnEnable(UI 맵 Push) 조기 발화 방지
        var popup = panelGO.AddComponent<SplitterFilterPopup>();
        return popup;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    protected override void OnDisable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        base.OnDisable();
    }

    private void Rebuild()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        MakeLabel(transform, "분배기 필터", 22, FontStyles.Bold);
        MakeLabel(transform, "출구를 고르고 아이템을 클릭해 지정/해제하세요. 지정 아이템은 그 출구로만 나갑니다.",
            13, FontStyles.Normal);

        // ── 출구 방향 탭
        var dirRow = new GameObject("Dirs", typeof(RectTransform)).AddComponent<HorizontalLayoutGroup>();
        dirRow.transform.SetParent(transform, false);
        dirRow.spacing = 8;
        dirRow.childForceExpandWidth = false;
        dirRow.childControlWidth = false;
        dirRow.childControlHeight = false;

        foreach (var dir in outputDirs)
        {
            bool selected = dir == selectedDir;
            var d = dir;   // 클로저 캡처
            var btn = MakeButton(dirRow.transform, $"{DirName(dir)}쪽 출구", new Vector2(120, 34),
                selected ? new Color(0.2f, 0.45f, 0.25f) : new Color(0.22f, 0.22f, 0.26f),
                () => { selectedDir = d; Rebuild(); });
            int count = target.AllowedAt(dir).Count;
            if (count > 0) btn.text += $" ({count})";
        }

        // ── 아이템 그리드 (ItemDatabase 전체)
        var db = ItemDatabaseSO.LoadDefault();
        if (db == null || db.items == null)
        {
            MakeLabel(transform, "ItemDatabase 없음 (Resources 확인)", 14, FontStyles.Italic);
            return;
        }

        var grid = new GameObject("Grid", typeof(RectTransform)).AddComponent<GridLayoutGroup>();
        grid.transform.SetParent(transform, false);
        grid.cellSize = new Vector2(124, 40);
        grid.spacing = new Vector2(6, 6);

        foreach (var item in db.items)
        {
            if (item == null) continue;
            var captured = item;

            // 한 아이템이 여러 출구에 걸릴 수 있다 — 선택 출구 지정 여부와
            // "다른 출구에도 있음"을 따로 표시한다
            bool onSelected = target.IsAllowedAt(selectedDir, item);
            var others = new List<Direction>();
            foreach (var d2 in target.DirectionsOf(item))
                if (d2 != selectedDir) others.Add(d2);

            string label = string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;
            if (others.Count > 0)
            {
                var names = new List<string>(others.Count);
                foreach (var d2 in others) names.Add(DirName(d2));
                label += $" [{string.Join(",", names)}]";
            }

            Color color = onSelected ? new Color(0.2f, 0.5f, 0.25f)        // 선택 출구 지정 — 초록
                : others.Count > 0 ? new Color(0.5f, 0.42f, 0.15f)          // 다른 출구에만 지정 — 노랑
                : new Color(0.22f, 0.22f, 0.26f);                           // 무지정

            MakeButton(grid.transform, label, Vector2.zero, color, () =>
            {
                // 선택 출구 기준 토글 — 다른 출구의 지정은 그대로 둔다
                if (target.IsAllowedAt(selectedDir, captured)) target.RemoveFilter(selectedDir, captured);
                else                                          target.AddFilter(selectedDir, captured);
                Rebuild();
            });
        }

        // ── 선택 출구 전체 해제
        MakeButton(transform, $"{DirName(selectedDir)}쪽 필터 전체 해제", new Vector2(200, 32),
            new Color(0.35f, 0.2f, 0.2f), () => { target.ClearFilter(selectedDir); Rebuild(); });
    }

    // ───────────────────── 위젯 헬퍼 ─────────────────────

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

    private static TextMeshProUGUI MakeButton(Transform parent, string text, Vector2 size, Color color,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        if (size != Vector2.zero) ((RectTransform)go.transform).sizeDelta = size;
        go.GetComponent<Image>().color = color;
        go.GetComponent<Button>().onClick.AddListener(onClick);

        var label = MakeLabel(go.transform, text, 13, FontStyles.Normal);
        var lrt = (RectTransform)label.transform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }
}
