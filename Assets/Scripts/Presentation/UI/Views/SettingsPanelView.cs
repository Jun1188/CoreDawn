using System;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Managers;
using CoreDawn.Settings;
using CoreDawn.Sound;

namespace CoreDawn.UI
{
    /// <summary>
    /// 설정 화면 (UITK) — 일시정지 메뉴의 "설정"에서 열린다.
    ///
    /// 탭이 늘어나도 전환 코드는 안 늘어나게 짰다: <see cref="pages"/>에 (탭 버튼, 페이지) 짝을
    /// 담고 <see cref="ShowPage"/>가 그 배열만 훑는다. 새 분류는 UXML에 버튼·페이지를 더하고
    /// <see cref="Bind"/>에서 짝을 하나 등록하면 끝이다.
    ///
    /// 값의 주인은 이 화면이 아니다 — 소리는 <see cref="AudioSaveSystem"/>·
    /// <see cref="SoundManager"/>, 그래픽은 <see cref="DisplaySettings"/>가 갖는다.
    /// 여기서는 그것들을 읽어 그리고, 조작이 들어오면 그쪽에 넘긴다. 그래야 게임 시작 때의
    /// 적용(창을 한 번도 열지 않은 경우)과 창에서의 조작이 같은 코드를 지난다.
    ///
    /// 세이브 슬롯 창(SaveLoadPanelView)과 마찬가지로 일시정지 메뉴를 닫지 않고 그 위에 뜬다 —
    /// UIPopup의 depth 우선순위가 ESC를 맨 위 창에만 주므로 ESC 한 번에 여기만 닫힌다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SettingsPanelView : UITKPopup
    {
        static SettingsPanelView cached;

        /// <summary>
        /// 탭 버튼과 그 탭이 보여줄 페이지의 짝. 순서가 곧 탭 순서다.
        /// handler를 함께 들고 있는 이유: 람다로 구독하면 Unbind에서 뗄 수가 없어
        /// 창을 열 때마다 구독이 한 겹씩 쌓인다(이 창은 여닫기를 반복하는 창이다).
        /// </summary>
        (Button tab, VisualElement page, Action handler)[] pages =
            Array.Empty<(Button, VisualElement, Action)>();

        Button btnClose, btnBack, btnReset;

        // 사운드
        Slider sliderMaster, sliderBgm, sliderSfx;
        Label valueMaster, valueBgm, valueSfx;

        // 그래픽 — 조작부는 런타임에 만든다(품질 레벨 수가 프로젝트마다 다르다)
        VisualElement stepQuality, segScreen, segVsync;

        // ───────────────────────── 열기 ─────────────────────────

        /// <summary>씬에 이 패널이 있으면 열고 true. 없으면 false — 호출부가 판단한다.</summary>
        public static bool TryOpen()
        {
            if (cached == null)
                cached = FindFirstObjectByType<SettingsPanelView>(FindObjectsInactive.Include);
            if (cached == null) return false;

            // 이미 열려 있으면 SetActive(true)가 no-op이라 OnEnable이 불리지 않는다 —
            // 그동안 밖에서 바뀌었을 수 있으니 다시 그린다 (다른 UITK 패널들과 같은 처리)
            if (cached.isActiveAndEnabled) { cached.RefreshAll(); return true; }

            cached.gameObject.SetActive(true);
            return true;
        }

        // ───────────────────── UITKPopup 계약 ─────────────────────

        protected override void Bind()
        {
            var r = Root;

            btnClose = r.Q<Button>("btn-close");
            btnBack = r.Q<Button>("btn-back");
            btnReset = r.Q<Button>("btn-reset");

            btnClose.clicked += Close;
            btnBack.clicked += Close;
            btnReset.clicked += ResetCurrentPage;

            pages = BuildPages(
                (r.Q<Button>("tab-sound"), r.Q("page-sound")),
                (r.Q<Button>("tab-graphics"), r.Q("page-graphics")));

            sliderMaster = r.Q<Slider>("slider-master");
            sliderBgm = r.Q<Slider>("slider-bgm");
            sliderSfx = r.Q<Slider>("slider-sfx");
            valueMaster = r.Q<Label>("value-master");
            valueBgm = r.Q<Label>("value-bgm");
            valueSfx = r.Q<Label>("value-sfx");

            sliderMaster?.RegisterValueChangedCallback(OnMasterChanged);
            sliderBgm?.RegisterValueChangedCallback(OnBgmChanged);
            sliderSfx?.RegisterValueChangedCallback(OnSfxChanged);

            stepQuality = r.Q("step-quality");
            segScreen = r.Q("seg-screen");
            segVsync = r.Q("seg-vsync");

            RefreshAll();
            ShowPage(pages.Length > 0 ? pages[0].page : null);
        }

        protected override void Unbind()
        {
            if (btnClose != null) btnClose.clicked -= Close;
            if (btnBack != null) btnBack.clicked -= Close;
            if (btnReset != null) btnReset.clicked -= ResetCurrentPage;

            sliderMaster?.UnregisterValueChangedCallback(OnMasterChanged);
            sliderBgm?.UnregisterValueChangedCallback(OnBgmChanged);
            sliderSfx?.UnregisterValueChangedCallback(OnSfxChanged);

            foreach (var (tab, _, handler) in pages)
                if (tab != null && handler != null) tab.clicked -= handler;
            pages = Array.Empty<(Button, VisualElement, Action)>();
        }

        // ───────────────────────── 탭 ─────────────────────────

        /// <summary>탭 짝마다 구독을 걸고, 뗄 수 있게 그 델리게이트를 함께 돌려준다.</summary>
        (Button, VisualElement, Action)[] BuildPages(params (Button tab, VisualElement page)[] pairs)
        {
            var built = new (Button, VisualElement, Action)[pairs.Length];

            for (int i = 0; i < pairs.Length; i++)
            {
                var (tab, page) = pairs[i];
                if (tab == null || page == null) { built[i] = (tab, page, null); continue; }

                Action handler = () => ShowPage(page);   // page는 반복마다 새 지역 변수다
                tab.clicked += handler;
                built[i] = (tab, page, handler);
            }

            return built;
        }

        void ShowPage(VisualElement target)
        {
            foreach (var (tab, page, _) in pages)
            {
                bool on = page == target;
                if (page != null) page.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                ToggleClass(tab, "ui-tab--active", on);
            }
        }

        VisualElement CurrentPage
        {
            get
            {
                foreach (var (_, page, _) in pages)
                    if (page != null && page.resolvedStyle.display == DisplayStyle.Flex) return page;
                return null;
            }
        }

        // ───────────────────────── 그리기 ─────────────────────────

        void RefreshAll()
        {
            RefreshSound();
            RefreshGraphics();
        }

        void RefreshSound()
        {
            var s = AudioSaveSystem.LoadSettings();

            // SetValueWithoutNotify — 여기서 값을 넣는 것은 "지금 상태를 보여주는" 일이지
            // 조작이 아니다. 알림까지 돌면 열 때마다 저장이 한 번씩 일어난다.
            sliderMaster?.SetValueWithoutNotify(s.masterVolume);
            sliderBgm?.SetValueWithoutNotify(s.bgmVolume);
            sliderSfx?.SetValueWithoutNotify(s.sfxVolume);

            SetPercent(valueMaster, s.masterVolume);
            SetPercent(valueBgm, s.bgmVolume);
            SetPercent(valueSfx, s.sfxVolume);
        }

        void RefreshGraphics()
        {
            BuildStepper(stepQuality, QualitySettings.names, DisplaySettings.QualityLevel,
                         i => DisplaySettings.QualityLevel = i);

            BuildSegment(segScreen, new[] { "전체화면", "창 모드" }, DisplaySettings.Fullscreen ? 0 : 1,
                         i => DisplaySettings.Fullscreen = i == 0);

            BuildSegment(segVsync, new[] { "켬", "끔" }, DisplaySettings.VSync ? 0 : 1,
                         i => DisplaySettings.VSync = i == 0);
        }

        /// <summary>
        /// 선택지 몇 개짜리 항목을 버튼 줄로 그린다. 고른 칸만 --on이 붙는다.
        /// 누르면 <paramref name="apply"/>가 값을 넘기고, 그 줄만 다시 칠한다 —
        /// 그래픽 항목끼리는 서로 영향을 주지 않으므로 전체를 다시 그릴 이유가 없다.
        /// </summary>
        void BuildSegment(VisualElement host, string[] options, int selected, Action<int> apply)
        {
            if (host == null || options == null) return;

            host.Clear();

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;                            // 클로저가 반복 변수를 붙잡지 않게 복사
                var btn = new Button(() =>
                {
                    apply(index);
                    BuildSegment(host, options, index, apply);
                })
                { text = options[i] };

                btn.AddToClassList("ui-btn");
                btn.AddToClassList("set-seg__opt");
                if (i == selected) btn.AddToClassList("set-seg__opt--on");

                host.Add(btn);
            }
        }

        /// <summary>
        /// 선택지가 많은 항목을 스테퍼(◀ 값 ▶) 한 줄로 그린다 — 폭이 선택지 수와 무관하다.
        /// <see cref="BuildSegment"/>와 계약이 같아서 둘을 맞바꿔도 호출부는 그대로다.
        ///
        /// 순환하지 않고 양끝에서 멈춘다: 화살표가 꺼지는 것이 "여기가 끝"이라는 유일한 신호다.
        /// 순환시키면 Ultra에서 한 번 더 눌렀을 때 Very Low로 떨어지는데, 목록을 외우지 않은
        /// 사람에게는 그게 조작 실수로만 보인다.
        /// </summary>
        void BuildStepper(VisualElement host, string[] options, int selected, Action<int> apply)
        {
            if (host == null || options == null || options.Length == 0) return;

            host.Clear();
            int index = Mathf.Clamp(selected, 0, options.Length - 1);

            var prev = new Button { text = "◀" };
            var value = new Label(options[index]);
            var next = new Button { text = "▶" };

            prev.clicked += () => Step(-1);
            next.clicked += () => Step(+1);

            prev.AddToClassList("ui-btn");
            prev.AddToClassList("set-step__arrow");
            next.AddToClassList("ui-btn");
            next.AddToClassList("set-step__arrow");
            value.AddToClassList("set-step__value");

            host.Add(prev);
            host.Add(value);
            host.Add(next);
            Sync();

            void Step(int delta)
            {
                int moved = Mathf.Clamp(index + delta, 0, options.Length - 1);
                if (moved == index) return;      // 끝에서 누른 것 — 적용도 저장도 하지 않는다
                index = moved;
                apply(index);
                value.text = options[index];
                Sync();
            }

            // 양끝에서 화살표를 끈다 — :disabled 스타일이 "더 갈 곳이 없다"를 보여준다
            void Sync()
            {
                prev.SetEnabled(index > 0);
                next.SetEnabled(index < options.Length - 1);
            }
        }

        static void SetPercent(Label label, float normalized)
        {
            if (label != null) label.text = $"{Mathf.RoundToInt(Mathf.Clamp01(normalized) * 100f)}%";
        }

        // ───────────────────────── 사운드 조작 ─────────────────────────
        //
        // 세 벌이 거의 같은 모양인데 하나로 합치지 않은 이유: 합치려면 "어느 볼륨인가"를
        // 값으로 넘겨야 하고, 그러면 AudioSettingsData의 필드를 문자열이나 enum으로 한 번 더
        // 되짚는 층이 생긴다. 지금은 필드 이름을 직접 쓰는 세 줄이 더 짧고 안전하다.

        void OnMasterChanged(ChangeEvent<float> e)
        {
            SoundManager.Instance?.SetMasterVolume(e.newValue);
            SetPercent(valueMaster, e.newValue);

            var s = AudioSaveSystem.LoadSettings();
            s.masterVolume = e.newValue;
            AudioSaveSystem.SaveSettings(s);
        }

        void OnBgmChanged(ChangeEvent<float> e)
        {
            SoundManager.Instance?.SetBGMVolume(e.newValue);
            SetPercent(valueBgm, e.newValue);

            var s = AudioSaveSystem.LoadSettings();
            s.bgmVolume = e.newValue;
            AudioSaveSystem.SaveSettings(s);
        }

        void OnSfxChanged(ChangeEvent<float> e)
        {
            SoundManager.Instance?.SetSFXVolume(e.newValue);
            SetPercent(valueSfx, e.newValue);

            var s = AudioSaveSystem.LoadSettings();
            s.sfxVolume = e.newValue;
            AudioSaveSystem.SaveSettings(s);
        }

        // ───────────────────────── 기본값 ─────────────────────────

        /// <summary>보고 있는 탭만 되돌린다 — 소리를 고치러 왔다가 그래픽까지 날리면 곤란하다.</summary>
        void ResetCurrentPage()
        {
            var page = CurrentPage;

            if (page != null && page.name == "page-graphics")
            {
                DisplaySettings.ResetToDefaults();
                RefreshGraphics();
                return;
            }

            var s = new AudioSettingsData();          // 필드 기본값이 곧 기본 볼륨이다
            AudioSaveSystem.SaveSettings(s);

            var sound = SoundManager.Instance;
            if (sound != null)
            {
                sound.SetMasterVolume(s.masterVolume);
                sound.SetBGMVolume(s.bgmVolume);
                sound.SetSFXVolume(s.sfxVolume);
            }

            RefreshSound();
        }
    }
}
