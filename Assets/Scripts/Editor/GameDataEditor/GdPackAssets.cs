#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Managers;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 편집기가 팩 폴더의 파일을 다루는 도우미 — 목록·고르기·아이콘 시트 미리보기·모델 미리보기 부품.
    /// 팩은 자기 폴더 안의 파일만 가리킨다(빌드·모드 배포 단위) — guid·에셋 복사 없음(3e-2 ③).
    /// </summary>
    static class GdPackAssets
    {
        public static string Folder => GdPack.PackFolder;

        /// <summary>하위 폴더의 파일을 팩 상대 경로("models/x.glb")로.</summary>
        public static List<string> Files(string sub, params string[] exts)
        {
            var dir = Path.Combine(Folder, sub);
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir)
                .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()) && !f.EndsWith(".meta"))
                .Select(f => sub + "/" + Path.GetFileName(f))
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
        }

        /// <summary>팩 하위 폴더에서 파일을 고른다 — 고른 파일의 팩 상대 경로, 취소하면 null. 팩 밖 파일은 거부.</summary>
        public static string PickFile(string sub, string exts)
        {
            string packDir = Path.GetFullPath(Folder);
            string start = Path.Combine(packDir, sub);
            if (!Directory.Exists(start)) start = packDir;
            string picked = EditorUtility.OpenFilePanel("팩 파일 고르기", start, exts);
            if (string.IsNullOrEmpty(picked)) return null;
            string full = Path.GetFullPath(picked);
            if (!full.StartsWith(packDir, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("팩 파일", $"팩 폴더 안의 파일만 쓸 수 있습니다.\n{Folder}\n\n밖의 파일은 먼저 그 폴더에 복사해 넣으세요.", "확인");
                return null;
            }
            return full.Substring(packDir.Length).TrimStart('\\', '/').Replace('\\', '/');
        }

        /// <summary>경로 입력 + "…" 고르기 + "✕" 지우기 한 줄.</summary>
        public static VisualElement FileRow(string value, string sub, string exts, Action<string> set, string tooltip = null)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var f = new TextField { value = value ?? "", style = { flexGrow = 1, marginLeft = 0, marginRight = 0 } };
            if (tooltip != null) f.tooltip = tooltip;
            f.RegisterValueChangedCallback(e => set(e.newValue));
            row.Add(f);
            var pick = new Button(() => { var p = PickFile(sub, exts); if (p != null) { f.SetValueWithoutNotify(p); set(p); } }) { text = "…", tooltip = $"팩 {sub}/ 에서 고르기 (*.{exts})" };
            pick.AddToClassList("gd-btn-mini");
            row.Add(pick);
            var clear = new Button(() => { f.SetValueWithoutNotify(""); set(""); }) { text = "✕", tooltip = "지우기" };
            clear.AddToClassList("gd-btn-mini");
            row.Add(clear);
            return row;
        }

        // ── 아이콘 시트(png + 같은 이름의 .json 좌표표) ──

        static readonly Dictionary<string, (Texture2D tex, JObject frames, float ppu)> sheets = new();
        static readonly Dictionary<string, Sprite> sprites = new();

        public static void ClearCache()
        {
            foreach (var s in sheets.Values) if (s.tex != null) UnityEngine.Object.DestroyImmediate(s.tex);
            sheets.Clear(); sprites.Clear();
        }

        static (Texture2D tex, JObject frames, float ppu) Sheet(string file)
        {
            if (string.IsNullOrEmpty(file)) return default;
            if (sheets.TryGetValue(file, out var s) && s.tex != null) return s;
            string png = Path.Combine(Folder, file), json = png + ".json";
            if (!File.Exists(png)) return default;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            tex.LoadImage(File.ReadAllBytes(png));
            JObject frames = null; float ppu = 100f;
            if (File.Exists(json))
            {
                var o = JObject.Parse(File.ReadAllText(json));
                frames = o["frames"] as JObject;
                ppu = (float?)o["pixelsPerUnit"] ?? 100f;
            }
            s = (tex, frames, ppu);
            sheets[file] = s;
            return s;
        }

        /// <summary>시트의 프레임 이름들(좌표표가 없으면 빈 목록 = 낱장).</summary>
        public static List<string> Frames(string file)
        {
            var s = Sheet(file);
            return s.frames == null ? new List<string>() : s.frames.Properties().Select(p => p.Name).ToList();
        }

        /// <summary>아이콘 스프라이트(미리보기용) — 좌표표가 있으면 프레임, 없으면 낱장 전체. 못 찾으면 null.</summary>
        public static Sprite Icon(string file, string frame)
        {
            if (string.IsNullOrEmpty(file)) return null;
            string key = file + "|" + frame;
            if (sprites.TryGetValue(key, out var sp) && sp != null) return sp;
            var s = Sheet(file);
            if (s.tex == null) return null;
            Rect rect; Vector2 pivot = new Vector2(0.5f, 0.5f);
            if (s.frames != null && !string.IsNullOrEmpty(frame) && s.frames[frame] is JObject fr)
            {
                rect = new Rect((float)fr["x"], (float)fr["y"], (float)fr["w"], (float)fr["h"]);
                if (fr["px"] != null && rect.width > 0 && rect.height > 0) pivot = new Vector2((float)fr["px"] / rect.width, (float)fr["py"] / rect.height);
            }
            else rect = new Rect(0, 0, s.tex.width, s.tex.height);
            sp = Sprite.Create(s.tex, rect, pivot, s.ppu);
            sp.hideFlags = HideFlags.HideAndDontSave;
            sprites[key] = sp;
            return sp;
        }

        /// <summary>아이콘 고르기 조각 — 미리보기 + 시트 파일(…) + 프레임 드롭다운.</summary>
        public static VisualElement IconRow(string file, string frame, Action<string, string> set)
        {
            var box = new VisualElement();
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var prev = new Image { style = { width = 34, height = 34, marginRight = 4, flexShrink = 0 }, sprite = Icon(file, frame) };
            row.Add(prev);
            string curFile = file ?? "", curFrame = frame ?? "";
            var frameDrop = new DropdownField(new List<string> { curFrame }, 0) { style = { width = 160, marginLeft = 4 } };
            void FillFrames()
            {
                var frames = Frames(curFile);
                if (frames.Count == 0) { frameDrop.choices = new List<string> { "" }; frameDrop.SetValueWithoutNotify(""); frameDrop.SetEnabled(false); return; }
                if (!frames.Contains(curFrame)) frames.Insert(0, curFrame);
                frameDrop.choices = frames; frameDrop.SetValueWithoutNotify(curFrame); frameDrop.SetEnabled(true);
            }
            var fileRow = FileRow(curFile, "textures", "png", v =>
            {
                curFile = v;
                var frames = Frames(curFile);
                if (frames.Count > 0 && !frames.Contains(curFrame)) curFrame = frames[0];
                if (frames.Count == 0) curFrame = "";
                FillFrames(); prev.sprite = Icon(curFile, curFrame); set(curFile, curFrame);
            }, "아이콘 시트(png) — 같은 이름의 .json 좌표표가 있으면 프레임을 고른다");
            fileRow.style.flexGrow = 1;
            row.Add(fileRow);
            frameDrop.RegisterValueChangedCallback(e => { curFrame = e.newValue; prev.sprite = Icon(curFile, curFrame); set(curFile, curFrame); });
            FillFrames();
            row.Add(frameDrop);
            box.Add(row);
            return box;
        }

        // ── 모델 미리보기 부품 — 팩 glb 템플릿을 잠시 세워 메시·재질·행렬만 거둔다(WorldPreviewDrawer와 같은 방식) ──

        public struct Part { public Mesh mesh; public Material[] mats; public Matrix4x4 local; }

        public static List<Part> ModelParts(string file, IReadOnlyList<string> materials, string owner)
        {
            var list = new List<Part>();
            if (string.IsNullOrEmpty(file)) return list;
            var tpl = PackAssets.ModelOf(file);
            if (tpl == null) return list;
            var root = new GameObject("__gd_preview") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var inst = UnityEngine.Object.Instantiate(tpl, root.transform);
                inst.SetActive(true);
                PackAssets.BindSlots(inst, materials ?? Array.Empty<string>(), owner);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    list.Add(new Part { mesh = mesh, mats = r.sharedMaterials, local = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix });
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return list;
        }

        /// <summary>팩 모델 참조 목록 편집 — 항목마다 파일(…)과 슬롯 재질 드롭다운. 배열의 [0]이 기본, 나머지는 변형.</summary>
        public static VisualElement ModelList(List<GameDataJson.ModelDto> models, Func<List<string>> materialIds, Action changed, string label = "모델")
        {
            var box = new VisualElement();
            void Render()
            {
                box.Clear();
                for (int i = 0; i < models.Count; i++)
                {
                    var m = models[i]; int idx = i;
                    var item = new VisualElement { style = { marginBottom = 4 } };
                    // 좁은 속성 칸에 들어가므로 머리줄(번호·삭제) 아래에 파일 줄을 따로 — 한 줄에 놓으면 파일 칸이 몇 글자로 눌린다
                    var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween } };
                    head.Add(new Label($"{label} [{idx}]") { style = { color = GdEnum.Faint, fontSize = 11 } });
                    var del = new Button(() => { models.RemoveAt(idx); changed(); Render(); }) { text = "항목 ✕" };
                    del.AddToClassList("gd-btn-mini");
                    head.Add(del);
                    item.Add(head);
                    var fr = FileRow(m.file, "models", "glb", v => { m.file = v; changed(); });
                    fr.style.marginLeft = 12; fr.style.marginTop = 2;
                    item.Add(fr);
                    // 슬롯 재질 — glb의 재질 슬롯 순서대로 팩 재질 id
                    var mats = m.materials?.ToList() ?? new List<string>();
                    var matsBox = new VisualElement { style = { marginLeft = 12 } };
                    for (int s = 0; s < mats.Count; s++)
                    {
                        int si = s;
                        var srow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 2 } };
                        srow.Add(new Label($"슬롯 {si}") { style = { width = 50, color = GdEnum.Faint, fontSize = 11 } });
                        var choices = materialIds();
                        if (!choices.Contains(mats[si])) choices.Insert(0, mats[si]);
                        var d = new DropdownField(choices, Mathf.Max(0, choices.IndexOf(mats[si]))) { style = { flexGrow = 1, flexShrink = 1, minWidth = 0 } };
                        d.RegisterValueChangedCallback(e => { mats[si] = e.newValue; m.materials = mats.ToArray(); changed(); });
                        srow.Add(d);
                        var sx = new Button(() => { mats.RemoveAt(si); m.materials = mats.ToArray(); changed(); Render(); }) { text = "✕" };
                        sx.AddToClassList("gd-btn-mini");
                        srow.Add(sx);
                        matsBox.Add(srow);
                    }
                    var addSlot = new Button(() => { mats.Add(materialIds().FirstOrDefault() ?? ""); m.materials = mats.ToArray(); changed(); Render(); }) { text = "+ 슬롯 재질" };
                    addSlot.AddToClassList("gd-btn-mini");
                    addSlot.style.alignSelf = Align.FlexStart;
                    matsBox.Add(addSlot);
                    item.Add(matsBox);
                    box.Add(item);
                }
                var add = new Button(() => { models.Add(new GameDataJson.ModelDto { file = "", materials = Array.Empty<string>() }); changed(); Render(); }) { text = $"+ {label} 추가" };
                add.AddToClassList("gd-btn-mini");
                add.style.alignSelf = Align.FlexStart;
                box.Add(add);
            }
            Render();
            return box;
        }
    }
}
#endif
