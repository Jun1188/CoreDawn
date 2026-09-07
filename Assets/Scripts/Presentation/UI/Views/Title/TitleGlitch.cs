using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreDawn.UI
{
    /// <summary>
    /// 글리치 전환 — 레퍼런스 <c>title.css</c> 의 <c>glitchOut</c>/<c>glitchIn</c> 키프레임(steps(1,end)) 이식.
    /// USS 에는 keyframes·clip-path·skew 가 없어 스케줄러로 단계마다 translate·scaleX·opacity 를 찍고,
    /// clip-path 의 "보이는 띠" 는 여집합을 배경색 띠(<c>.glitch-fx__mask</c>)로 덮어 흉내 낸다. 밝기는 마스크 아래 옅은 청록 판, 스캔라인은 줄무늬 텍스처.
    /// 요소 하나에 재생이 겹치면 새 것이 이긴다. Out 이 끝난 요소는 <see cref="IsOut"/>(레퍼런스 <c>.out</c> 클래스) — 다시 In 될 때까지 안 보이고 클릭도 안 받는다.
    /// </summary>
    public static class TitleGlitch
    {
        sealed class Key
        {
            public float At, Opacity = 1f, Tx, Sx = 1f, Bright = 1f;
            public float[] Bands;   // 보이는 가로 띠 [y0, y1, y0, y1, ...] (%), null = 전부
        }

        static readonly Key[] OutKeys =
        {
            new Key { At = 0f },
            new Key { At = .08f, Tx = -6, Bands = new[] { 0f, 18f, 42f, 60f } },
            new Key { At = .16f, Tx = 8, Bright = 2f, Bands = new[] { 10f, 30f, 55f, 72f } },
            new Key { At = .24f, Tx = -12, Bands = new[] { 0f, 14f, 68f, 80f } },
            new Key { At = .32f, Tx = 14, Opacity = .9f, Bright = 3f, Bands = new[] { 30f, 38f, 84f, 92f } },
            new Key { At = .42f, Tx = -20, Sx = 1.06f, Bright = 4f, Bands = new[] { 46f, 54f } },
            new Key { At = .55f, Tx = 30, Sx = 1.2f, Opacity = .8f, Bands = new[] { 48f, 52f } },
            new Key { At = .70f, Tx = -60, Sx = 1.5f, Opacity = .5f, Bands = new[] { 49.5f, 50.5f } },
            new Key { At = 1f, Tx = -120, Sx = 2f, Opacity = 0f, Bands = new[] { 50f, 50f } },
        };

        static readonly Key[] InKeys =
        {
            new Key { At = 0f, Opacity = 0f, Tx = 120, Sx = 2f, Bright = 4f, Bands = new[] { 50f, 50f } },
            new Key { At = .12f, Opacity = .6f, Tx = 40, Sx = 1.4f, Bands = new[] { 49f, 51f } },
            new Key { At = .25f, Tx = -14, Sx = 1.05f, Bright = 3f, Bands = new[] { 46f, 54f } },
            new Key { At = .36f, Tx = 10, Bands = new[] { 30f, 38f, 46f, 54f, 84f, 92f } },
            new Key { At = .48f, Tx = -8, Bright = 2f, Bands = new[] { 0f, 14f, 40f, 62f, 70f, 100f } },
            new Key { At = .60f, Tx = 5, Bands = new[] { 8f, 32f, 38f, 100f } },
            new Key { At = .75f, Tx = -3, Bright = 1.6f },
            new Key { At = .88f, Tx = 2, Bright = 1.2f, Bands = new[] { 0f, 60f, 66f, 100f } },
            new Key { At = 1f },
        };

        const long OutMs = 700, InMs = 800;

        static readonly Dictionary<VisualElement, IVisualElementScheduledItem> running = new();
        static readonly HashSet<VisualElement> outSet = new();
        static Texture2D scanTex;

        public static bool IsAnimating(VisualElement el) => el != null && running.ContainsKey(el);
        /// <summary>Out 이 시작됐거나 끝난 요소(다시 In 되기 전까지).</summary>
        public static bool IsOut(VisualElement el) => el != null && outSet.Contains(el);

        /// <summary>순차 소멸 — i 번째 요소는 i·step ms 뒤에 시작.</summary>
        public static void Out(IList<VisualElement> els, int stepMs = 120)
        {
            for (int i = 0; i < els.Count; i++) Play(els[i], OutKeys, OutMs, i * stepMs, true, null);
        }

        /// <summary>순차 등장. 끝나면 <paramref name="onShown"/>(el).</summary>
        public static void In(IList<VisualElement> els, int stepMs = 140, int delayMs = 0, Action<VisualElement> onShown = null)
        {
            for (int i = 0; i < els.Count; i++) Play(els[i], InKeys, InMs, delayMs + i * stepMs, false, onShown);
        }

        public static void Out(VisualElement el) => Out(new[] { el });

        /// <summary>연출 없이 소멸 끝 상태로 둔다 — 씬이 열릴 때 메뉴가 처음부터 보이지 않고 <see cref="In"/> 으로 생겨나게(2026-09-07 사용자).</summary>
        public static void Hide(IList<VisualElement> els)
        {
            foreach (var el in els)
            {
                if (el == null) continue;
                if (running.TryGetValue(el, out var prev)) { prev.Pause(); running.Remove(el); }
                outSet.Add(el);
                var end = OutKeys[OutKeys.Length - 1];
                el.style.opacity = end.Opacity;
                el.style.translate = new Translate(new Length(end.Tx, LengthUnit.Pixel), new Length(0f, LengthUnit.Pixel));
                el.style.scale = new Scale(new Vector2(end.Sx, 1f));
                el.pickingMode = PickingMode.Ignore;
            }
        }
        public static void In(VisualElement el, int delayMs = 0, Action<VisualElement> onShown = null) => In(new[] { el }, 0, delayMs, onShown);

        static void Play(VisualElement el, Key[] keys, long durationMs, int delayMs, bool isOut, Action<VisualElement> done)
        {
            if (el == null) return;
            if (running.TryGetValue(el, out var prev)) prev.Pause();
            if (isOut) outSet.Add(el); else outSet.Remove(el);

            var fx = GetFx(el);
            fx.style.display = DisplayStyle.Flex;
            el.pickingMode = PickingMode.Ignore;   // 전환 중·소멸 뒤에는 클릭을 받지 않는다(레퍼런스 pointer-events:none)
            Apply(el, fx, keys[0]);

            long start = -1;
            bool finished = false;
            IVisualElementScheduledItem item = null;
            item = el.schedule.Execute(ts =>
            {
                if (start < 0) start = ts.now;
                long elapsed = ts.now - start - delayMs;
                if (elapsed < 0) return;
                float t = Mathf.Clamp01((float)elapsed / durationMs);
                var k = keys[0];
                foreach (var kk in keys) if (kk.At <= t) k = kk;
                Apply(el, fx, k);
                if (elapsed < durationMs) return;

                finished = true;
                if (running.TryGetValue(el, out var cur) && cur == item) running.Remove(el);
                fx.style.display = DisplayStyle.None;
                if (isOut) return;   // 끝 상태(투명·밀림) 유지 — In 이 되돌린다
                el.style.opacity = StyleKeyword.Null;
                el.style.translate = StyleKeyword.Null;
                el.style.scale = StyleKeyword.Null;
                el.pickingMode = PickingMode.Position;
                done?.Invoke(el);
            }).Every(16);
            item.Until(() => finished);
            running[el] = item;
        }

        static void Apply(VisualElement el, VisualElement fx, Key k)
        {
            el.style.opacity = k.Opacity;
            el.style.translate = new Translate(new Length(k.Tx, LengthUnit.Pixel), new Length(0f, LengthUnit.Pixel));
            el.style.scale = new Scale(new Vector2(k.Sx, 1f));

            var masks = fx.Q("glitch-masks");
            int mi = 0;
            if (k.Bands != null)
            {
                float prevEnd = 0f;
                for (int i = 0; i + 1 < k.Bands.Length; i += 2)
                {
                    SetMask(masks, mi++, prevEnd, k.Bands[i]);
                    prevEnd = k.Bands[i + 1];
                }
                SetMask(masks, mi++, prevEnd, 100f);
            }
            for (; mi < masks.childCount; mi++) masks[mi].style.display = DisplayStyle.None;

            // 밝기: 마스크 아래에 깔린 옅은 청록 판 — 보이는 띠 안에서만 비친다(CSS brightness 는 요소 픽셀만 밝힌다)
            fx.Q("glitch-bright").style.opacity = Mathf.Clamp01((k.Bright - 1f) * 0.07f);
        }

        static void SetMask(VisualElement masks, int idx, float y0, float y1)
        {
            while (masks.childCount <= idx)
            {
                var m = new VisualElement { pickingMode = PickingMode.Ignore };
                m.AddToClassList("glitch-fx__mask");
                masks.Add(m);
            }
            var e = masks[idx];
            if (y1 - y0 < 0.01f) { e.style.display = DisplayStyle.None; return; }
            e.style.display = DisplayStyle.Flex;
            e.style.top = Length.Percent(y0);
            e.style.height = Length.Percent(y1 - y0);
        }

        // 요소마다 오버레이 한 벌(마스크 띠 · 밝기 · 스캔라인) — 맨 위 자식으로 둔다
        static VisualElement GetFx(VisualElement el)
        {
            var fx = el.Q("glitch-fx");
            if (fx != null) { fx.BringToFront(); return fx; }
            fx = new VisualElement { name = "glitch-fx", pickingMode = PickingMode.Ignore };
            fx.AddToClassList("glitch-fx");
            var masks = new VisualElement { name = "glitch-masks", pickingMode = PickingMode.Ignore };
            masks.AddToClassList("glitch-fx__layer");
            var bright = new VisualElement { name = "glitch-bright", pickingMode = PickingMode.Ignore };
            bright.AddToClassList("glitch-fx__layer");
            bright.AddToClassList("glitch-fx__bright");
            var scan = new VisualElement { name = "glitch-scan", pickingMode = PickingMode.Ignore };
            scan.AddToClassList("glitch-fx__layer");
            SetStripes(scan, ScanTexture(), 5);
            fx.Add(bright); fx.Add(scan); fx.Add(masks);   // 마스크가 맨 위 — 밝기·스캔라인은 보이는 띠에만
            el.Add(fx);
            return fx;
        }

        // 알파는 레퍼런스(.25)보다 낮게 — 선형 색공간에서 어두운 바탕 위 청록이 훨씬 밝게 섞인다
        static Texture2D ScanTexture() => scanTex != null ? scanTex : scanTex = MakeStripes(5, 2, new Color(79f / 255f, 232f / 255f, 240f / 255f, 0.06f));

        /// <summary>1×period 줄무늬 텍스처 — 위 <paramref name="on"/>줄만 <paramref name="color"/>, 나머지 투명. 반복 배경으로 쓴다.</summary>
        public static Texture2D MakeStripes(int period, int on, Color color)
        {
            var t = new Texture2D(1, period, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < period; y++) t.SetPixel(0, y, y >= period - on ? color : Color.clear);
            t.Apply();
            return t;
        }

        /// <summary>요소 배경을 줄무늬 텍스처 반복으로.</summary>
        public static void SetStripes(VisualElement el, Texture2D tex, int period)
        {
            el.style.backgroundImage = new StyleBackground(tex);
            el.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
            el.style.backgroundSize = new BackgroundSize(new Length(1f, LengthUnit.Pixel), new Length(period, LengthUnit.Pixel));
        }
    }
}
