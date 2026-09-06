using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Settings;
using CoreDawn.Sound;

namespace CoreDawn.UI
{
    /// <summary>
    /// 타이틀 설정 패널의 조작부 — 레퍼런스 <c>menu.js</c> 의 네온 바(0~10)·캐러셀(◀ 값 ▶)·세그먼트.
    /// 값의 주인은 게임 안 설정창(SettingsPanelView)과 같다: 소리는 AudioSaveSystem·SoundManager, 그래픽은 DisplaySettings.
    /// 캐러셀은 레퍼런스와 달리 양끝에서 멈춘다 — 게임 안 스테퍼와 같은 결정(순환하면 조작 실수로 보인다).
    /// </summary>
    public sealed class TitleSettingsPanel
    {
        readonly VisualElement root;
        readonly List<Action> refreshers = new();
        readonly List<Vector2Int> resolutions;

        public TitleSettingsPanel(VisualElement root)
        {
            this.root = root;

            Bar("bar-master", () => AudioSaveSystem.LoadSettings().masterVolume, v =>
            {
                SoundManager.Instance?.SetMasterVolume(v);
                var s = AudioSaveSystem.LoadSettings(); s.masterVolume = v; AudioSaveSystem.SaveSettings(s);
            });
            Bar("bar-bgm", () => AudioSaveSystem.LoadSettings().bgmVolume, v =>
            {
                SoundManager.Instance?.SetBGMVolume(v);
                var s = AudioSaveSystem.LoadSettings(); s.bgmVolume = v; AudioSaveSystem.SaveSettings(s);
            });
            Bar("bar-sfx", () => AudioSaveSystem.LoadSettings().sfxVolume, v =>
            {
                SoundManager.Instance?.SetSFXVolume(v);
                var s = AudioSaveSystem.LoadSettings(); s.sfxVolume = v; AudioSaveSystem.SaveSettings(s);
            });

            resolutions = Resolutions();
            var resNames = resolutions.ConvertAll(r => $"{r.x}×{r.y}").ToArray();
            Carousel("car-res", resNames, CurrentResolutionIndex, i => DisplaySettings.Resolution = resolutions[i]);
            Segment("seg-vsync", new[] { "OFF", "ON" }, () => DisplaySettings.VSync ? 1 : 0, i => DisplaySettings.VSync = i == 1);
            Segment("seg-mode", new[] { "창 모드", "전체화면" }, () => DisplaySettings.Fullscreen ? 1 : 0, i => DisplaySettings.Fullscreen = i == 1);
            Carousel("car-quality", QualitySettings.names, () => DisplaySettings.QualityLevel, i => DisplaySettings.QualityLevel = i);
        }

        /// <summary>지금 값으로 다시 그린다 — 패널을 열 때.</summary>
        public void Refresh() { foreach (var r in refreshers) r(); }

        // ── 네온 바: 칸 10개, 누르거나 끌어서 고른다 ──
        void Bar(string name, Func<float> get, Action<float> set)
        {
            var bar = root.Q(name);
            if (bar == null) { Debug.LogError($"[TitleSettingsPanel] '{name}' 이 UXML 에 없습니다."); return; }
            bar.Clear();
            var cells = new VisualElement[10];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                cells[i].AddToClassList("holo-bar__cell");
                bar.Add(cells[i]);
            }
            int value = 0;
            void Paint() { for (int i = 0; i < cells.Length; i++) cells[i].EnableInClassList("holo-bar__cell--on", i < value); }
            void Pick(float localX)
            {
                float w = bar.resolvedStyle.width;
                if (w <= 0f) return;
                int v = Mathf.RoundToInt(Mathf.Clamp01(localX / w) * 10f);
                if (v == value) return;
                value = v; Paint(); set(value / 10f);
            }
            bar.RegisterCallback<PointerDownEvent>(e => { bar.CapturePointer(e.pointerId); Pick(e.localPosition.x); e.StopPropagation(); });
            bar.RegisterCallback<PointerMoveEvent>(e => { if (bar.HasPointerCapture(e.pointerId)) Pick(e.localPosition.x); });
            bar.RegisterCallback<PointerUpEvent>(e => { if (bar.HasPointerCapture(e.pointerId)) bar.ReleasePointer(e.pointerId); });
            refreshers.Add(() => { value = Mathf.RoundToInt(Mathf.Clamp01(get()) * 10f); Paint(); });
        }

        // ── 캐러셀: ◀ 값 ▶ — 선택지가 많아도 폭이 고정 ──
        void Carousel(string name, string[] options, Func<int> get, Action<int> set)
        {
            var car = root.Q(name);
            if (car == null) { Debug.LogError($"[TitleSettingsPanel] '{name}' 이 UXML 에 없습니다."); return; }
            car.Clear();
            if (options == null || options.Length == 0) return;
            var prev = new Button { text = "◀" };
            var value = new Label(options[0]);
            var next = new Button { text = "▶" };
            prev.AddToClassList("holo-ctl"); prev.AddToClassList("holo-car__arrow");
            next.AddToClassList("holo-ctl"); next.AddToClassList("holo-car__arrow");
            value.AddToClassList("holo-car__value");
            car.Add(prev); car.Add(value); car.Add(next);

            int index = 0;
            void Sync()
            {
                value.text = options[index];
                prev.SetEnabled(index > 0);
                next.SetEnabled(index < options.Length - 1);
            }
            void Step(int d)
            {
                int moved = Mathf.Clamp(index + d, 0, options.Length - 1);
                if (moved == index) return;
                index = moved; set(index); Sync();
            }
            prev.clicked += () => Step(-1);
            next.clicked += () => Step(+1);
            refreshers.Add(() => { index = Mathf.Clamp(get(), 0, options.Length - 1); Sync(); });
        }

        // ── 세그먼트: 선택지 몇 개를 한 줄로, 고른 칸만 켜진다 ──
        void Segment(string name, string[] options, Func<int> get, Action<int> set)
        {
            var seg = root.Q(name);
            if (seg == null) { Debug.LogError($"[TitleSettingsPanel] '{name}' 이 UXML 에 없습니다."); return; }
            seg.Clear();
            var btns = new Button[options.Length];
            int selected = 0;
            void Paint() { for (int i = 0; i < btns.Length; i++) btns[i].EnableInClassList("holo-seg__opt--on", i == selected); }
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                btns[i] = new Button(() => { if (selected == idx) return; selected = idx; set(idx); Paint(); }) { text = options[i] };
                btns[i].AddToClassList("holo-ctl"); btns[i].AddToClassList("holo-seg__opt");
                seg.Add(btns[i]);
            }
            refreshers.Add(() => { selected = Mathf.Clamp(get(), 0, options.Length - 1); Paint(); });
        }

        // 지원 해상도(가로×세로 중복 제거, 오름차순) — 지금 화면 크기는 항상 포함
        static List<Vector2Int> Resolutions()
        {
            var set = new HashSet<Vector2Int>();
            foreach (var r in Screen.resolutions) set.Add(new Vector2Int(r.width, r.height));
            set.Add(new Vector2Int(Screen.width, Screen.height));
            var saved = DisplaySettings.Resolution;
            if (saved.x > 0 && saved.y > 0) set.Add(saved);
            var list = new List<Vector2Int>(set);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        int CurrentResolutionIndex()
        {
            var cur = DisplaySettings.Resolution;
            if (cur.x <= 0 || cur.y <= 0) cur = new Vector2Int(Screen.width, Screen.height);
            int idx = resolutions.IndexOf(cur);
            if (idx >= 0) return idx;
            int best = 0; long bestErr = long.MaxValue;
            for (int i = 0; i < resolutions.Count; i++)
            {
                long err = Math.Abs((long)resolutions[i].x - cur.x) + Math.Abs((long)resolutions[i].y - cur.y);
                if (err < bestErr) { bestErr = err; best = i; }
            }
            return best;
        }
    }
}
