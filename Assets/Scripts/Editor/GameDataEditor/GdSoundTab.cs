#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.UI;

namespace CoreDawn.EditorTools
{
    // ═══════════════════════════════════════════════════════════
    //  사운드 탭 — 팩 sounds(소리 = 변형 클립 묶음)와 최상위 sfx(공용 소리 자리) 편집.
    //  소리 자체는 클립 파일만 가리키고, 볼륨·공간감은 쓰는 자리(여기의 공용 자리, 각 정의 패널의 뷰 조각)가 적는다.
    //  클립은 팩 상대 경로("sounds/x.wav")다 — 팩 폴더 안의 파일만.
    // ═══════════════════════════════════════════════════════════
    class GClip { public string clip = ""; }
    class GSound { public string id = "", displayName = ""; public List<GClip> clips = new(); }
    class GSfx { public string name = "", sound = ""; public float volume = 1f; public bool spatial; }

    class GdSoundTab : GdTab
    {
        public override string Title => "사운드";
        public GdSoundTab(GameDataEditorWindow win) : base(win) { win.SoundIds = () => sounds.Select(s => s.id).Where(id => !string.IsNullOrEmpty(id)).ToList(); }

        List<GSound> sounds = new();
        List<GSfx> common = new();
        int cur;

        internal override (string section, string id) RawCursor => ("sounds", GdPack.Bare(sounds.ElementAtOrDefault(cur)?.id));
        internal override void SelectRaw(string section, string id)
        {
            if (section != "sounds") return;
            int i = sounds.FindIndex(x => GdPack.Bare(x.id) == id);
            if (i < 0) return;
            cur = i;
            if (listBox != null) Render();
        }
        GdHistory hist;
        VisualElement listBox, detailBox, commonBox;
        Label statLabel;

        // ── 데이터 왕복 ──
        public override void OnDataLoaded()
        {
            sounds = (win.root?.sounds ?? Array.Empty<GameDataJson.SoundDto>()).Select(s => new GSound
            {
                id = s.id ?? "", displayName = s.displayName ?? "",
                clips = (s.clips ?? Array.Empty<GameDataJson.ClipDto>()).Select(c => new GClip { clip = c.clip ?? "" }).ToList(),
            }).ToList();
            common = (win.root?.sfx ?? new Dictionary<string, GameDataJson.SfxUseDto>())
                .Select(kv => new GSfx { name = kv.Key, sound = kv.Value?.sound ?? "", volume = kv.Value?.volume ?? 1f, spatial = kv.Value?.spatial ?? false }).ToList();
            cur = 0;
            hist = new GdHistory(Snapshot, Restore, 60);
            hist.Reset();
        }

        public override void SyncToRoot()
        {
            if (win.root == null || hist == null) return;
            win.root.sounds = sounds.Select(s => new GameDataJson.SoundDto
            {
                id = s.id, displayName = string.IsNullOrEmpty(s.displayName) ? null : s.displayName,
                clips = s.clips.Where(c => !string.IsNullOrEmpty(c.clip)).Select(c => new GameDataJson.ClipDto { clip = c.clip }).ToArray(),
            }).ToArray();
            win.root.sfx = common.Where(c => !string.IsNullOrEmpty(c.name))
                .ToDictionary(c => c.name, c => new GameDataJson.SfxUseDto { sound = c.sound, volume = c.volume, spatial = c.spatial });
        }

        string Snapshot() => JsonConvert.SerializeObject(new { sounds, common, cur });
        void Restore(string snap)
        {
            var o = JsonConvert.DeserializeAnonymousType(snap, new { sounds = new List<GSound>(), common = new List<GSfx>(), cur = 0 });
            sounds = o.sounds ?? new(); common = o.common ?? new(); cur = Mathf.Clamp(o.cur, 0, Mathf.Max(0, sounds.Count - 1));
            Render();
        }
        void PushHist() { hist?.Push(); win.MarkDirty(); }
        public override void Undo() { if (hist?.Undo() == true) win.MarkDirty(); }
        public override void Redo() { if (hist?.Redo() == true) win.MarkDirty(); }
        public override bool DeleteSelection() { if (sounds.Count == 0) return false; DeleteCurrent(); return true; }

        // ── UI ──
        public override void Build(VisualElement host)
        {
            host.style.backgroundColor = GdEnum.Bg;
            var top = new VisualElement(); top.AddToClassList("gd-topbar"); host.Add(top);
            var title = new Label("사운드 에디터"); title.AddToClassList("gd-topbar-title"); top.Add(title);
            var small = new Label("소리(변형 클립 묶음) · 공용 자리"); small.AddToClassList("gd-topbar-small"); top.Add(small);
            var addB = new Button(AddSound) { text = "+ 소리 추가" }; addB.AddToClassList("gd-btn-mini"); top.Add(addB);
            var delB = new Button(DeleteCurrent) { text = "삭제" }; delB.AddToClassList("gd-btn-mini"); delB.AddToClassList("gd-btn-warn"); top.Add(delB);
            top.Add(new VisualElement { style = { flexGrow = 1 } });
            statLabel = new Label(); statLabel.AddToClassList("gd-stat"); Mono(statLabel); top.Add(statLabel);

            var main = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            host.Add(main);
            var left = new ScrollView { style = { width = 260 } }; left.AddToClassList("gd-leftcol"); main.Add(left);
            listBox = new VisualElement { style = { marginTop = 6, minHeight = 200 } }; left.Add(listBox);
            var right = new ScrollView { style = { flexGrow = 1, paddingLeft = 14, paddingRight = 14, paddingTop = 10 } }; main.Add(right);
            detailBox = new VisualElement(); right.Add(detailBox);
            commonBox = new VisualElement { style = { marginTop = 16 } }; right.Add(commonBox);
            right.Add(Hint("소리는 클립 파일만 가리킨다 — 같은 클립도 총에서는 크게 3D로, UI에서는 작게 2D로 틀 수 있으니 볼륨·공간감은 쓰는 자리의 값이다. " +
                           "정의(총·건물·몬스터)의 자리는 각 패널의 '뷰' 조각에서, 정의에 속하지 않는 공용 자리(ui_click·construct·mine…)는 아래 표에서 적는다. " +
                           "클립을 여럿 넣으면 재생 때 하나를 무작위로 고른다(기관총처럼 연사가 빠른 소리는 여럿 넣어야 귀가 덜 피곤하다)."));
            Render();
        }

        void AddSound() { sounds.Add(new GSound { id = "", displayName = "새 소리" }); cur = sounds.Count - 1; PushHist(); Render(); }
        void DeleteCurrent()
        {
            if (sounds.Count == 0) return;
            sounds.RemoveAt(Mathf.Clamp(cur, 0, sounds.Count - 1));
            cur = Mathf.Clamp(cur, 0, Mathf.Max(0, sounds.Count - 1));
            PushHist(); Render();
        }

        void Render() { RenderList(); RenderDetail(); RenderCommon(); RefreshMeta(); }

        void RenderList()
        {
            listBox.Clear();
            if (sounds.Count == 0)
            {
                listBox.Add(new Label("소리가 없습니다 — 위의 + 소리 추가로 시작하세요") { style = { color = GdEnum.Faint, fontSize = 12, whiteSpace = WhiteSpace.Normal } });
                return;
            }
            for (int i = 0; i < sounds.Count; i++)
            {
                var s = sounds[i]; int idx = i;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                row.AddToClassList("gd-bitem");
                if (i == cur) row.AddToClassList("gd-bitem--sel");
                row.RegisterCallback<ClickEvent>(_ => { cur = idx; Render(); });
                row.Add(new Label(string.IsNullOrEmpty(s.id) ? "(id 없음)" : GdPack.Bare(s.id)) { style = { flexGrow = 1 } });
                var meta = new Label($"×{s.clips.Count}") { style = { color = GdEnum.Muted, fontSize = 11 } };
                Mono(meta); row.Add(meta);
                listBox.Add(row);
            }
        }

        void RenderDetail()
        {
            detailBox.Clear();
            var s = sounds.ElementAtOrDefault(cur);
            if (s == null) return;
            detailBox.Add(H3("소리"));
            string bare = GdPack.Bare(s.id);
            var idF = Mono(new TextField { value = bare, tooltip = "coredawn:sound/ 접두는 자동으로 붙는다. 뷰의 자리들이 이 id로 가리킨다 — 바꾸면 그 자리들도 바꿔야 한다" });
            idF.RegisterValueChangedCallback(e =>
            {
                var clean = new string(e.newValue.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                s.id = string.IsNullOrEmpty(clean) ? "" : GdPack.Id("sound", clean);
                RefreshMeta();
            });
            idF.RegisterCallback<FocusOutEvent>(_ => { PushHist(); RenderList(); });
            detailBox.Add(Field2("Id", idF));
            detailBox.Add(Text("이름(표시)", s.displayName, v => s.displayName = v));

            detailBox.Add(H3("클립 — 변형 묶음"));
            for (int i = 0; i < s.clips.Count; i++)
            {
                var c = s.clips[i]; int ci = i;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 3 } };
                var f = GdPackAssets.FileRow(c.clip, "sounds", "wav,ogg", v => { c.clip = v; PushHist(); RenderList(); RefreshMeta(); });
                f.style.flexGrow = 1;
                row.Add(f);
                var x = new Label("✕") { style = { color = GdEnum.Faint, fontSize = 12, paddingLeft = 6 } };
                x.RegisterCallback<PointerDownEvent>(_ => { s.clips.RemoveAt(ci); PushHist(); Render(); });
                row.Add(x);
                detailBox.Add(row);
            }
            var addC = new Button(() => { s.clips.Add(new GClip()); PushHist(); Render(); }) { text = "+ 클립" };
            addC.AddToClassList("gd-btn-mini");
            detailBox.Add(addC);
        }

        void RenderCommon()
        {
            commonBox.Clear();
            commonBox.Add(H3("공용 자리 (sfx) — 정의에 속하지 않는 소리"));
            var ids = sounds.Select(x => x.id).Where(id => !string.IsNullOrEmpty(id)).ToList();
            for (int i = 0; i < common.Count; i++)
            {
                var u = common[i]; int ui = i;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 3 } };
                var nameF = Mono(new TextField { value = u.name, style = { width = 120 }, tooltip = "코드가 SoundManager.PlayCommon(\"이름\")으로 부르는 자리 이름" });
                nameF.RegisterValueChangedCallback(e => u.name = e.newValue);
                nameF.RegisterCallback<FocusOutEvent>(_ => PushHist());
                row.Add(nameF);
                var choices = new List<string> { "(없음)" }; choices.AddRange(ids);
                int idx = 0;
                if (!string.IsNullOrEmpty(u.sound)) { int at = ids.IndexOf(u.sound); if (at >= 0) idx = at + 1; else { choices.Add(u.sound + " — 없음"); idx = choices.Count - 1; } }
                var d = new DropdownField(choices, idx) { style = { flexGrow = 1, marginLeft = 4 } };
                d.RegisterValueChangedCallback(e => { int k = choices.IndexOf(e.newValue); u.sound = k <= 0 || k - 1 >= ids.Count ? "" : ids[k - 1]; PushHist(); });
                row.Add(d);
                var volF = new FloatField { value = u.volume, style = { width = 52, marginLeft = 4 } };
                volF.RegisterValueChangedCallback(e => u.volume = Mathf.Clamp01(e.newValue));
                volF.RegisterCallback<FocusOutEvent>(_ => PushHist());
                row.Add(volF);
                var spT = new Toggle("3D") { value = u.spatial, style = { marginLeft = 4, fontSize = 10.5f } };
                spT.RegisterValueChangedCallback(e => { u.spatial = e.newValue; PushHist(); });
                row.Add(spT);
                var x = new Label("✕") { style = { color = GdEnum.Faint, fontSize = 12, paddingLeft = 6 } };
                x.RegisterCallback<PointerDownEvent>(_ => { common.RemoveAt(ui); PushHist(); RenderCommon(); RefreshMeta(); });
                row.Add(x);
                commonBox.Add(row);
            }
            var addB = new Button(() => { common.Add(new GSfx { name = "" }); PushHist(); RenderCommon(); }) { text = "+ 자리" };
            addB.AddToClassList("gd-btn-mini");
            commonBox.Add(addB);
        }

        void RefreshMeta()
        {
            int clips = sounds.Sum(s => s.clips.Count(c => !string.IsNullOrEmpty(c.clip)));
            var dup = sounds.Where(s => !string.IsNullOrEmpty(s.id)).GroupBy(s => s.id).Count(g => g.Count() > 1);
            statLabel.text = $"소리 {sounds.Count} · 클립 {clips} · 공용 자리 {common.Count}" + (dup > 0 ? $" · id 중복 {dup}" : "");
        }
    }
}
#endif
