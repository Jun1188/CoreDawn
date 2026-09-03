#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Sim;

namespace CoreDawn.EditorTools
{
    // ═══════════════════════════════════════════════════════════
    //  Raw 탭 — 팩 data.json의 모든 섹션을 정의 스키마 그대로(yaml 꼴) 직접 편집한다.
    //  3e-2(편집기 v2 직접 편집)의 첫 조각(사용자 지시 2026-09-03: "모듈 형태로 바로 편집할 수 있는
    //  Raw 편집 탭 … yaml 같은 직관적인 ui", "다른 것들도 옮기는데 … 그냥 raw로 넣지는 말고 UI만 직관적으로").
    //
    //  모델: JToken 트리 — 스키마에 없는 키도 그대로 보존한다(v1 DTO를 거치지 않는다).
    //  스키마: 섹션마다 심의 정의 타입(EntityDef·ItemDef·RecipeDef…)을 리플렉션으로 읽어 필드 목록·기본값·
    //          enum·중첩 타입을 안다. json에 없는 스키마 필드는 흐린 "유령 행"으로 기본값을 보여 주고
    //          "+ 추가"를 누르면 생긴다. 심에 스키마가 없는 view 블록은 편집기 전용 힌트(RawHints)로
    //          type 드롭다운·model/icon/pose/sfx 꼴을 알려 준다(힌트는 유령 행을 만들지 않는다).
    //          id 참조(item·effect·gun·sound·materials)는 팩의 해당 섹션 키로 드롭다운.
    //  검증: 편집할 때마다 SimSchema 직렬화기(모르는 키 = 오류)로 정의를 역직렬화해 본다.
    //  저장: data.json에 직접(파일의 개행 유지). v1 편집기의 "저장"은 내보내기로 data.json을 재생성하므로
    //        셸(GameDataEditorWindow.Save)이 Raw 편집이 있으면 묻는다 — 과도기.
    //  언두: 숫자·문자열은 키 입력마다 본문을 다시 짓지 않는다(포커스 끊김·Ctrl+Z 무효 지적 전례) —
    //        값은 즉시 모델에, 스냅샷은 포커스 단위. 구조 변경(추가·삭제·순서)은 한 스텝.
    // ═══════════════════════════════════════════════════════════
    sealed class GdRawTab : GdTab
    {
        public override string Title => "Raw";

        internal static string PackPath => GameDataExporterV2.OutputPath;
        const string HashPref = "CoreDawn.GdRaw.SavedHash";

        static readonly JsonSerializerSettings ParseSettings = new()
        {
            DateParseHandling = DateParseHandling.None,   // "2026-09-03" 같은 문자열을 날짜로 바꾸지 않는다
        };

        /// <summary>팩 섹션 — 키, 팩 id의 단수형(coredawn:item/…), 표시 이름, 정의 타입, 사전형인지.</summary>
        sealed class Section
        {
            public readonly string Key, Singular, Title;
            public readonly Type DefType;
            public readonly bool IsMap;
            public Section(string key, string singular, string title, Type defType, bool isMap)
            { Key = key; Singular = singular; Title = title; DefType = defType; IsMap = isMap; }
        }

        static readonly Section[] Sections =
        {
            new("entities", "entity", "entities — 엔티티", typeof(EntityDef), true),
            new("items", "item", "items — 아이템", typeof(ItemDef), true),
            new("recipes", "recipe", "recipes — 레시피", typeof(RecipeDef), true),
            new("effects", "effect", "effects — 효과", typeof(EffectSpec), true),
            new("guns", "gun", "guns — 총", typeof(GunDef), true),
            new("tutorial", "tutorial", "tutorial — 튜토리얼", typeof(TutorialStepDef), true),
            new("sounds", "sound", "sounds — 소리", typeof(SoundDef), true),
            new("materials", "material", "materials — 재질", typeof(MaterialDef), true),
            new("sfx", "sfx", "sfx — 공용 소리 자리", typeof(RawHints.SoundUse), true),
            new("wave", "wave", "wave — 웨이브 규칙", typeof(WaveRuleDef), false),
            new("dayCycle", "dayCycle", "dayCycle — 낮·밤 길이", typeof(DayCycleDef), false),
        };

        JObject pack;
        string loadError;
        Section section = Sections[0];
        string selectedId;
        bool dirty;
        bool crlf;

        readonly Stack<string> undo = new();
        readonly Stack<string> redo = new();
        readonly HashSet<string> collapsed = new();

        VisualElement listHost, listTools;
        ScrollView treeHost;
        TextField filter, newIdField;
        Label status, validation;
        Button dupBtn, delBtn;

        public GdRawTab(GameDataEditorWindow win) : base(win) { }

        // ── 현재 위치 ───────────────────────────────────────────

        /// <summary>섹션 값 — 사전형이면 id → 정의 사전, 단일형이면 정의 자체.</summary>
        JObject SectionObj => pack?[section.Key] as JObject;

        /// <summary>지금 편집 중인 정의 객체.</summary>
        JObject Current => section.IsMap ? (selectedId != null ? SectionObj?[selectedId] as JObject : null) : SectionObj;

        string CurrentTitle => section.IsMap ? selectedId : section.Key;

        // ── 파일 ────────────────────────────────────────────────

        public override void OnDataLoaded() => LoadPack();

        void LoadPack()
        {
            loadError = null; pack = null;
            try
            {
                var text = File.ReadAllText(PackPath);
                crlf = text.Contains("\r\n");   // 내보내기는 LF로 쓰지만 git autocrlf가 체크아웃 때 CRLF로 바꾼다 — 파일의 것을 따른다
                pack = JsonConvert.DeserializeObject<JObject>(text, ParseSettings);
                if (pack["entities"] is not JObject) throw new Exception("entities 섹션이 없습니다");
            }
            catch (Exception e) { loadError = e.Message; }
            dirty = false; undo.Clear(); redo.Clear();
            EnsureSelection();
        }

        void EnsureSelection()
        {
            if (!section.IsMap) { selectedId = null; return; }
            var so = SectionObj;
            if (so == null) { selectedId = null; return; }
            if (selectedId == null || so[selectedId] == null) selectedId = so.Properties().FirstOrDefault()?.Name;
        }

        /// <summary>디스크의 data.json을 다시 읽는다(v1 내보내기가 덮어쓴 뒤 등).</summary>
        internal void ReloadFromDisk()
        {
            LoadPack();
            if (listHost != null) { RenderList(); RenderTree(); }
        }

        internal void SaveRaw()
        {
            if (pack == null) return;
            // 내보내기와 같은 꼴(2칸 들여쓰기, 끝 개행) + 파일이 쓰던 개행 — 편집 없이 저장하면 바이트 동일
            string nl = crlf ? "\r\n" : "\n";
            var text = pack.ToString(Formatting.Indented).Replace("\r\n", "\n").Replace("\n", nl) + nl;
            File.WriteAllText(PackPath, text);
            AssetDatabase.ImportAsset(PackPath);
            EditorPrefs.SetString(HashPref, Hash(text));
            SimHost.Database = null;   // 에디트 모드 도구가 새 팩을 다시 읽게
            dirty = false;
            RefreshStatus();
            Debug.Log($"[GdRaw] data.json 저장 — {section.Key} {(SectionObj?.Count ?? 0)}");
        }

        /// <summary>
        /// Raw 편집이 살아 있는가 — 미저장 편집이 있거나, 디스크의 data.json이 마지막 Raw 저장 그대로인가.
        /// 셸의 v1 저장(내보내기)이 이걸 보고 덮어쓸지 묻는다.
        /// </summary>
        internal bool HasRawEdits
        {
            get
            {
                if (dirty) return true;
                try { return File.Exists(PackPath) && EditorPrefs.GetString(HashPref, "") == Hash(File.ReadAllText(PackPath)); }
                catch { return false; }
            }
        }

        static string Hash(string s)
        {
            using var sha = SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "");
        }

        // ── 셸 ──────────────────────────────────────────────────

        public override void Build(VisualElement host)
        {
            host.style.flexDirection = FlexDirection.Row;

            var left = new VisualElement { style = { width = 290, flexShrink = 0, minHeight = 0 } };
            left.AddToClassList("gd-leftcol");
            host.Add(left);
            var h = new Label("packs/coredawn/data.json"); h.AddToClassList("gd-h3"); left.Add(h);

            // 섹션 선택 — 어느 섹션이든 같은 트리·같은 규칙으로 편집한다
            var titles = Sections.Select(s => s.Title).ToList();
            var secDrop = new DropdownField(titles, Array.IndexOf(Sections, section)) { style = { marginLeft = 0, marginRight = 0, marginBottom = 6 } };
            secDrop.RegisterValueChangedCallback(e =>
            {
                int i = titles.IndexOf(e.newValue);
                if (i < 0 || Sections[i] == section) return;
                section = Sections[i];
                selectedId = null;
                undo.Clear(); redo.Clear();
                EnsureSelection();
                RenderList();
                RenderTree();
            });
            left.Add(secDrop);

            filter = new TextField { value = "" };
            filter.AddToClassList("gd-field-input");
            filter.RegisterValueChangedCallback(_ => RenderList());
            left.Add(filter);
            // min-height 0 — 스크롤뷰의 기본 min-height(auto)는 내용 높이라 열이 창보다 길어져 아래 행이 창 밖으로 밀린다
            listHost = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1, minHeight = 0, marginTop = 8 }, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            left.Add(listHost);
            listTools = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Stretch, marginTop = 8 } };
            newIdField = new TextField { value = "new_id", style = { flexGrow = 1, marginLeft = 0, marginRight = 0, marginTop = 0, marginBottom = 0 } };
            Mono(newIdField);
            listTools.Add(newIdField);
            var addBtn = new Button(AddDef) { text = "+ 새 항목", tooltip = "id는 소문자·숫자·_", style = { marginLeft = 6, marginRight = 0, marginTop = 0, marginBottom = 0 } };
            listTools.Add(addBtn);   // 입력칸과 같은 높이로 늘어난다(Stretch)
            left.Add(listTools);

            var right = new VisualElement { style = { flexGrow = 1, minWidth = 0, minHeight = 0, paddingLeft = 14, paddingRight = 14, paddingTop = 10 } };
            host.Add(right);
            var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            status = new Label();
            status.AddToClassList("gd-stat");
            status.style.flexGrow = 1;
            status.style.unityTextAlign = TextAnchor.MiddleLeft;
            bar.Add(status);
            bar.Add(new Button(ReloadFromDisk) { text = "다시 읽기" });
            bar.Add(dupBtn = new Button(Duplicate) { text = "복제" });
            bar.Add(delBtn = new Button(DeleteSelected) { text = "삭제" });
            bar.Add(new Button(SaveRaw) { text = "Raw 저장 (data.json)" });
            right.Add(bar);
            validation = new Label { style = { display = DisplayStyle.None } };
            right.Add(validation);
            treeHost = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1, minHeight = 0 } };
            right.Add(treeHost);

            if (pack == null && loadError == null) LoadPack();
            RenderList();
            RenderTree();
        }

        // ── 목록 ────────────────────────────────────────────────

        void RenderList()
        {
            if (listHost == null) return;
            listHost.Clear();
            bool map = section.IsMap;
            listTools.style.display = map ? DisplayStyle.Flex : DisplayStyle.None;
            dupBtn.style.display = map ? DisplayStyle.Flex : DisplayStyle.None;
            delBtn.style.display = map ? DisplayStyle.Flex : DisplayStyle.None;
            var so = SectionObj;
            if (so == null) return;
            if (!map)
            {
                var row = new VisualElement();
                row.AddToClassList("gd-bitem");
                row.AddToClassList("gd-bitem--sel");
                var nm = new Label(section.Key); nm.AddToClassList("gd-bitem-nm"); Mono(nm); row.Add(nm);
                listHost.Add(row);
                return;
            }
            string q = (filter?.value ?? "").Trim().ToLowerInvariant();
            foreach (var p in so.Properties())
            {
                string id = p.Name;
                string name = (p.Value as JObject)?["displayName"]?.ToString() ?? (p.Value as JObject)?["sound"]?.ToString() ?? "";
                if (q.Length > 0 && !id.ToLowerInvariant().Contains(q) && !name.ToLowerInvariant().Contains(q)) continue;
                var row = new VisualElement();
                row.AddToClassList("gd-bitem");
                if (id == selectedId) row.AddToClassList("gd-bitem--sel");
                var nm = new Label(id); nm.AddToClassList("gd-bitem-nm"); Mono(nm); row.Add(nm);
                var kd = new Label(name) { style = { flexShrink = 1, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis, maxWidth = 140 } };
                kd.AddToClassList("gd-bitem-kd");   // 긴 표시명이 목록을 가로로 넓혀 스크롤바를 만들지 않게 말줄임
                row.Add(kd);
                row.RegisterCallback<ClickEvent>(_ => Select(id));
                listHost.Add(row);
            }
        }

        void Select(string id)
        {
            if (id == selectedId) return;
            selectedId = id;
            undo.Clear(); redo.Clear();
            RenderList();
            RenderTree();
        }

        void AddDef()
        {
            var so = SectionObj;
            if (so == null || !section.IsMap) return;
            string id = (newIdField.value ?? "").Trim();
            if (id.Length == 0 || id.Any(c => !(char.IsLower(c) || char.IsDigit(c) || c == '_')))
            { EditorUtility.DisplayDialog("Raw", "id는 소문자·숫자·_ 만 쓸 수 있습니다.", "확인"); return; }
            if (so[id] != null) { EditorUtility.DisplayDialog("Raw", $"'{id}'는 이미 있습니다.", "확인"); return; }
            // 정의 타입의 기본값에서 시작 — 필드가 전부 기본값으로 들어간다(유령 행이 아니라 진짜 키)
            var o = RawSchema.DefaultObject(section.DefType);
            if (o["displayName"] != null) o["displayName"] = id;
            so.Add(id, o);
            dirty = true; win.MarkDirty();
            Select(id);
        }

        void Duplicate()
        {
            var so = SectionObj;
            if (!section.IsMap || selectedId == null || so?[selectedId] == null) return;
            string id = selectedId + "_copy";
            for (int n = 2; so[id] != null; n++) id = $"{selectedId}_copy{n}";
            so.Add(id, so[selectedId].DeepClone());
            dirty = true; win.MarkDirty();
            Select(id);
        }

        void DeleteSelected()
        {
            var so = SectionObj;
            if (!section.IsMap || selectedId == null || so?[selectedId] == null) return;
            if (!EditorUtility.DisplayDialog("Raw", $"'{selectedId}'를 지울까요? 다른 정의가 참조하고 있으면 팩 로드가 실패합니다.", "삭제", "취소")) return;
            so.Remove(selectedId);
            selectedId = so.Properties().FirstOrDefault()?.Name;
            undo.Clear(); redo.Clear();
            dirty = true; win.MarkDirty();
            RenderList();
            RenderTree();
        }

        // ── 언두 — 현재 정의의 json 스냅샷 ─────────────────────

        string Snapshot() => Current?.ToString(Formatting.None);

        void PushUndo()
        {
            var s = Snapshot();
            if (s == null) return;
            undo.Push(s);
            redo.Clear();
        }

        /// <summary>구조 변경 한 스텝 — 스냅샷 → 적용 → 본문 재생성.</summary>
        void Mutate(Action apply)
        {
            PushUndo();
            apply();
            dirty = true; win.MarkDirty();
            RenderTree();
        }

        /// <summary>값만 바뀐 편집 — 본문은 그대로, 검증·상태만 갱신.</summary>
        void AfterValueEdit()
        {
            dirty = true; win.MarkDirty();
            if (Current is JObject cur) RefreshValidation(cur);
            RefreshStatus();
        }

        void Restore(string snapshot)
        {
            if (snapshot == null) return;
            var o = JsonConvert.DeserializeObject<JObject>(snapshot, ParseSettings);
            if (section.IsMap) { if (selectedId == null) return; SectionObj[selectedId] = o; }
            else pack[section.Key] = o;
            dirty = true; win.MarkDirty();
            RenderList();
            RenderTree();
        }

        public override void Undo()
        {
            if (undo.Count == 0) return;
            var cur = Snapshot();
            if (cur != null) redo.Push(cur);
            Restore(undo.Pop());
        }

        public override void Redo()
        {
            if (redo.Count == 0) return;
            var cur = Snapshot();
            if (cur != null) undo.Push(cur);
            Restore(redo.Pop());
        }

        // ── 본문 ────────────────────────────────────────────────

        void RenderTree()
        {
            if (treeHost == null) return;
            treeHost.Clear();
            if (loadError != null)
            {
                var w = new Label("data.json 읽기 실패: " + loadError); w.AddToClassList("gd-warn"); treeHost.Add(w);
                RefreshStatus();
                return;
            }
            if (SectionObj == null)
            {
                var w = new Label($"data.json에 '{section.Key}' 섹션이 없습니다."); w.AddToClassList("gd-warn"); treeHost.Add(w);
                RefreshStatus();
                return;
            }
            if (Current is not JObject cur)
            {
                var hint = new Label("왼쪽에서 항목을 고르세요."); hint.AddToClassList("gd-hint"); treeHost.Add(hint);
                RefreshStatus();
                return;
            }
            var ttl = new Label(CurrentTitle + ":"); ttl.AddToClassList("gd-raw-idttl"); Mono(ttl);
            treeHost.Add(ttl);
            var block = Block();
            treeHost.Add(block);
            RenderObject(block, cur, section.DefType, CurrentTitle, null);
            RefreshValidation(cur);
            RefreshStatus();
        }

        void RefreshValidation(JObject cur)
        {
            if (validation == null) return;
            string err = RawSchema.Validate(cur, section.DefType);
            validation.text = err == null ? "" : "검증 실패: " + err;
            validation.ClearClassList();
            validation.AddToClassList("gd-warn");
            validation.style.display = err == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void RefreshStatus()
        {
            if (status == null) return;
            var so = SectionObj;
            status.text = so == null ? "" :
                (section.IsMap ? $"{section.Key} {so.Count}" : section.Key) +
                (dirty ? " · 미저장 (Raw 저장 또는 Ctrl+S)" : " · 저장됨") +
                (undo.Count > 0 ? $" · 언두 {undo.Count}" : "");
        }

        /// <summary>
        /// 객체의 멤버 — json에 있는 키는 <b>파일 순서 그대로</b>(스키마를 알면 그 타입으로), 스키마에만 있는
        /// 필드는 뒤에 유령 행으로(힌트 타입은 유령 없음). 스키마 순서를 앞세우면 Def 기본 클래스의 view가
        /// 맨 위로 올라와 파일과 달라진다. parentKey는 사전형 자식의 힌트(sfx → SoundUse 등)에 쓴다.
        /// </summary>
        void RenderObject(VisualElement host, JObject obj, Type schemaType, string path, string parentKey)
        {
            var fields = RawSchema.FieldsOf(schemaType);
            var byName = new Dictionary<string, FieldInfo>();
            if (fields != null) foreach (var (name, f) in fields) byName[name] = f;
            Type childHint = fields == null ? RawHints.ChildHint(parentKey) : null;

            foreach (var p in obj.Properties().ToList())
            {
                string key = p.Name;
                if (key == "type" && RawSchema.IsModuleType(schemaType)) continue;   // 모듈의 "type"은 제목이 대신한다
                byName.TryGetValue(key, out var f);
                var table = f != null ? RawSchema.ModuleTableFor(f.FieldType) : null;
                if (table != null) { RenderModules(host, obj, key, path + "/" + key, table); continue; }
                Type memberType = f?.FieldType ?? childHint;
                if (f != null && f.FieldType == typeof(JObject)) memberType = RawHints.ForJObjectField(key) ?? typeof(JObject);
                RenderMember(host, key, p.Value, memberType, path + "/" + key, key,
                    set: v => obj[key] = v, remove: () => obj.Remove(key), ghostDefault: null);
            }
            if (fields == null) { RenderAddKey(host, obj, parentKey); return; }
            if (RawHints.NoGhost.Contains(schemaType)) { RenderAddHintField(host, obj, schemaType, fields); return; }
            foreach (var (name, f) in fields)
            {
                if (obj[name] != null) continue;
                if (name == "type" && RawSchema.IsModuleType(schemaType)) continue;
                var table = RawSchema.ModuleTableFor(f.FieldType);
                if (table != null) { RenderModules(host, obj, name, path + "/" + name, table); continue; }
                Type memberType = f.FieldType == typeof(JObject) ? (RawHints.ForJObjectField(name) ?? typeof(JObject)) : f.FieldType;
                RenderMember(host, name, null, memberType, path + "/" + name, name,
                    set: v => obj[name] = v, remove: () => obj.Remove(name),
                    ghostDefault: () => RawSchema.DefaultOfField(schemaType, f));
            }
        }

        /// <summary>
        /// 힌트 타입 블록(view)에 없는 키를 더하는 드롭다운 — 유령 행 대신(정의마다 안 쓰는 키가 잔뜩 뜨지 않게).
        /// 사용자: "view에서 건물에 필드를 추가하는 기능이 없는데?"
        /// </summary>
        void RenderAddHintField(VisualElement host, JObject obj, Type schemaType, List<(string name, FieldInfo field)> fields)
        {
            var missing = fields.Where(f => obj[f.name] == null).ToList();
            if (missing.Count == 0) return;
            var choices = new List<string> { "+ 필드 추가…" };
            choices.AddRange(missing.Select(f => f.name));
            var add = new DropdownField(choices, 0) { style = { width = 200, marginTop = 2 } };
            add.RegisterValueChangedCallback(e =>
            {
                int k = choices.IndexOf(e.newValue);
                if (k <= 0) return;
                var (name, f) = missing[k - 1];
                Mutate(() => obj[name] = RawSchema.DefaultOfField(schemaType, f));
            });
            host.Add(add);
        }

        /// <summary>스키마 없는 사전형 블록(sfx·textures·colors·floats…)에 키를 더한다 — 값은 부모 키의 힌트 기본값.</summary>
        void RenderAddKey(VisualElement host, JObject obj, string parentKey)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };
            var name = new TextField { value = "", style = { width = 180, marginLeft = 0 } };
            name.textEdition.placeholder = "키 이름";
            Mono(name);
            row.Add(name);
            row.Add(Btn("+ 키", () =>
            {
                string key = (name.value ?? "").Trim();
                if (key.Length == 0) return;
                if (obj[key] != null) { EditorUtility.DisplayDialog("Raw", $"'{key}'는 이미 있습니다.", "확인"); return; }
                Mutate(() => obj[key] = RawHints.DefaultChild(parentKey));
            }, "이 블록에 키를 더한다 — 값은 블록 종류의 기본값"));
            host.Add(row);
        }

        void RenderMember(VisualElement host, string key, JToken tok, Type schemaType, string path, string hintKey,
            Action<JToken> set, Action remove, Func<JToken> ghostDefault)
        {
            bool ghost = tok == null;
            if (ghost) tok = ghostDefault?.Invoke() ?? JValue.CreateNull();
            Type elem = RawSchema.ElementOf(schemaType);
            bool isList = tok is JArray || RawSchema.IsList(schemaType);
            bool isObj = !isList && (tok is JObject || RawSchema.IsObjectLike(schemaType) || schemaType == typeof(JObject));
            if (isList) RenderArray(host, key, tok as JArray ?? new JArray(), elem, path, ghost, set, remove, ghostDefault);
            else if (isObj) RenderNested(host, key, tok as JObject ?? new JObject(), schemaType, path, ghost, set, remove, ghostDefault);
            else RenderScalar(host, key, tok, schemaType, path, hintKey, ghost, set, remove, ghostDefault);
        }

        void RenderScalar(VisualElement host, string key, JToken tok, Type t, string path, string hintKey, bool ghost,
            Action<JToken> set, Action remove, Func<JToken> ghostDefault)
        {
            var row = Row(); host.Add(row);
            if (key.Length > 0) row.Add(Key(key + ":", ghost));   // 배열의 스칼라 항목은 "- " 뒤에 값만
            VisualElement editor;
            var choices = t != null && t.IsEnum ? Enum.GetNames(t).ToList() : Choices(hintKey, path);
            if (choices != null && (t == null || t == typeof(string) || t.IsEnum))
            {
                // 고정 목록(enum) 또는 id 참조 — 값이 목록에 없으면(오타·옛 id) 맨 위에 끼워 보이게 한다
                string cur = tok.Type == JTokenType.String ? (string)tok : "";
                if (!choices.Contains(cur)) choices.Insert(0, cur);
                var d = new DropdownField(choices, Mathf.Max(0, choices.IndexOf(cur)));
                d.AddToClassList("gd-raw-drop");
                d.RegisterValueChangedCallback(e => { PushUndo(); set(e.newValue); AfterValueEdit(); });
                editor = d;
            }
            else if (t == typeof(bool) || (t == null && tok.Type == JTokenType.Boolean))
            {
                var tg = new Toggle { value = tok.Type == JTokenType.Boolean && (bool)tok };
                tg.RegisterValueChangedCallback(e => { PushUndo(); set(e.newValue); AfterValueEdit(); });
                editor = tg;
            }
            else if (t == typeof(int) || t == typeof(long) || (t == null && tok.Type == JTokenType.Integer))
            {
                var f = new IntegerField { value = tok.Type == JTokenType.Integer ? (int)tok : 0 };
                f.AddToClassList("gd-raw-num");
                Scalar(f, v => set(v));
                editor = f;
            }
            else if (t == typeof(float) || t == typeof(double) || (t == null && tok.Type == JTokenType.Float))
            {
                // float 기본값(1.3f)은 double로 읽으면 1.2999999523… — 표시용으로 6자리에서 반올림
                var f = new DoubleField { value = tok.Type is JTokenType.Float or JTokenType.Integer ? Math.Round((double)tok, 6) : 0 };
                f.AddToClassList("gd-raw-num");
                Scalar(f, v => set(NumToken(v)));
                editor = f;
            }
            else
            {
                var f = new TextField { value = tok.Type == JTokenType.Null ? "" : tok.ToString() };
                f.AddToClassList("gd-raw-text");
                Mono(f);
                Scalar(f, v => set(v));
                editor = f;
            }
            row.Add(editor);
            if (ghost)
            {
                editor.SetEnabled(false);
                editor.AddToClassList("gd-raw-ghost");
                row.Add(Btn("+ 추가", () => Mutate(() => set(ghostDefault())), "json에 없는 필드 — 누르면 기본값으로 생긴다"));
            }
            else
            {
                var kind = FileKind(hintKey, path);
                if (kind != null) row.Add(Btn("…", () => PickFile(kind, set), $"팩 폴더 {kind.folder}/ 안에서 파일 고르기 (*.{kind.ext})"));
                if (remove != null) row.Add(Btn("×", () => Mutate(remove), "키 삭제 — 스키마 필드면 기본값으로 돌아간다"));
            }
        }

        // ── 파일 고르기 — 팩 폴더 안의 파일을 팩 상대 경로로 ──────

        sealed class FileSlot { public string folder, ext; }

        /// <summary>팩 파일을 가리키는 키인가 — 어느 하위 폴더·확장자인지. 아니면 null.</summary>
        static FileSlot FileKind(string hintKey, string path)
        {
            if (hintKey == "clips") return new FileSlot { folder = "sounds", ext = "wav,ogg" };
            if (hintKey != "file") return null;
            if (path.Contains("/model/")) return new FileSlot { folder = "models", ext = "glb" };
            if (path.Contains("/textures/") || path.Contains("/icon/")) return new FileSlot { folder = "textures", ext = "png" };
            return new FileSlot { folder = "", ext = "" };
        }

        void PickFile(FileSlot kind, Action<JToken> set)
        {
            string packDir = Path.GetFullPath(GameDataExporterV2.PackFolder);
            string start = Path.Combine(packDir, kind.folder);
            if (!Directory.Exists(start)) start = packDir;
            string picked = EditorUtility.OpenFilePanel("팩 파일 고르기", start, kind.ext);
            if (string.IsNullOrEmpty(picked)) return;
            string full = Path.GetFullPath(picked);
            if (!full.StartsWith(packDir, StringComparison.OrdinalIgnoreCase))
            {
                // 팩은 자기 폴더 안의 파일만 가리킨다 — 밖의 파일은 먼저 복사해 넣어야 한다(빌드·모드 배포 단위)
                EditorUtility.DisplayDialog("Raw", $"팩 폴더 안의 파일만 쓸 수 있습니다.\n{GameDataExporterV2.PackFolder}", "확인");
                return;
            }
            string rel = full.Substring(packDir.Length).TrimStart('\\', '/').Replace('\\', '/');
            Mutate(() => set(rel));
        }

        /// <summary>값 편집기 — 값은 즉시 모델에, 언두 스냅샷은 포커스 단위(키 입력마다 본문을 다시 짓지 않는다).</summary>
        void Scalar<T>(BaseField<T> field, Action<T> apply)
        {
            string focusSnap = null;
            field.RegisterCallback<FocusInEvent>(_ => focusSnap = Snapshot());
            field.RegisterValueChangedCallback(e => { apply(e.newValue); AfterValueEdit(); });
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (focusSnap != null && focusSnap != Snapshot()) { undo.Push(focusSnap); redo.Clear(); RefreshStatus(); }
                focusSnap = null;
            });
        }

        /// <summary>정수로 떨어지는 실수는 정수 토큰으로 — 내보내기가 6을 6으로 쓰므로 diff를 안 만든다.</summary>
        static JToken NumToken(double v) =>
            Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15 ? new JValue((long)Math.Round(v)) : new JValue(v);

        void RenderNested(VisualElement host, string key, JObject obj, Type schemaType, string path, bool ghost,
            Action<JToken> set, Action remove, Func<JToken> ghostDefault)
        {
            bool folded = IsFolded(path);
            var group = Group(host, false, ghost);
            var row = HeadRow(); group.Add(row);
            row.Add(Fold(folded, path));
            row.Add(Key(key + ":", ghost));
            if (ghost) row.Add(Btn("+ 추가", () => Mutate(() => set(ghostDefault())), "json에 없는 객체 — 누르면 기본값으로 생긴다"));
            else if (remove != null) row.Add(Btn("×", () => Mutate(remove)));
            if (folded) return;
            var block = Block(); group.Add(block);
            if (ghost) block.SetEnabled(false);
            RenderObject(block, obj, schemaType == typeof(JObject) ? null : schemaType, path, key);
        }

        void RenderArray(VisualElement host, string key, JArray arr, Type elem, string path, bool ghost,
            Action<JToken> set, Action remove, Func<JToken> ghostDefault)
        {
            bool folded = IsFolded(path);
            var group = Group(host, false, ghost);
            var row = HeadRow(); group.Add(row);
            row.Add(Fold(folded, path));
            row.Add(Key(key + ":", ghost));
            row.Add(Count(arr.Count));
            if (ghost) row.Add(Btn("+ 추가", () => Mutate(() => set(ghostDefault())), "json에 없는 배열 — 누르면 빈 배열로 생긴다"));
            else if (remove != null) row.Add(Btn("×", () => Mutate(remove)));
            if (folded) return;
            var block = Block(); group.Add(block);
            if (ghost) block.SetEnabled(false);
            for (int i = 0; i < arr.Count; i++)
            {
                int idx = i;
                var item = arr[i];
                string ip = path + "/" + i;
                bool objItem = item is JObject || RawSchema.IsObjectLike(elem);
                if (objItem)
                {
                    bool f2 = IsFolded(ip);
                    var ig = Group(block, false, false);   // 배열의 객체 항목(struct) — 제 패널
                    var r = HeadRow(); ig.Add(r);
                    r.Add(Fold(f2, ip));
                    r.Add(Dash());
                    r.Add(Summary(item as JObject));
                    r.Add(Spacer());
                    AddMoveButtons(r, arr, idx);
                    if (f2) continue;
                    var b = Block(); ig.Add(b);
                    RenderObject(b, item as JObject ?? new JObject(), elem, ip, key);
                }
                else
                {
                    var r = Row(); block.Add(r);
                    r.Add(Dash());
                    var cell = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1, minWidth = 0 } };
                    r.Add(cell);
                    RenderScalar(cell, "", item, elem, ip, key, false, v => arr[idx] = v, null, null);   // 원소의 힌트 키 = 배열 키(materials·ammoFilter…)
                    AddMoveButtons(r, arr, idx);
                }
            }
            if (!ghost)
                block.Add(Btn("+ 추가", () => Mutate(() =>
                    arr.Add(elem != null ? RawSchema.DefaultOf(elem) : arr.Count > 0 ? arr[arr.Count - 1].DeepClone() : ""))));
        }

        void AddMoveButtons(VisualElement row, JArray arr, int i)
        {
            if (i > 0) row.Add(Btn("▲", () => Mutate(() => { var t = arr[i]; arr.RemoveAt(i); arr.Insert(i - 1, t); })));
            if (i < arr.Count - 1) row.Add(Btn("▼", () => Mutate(() => { var t = arr[i]; arr.RemoveAt(i); arr.Insert(i + 1, t); })));
            row.Add(Btn("×", () => Mutate(() => arr.RemoveAt(i))));
        }

        /// <summary>modules — 항목 제목이 "type", 본문은 그 정의 타입의 스키마. 추가는 표(엔티티/아이템 모듈)의 드롭다운.</summary>
        void RenderModules(VisualElement host, JObject owner, string key, string path, IReadOnlyDictionary<string, Type> table)
        {
            var arr = owner[key] as JArray;
            bool folded = IsFolded(path);
            var group = Group(host, false, arr == null);
            var row = HeadRow(); group.Add(row);
            row.Add(Fold(folded, path));
            row.Add(Key(key + ":", arr == null));
            row.Add(Count(arr?.Count ?? 0));
            if (folded) return;
            var block = Block(); group.Add(block);
            if (arr != null)
                for (int i = 0; i < arr.Count; i++)
                {
                    int idx = i;
                    var m = arr[i] as JObject;
                    string ip = path + "/" + i;
                    string type = m?["type"]?.ToString() ?? "?";
                    bool known = table.TryGetValue(type, out var defType);
                    bool f2 = IsFolded(ip);
                    var mg = Group(block, true, false);   // 모듈 하나 = 시안 테두리 패널
                    var r = HeadRow(); mg.Add(r);
                    r.Add(Fold(f2, ip));
                    r.Add(Dash());
                    var ttl = new Label(type); ttl.AddToClassList("gd-raw-modttl"); Mono(ttl); r.Add(ttl);
                    if (!known) { var w = new Label("모르는 모듈 종류"); w.AddToClassList("gd-bitem-kd"); r.Add(w); }
                    r.Add(Spacer());
                    AddMoveButtons(r, arr, idx);
                    if (f2) continue;
                    var b = Block(); mg.Add(b);
                    if (m != null) RenderObject(b, m, known ? defType : null, ip, key);
                }
            var choices = new List<string> { "+ 모듈 추가…" };
            choices.AddRange(table.Keys.OrderBy(k => k, StringComparer.Ordinal));
            var add = new DropdownField(choices, 0) { style = { width = 200, marginTop = 2 } };
            add.RegisterValueChangedCallback(e =>
            {
                int k = choices.IndexOf(e.newValue);
                if (k <= 0) return;
                string type = choices[k];
                Mutate(() =>
                {
                    if (owner[key] is not JArray a) { a = new JArray(); owner[key] = a; }
                    a.Add(RawSchema.DefaultModule(table, type));
                });
            });
            block.Add(add);
        }

        // ── 참조 드롭다운 — id는 팩 섹션 키에서 ────────────────

        /// <summary>필드 이름·경로로 고정 선택지를 정한다. 없으면 null(자유 입력).</summary>
        List<string> Choices(string hintKey, string path)
        {
            switch (hintKey)
            {
                case "type":
                    if (path.EndsWith("/view/type")) return Data.ViewSchema.Types.Keys.ToList();
                    if (path.Contains("/conditions/")) return Tutorial.TutorialConditions.Kinds.ToList();
                    return null;
                case "item": return Ids("items");
                case "filter": return path.Contains("/ammo/") ? Ids("items") : null;   // GunDef.ammo.filter
                case "effect": return Ids("effects");
                case "gun": return Ids("guns");
                case "sound": return Ids("sounds");
                case "materials": return Ids("materials");
                default: return null;
            }
        }

        List<string> Ids(string sectionKey)
        {
            var sec = Sections.FirstOrDefault(s => s.Key == sectionKey);
            if (sec == null || pack?[sectionKey] is not JObject so) return null;
            string packName = pack["pack"]?.ToString() ?? "coredawn";
            return so.Properties().Select(p => $"{packName}:{sec.Singular}/{p.Name}").ToList();
        }

        // ── 조각 ────────────────────────────────────────────────

        static VisualElement Row()
        {
            var r = new VisualElement();
            r.AddToClassList("gd-raw-row");
            return r;
        }

        /// <summary>헤더 행 — 패널의 제목 줄. 스칼라 행과 달리 자기 배경이 없다(패널 배경이 곧 배경).</summary>
        static VisualElement HeadRow()
        {
            var r = Row();
            r.AddToClassList("gd-raw-row--head");
            return r;
        }

        static VisualElement Block()
        {
            var b = new VisualElement();
            b.AddToClassList("gd-raw-block");
            return b;
        }

        /// <summary>
        /// 패널 — 객체·배열·모듈처럼 내부 필드를 가진 큰 값 하나를 감싼다(헤더 행 + 본문). 반투명 배경이라
        /// 중첩될수록 겹쳐 밝아지고, 모듈은 시안 테두리로 한 단계 더 구분한다(사용자: "큰 객체는 내부 필드보다 더 넓은 배경").
        /// </summary>
        static VisualElement Group(VisualElement host, bool module, bool ghost)
        {
            var g = new VisualElement();
            g.AddToClassList("gd-raw-group");
            if (module) g.AddToClassList("gd-raw-group--module");
            if (ghost) g.AddToClassList("gd-raw-ghost");
            host.Add(g);
            return g;
        }

        /// <summary>키 라벨 — json에 없는(유령) 필드는 제목 자체를 반투명하게(사용자: "# 말고 필드 제목 자체도 반투명").</summary>
        static Label Key(string text, bool ghost)
        {
            var l = new Label(text);
            l.AddToClassList("gd-raw-key");
            if (ghost) { l.AddToClassList("gd-raw-key--ghost"); l.AddToClassList("gd-raw-ghost"); }
            Mono(l);
            return l;
        }

        static Label Dash()
        {
            var l = new Label("-");
            l.AddToClassList("gd-raw-dash");
            Mono(l);
            return l;
        }

        static Label Count(int n)
        {
            var l = new Label($"[{n}]");
            l.AddToClassList("gd-raw-count");
            return l;
        }

        static VisualElement Spacer() => new() { style = { flexGrow = 1 } };

        /// <summary>객체 항목 한 줄 요약 — item·type·file 같은 첫 스칼라 키를 보여 접힌 상태에서도 구분되게.</summary>
        static Label Summary(JObject o)
        {
            string s = "";
            if (o != null)
            {
                var first = o.Properties().FirstOrDefault(p => p.Value is JValue);
                if (first != null) s = $"{first.Name}: {first.Value}";
            }
            var l = new Label(s); l.AddToClassList("gd-bitem-kd"); Mono(l);
            return l;
        }

        /// <summary>접힘 상태 — 전부 기본 펼침(사용자: "view 왜 다 기본으로 접혀 있음? 펼쳐놔"). 접은 것만 기억한다.</summary>
        bool IsFolded(string path) => collapsed.Contains(path);

        Button Fold(bool folded, string path)
        {
            var b = new Button(() => { if (!collapsed.Remove(path)) collapsed.Add(path); RenderTree(); }) { text = folded ? "▸" : "▾" };
            b.AddToClassList("gd-raw-fold");
            return b;
        }

        static Button Btn(string text, Action a, string tooltip = null)
        {
            var b = new Button(a) { text = text };
            b.AddToClassList("gd-raw-btn");
            b.style.alignSelf = Align.FlexStart;   // 세로 블록 안에서 가로로 늘어나지 않게
            if (tooltip != null) b.tooltip = tooltip;
            return b;
        }

        static void Mono(VisualElement ve)
        {
            if (GameDataEditorWindow.monoFont != null)
                ve.style.unityFontDefinition = FontDefinition.FromFont(GameDataEditorWindow.monoFont);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  편집기 전용 힌트 스키마 — 심에 타입이 없는 블록(view·sfx)의 꼴을 편집기가 알게 한다.
    //  json 정본은 여전히 자유 JObject다(런타임은 ViewSpec이 키로 읽는다). 여기 타입은 UI 힌트일 뿐이라
    //  유령 행(없는 필드 제안)은 View·Pose에서 만들지 않는다 — 정의마다 안 쓰는 키가 잔뜩 뜨지 않게.
    // ═══════════════════════════════════════════════════════════
    static class RawHints
    {
        /// <summary>정의의 view 블록(Def.View). 키 이름은 ViewSpec이 읽는 것과 같다.</summary>
        public sealed class View
        {
            [JsonProperty("type")] public string Type;
            [JsonProperty("model")] public List<ModelRef> Model;
            [JsonProperty("icon")] public Icon Icon;
            [JsonProperty("pose")] public Pose Pose;
            [JsonProperty("poseCurveL")] public Pose PoseCurveL;
            [JsonProperty("poseCurveR")] public Pose PoseCurveR;
            [JsonProperty("sfx")] public JObject Sfx;
            [JsonProperty("clips")] public List<string> Clips;
            [JsonProperty("shader")] public string Shader;
            [JsonProperty("textures")] public JObject Textures;
            [JsonProperty("colors")] public JObject Colors;
            [JsonProperty("floats")] public JObject Floats;
            [JsonProperty("keywords")] public List<string> Keywords;
        }

        public sealed class ModelRef
        {
            [JsonProperty("file")] public string File = "";
            [JsonProperty("materials")] public List<string> Materials = new();
        }

        public sealed class Icon
        {
            [JsonProperty("file")] public string File = "";
            [JsonProperty("frame")] public string Frame = "";
        }

        public sealed class Pose
        {
            [JsonProperty("position")] public float[] Position = { 0f, 0f, 0f };
            [JsonProperty("rotation")] public float[] Rotation = { 0f, 0f, 0f };
            [JsonProperty("scale")] public float Scale = 1f;
        }

        /// <summary>소리 자리 하나 — view.sfx의 값이자 팩 최상위 sfx 섹션의 항목(Data.SoundUse와 같은 키).</summary>
        public sealed class SoundUse
        {
            [JsonProperty("sound")] public string Sound = "";
            [JsonProperty("volume")] public float Volume = 1f;
            [JsonProperty("spatial")] public bool Spatial = true;
        }

        public sealed class TextureRef
        {
            [JsonProperty("file")] public string File = "";
            [JsonProperty("linear")] public bool Linear;
        }

        public static readonly HashSet<Type> NoGhost = new() { typeof(View), typeof(Pose) };

        /// <summary>JObject 타입 필드에 붙는 힌트 — 지금은 view뿐.</summary>
        public static Type ForJObjectField(string fieldName) => fieldName == "view" ? typeof(View) : null;

        /// <summary>사전형 블록의 자식 힌트 — sfx의 값은 소리 자리, textures의 값은 텍스처 참조.</summary>
        public static Type ChildHint(string parentKey) => parentKey switch
        {
            "sfx" => typeof(SoundUse),
            "textures" => typeof(TextureRef),
            _ => null,
        };

        /// <summary>사전형 블록에 새 키를 더할 때의 값 — 힌트 타입이 있으면 그 기본, colors는 흰색 RGBA, floats는 0, 나머지는 빈 문자열.</summary>
        public static JToken DefaultChild(string parentKey)
        {
            var t = ChildHint(parentKey);
            if (t != null) return RawSchema.DefaultOf(t);
            return parentKey switch
            {
                "colors" => new JArray(1f, 1f, 1f, 1f),
                "floats" => 0f,
                _ => "",
            };
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  스키마 조회 — 정의 타입을 리플렉션으로 읽는다(편집기 전용; 심은 리플렉션을 안 쓴다).
    // ═══════════════════════════════════════════════════════════
    static class RawSchema
    {
        static readonly JsonSerializer Ser = SimSchema.CreateSerializer();
        static readonly Dictionary<Type, List<(string name, FieldInfo field)>> cache = new();

        /// <summary>[JsonProperty] 공개 필드 — 기본 클래스(Def) 것이 먼저, 선언 순서.</summary>
        public static List<(string name, FieldInfo field)> FieldsOf(Type t)
        {
            if (t == null) return null;
            if (cache.TryGetValue(t, out var list)) return list;
            list = new List<(string, FieldInfo)>();
            var chain = new List<Type>();
            for (var c = t; c != null && c != typeof(object); c = c.BaseType) chain.Insert(0, c);
            foreach (var c in chain)
                foreach (var f in c.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var jp = f.GetCustomAttribute<JsonPropertyAttribute>();
                    if (jp == null || f.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                    list.Add((jp.PropertyName ?? f.Name, f));
                }
            cache[t] = list;
            return list;
        }

        public static bool IsList(Type t) => t != null && (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)));
        public static Type ElementOf(Type t) => t == null ? null : t.IsArray ? t.GetElementType() : IsList(t) ? t.GetGenericArguments()[0] : null;

        public static bool IsObjectLike(Type t) =>
            t != null && t != typeof(string) && !t.IsPrimitive && !t.IsEnum && t != typeof(JObject) && !IsList(t)
            && (FieldsOf(t)?.Count ?? 0) > 0;

        public static bool IsModuleType(Type t) => t != null && typeof(ModuleDef).IsAssignableFrom(t);

        /// <summary>모듈 리스트 필드면 그 표(엔티티/아이템), 아니면 null.</summary>
        public static IReadOnlyDictionary<string, Type> ModuleTableFor(Type fieldType)
        {
            if (fieldType == typeof(List<EntityModuleDef>)) return SimSchema.EntityModules;
            if (fieldType == typeof(List<ItemModuleDef>)) return SimSchema.ItemModules;
            return null;
        }

        public static JToken DefaultOf(Type t)
        {
            if (t == null) return "";
            if (t == typeof(string)) return "";
            if (t == typeof(bool)) return false;
            if (t == typeof(int) || t == typeof(long)) return 0;
            if (t == typeof(float) || t == typeof(double)) return 0;
            if (t.IsEnum) return Enum.GetNames(t)[0];
            if (IsList(t)) return new JArray();
            if (t == typeof(JObject)) return new JObject();
            try { return JToken.FromObject(Activator.CreateInstance(t), Ser); } catch { return new JObject(); }
        }

        /// <summary>필드 기본값 — 정의 타입의 기본 인스턴스에서 읽는다(필드 초기화식이 곧 기본값).</summary>
        public static JToken DefaultOfField(Type owner, FieldInfo f)
        {
            try
            {
                var inst = Activator.CreateInstance(owner);
                var v = f.GetValue(inst);
                return v == null ? DefaultOf(f.FieldType) : JToken.FromObject(v, Ser);
            }
            catch { return DefaultOf(f.FieldType); }
        }

        /// <summary>정의 타입의 기본 json — 새 항목의 출발점(모든 필드가 기본값으로 들어간다).</summary>
        public static JObject DefaultObject(Type defType)
        {
            try
            {
                var o = JToken.FromObject(Activator.CreateInstance(defType), Ser) as JObject ?? new JObject();
                if (FieldsOf(defType).Any(f => f.name == "displayName") && o["displayName"] == null) o.AddFirst(new JProperty("displayName", ""));
                return o;
            }
            catch { return new JObject(); }
        }

        /// <summary>모듈 기본 json — "type"이 앞에 오고 모든 필드가 기본값으로 들어간다.</summary>
        public static JObject DefaultModule(IReadOnlyDictionary<string, Type> table, string type)
        {
            var t = table[type];
            var o = (JObject)JToken.FromObject(Activator.CreateInstance(t), Ser);
            if (o["type"] == null) o.AddFirst(new JProperty("type", type));
            return o;
        }

        /// <summary>정의 json을 심의 직렬화기로 읽어 본다 — 모르는 키·타입 불일치를 잡는다. 통과하면 null.</summary>
        public static string Validate(JObject def, Type defType)
        {
            try { def.ToObject(defType, Ser); return null; }
            catch (Exception e) { return e.Message; }
        }
    }
}
#endif
