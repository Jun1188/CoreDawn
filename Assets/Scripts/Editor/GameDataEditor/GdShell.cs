#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.UI;

namespace CoreDawn.EditorTools
{
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
            var combat = new GdCombatTab(this);
            tabs = new GdTab[]
            {
                new GdGraphTab(this),
                new GdBuildingTab(this),
                combat,
                new GdMapTab(this),
                new GdWaveTab(this, combat),   // 웨이브 데이터의 정본은 전투 탭 — 같은 배열의 표 뷰
                new GdMonsterTab(this, combat),   // 몬스터 종류 — 정본은 전투 탭의 monsters
                new GdTutorialTab(this),
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
            // 심이 읽는 v2 팩 data.json은 여기서 생성된다 — v1은 편집 형식, v2는 게임·모드 형식(편집 정본 하나, 파생 산출물 둘)
            try { Debug.Log(GameDataExporterV2.Export()); }
            catch (System.Exception e) { Debug.LogError("[v2 export] 실패: " + e.Message); }
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

            // 공용 테마(USS) + 원본 폰트 (body: 'Segoe UI','Malgun Gothic' / .mono: 'IBM Plex Mono',Consolas)
            rootVe.AddToClassList("gd-root");
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Scripts/Editor/GameDataEditor/GdEditor.uss");
            if (uss != null && !rootVe.styleSheets.Contains(uss)) rootVe.styleSheets.Add(uss);
            // 플레이 모드 씬 전환은 DontSave 아닌 오브젝트를 지운다 — 파괴된 폰트는
            // C# null 이 아니라 ??= 로는 못 걸러낸다. 유니티 == 로 확인하고,
            // HideAndDontSave 를 줘서 애초에 살아남게 한다.
            if (gdFont == null)
            {
                gdFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Malgun Gothic" }, 14);
                if (gdFont != null) gdFont.hideFlags = HideFlags.HideAndDontSave;
            }
            if (monoFont == null)
            {
                // 첫 이름이 미설치면 페이스가 무효가 되어 글자가 아예 안 그려진다 — 설치된 것을 골라 준다
                monoFont = Font.CreateDynamicFontFromOSFont(
                    Array.IndexOf(Font.GetOSInstalledFontNames(), "IBM Plex Mono") >= 0 ? "IBM Plex Mono" : "Consolas", 13);
                if (monoFont != null) monoFont.hideFlags = HideFlags.HideAndDontSave;
            }
            if (gdFont != null) rootVe.style.unityFontDefinition = FontDefinition.FromFont(gdFont);

            // #tabs — padding 8×14 · 밑줄 #223350 · 브랜드 14px + small 11.5px
            var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center,
                paddingLeft = 14, paddingRight = 14, paddingTop = 8, paddingBottom = 8,
                borderBottomWidth = 1, borderBottomColor = (Color)new Color32(0x22, 0x33, 0x50, 0xFF) } };
            rootVe.Add(bar);

            var brand = new Label("GameData 에디터") { style = { unityFontStyleAndWeight = FontStyle.Bold,
                fontSize = 14, marginRight = 12 } };
            bar.Add(brand);
            bar.Add(new Label("LevelUpProj26") { style = { color = (Color)new Color32(0x5C, 0x6E, 0x8C, 0xFF),
                fontSize = 11.5f, marginRight = 8, unityTextAlign = TextAnchor.MiddleLeft } });

            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                var b = new Button(() => SelectTab(idx)) { text = tabs[i].Title };
                b.AddToClassList("gd-tabbtn");
                bar.Add(b);
                tabButtons.Add(b);
            }

            bar.Add(new VisualElement { style = { flexGrow = 1 } });
            sharedStat = new Label { style = { marginRight = 8, unityTextAlign = TextAnchor.MiddleRight } };
            sharedStat.AddToClassList("gd-stat");
            bar.Add(sharedStat);

            bar.Add(new Button(() =>
            {
                if (!hasUnsavedChanges || EditorUtility.DisplayDialog("다시 불러오기",
                    "저장하지 않은 변경을 버리고 파일을 다시 읽습니다.", "버린다", "취소"))
                { LoadFile(); BuildShell(); }
            }) { text = "다시 불러오기" });
            bar.Add(new Button(() => Save(false)) { text = "저장" });
            var si = new Button(() => Save(true)) { text = "저장 + 임포트" };
            si.AddToClassList("gd-btn-primary");   // button.primary — 시안 테두리·글자
            bar.Add(si);

            paneHost = new VisualElement { style = { flexGrow = 1 }, focusable = true };
            rootVe.Add(paneHost);

            // 포커스를 항상 창 안에 둔다 — 패널 안에 포커스가 없으면 Ctrl+Z 가 우리 창을
            // 거치지 않고 유니티 전역 Undo(씬!)로 흘러간다. 텍스트 필드 클릭은 방해하지 않는다.
            paneHost.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!InTextInput(e.target)) paneHost.Focus();
            }, TrickleDown.TrickleDown);

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

        static Font gdFont;
        internal static Font monoFont;

        GdTab CurrentTab => tabs[Mathf.Clamp(tabIndex, 0, tabs.Length - 1)];

        void SelectTab(int idx)
        {
            tabIndex = idx;
            for (int i = 0; i < tabButtons.Count; i++)
                tabButtons[i].EnableInClassList("gd-tabbtn--on", i == idx);
            paneHost.Clear();
            if (root == null)
            {
                paneHost.Add(new HelpBox($"{JsonPath} 를 읽지 못했습니다.\n{loadError}", HelpBoxMessageType.Error));
                return;
            }
            tabs[idx].Build(paneHost);
            paneHost.Focus();   // 키(Ctrl+Z/Y/S)가 유니티 전역이 아니라 이 창으로 오게
            RefreshSharedStat();
        }

        internal void RefreshSharedStat()
        {
            if (sharedStat == null || root == null) return;
            foreach (var t in tabs) t.SyncToRoot();
            sharedStat.text =
                $"아이템 {root.items?.Length ?? 0} · 레시피 {root.recipes?.Length ?? 0} · 건물 {root.buildings?.Length ?? 0}" +
                $" · 효과 {root.effects?.Length ?? 0} · 화기 {root.guns?.Length ?? 0} · 웨이브 {root.waves?.Length ?? 0}" +
                $" · 몬스터 {root.monsters?.Length ?? 0}" +
                $" · 튜토리얼 {root.tutorial?.Length ?? 0}" +
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

        // ── 공용 폼 조각 — 원본 EdUtil.field 와 동일한 구조: 라벨이 위, 입력칸이 전체 폭 ──

        protected static VisualElement Field(string label, VisualElement input, string tooltip = null)
        {
            var box = new VisualElement();
            box.AddToClassList("gd-field");
            if (tooltip != null) box.tooltip = tooltip;
            var lbl = new Label(label);
            lbl.AddToClassList("gd-field-label");
            box.Add(lbl);
            input.AddToClassList("gd-field-input");
            box.Add(input);
            return box;
        }

        protected static VisualElement Text(string label, string value, Action<string> set, bool multiline = false)
        {
            var f = new TextField { value = value ?? "", multiline = multiline };
            if (multiline)
            {
                // multiline 플래그만으로는 한 줄 높이 그대로다 — 높이는 USS(.gd-multiline)가 준다
                f.AddToClassList("gd-multiline");
                f.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }
            f.RegisterValueChangedCallback(e => set(e.newValue));
            return Field(label, f);
        }

        protected static VisualElement Num(string label, float value, Action<float> set)
        {
            var f = new FloatField { value = value };
            f.RegisterValueChangedCallback(e => set(e.newValue));
            return Field(label, f);
        }

        protected static VisualElement Int(string label, int value, Action<int> set)
        {
            var f = new IntegerField { value = value };
            f.RegisterValueChangedCallback(e => set(e.newValue));
            return Field(label, f);
        }

        /// <summary>라벨 위 드롭다운 — 원본 field(label, select) 대응.</summary>
        protected static VisualElement Drop(string label, List<string> choices, int index,
            Action<int> set, string tooltip = null)
        {
            var f = new DropdownField(choices, Mathf.Clamp(index, 0, Mathf.Max(0, choices.Count - 1)));
            f.RegisterValueChangedCallback(e =>
            {
                int i = choices.IndexOf(e.newValue);
                if (i >= 0) set(i);
            });
            return Field(label, f, tooltip);
        }

        /// <summary>격자 속 작은 칸 — 원본 .gcell/.bcell (라벨 위 + 좁은 입력).</summary>
        protected static VisualElement MiniCell(string label, float value, Action<float> set,
            string tooltip = null, float widthPercent = 33f)
        {
            var box = new VisualElement { style = { width = Length.Percent(widthPercent),
                paddingRight = 6, marginBottom = 4 } };
            if (tooltip != null) box.tooltip = tooltip;
            var lbl = new Label(label);
            lbl.AddToClassList("gd-field-label");
            box.Add(lbl);
            var f = new FloatField { value = value };
            f.AddToClassList("gd-field-input");   // 마진 0 — 칸 밖으로 안 나가게
            f.RegisterValueChangedCallback(e => set(e.newValue));
            box.Add(f);
            return box;
        }

        /// <summary>전투·건물·맵 탭의 .field — 라벨 왼쪽(110px) + 입력칸 오른쪽 2열.</summary>
        protected static VisualElement Field2(string label, VisualElement input, string tooltip = null)
        {
            var box = new VisualElement();
            box.AddToClassList("gd-field2");
            if (tooltip != null) box.tooltip = tooltip;
            var lbl = new Label(label);
            lbl.AddToClassList("gd-field-label");
            box.Add(lbl);
            input.AddToClassList("gd-field-input");
            box.Add(input);
            return box;
        }

        /// <summary>그룹 제목 (.field.wide>label) — 우퍼케이스 시안 + 밑줄.</summary>
        protected static Label GroupTitle(string text)
        {
            var l = new Label(text.ToUpperInvariant());
            l.AddToClassList("gd-groupttl");
            return l;
        }

        protected static Label H3(string text)
        {
            var l = new Label(text);
            l.AddToClassList("gd-h3");
            return l;
        }

        /// <summary>원본 .mono — 'IBM Plex Mono',Consolas,monospace.</summary>
        protected static T Mono<T>(T el) where T : VisualElement
        {
            if (GameDataEditorWindow.monoFont != null)
                el.style.unityFontDefinition = FontDefinition.FromFont(GameDataEditorWindow.monoFont);
            return el;
        }

        protected static VisualElement DividerEl()
        {
            var d = new VisualElement();
            d.AddToClassList("gd-divider");
            return d;
        }

        protected static Label WarnItem(string text)
        {
            var l = new Label(text);
            l.AddToClassList("gd-warn");
            return l;
        }

        protected static Label OkMsg(string text)
        {
            var l = new Label(text);
            l.AddToClassList("gd-okmsg");
            return l;
        }

        protected static Label Hint(string text)
        {
            var l = new Label(text);
            l.AddToClassList("gd-hint");
            return l;
        }
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
}
#endif
