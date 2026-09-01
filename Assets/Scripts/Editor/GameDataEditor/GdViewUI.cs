#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Data;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 정의의 표현 블록(view{type, sfx}) 편집 조각 — 총·건물·몬스터 패널이 같은 것을 붙인다.
    /// 뷰 종류는 <see cref="ViewSchema.Types"/>에서, 종류가 허용하는 소리 자리마다 소리(팩 sounds) + 볼륨 + 공간감 한 줄.
    /// 값은 <see cref="GameDataJson.ViewDto"/>를 제자리에서 고친다(호출 탭의 src DTO와 같은 객체).
    /// </summary>
    static class GdViewUI
    {
        const string None = "(없음)";

        public static VisualElement Build(GameDataJson.ViewDto view, Action pushHist, Func<List<string>> soundIds)
        {
            var box = new VisualElement { style = { marginTop = 6 } };
            Render(box, view, pushHist, soundIds);
            return box;
        }

        static void Render(VisualElement box, GameDataJson.ViewDto view, Action pushHist, Func<List<string>> soundIds)
        {
            box.Clear();
            var title = new Label("뷰 — 종류와 소리 자리") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } };
            box.Add(title);

            var types = ViewSchema.Types.Keys.ToList();
            var typeChoices = new List<string> { None }; typeChoices.AddRange(types);
            int typeIdx = string.IsNullOrEmpty(view.type) ? 0 : Mathf.Max(0, types.IndexOf(view.type) + 1);
            var typeD = new DropdownField(typeChoices, typeIdx) { tooltip = "뷰 종류(ViewSchema 표) — 조립기가 이 키로 컴포넌트·콜라이더를 정한다. 종류마다 쓸 수 있는 소리 자리가 다르다" };
            typeD.RegisterValueChangedCallback(e =>
            {
                view.type = e.newValue == None ? null : e.newValue;
                pushHist();
                Render(box, view, pushHist, soundIds);
            });
            box.Add(Field("type — 뷰 종류", typeD));

            if (string.IsNullOrEmpty(view.type) || !ViewSchema.Types.TryGetValue(view.type, out var allowed)) return;
            if (allowed.Length == 0)
            {
                box.Add(new Label("이 종류는 소리 자리가 없다") { style = { fontSize = 11, color = new Color(0.55f, 0.6f, 0.7f) } });
                return;
            }

            var ids = soundIds() ?? new List<string>();
            foreach (var name in allowed)
            {
                var use = view.sfx != null && view.sfx.TryGetValue(name, out var u) ? u : null;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 3 } };
                var lbl = new Label(name) { style = { width = 70, fontSize = 11 } };
                row.Add(lbl);

                var choices = new List<string> { None }; choices.AddRange(ids);
                int idx = 0;
                if (use != null && !string.IsNullOrEmpty(use.sound))
                {
                    int at = ids.IndexOf(use.sound);
                    if (at >= 0) idx = at + 1; else { choices.Add(use.sound + " — 없음"); idx = choices.Count - 1; }
                }
                var soundD = new DropdownField(choices, idx) { style = { flexGrow = 1 }, tooltip = "팩 sounds의 소리(변형 클립 묶음). 사운드 탭에서 만든다" };
                soundD.RegisterValueChangedCallback(e =>
                {
                    int i = choices.IndexOf(e.newValue);
                    if (i <= 0 || i - 1 >= ids.Count) { view.sfx?.Remove(name); }
                    else
                    {
                        view.sfx ??= new Dictionary<string, GameDataJson.SfxUseDto>();
                        if (!view.sfx.TryGetValue(name, out var cur)) view.sfx[name] = cur = new GameDataJson.SfxUseDto();
                        cur.sound = ids[i - 1];
                    }
                    pushHist();
                    Render(box, view, pushHist, soundIds);
                });
                row.Add(soundD);

                var volF = new FloatField { value = use?.volume ?? 1f, style = { width = 52, marginLeft = 4 }, tooltip = "볼륨(0~1) — 쓰는 자리의 값" };
                volF.SetEnabled(use != null);
                volF.RegisterValueChangedCallback(e => { if (use != null) use.volume = Mathf.Clamp01(e.newValue); });
                volF.RegisterCallback<FocusOutEvent>(_ => pushHist());
                row.Add(volF);

                var spT = new Toggle("3D") { value = use?.spatial ?? true, style = { marginLeft = 4, fontSize = 10.5f }, tooltip = "켜면 위치에서 나는 3D 소리, 끄면 2D(UI·알림)" };
                spT.SetEnabled(use != null);
                spT.RegisterValueChangedCallback(e => { if (use != null) { use.spatial = e.newValue; pushHist(); } });
                row.Add(spT);
                box.Add(row);
            }
        }

        static VisualElement Field(string label, VisualElement input)
        {
            var f = new VisualElement();
            f.AddToClassList("gd-field");
            var l = new Label(label); l.AddToClassList("gd-field-label"); f.Add(l);
            input.AddToClassList("gd-field-input"); f.Add(input);
            return f;
        }
    }
}
#endif
