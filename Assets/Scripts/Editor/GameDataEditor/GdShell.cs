#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using CoreDawn.Sim;
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

        // 정본은 팩 data.json 하나(3e-2 ③). 폼 탭은 v1 꼴 DTO(root)로 편집하고, GdPack이 양방향으로 변환한다.
        // Raw 탭은 팩 문서(JObject)를 직접 편집한다 — 탭을 오갈 때 두 모델을 동기화한다(아래 SelectTab).
        JObject packDoc;          // Raw 탭이 보는 팩 문서(root에서 만든 것)
        bool packCrlf;            // 파일이 쓰던 개행
        bool uiDirtySinceRaw;     // 폼 탭에서 고친 뒤 Raw 문서를 다시 만들지 않았다

        internal GameDataJson.Root root;
        /// <summary>팩 소리 id 목록(편집 중인 값) — 사운드 탭이 꽂는다. 뷰 조각(GdViewUI)의 드롭다운이 읽는다.</summary>
        internal Func<List<string>> SoundIds = () => new List<string>();
        string loadError;

        GdTab[] tabs;
        GdRawTab rawTab;
        int tabIndex;
        readonly List<Button> tabButtons = new();
        VisualElement paneHost;
        Label sharedStat;

        [MenuItem("Tools/CoreDawn/GameData 에디터")]
        public static void Open()
        {
            var w = GetWindow<GameDataEditorWindow>();
            w.titleContent = new GUIContent("GameData 에디터");
            w.minSize = new Vector2(760, 440);
        }

        void CreateGUI()
        {
            saveChangesMessage = "data.json에 저장하지 않은 변경이 있습니다. 저장할까요?";
            var combat = new GdCombatTab(this);
            var map = new GdMapTab(this, combat);   // 둥지의 보스·방어자 종류 드롭다운이 전투 탭의 몬스터 목록을 본다
            tabs = new GdTab[]
            {
                new GdGraphTab(this),
                new GdBuildingTab(this),
                combat,
                map,
                new GdWaveTab(this, combat, map),   // 웨이브 데이터의 정본은 전투 탭 — 같은 배열의 표 뷰. 미리보기는 맵의 둥지 수를 본다
                new GdMonsterTab(this, combat),   // 몬스터 종류 — 정본은 전투 탭의 monsters
                new GdTutorialTab(this),
                new GdSoundTab(this),   // 소리(변형 클립 묶음)·공용 소리 자리 — 각 정의 패널의 뷰 조각이 이 id 목록을 쓴다
                rawTab = new GdRawTab(this),   // 팩 data.json의 entities를 모듈 리스트 그대로 편집 — 3e-2의 첫 조각(v1과 독립)
            };
            LoadFile();
            BuildShell();
        }

        // ── 파일 입출력 ─────────────────────────────────────────

        void LoadFile()
        {
            loadError = null;
            try
            {
                packDoc = GdPack.ReadPack(out packCrlf);
                root = GdPack.ToV1(packDoc).ToObject<GameDataJson.Root>(JsonSerializer.Create(JsonSettings));
            }
            catch (Exception e) { root = null; packDoc = null; loadError = e.Message; }
            hasUnsavedChanges = false; uiDirtySinceRaw = false;
            GdPackAssets.ClearCache();
            foreach (var t in tabs) t.OnDataLoaded();
            rawTab?.LoadFrom(packDoc, loadError);
        }

        /// <summary>폼 모델(root) → 팩 문서. 탭들의 자체 모델을 root에 먼저 반영한다.</summary>
        JObject BuildPackDoc()
        {
            foreach (var t in tabs) if (t != rawTab) t.SyncToRoot();
            var v1 = JObject.Parse(JsonConvert.SerializeObject(root, JsonSettings));   // 텍스트 경유 — FromObject 는 float 를 double 로 풀어 1.2 가 1.2000000476837158 이 된다
            return (JObject)GdPack.OrderLike(GdPack.ToPack(v1), packDoc);   // 디스크 키 순서 유지 — diff 에 값 변화만 남는다
        }

        /// <summary>Raw 탭에서 고친 팩 문서 → 폼 모델(root). 폼 탭들이 다시 읽는다.</summary>
        void PullFromRaw()
        {
            if (rawTab == null || rawTab.Pack == null) return;
            packDoc = rawTab.Pack;
            root = GdPack.ToV1(packDoc).ToObject<GameDataJson.Root>(JsonSerializer.Create(JsonSettings));
            foreach (var t in tabs) if (t != rawTab) t.OnDataLoaded();
            rawTab.ClearDirty();
            uiDirtySinceRaw = false;
        }

        /// <summary>폼 모델이 바뀌었으면 Raw 탭에 새 팩 문서를 준다(Raw로 들어갈 때).</summary>
        void PushToRaw()
        {
            if (rawTab == null || root == null || !uiDirtySinceRaw) return;
            try { packDoc = BuildPackDoc(); rawTab.LoadFrom(packDoc); uiDirtySinceRaw = false; }
            catch (Exception e) { rawTab.LoadFrom(packDoc, "폼 모델을 팩으로 바꾸지 못했습니다 — " + e.Message); }
        }

        internal void Save(bool import)
        {
            if (root == null) return;
            try
            {
                // 어느 쪽이 최신인가 — Raw에서 고쳤으면 그쪽이 정본, 아니면 폼 모델에서 만든다
                if (rawTab != null && rawTab.Dirty) PullFromRaw();
                packDoc = BuildPackDoc();
                var errors = GdPack.Validate(packDoc);
                GdPack.WritePack(packDoc, packCrlf);
                SimHost.Database = null;   // 에디트 모드 도구(미리보기 등)가 새 팩을 다시 읽게
                rawTab?.LoadFrom(packDoc);
                uiDirtySinceRaw = false;
                if (errors.Count > 0) Debug.LogError($"[GameData] {GdPack.DataPath} 저장 — 로드 오류 {errors.Count}건:\n  " + string.Join("\n  ", errors));
                else Debug.Log($"[GameData] {GdPack.DataPath} 저장 — entities {(packDoc["entities"] as JObject)?.Count ?? 0} · items {(packDoc["items"] as JObject)?.Count ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError("[GameData] 저장 실패(팩으로 변환하지 못함): " + e.Message);
                return;
            }
            foreach (var t in tabs) t.SaveExtraFiles(import);
            hasUnsavedChanges = false;
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
            if (CurrentTab != rawTab) uiDirtySinceRaw = true;
            RefreshSharedStat();
        }

        // ── [UI | Raw] 전환 — 같은 데이터를 폼으로 볼지 트리로 볼지 ──

        /// <summary>폼 탭 → 그 탭이 담당하는 팩 섹션. 재질은 폼이 없어 Raw뿐.</summary>
        static readonly Dictionary<Type, string> SectionOfTab = new()
        {
            [typeof(GdGraphTab)] = "items", [typeof(GdBuildingTab)] = "entities", [typeof(GdCombatTab)] = "effects",
            [typeof(GdWaveTab)] = "wave", [typeof(GdMonsterTab)] = "entities", [typeof(GdTutorialTab)] = "tutorial",
            [typeof(GdSoundTab)] = "sounds",
        };

        static readonly Dictionary<string, Type> TabOfSection = new()
        {
            ["items"] = typeof(GdGraphTab), ["recipes"] = typeof(GdGraphTab), ["entities"] = typeof(GdBuildingTab),
            ["effects"] = typeof(GdCombatTab), ["guns"] = typeof(GdCombatTab), ["wave"] = typeof(GdWaveTab), ["dayCycle"] = typeof(GdWaveTab),
            ["tutorial"] = typeof(GdTutorialTab), ["sounds"] = typeof(GdSoundTab), ["sfx"] = typeof(GdSoundTab),
        };

        void ToggleRaw()
        {
            if (rawTab == null) return;
            int rawIdx = Array.IndexOf(tabs, rawTab);
            if (CurrentTab == rawTab)
            {
                var (sec, id) = rawTab.Cursor;
                // 몬스터는 entities 안에 있지만 폼은 몬스터 탭 — 고른 정의에 MonsterBrain이 있으면 그쪽으로
                Type target = TabOfSection.TryGetValue(sec, out var t) ? t : null;
                if (sec == "entities" && id != null && rawTab.Pack?["entities"]?[id]?["modules"] is JArray mods
                    && mods.Any(m => (string)m["type"] == "MonsterBrain")) target = typeof(GdMonsterTab);
                if (target == null) { Debug.Log($"[GameData] '{sec}' 섹션은 폼이 없습니다 — Raw에서 편집하세요."); return; }
                int idx = Array.FindIndex(tabs, x => x.GetType() == target);
                if (idx < 0) return;
                SelectTab(idx);
                if (id != null) tabs[idx].SelectRaw(sec, id);
            }
            else
            {
                var (sec, id) = CurrentTab.RawCursor;
                if (sec == null) sec = SectionOfTab.TryGetValue(CurrentTab.GetType(), out var s) ? s : "entities";
                SelectTab(rawIdx);
                rawTab.ShowSection(sec, string.IsNullOrEmpty(id) ? null : id);
            }
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
            // 탭이 늘어 창이 좁으면 통계가 저장 버튼을 밀어낸다 — 통계 쪽이 줄어들고(말줄임) 버튼은 남긴다
            sharedStat = new Label { style = { marginRight = 8, unityTextAlign = TextAnchor.MiddleRight,
                flexShrink = 1, minWidth = 0, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis } };
            sharedStat.AddToClassList("gd-stat");
            bar.Add(sharedStat);

            bar.Add(new Button(() =>
            {
                if (!hasUnsavedChanges || EditorUtility.DisplayDialog("다시 불러오기",
                    "저장하지 않은 변경을 버리고 파일을 다시 읽습니다.", "버린다", "취소"))
                { LoadFile(); BuildShell(); }
            }) { text = "다시 불러오기" });
            var toggle = new Button(ToggleRaw) { text = "UI ⇄ Raw", tooltip = "같은 데이터를 폼으로/트리로 — 현재 탭의 섹션으로 오간다" };
            bar.Add(toggle);
            var si = new Button(() => Save(true)) { text = "저장 (data.json + 맵)" };
            si.AddToClassList("gd-btn-primary");   // button.primary — 시안 테두리·글자
            bar.Add(si);

            // min-height 0 — 기본(auto)은 내용 높이라 긴 목록을 가진 탭(Raw)이 창보다 커져 아래 행이 창 밖으로 밀린다
            paneHost = new VisualElement { style = { flexGrow = 1, minHeight = 0 }, focusable = true };
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
            // 두 모델 동기화 — Raw에서 나오면 폼으로 되가져오고, Raw로 들어가면 폼에서 새로 만든다
            if (tabs != null && rawTab != null && tabIndex != idx)
            {
                if (tabs[tabIndex] == rawTab && rawTab.Dirty) PullFromRaw();
                if (tabs[idx] == rawTab) PushToRaw();
            }
            tabIndex = idx;
            for (int i = 0; i < tabButtons.Count; i++)
                tabButtons[i].EnableInClassList("gd-tabbtn--on", i == idx);
            paneHost.Clear();
            if (root == null)
            {
                paneHost.Add(new HelpBox($"{GdPack.DataPath} 를 읽지 못했습니다.\n{loadError}", HelpBoxMessageType.Error));
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
                $" · 효과 {root.effects?.Length ?? 0} · 화기 {root.guns?.Length ?? 0} · 웨이브 명단 {root.wave?.roster?.Length ?? 0}" +
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
        /// <summary>[UI ⇄ Raw] 전환용 — 지금 고른 항목의 (팩 섹션, 섹션 안 키). 항목 개념이 없는 탭은 (null, null).</summary>
        internal virtual (string section, string id) RawCursor => (null, null);
        /// <summary>Raw에서 돌아올 때 같은 항목을 고른다(id는 섹션 안 키, 접두 없음). 없으면 그대로.</summary>
        internal virtual void SelectRaw(string section, string id) { }

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
