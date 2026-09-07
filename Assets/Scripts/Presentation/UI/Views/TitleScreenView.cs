using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Managers;
using CoreDawn.Save;
using CoreDawn.Sound;
using CoreDawn.Title;

namespace CoreDawn.UI
{
    /// <summary>
    /// 타이틀 화면 — 레퍼런스 <c>Ref/TitleScreen/title.html</c>(2026-09-07) 이식.
    ///
    /// 왼쪽 컬럼에 홀로그램 메뉴, 뒤에는 <see cref="TitleBeltScene"/>(벨트·아이템·카메라), 그 사이를 <see cref="TitleWires"/>가 잇는다.
    /// 흐름: 메인(게임 시작·설정·게임 종료) → [게임 시작] 발사 시퀀스 → 도킹 서브 메뉴(새 게임·불러오기·뒤로가기).
    /// 새 게임은 이륙 + 페이드 뒤 <see cref="SaveManager.NewGame"/>, 불러오기는 서브 메뉴 자리에 슬롯 목록 패널, 설정은 메인 메뉴 자리에 패널.
    /// 전환은 전부 <see cref="TitleGlitch"/>. 상태 플래그(<c>uiSettings·uiBusy·uiLoad</c>)는 레퍼런스 body 클래스에 해당한다.
    ///
    /// 다른 화면들과 달리 <see cref="UITKPopup"/>을 상속하지 않는다 — 그쪽 계약은 씬에 InputManager 가 있다고 전제하는데
    /// 타이틀 씬에는 플레이어도 입력 파이프라인도 없다. 그래서 UIDocument 를 직접 다룬다(슬롯 목록도 같은 문서 안의 패널).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(100)]
    public class TitleScreenView : MonoBehaviour
    {
        [SerializeField] UIDocument document;
        [SerializeField] TitleBeltScene scene;
        [SerializeField] TitleBootstrap boot;
        [Tooltip("새 게임: 이륙 클립 시작 뒤 페이드가 시작되기까지(초)")]
        [SerializeField] float takeoffFadeDelay = 5f;
        [Tooltip("새 게임: 페이드 길이(초) — 끝나면 World 를 연다")]
        [SerializeField] float takeoffFadeSeconds = 2f;

        const int GlitchOutMs = 700, GoBackMs = 900;

        readonly SaveSlotList slots = new();
        readonly List<HoloBox> holos = new();

        VisualElement root, titleBlock, menuMain, menuSub, menuSettings, menuLoad, settingsPanel, loadPanel, fade, loading;
        Button[] mainBtns = System.Array.Empty<Button>(), subBtns = System.Array.Empty<Button>();
        Button btnSettingsBack, btnLoadBack, btnLoadConfirm, btnDelete;
        ScrollView list;
        Label emptyNote;
        TitleWires wires;
        TitleSettingsPanel settings;
        Texture2D scanTex;
        HoloBar loadBar;
        Label loadTitle, loadPct, loadMsg;

        bool uiSettings, uiBusy, uiLoad, returning, loadingHidden;

        void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (scene == null) scene = FindFirstObjectByType<TitleBeltScene>();
            if (boot == null) boot = FindFirstObjectByType<TitleBootstrap>();
        }

        void OnEnable()
        {
            root = document != null ? document.rootVisualElement : null;
            if (root == null)
            {
                Debug.LogError("[TitleScreenView] UIDocument.rootVisualElement 가 null 입니다 — Source Asset(TitleScreen.uxml)과 Panel Settings 를 확인하세요.", this);
                return;
            }

            // 타이틀에서는 마우스를 써야 한다 — 게임플레이 씬에서 잠가둔 채로 넘어올 수 있다(UICursor 머리말 참고)
            UICursor.ResetFree();
            Time.timeScale = 1f;
            if (SoundManager.Instance != null) SoundManager.Instance.PlayBGM(BGMType.Main);

            var screen = root.Q("title-root");
            titleBlock = root.Q("title-block");
            menuMain = root.Q("menu-main");
            menuSub = root.Q("menu-sub");
            menuSettings = root.Q("menu-settings");
            menuLoad = root.Q("menu-load");
            settingsPanel = root.Q("settings-panel");
            loadPanel = root.Q("load-panel");
            fade = root.Q("fade");
            loading = root.Q("loading");
            list = root.Q<ScrollView>("slot-list");
            emptyNote = root.Q<Label>("empty-note");

            mainBtns = new[] { root.Q<Button>("btn-start"), root.Q<Button>("btn-settings"), root.Q<Button>("btn-quit") };
            subBtns = new[] { root.Q<Button>("btn-new"), root.Q<Button>("btn-load"), root.Q<Button>("btn-back") };
            btnSettingsBack = root.Q<Button>("btn-settings-back");
            btnLoadBack = root.Q<Button>("btn-load-back");
            btnLoadConfirm = root.Q<Button>("btn-load-confirm");
            btnDelete = root.Q<Button>("btn-delete");

            // 배경 층: 와이어(맨 아래 자식). CRT 스캔라인은 별도 요소가 아니라 루트의 배경 이미지 — 요소 배경은 자식보다 먼저 그려지니 확실히 맨 뒤다.
            // 알파 3/255: 선형 색공간에서 어두운 바탕 위 청록은 sRGB 브라우저(레퍼런스 .045)보다 훨씬 밝게 섞인다(실측)
            wires = new TitleWires(TitleBeltScene.LinkCount);
            screen.Insert(0, wires);
            scanTex = TitleGlitch.MakeStripes(4, 1, new Color(79f / 255f, 232f / 255f, 240f / 255f, 3f / 255f));
            TitleGlitch.SetStripes(screen, scanTex, 4);
            fade.pickingMode = PickingMode.Ignore;

            // 로딩 상자 — Boot 씬(BootScreenView)과 같은 부품
            loadTitle = root.Q<Label>("load-title");
            loadPct = root.Q<Label>("load-pct");
            loadMsg = root.Q<Label>("load-msg");
            var barHost = root.Q("load-bar");
            if (barHost != null) { loadBar = new HoloBar(); barHost.Add(loadBar); }

            // 홀로그램 배경 — .holo 요소마다 HoloBox 를 첫 자식으로. 호버(밝아짐)·위험(종료) 색은 버튼(.holo-btn)에만 —
            // 패널(설정·불러오기)까지 걸면 패널 배경이 마우스에 반응한다(레퍼런스는 .btn:hover 뿐, 2026-09-07 사용자 버그 보고)
            holos.Clear();
            root.Query(className: "holo").ForEach(el =>
            {
                var box = new HoloBox { Danger = el.ClassListContains("holo-btn--quit") };
                el.Insert(0, box);
                holos.Add(box);
                if (!el.ClassListContains("holo-btn")) return;
                el.RegisterCallback<PointerEnterEvent>(_ => box.Hot = true);
                el.RegisterCallback<PointerLeaveEvent>(_ => box.Hot = false);
            });

            var version = root.Q<Label>("version");
            if (version != null) version.text = $"v{Application.version} // prototype";

            settings = new TitleSettingsPanel(settingsPanel);

            mainBtns[0].clicked += StartGame;
            mainBtns[1].clicked += OpenSettings;
            mainBtns[2].clicked += QuitGame;
            subBtns[0].clicked += NewGame;
            subBtns[1].clicked += OpenLoad;
            subBtns[2].clicked += GoBack;
            btnSettingsBack.clicked += CloseSettings;
            btnLoadBack.clicked += CloseLoad;
            btnLoadConfirm.clicked += LoadSelected;
            btnDelete.clicked += DeleteSelected;

            slots.SelectionChanged += UpdateLoadButtons;
            SaveManager.SlotsChanged += RebuildSlots;
            if (scene != null) { scene.ArrivedEvent += OnArrived; scene.DockedEvent += OnDocked; }

            uiSettings = uiBusy = uiLoad = returning = false;
            loadingHidden = false;
            menuSub.style.display = DisplayStyle.None;
            menuSettings.style.display = DisplayStyle.None;
            menuLoad.style.display = DisplayStyle.None;
            StartFlicker();
        }

        void OnDisable()
        {
            SaveManager.SlotsChanged -= RebuildSlots;
            slots.SelectionChanged -= UpdateLoadButtons;
            if (scene != null) { scene.ArrivedEvent -= OnArrived; scene.DockedEvent -= OnDocked; }

            if (mainBtns.Length == 3)
            {
                if (mainBtns[0] != null) mainBtns[0].clicked -= StartGame;
                if (mainBtns[1] != null) mainBtns[1].clicked -= OpenSettings;
                if (mainBtns[2] != null) mainBtns[2].clicked -= QuitGame;
            }
            if (subBtns.Length == 3)
            {
                if (subBtns[0] != null) subBtns[0].clicked -= NewGame;
                if (subBtns[1] != null) subBtns[1].clicked -= OpenLoad;
                if (subBtns[2] != null) subBtns[2].clicked -= GoBack;
            }
            if (btnSettingsBack != null) btnSettingsBack.clicked -= CloseSettings;
            if (btnLoadBack != null) btnLoadBack.clicked -= CloseLoad;
            if (btnLoadConfirm != null) btnLoadConfirm.clicked -= LoadSelected;
            if (btnDelete != null) btnDelete.clicked -= DeleteSelected;

            slots.Clear();
            if (scanTex != null) { Destroy(scanTex); scanTex = null; }
        }

        // ───────────────────────── 프레임 ─────────────────────────

        void Update()
        {
            if (root == null) return;
            UpdateLoading();
            if (boot != null && boot.IsGate) return;   // 게이트 모드(World 로 가는 중) — 메뉴는 쉰다
            if (scene == null || !scene.Ready || scene.LoadFailed) return;

            scene.DimOthers = uiSettings || uiBusy || uiLoad;
            titleBlock.EnableInClassList("title-block--hidden", uiSettings);
            DrawWires(Mathf.Min(Time.deltaTime, 0.05f), Time.time);
        }

        static bool Frozen(VisualElement el) => TitleGlitch.IsAnimating(el) || TitleGlitch.IsOut(el);

        // 선: 상태별로 어느 UI 앵커 → 어느 3D 지점을 잇는지 정한다(레퍼런스 drawWires)
        void DrawWires(float dt, float time)
        {
            var cam = scene.Cam;
            for (int i = 0; i < wires.List.Count; i++)
            {
                var w = wires.List[i];

                if (scene.Docked)   // 서브 메뉴 → 우주선(불러오기 패널이 열리면 1번 선은 패널로)
                {
                    var anchorsSorted = scene.ShipAnchorsSorted;
                    if (anchorsSorted == null || i >= anchorsSorted.Length) { wires.Hide(w); continue; }
                    VisualElement el = subBtns[i];
                    if (uiLoad)
                    {
                        bool usePanel = i == 1 && !TitleGlitch.IsAnimating(loadPanel);
                        if (usePanel) { wires.Rebind(w, loadPanel); el = loadPanel; }
                        w.Target = usePanel ? 1f : 0f;
                    }
                    else
                    {
                        if (i == 1 && w.AnchorEl == loadPanel) wires.Rebind(w, subBtns[1]);
                        w.Target = !TitleGlitch.IsOut(subBtns[i]) ? 1f : 0f;
                    }
                    wires.DrawWire(w, i, anchorsSorted[i], el, Frozen(el), cam, dt, time);
                    continue;
                }

                bool has = scene.TryGetLinkTarget(i, out var pos);
                VisualElement anchor = mainBtns[i];
                if (uiSettings || (uiBusy && !scene.Launching && TitleGlitch.IsOut(mainBtns[0])))
                {
                    bool usePanel = i == 1 && uiSettings && !TitleGlitch.IsAnimating(settingsPanel);   // 설정 아이템 → 패널
                    if (usePanel) { wires.Rebind(w, settingsPanel); anchor = settingsPanel; }
                    w.Target = usePanel ? 1f : 0f;
                }
                else
                {
                    if (i == 1 && w.AnchorEl == settingsPanel) wires.Rebind(w, mainBtns[1]);
                    w.Target = scene.Arrived ? 0f : scene.Launching ? (i == 0 ? 1f : 0f) : (has && !TitleGlitch.IsAnimating(mainBtns[i])) ? 1f : 0f;
                }
                if (!has && w.Draw < 0.01f) { wires.Hide(w); continue; }
                Vector3 endPt;
                if (has) { endPt = pos; w.LastEnd = pos; w.HasLastEnd = true; }
                else if (w.HasLastEnd) endPt = w.LastEnd;
                else { wires.Hide(w); continue; }
                wires.DrawWire(w, i, endPt, anchor, Frozen(anchor), cam, dt, time);
            }
        }

        // 로딩 상자 — 옛 Boot 씬 화면(레퍼런스 #load)을 타이틀이 직접 그린다.
        // 메뉴 모드: 팩 자원(TitleBootstrap) → 배경 모델(TitleBeltScene) 순으로 진행률, 둘 다 끝나면 걷는다.
        // 게이트 모드(새 게임·불러오기): 다시 띄우고 "WORLD GENERATING" — 자원은 이미 있어 바는 흐르는 조각, 씬이 열릴 때까지 남는다.
        void UpdateLoading()
        {
            if (loading == null) return;
            bool gate = boot != null && boot.IsGate;
            bool failed = boot != null && boot.Failed;
            if (gate && loadingHidden) ShowLoading();
            if (loadingHidden) return;

            string title = gate && PackAssets.IsReady ? "WORLD GENERATING" : "LOADING";
            float p; string msg; bool indeterminate = false;
            if (failed) { p = 0f; msg = boot.Status; }
            else if (!PackAssets.IsReady)
            {
                var (done, total) = PackAssets.Progress;
                p = total > 0 ? (float)done / total : 0f;
                msg = !string.IsNullOrEmpty(PackAssets.Current) ? PackAssets.Current : boot != null ? boot.Status : "";
            }
            else if (gate) { p = 1f; msg = boot.Status; indeterminate = true; }
            else if (scene != null && !scene.Ready)
            {
                var (done, total) = scene.LoadProgress;
                p = total > 0 ? (float)done / total : 0f;
                msg = scene.Loading ?? "";
            }
            else { HideLoading(); return; }

            if (loadTitle != null) loadTitle.text = title;
            if (loadBar != null)
            {
                loadBar.Indeterminate = indeterminate;
                if (indeterminate) loadBar.Tick(Time.unscaledTime); else loadBar.Progress = p;
            }
            if (loadPct != null)
            {
                loadPct.text = Mathf.RoundToInt(p * 100f) + "%";
                loadPct.style.visibility = indeterminate ? Visibility.Hidden : Visibility.Visible;
            }
            if (loadMsg != null) { loadMsg.text = msg.ToUpperInvariant(); loadMsg.EnableInClassList("load-msg--error", failed); }
        }

        void ShowLoading()
        {
            loadingHidden = false;
            loading.style.display = DisplayStyle.Flex;
            // 즉시 — USS 의 0.5s 페이드인은 게이트의 300ms 대기보다 길어 상자가 다 뜨기 전에 World 로드(동기)가 프레임을 세운다(실측)
            loading.style.transitionDuration = new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) };
            loading.RemoveFromClassList("title-loading--hide");
            loading.pickingMode = PickingMode.Position;
            if (fade != null)   // 이륙 페이드(검정, 2초 전환)가 로딩 상자 위에 남지 않게 즉시 걷는다 — 상자 배경이 같은 잉크색이라 끊김이 없다
            {
                fade.style.transitionDuration = new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) };
                fade.style.opacity = 0f;
            }
        }

        void HideLoading()
        {
            loadingHidden = true;
            if (loading == null) return;
            if (loadBar != null) loadBar.Progress = 1f;
            if (loadPct != null) loadPct.text = "100%";
            if (loadMsg != null) loadMsg.text = "READY";
            loading.style.transitionDuration = StyleKeyword.Null;   // 걷을 때는 USS 페이드(0.5s)
            loading.AddToClassList("title-loading--hide");
            loading.pickingMode = PickingMode.Ignore;
            loading.schedule.Execute(() => { if (loadingHidden) loading.style.display = DisplayStyle.None; }).StartingIn(600);
        }

        // 제목 깜빡임 — 7초 주기 끝에 잠깐(레퍼런스 flicker)
        void StartFlicker()
        {
            var title = root.Q("game-title");
            if (title == null) return;
            title.schedule.Execute(() =>
            {
                float p = Mathf.Repeat(Time.unscaledTime, 7f) / 7f;
                float o = p < .93f ? 1f : p < .94f ? .75f : p < .95f ? 1f : p < .97f ? .85f : 1f;
                title.style.opacity = o;
            }).Every(50);
        }

        // ───────────────────────── 게임 시작 → 도킹 ─────────────────────────

        void StartGame()
        {
            if (scene == null || !scene.Ready || scene.LoadFailed || scene.Launching || uiSettings || uiBusy) return;
            if (!scene.StartLaunch()) return;
            mainBtns[0].AddToClassList("holo-btn--launching");
            TitleGlitch.Out(new VisualElement[] { mainBtns[1], mainBtns[2] });
        }

        void OnArrived() => TitleGlitch.Out(mainBtns[0]);

        void OnDocked()
        {
            wires.Reset();
            menuSub.style.display = DisplayStyle.Flex;
            bool has = LatestMeta() != null;
            subBtns[1].EnableInClassList("holo-btn--disabled", !has);
            TitleGlitch.In(new VisualElement[] { subBtns[0], subBtns[1], subBtns[2] });
        }

        // 뒤로가기: 서브 메뉴 소멸 → 카메라를 아이템 흐름으로 복귀 → 메인 메뉴 등장
        void GoBack()
        {
            if (returning || uiLoad || uiBusy) return;
            returning = true;
            TitleGlitch.Out(new VisualElement[] { subBtns[0], subBtns[1], subBtns[2] });
            root.schedule.Execute(() =>
            {
                scene.ReturnFromDock();
                menuSub.style.display = DisplayStyle.None;
                wires.Reset();
                mainBtns[0].RemoveFromClassList("holo-btn--launching");
                TitleGlitch.In(new VisualElement[] { mainBtns[0], mainBtns[1], mainBtns[2] }, 140, 200);
                returning = false;
            }).StartingIn(GoBackMs);
        }

        // 새 게임: 이륙 → 페이드 → World(Boot 경유)
        void NewGame()
        {
            if (uiBusy || uiLoad || returning) return;
            uiBusy = true;
            scene.Takeoff();
            TitleGlitch.Out(new VisualElement[] { subBtns[0], subBtns[1], subBtns[2] });
            root.schedule.Execute(() =>
            {
                fade.style.transitionDuration = new List<TimeValue> { new TimeValue(takeoffFadeSeconds, TimeUnit.Second) };
                fade.style.opacity = 1f;
                root.schedule.Execute(() =>
                {
                    if (SaveManager.Instance == null || !SaveManager.Instance.NewGame())
                    {
                        Debug.LogError("[Title] 새 게임을 시작하지 못했습니다.");
                        fade.style.opacity = 0f;
                        uiBusy = false;
                        TitleGlitch.In(new VisualElement[] { subBtns[0], subBtns[1], subBtns[2] });
                    }
                }).StartingIn((long)(takeoffFadeSeconds * 1000f));
            }).StartingIn((long)(takeoffFadeDelay * 1000f));
        }

        // ───────────────────────── 불러오기 패널 ─────────────────────────

        void OpenLoad()
        {
            if (scene == null || !scene.Docked) return;   // 도킹 서브 메뉴에서만 — 메인 메뉴 위에 겹쳐 열리던 것(2026-09-07 실측)
            if (uiLoad || uiBusy || returning || LatestMeta() == null) return;   // 고를 것이 없으면 들어가지 않는다
            uiBusy = true;
            TitleGlitch.Out(new VisualElement[] { subBtns[0], subBtns[1], subBtns[2] });
            root.schedule.Execute(() =>
            {
                uiLoad = true;
                menuLoad.style.display = DisplayStyle.Flex;
                slots.Clear();
                RebuildSlots();
                TitleGlitch.In(loadPanel);
                uiBusy = false;
            }).StartingIn(GlitchOutMs);
        }

        void CloseLoad()
        {
            if (!uiLoad || uiBusy) return;
            uiBusy = true;
            TitleGlitch.Out(loadPanel);
            root.schedule.Execute(() =>
            {
                uiLoad = false;
                menuLoad.style.display = DisplayStyle.None;
                subBtns[1].EnableInClassList("holo-btn--disabled", LatestMeta() == null);
                TitleGlitch.In(new VisualElement[] { subBtns[0], subBtns[1], subBtns[2] });
                uiBusy = false;
            }).StartingIn(GlitchOutMs);
        }

        void RebuildSlots()
        {
            if (!uiLoad) return;
            int usable = slots.Rebuild(list?.contentContainer, saveMode: false);
            if (emptyNote != null) emptyNote.style.display = usable == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateLoadButtons();
        }

        void UpdateLoadButtons()
        {
            bool has = !string.IsNullOrEmpty(slots.Selected);
            btnLoadConfirm?.EnableInClassList("holo-btn--disabled", !has);
            btnDelete?.EnableInClassList("holo-btn--disabled", !has);
        }

        void LoadSelected()
        {
            if (string.IsNullOrEmpty(slots.Selected) || SaveManager.Instance == null) return;
            SaveManager.Instance.Load(slots.Selected);
        }

        void DeleteSelected()
        {
            if (string.IsNullOrEmpty(slots.Selected) || SaveManager.Instance == null) return;
            SaveManager.Instance.DeleteSlot(slots.Selected);
            slots.Clear();
            RebuildSlots();
        }

        /// <summary>가장 최근 세이브의 요약. 하나도 없으면 null.</summary>
        static SaveMeta LatestMeta() => SaveManager.Instance != null ? SaveManager.Instance.LatestMeta() : null;

        // ───────────────────────── 설정 ─────────────────────────

        void OpenSettings()
        {
            if (scene != null && scene.Launching) return;
            if (uiSettings || uiBusy) return;
            uiBusy = true;
            TitleGlitch.Out(new VisualElement[] { mainBtns[0], mainBtns[1], mainBtns[2] });
            root.schedule.Execute(() =>
            {
                uiSettings = true;
                menuSettings.style.display = DisplayStyle.Flex;
                settings.Refresh();
                TitleGlitch.In(settingsPanel);
                uiBusy = false;
            }).StartingIn(GlitchOutMs);
        }

        void CloseSettings()
        {
            if (!uiSettings || uiBusy) return;
            uiBusy = true;
            TitleGlitch.Out(settingsPanel);
            root.schedule.Execute(() =>
            {
                uiSettings = false;
                menuSettings.style.display = DisplayStyle.None;
                TitleGlitch.In(new VisualElement[] { mainBtns[0], mainBtns[1], mainBtns[2] });
                uiBusy = false;
            }).StartingIn(GlitchOutMs);
        }

        static void QuitGame()
        {
    #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
    #else
            Application.Quit();
    #endif
        }
    }
}
