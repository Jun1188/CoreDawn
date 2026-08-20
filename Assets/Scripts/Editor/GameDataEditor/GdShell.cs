#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// ═══════════════════════════════════════════════════════════
//  GameData 에디터 — 통합 셸 (Web/js/shell.js 대응)
//
//  탭 전환 · 공용 GameData 입출력. 웹판의 "붙여넣기/내보내기" 모달은
//  유니티에서 파일 직접 읽기/쓰기 + "저장/저장+임포트" 버튼이 된다.
//
//  각 탭은 GdTab 파생 — 자기 pane 을 짓고(Build), 데이터를 root 와
//  주고받는다(Sync). 언두는 원본 EdHistory 와 같은 스냅샷 방식으로
//  탭마다 따로 둔다(js: 탭마다 같은 스택을 따로 만들어 쓴다).
//
//  JSON 왕복은 Newtonsoft — JsonUtility 는 null 배열을 []로 써서
//  "생략=유지 / []=비우기" 임포터 규약을 파괴한다. 스키마에 없는
//  필드는 JsonDtoBase(JsonExtensionData)가 보존한다.
// ═══════════════════════════════════════════════════════════
public class GameDataEditorWindow : EditorWindow
{
    internal static readonly JsonSerializerSettings JsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented,
    };

    static string JsonPath => $"{GameDataImporter.ImportFolder}/GameData.json";

    internal GameDataImporter.Root root;
    string loadError;

    GdTab[] tabs;
    int tabIndex;
    readonly List<Button> tabButtons = new();
    VisualElement paneHost;
    Label sharedStat;

    [MenuItem("Tools/Factory/GameData 에디터")]
    public static void Open()
    {
        var w = GetWindow<GameDataEditorWindow>();
        w.titleContent = new GUIContent("GameData 에디터");
        w.minSize = new Vector2(760, 440);
    }

    void CreateGUI()
    {
        saveChangesMessage = "GameData.json에 저장하지 않은 변경이 있습니다. 저장할까요?";
        tabs = new GdTab[]
        {
            new GdGraphTab(this),
            new GdPlaceholderTab(this, "건물", "Building 에디터 이식 중 — 다음 단계"),
            new GdPlaceholderTab(this, "전투", "전투 에디터 이식 중 — 다음 단계"),
            new GdPlaceholderTab(this, "맵", "맵 에디터 이식 중 — 다음 단계"),
            new GdPlaceholderTab(this, "웨이브", "웨이브 에디터 이식 중 — 다음 단계"),
        };
        LoadFile();
        BuildShell();
    }

    // ── 파일 입출력 ─────────────────────────────────────────

    void LoadFile()
    {
        loadError = null;
        try { root = JsonConvert.DeserializeObject<GameDataImporter.Root>(File.ReadAllText(JsonPath)); }
        catch (Exception e) { root = null; loadError = e.Message; }
        hasUnsavedChanges = false;
        foreach (var t in tabs) t.OnDataLoaded();
    }

    internal void Save(bool import)
    {
        if (root == null) return;
        foreach (var t in tabs) t.SyncToRoot();   // 그래프처럼 자체 모델을 가진 탭이 root 에 반영한다
        File.WriteAllText(JsonPath, JsonConvert.SerializeObject(root, JsonSettings) + "\n");
        AssetDatabase.ImportAsset(JsonPath);
        foreach (var t in tabs) t.SaveExtraFiles(import);
        hasUnsavedChanges = false;
        if (import) GameDataImporter.ImportAll();
        RefreshSharedStat();
    }

    public override void SaveChanges()
    {
        Save(false);
        base.SaveChanges();
    }

    internal void MarkDirty()
    {
        hasUnsavedChanges = true;
        RefreshSharedStat();
    }

    // ── 셸 UI — 원본 #tabs 줄: 브랜드 · 탭 · 공용 통계 · 입출력 ──

    void BuildShell()
    {
        var rootVe = rootVisualElement;
        rootVe.Clear();
        tabButtons.Clear();

        var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
            paddingLeft = 10, paddingRight = 10, paddingTop = 5, paddingBottom = 5,
            borderBottomWidth = 1, borderBottomColor = new Color(0, 0, 0, 0.35f) } };
        rootVe.Add(bar);

        var brand = new Label("GameData 에디터") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 4 } };
        bar.Add(brand);
        bar.Add(new Label("LevelUpProj26") { style = { opacity = 0.5f, fontSize = 10, marginRight = 10,
            unityTextAlign = TextAnchor.MiddleLeft } });

        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            var b = new Button(() => SelectTab(idx)) { text = tabs[i].Title };
            bar.Add(b);
            tabButtons.Add(b);
        }

        bar.Add(new VisualElement { style = { flexGrow = 1 } });
        sharedStat = new Label { style = { opacity = 0.6f, marginRight = 8, unityTextAlign = TextAnchor.MiddleRight } };
        bar.Add(sharedStat);

        bar.Add(new Button(() =>
        {
            if (!hasUnsavedChanges || EditorUtility.DisplayDialog("다시 불러오기",
                "저장하지 않은 변경을 버리고 파일을 다시 읽습니다.", "버린다", "취소"))
            { LoadFile(); BuildShell(); }
        }) { text = "다시 불러오기" });
        bar.Add(new Button(() => Save(false)) { text = "저장" });
        var si = new Button(() => Save(true)) { text = "저장 + 임포트" };
        si.style.unityFontStyleAndWeight = FontStyle.Bold;
        bar.Add(si);

        paneHost = new VisualElement { style = { flexGrow = 1 } };
        rootVe.Add(paneHost);

        // 전역 키 — Ctrl+Z/Y 는 현재 탭의 스택으로, Ctrl+S 는 저장으로.
        // 텍스트 필드 입력 중의 Z/Y 는 필드 자체 편집 취소에 양보한다.
        rootVe.RegisterCallback<KeyDownEvent>(OnGlobalKey, TrickleDown.TrickleDown);
        rootVe.RegisterCallback<ValidateCommandEvent>(e =>
        { if (e.commandName is "Undo" or "Redo" && !InTextInput(e.target)) e.StopPropagation(); }, TrickleDown.TrickleDown);
        rootVe.RegisterCallback<ExecuteCommandEvent>(e =>
        {
            if (InTextInput(e.target)) return;
            if (e.commandName == "Undo") { CurrentTab.Undo(); e.StopPropagation(); }
            else if (e.commandName == "Redo") { CurrentTab.Redo(); e.StopPropagation(); }
        }, TrickleDown.TrickleDown);

        SelectTab(tabIndex);
        RefreshSharedStat();
    }

    GdTab CurrentTab => tabs[Mathf.Clamp(tabIndex, 0, tabs.Length - 1)];

    void SelectTab(int idx)
    {
        tabIndex = idx;
        for (int i = 0; i < tabButtons.Count; i++)
            tabButtons[i].style.unityFontStyleAndWeight = i == idx ? FontStyle.Bold : FontStyle.Normal;
        paneHost.Clear();
        if (root == null)
        {
            paneHost.Add(new HelpBox($"{JsonPath} 를 읽지 못했습니다.\n{loadError}", HelpBoxMessageType.Error));
            return;
        }
        tabs[idx].Build(paneHost);
        RefreshSharedStat();
    }

    internal void RefreshSharedStat()
    {
        if (sharedStat == null || root == null) return;
        foreach (var t in tabs) t.SyncToRoot();
        sharedStat.text =
            $"아이템 {root.items?.Length ?? 0} · 레시피 {root.recipes?.Length ?? 0} · 건물 {root.buildings?.Length ?? 0}" +
            $" · 효과 {root.effects?.Length ?? 0} · 화기 {root.guns?.Length ?? 0} · 웨이브 {root.waves?.Length ?? 0}" +
            (hasUnsavedChanges ? "  ●" : "");
    }

    static bool InTextInput(IEventHandler target)
    {
        for (var ve = target as VisualElement; ve != null; ve = ve.parent)
            if (ve.ClassListContains("unity-base-text-field")) return true;
        return false;
    }

    void OnGlobalKey(KeyDownEvent e)
    {
        bool ctrl = e.ctrlKey || e.commandKey;
        if (ctrl && e.keyCode == KeyCode.S) { Save(false); e.StopPropagation(); return; }
        if (InTextInput(e.target)) return;
        if (ctrl && e.keyCode == KeyCode.Z && !e.shiftKey) { CurrentTab.Undo(); e.StopPropagation(); }
        else if (ctrl && (e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shiftKey))) { CurrentTab.Redo(); e.StopPropagation(); }
        else if (e.keyCode is KeyCode.Delete or KeyCode.Backspace) { if (CurrentTab.DeleteSelection()) e.StopPropagation(); }
    }
}

// ═══════════════════════════════════════════════════════════
//  탭 공통 뼈대 + 원본 EdHistory 대응 스냅샷 언두
// ═══════════════════════════════════════════════════════════
abstract class GdTab
{
    protected readonly GameDataEditorWindow win;
    public abstract string Title { get; }
    protected GdTab(GameDataEditorWindow win) { this.win = win; }

    public abstract void Build(VisualElement host);
    public virtual void OnDataLoaded() { }
    public virtual void SyncToRoot() { }
    public virtual void SaveExtraFiles(bool import) { }
    public virtual void Undo() { }
    public virtual void Redo() { }
    public virtual bool DeleteSelection() => false;

    // ── 공용 폼 조각 (원본 EdUtil.field 대응) ──

    protected static TextField Text(string label, string value, Action<string> set, bool multiline = false)
    {
        var f = new TextField(label) { value = value ?? "", multiline = multiline };
        f.RegisterValueChangedCallback(e => set(e.newValue));
        return f;
    }

    protected static FloatField Num(string label, float value, Action<float> set)
    {
        var f = new FloatField(label) { value = value };
        f.RegisterValueChangedCallback(e => set(e.newValue));
        return f;
    }

    protected static IntegerField Int(string label, int value, Action<int> set)
    {
        var f = new IntegerField(label) { value = value };
        f.RegisterValueChangedCallback(e => set(e.newValue));
        return f;
    }

    protected static Label Hint(string text) => new(text)
    { style = { opacity = 0.55f, whiteSpace = WhiteSpace.Normal, marginTop = 8, fontSize = 11 } };
}

/// <summary>원본 EdHistory — 스냅샷 스택. 바뀐 게 없으면 쌓지 않는다.</summary>
class GdHistory
{
    readonly Func<string> take;
    readonly Action<string> apply;
    readonly int limit;
    readonly List<string> past = new();
    readonly List<string> future = new();
    string last;

    public GdHistory(Func<string> take, Action<string> apply, int limit = 60)
    { this.take = take; this.apply = apply; this.limit = limit; }

    public void Push()
    {
        var cur = take();
        if (cur == last) return;
        if (last != null) { past.Add(last); if (past.Count > limit) past.RemoveAt(0); }
        future.Clear();
        last = cur;
    }

    void Restore(string s) { apply(s); last = s; }
    public bool Undo() { if (past.Count == 0) return false; future.Add(last); Restore(past[^1]); past.RemoveAt(past.Count - 1); return true; }
    public bool Redo() { if (future.Count == 0) return false; past.Add(last); Restore(future[^1]); future.RemoveAt(future.Count - 1); return true; }
    public void Reset() { past.Clear(); future.Clear(); last = take(); }
}

class GdPlaceholderTab : GdTab
{
    readonly string title, note;
    public override string Title => title;
    public GdPlaceholderTab(GameDataEditorWindow win, string title, string note) : base(win)
    { this.title = title; this.note = note; }
    public override void Build(VisualElement host) => host.Add(new HelpBox(note, HelpBoxMessageType.Info));
}
#endif
