using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using CoreDawn.Combat;
using CoreDawn.Save;
using CoreDawn.Sim;
using CoreDawn.UI;
using CoreDawn.Worlds;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 앱 흐름 — 씬 전환을 한 곳이 절차대로 몬다(2026-09-07, 사용자 "뭔가 많이 꼬인 느낌": TitleBootstrap/SceneGate 의 static 대기 목표,
    /// GameBootstrap 의 sceneLoaded 훅·라운드트립, SaveManager 의 프레임 세기 복원이 각자 순서를 관례로 맞추던 것을 코루틴 하나로).
    /// 플레이 시작 때 스스로 생기고(DontDestroyOnLoad) 로딩 오버레이를 갖는다.
    /// <list type="bullet">
    /// <item><b>타이틀에서 시작</b>: 팩 정의·자원을 다 읽고 <see cref="PackReady"/>. 타이틀 화면이 그 진행률을 자기 로딩 상자에 그린다.</item>
    /// <item><b><see cref="LoadWorld"/></b>(새 게임·불러오기): 오버레이 → 심 리셋 → 팩 준비 → World 를 Additive 로 비동기 로드(활성화 직후 루트를 꺼 Start 를 붙든다)
    /// → 옛 씬 언로드 → 지형 생성 코루틴(진행률) → 기능 씬 동기 로드 요청 → 첫 기능 씬 통합 때 루트 켜기 → 조립(GameBootstrap) → Start → 복원 → 오버레이 끔.</item>
    /// <item><b>게임 씬을 바로 재생</b>(에디터·테스트): 타이틀로 돌아가지 않고 제자리에서 같은 절차. AfterSceneLoad 시점은 Awake 뒤·Start 앞이라 루트를 꺼 둘 수 있다.</item>
    /// </list>
    /// 기능 씬이 게임 씬의 Start 앞에 있어야 하는 계약(InputManager 등)은 "루트를 껐다가 기능 씬이 통합되는 순간 켠다"로 지킨다(<see cref="GameBootstrap.LoadFeatures"/>).
    /// </summary>
    public sealed class AppFlow : MonoBehaviour
    {
        public static AppFlow Instance { get; private set; }
        public const string TitleScene = "Title";

        /// <summary>팩 정의·자원이 준비됐다(타이틀 메뉴가 열려도 되는 상태).</summary>
        public bool PackReady { get; private set; }
        /// <summary>월드 전환·초기화가 진행 중.</summary>
        public bool Busy { get; private set; }
        public bool Failed { get; private set; }
        /// <summary>오버레이 머리글. 월드 전환은 "항성계 워프중" — 완료 문구 없이 페이드 아웃해 컷신으로 잇는다(2026-09-08 사용자).</summary>
        public string Phase { get; private set; } = WarpPhase;
        public const string WarpPhase = "WARPING TO STAR SYSTEM";
        const float FadeOutSeconds = 0.6f, BlackHoldSeconds = 0.3f, FadeInSeconds = 0.6f;
        const float FirstLoadDelaySeconds = 0.5f;
        /// <summary>지금 하는 일(파일 이름·단계) — 오버레이 문구.</summary>
        public string Current { get; private set; } = "INIT";
        public float Progress { get; private set; }

        UIDocument overlay;
        Camera clearCam;   // 전환 중 유일한 카메라 — 옛 씬을 내리고 새 루트를 켜기 전까지 카메라가 없어 백버퍼가 안 지워진다(에디터 "No cameras rendering")
        Label ovTitle, ovPct, ovMsg;
        HoloBar ovBar;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("[AppFlow]");
            DontDestroyOnLoad(go);
            var flow = go.AddComponent<AppFlow>();
            GameBootstrap.Register();
            flow.StartCoroutine(flow.BootRoutine());
        }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        // ── 진입 ─────────────────────────────────────────────────

        IEnumerator BootRoutine()
        {
            var scene = SceneManager.GetActiveScene();
            var world = FindFirstObjectByType<World>(FindObjectsInactive.Include);
            if (world != null && scene.name != TitleScene)
            {
                // 게임 씬을 바로 재생 — Awake 는 끝났고 Start 는 아직: 루트를 꺼 두고 제자리에서 초기화한다
                var roots = scene.GetRootGameObjects();
                SetActive(roots, false);
                yield return WorldInitRoutine(scene, roots);
                yield break;
            }
            yield return new WaitForSecondsRealtime(FirstLoadDelaySeconds);   // 로딩 상자가 0% 로 잠깐 서 있다가 흐른다(2026-09-08 사용자)
            yield return EnsurePackRoutine();
            PackReady = !Failed;
        }

        /// <summary>목표 게임 씬(경로 또는 이름)을 절차대로 연다. 새 게임·불러오기가 부른다. <paramref name="intro"/>: 워프 끝 검정 구간에서 인트로 컷신(<see cref="CutscenePlayer.IntroPath"/>)을 튼다 — 새 게임만.</summary>
        public void LoadWorld(string scene, bool intro = false)
        {
            if (Busy) { Debug.LogWarning("[AppFlow] 이미 전환 중입니다 — 무시합니다."); return; }
            pendingIntro = intro;
            StartCoroutine(LoadWorldRoutine(scene));
        }

        bool pendingIntro;

        IEnumerator LoadWorldRoutine(string scenePath)
        {
            Busy = true; Failed = false;
            ShowOverlay(WarpPhase);
            Report(0f, "RESET");
            SimRunner.Reset();
            SimHost.Reset();

            yield return EnsurePackRoutine();
            if (Failed) { Busy = false; yield break; }

            string sceneName = Path.GetFileNameWithoutExtension(scenePath);

            // 옛 씬들을 먼저 전부 내린다 — 타이틀뿐 아니라 게임 안에서 불러올 때의 옛 World 와 그 기능 씬(Systems·Factory·Combat·GameUI)도.
            // 옛 월드가 남은 채 새 월드의 Awake 가 돌면 중복 가드(GameManager·TimeManager 등)가 새 오브젝트를 지우고(실측 2026-09-07: 새 World 에 플레이어 없음),
            // 기능 씬을 남기면 GameBootstrap 이 "이미 있다"고 다시 얹지 않는다. 씬은 하나는 남아야 하므로 빈 닻 씬을 활성으로 두고 나머지를 내린다.
            Report(0.05f, "UNLOAD");
            var sky = RenderSettings.skybox;   // 닻 씬에 옛 스카이박스를 물려준다 — 서드파티 SkyboxSettings.OnValidate(에디터 전용)가 RenderSettings.skybox 를 null 검사 없이 읽는다
            var anchor = SceneManager.CreateScene("__AppFlow");
            SceneManager.SetActiveScene(anchor);
            if (sky != null) RenderSettings.skybox = sky;
            yield return UnloadOthers(anchor);

            Report(0.08f, "SCENE " + sceneName);
            var op = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            if (op == null) { Fail($"씬 '{scenePath}' 을 열 수 없습니다 — Build Settings 를 확인하세요."); Busy = false; yield break; }
            op.allowSceneActivation = false;
            while (op.progress < 0.9f) { Report(0.08f + 0.12f * (op.progress / 0.9f), "SCENE " + sceneName); yield return null; }

            // 활성화 직후(Awake 뒤·Start 앞)에 루트를 꺼 Start 를 붙든다
            GameObject[] roots = null; Scene loaded = default;
            UnityAction<Scene, LoadSceneMode> onLoaded = null;
            onLoaded = (s, _) =>
            {
                if (s.path != scenePath && s.name != sceneName) return;
                SceneManager.sceneLoaded -= onLoaded;
                loaded = s; roots = s.GetRootGameObjects();
                SetActive(roots, false);
            };
            SceneManager.sceneLoaded += onLoaded;
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;
            SceneManager.sceneLoaded -= onLoaded;
            if (roots == null)
            {
                loaded = SceneManager.GetSceneByPath(scenePath);
                if (!loaded.IsValid()) loaded = SceneManager.GetSceneByName(sceneName);
                roots = loaded.GetRootGameObjects();
                SetActive(roots, false);
            }
            SceneManager.SetActiveScene(loaded);
            yield return UnloadOthers(loaded);   // 닻 씬

            yield return WorldInitRoutine(loaded, roots);
        }

        // keep 만 남기고 다 내린다(DontDestroyOnLoad 씬은 목록에 없다)
        static IEnumerator UnloadOthers(Scene keep)
        {
            var old = new System.Collections.Generic.List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) { var sc = SceneManager.GetSceneAt(i); if (sc != keep && sc.isLoaded) old.Add(sc); }
            foreach (var sc in old)
            {
                var unload = SceneManager.UnloadSceneAsync(sc);
                while (unload != null && !unload.isDone) yield return null;
            }
        }

        // 루트가 꺼진 게임 씬을 준비해 켠다 — 지형 → 루트 켜기 + 기능 씬 → 조립 → 복원
        IEnumerator WorldInitRoutine(Scene scene, GameObject[] roots)
        {
            Busy = true;
            ShowOverlay(WarpPhase);
            if (!PackReady)
            {
                yield return EnsurePackRoutine();
                if (Failed) { SetActive(roots, true); Busy = false; HideOverlay(); yield break; }
            }

            var world = FindWorld(roots);
            if (world != null)
                yield return WorldTerrainBuilder.BuildRoutine(world, (p, what) => Report(0.2f + 0.7f * p, what));

            Report(0.92f, "SYSTEMS");
            // 기능 씬을 루트가 꺼진 채 요청하고, 첫 기능 씬이 통합되는 순간(다음 프레임 첫머리, 조립 앞) 루트를 켠다 — 같은 통합 패스에서
            // 나머지 기능 씬의 Awake·조립이 이어지고 그 뒤에야 모든 Start 가 돈다. 루트를 먼저 켜면 게임 씬 Start 가 통합보다 앞선다(실측: InputManager 없음)
            bool activated = false;
            Action activate = () => { if (activated) return; activated = true; SetActive(roots, true); };
            if (!GameBootstrap.LoadFeatures(activate)) activate();   // 얹을 기능 씬이 없는 씬(테스트)은 바로 켠다
            Tutorial.TutorialManager.EnsureSpawned();                // 튜토리얼도 여기서 — 세이브 복원(tutorial 모듈)보다 먼저 있어야 한다
            yield return null;               // 기능 씬 통합·Awake·조립 → 루트 켜짐 → 게임 씬·기능 씬 Start
            activate();                      // 안전망 — 통합 콜백이 오지 않았어도 켠다
            yield return null;               // Start 에서 만들어진 것들(코어 연결 등) 정착

            if (SaveLoadContext.Pending != null)
            {
                Report(0.97f, "RESTORE");
                if (SaveManager.Instance != null) SaveManager.Instance.RestorePending();
                yield return null;
            }
            Report(1f, Current);   // 완료 문구 없음 — 마지막 단계 글씨 그대로 두고 페이드 아웃
            yield return null;
            yield return FadeOutOverlay();
            Busy = false;
        }

        // 워프 끝 — 검정으로 페이드 아웃(로딩 상자가 사라지며 바탕이 검정으로), 검정에서 잠깐 멈춘 뒤 검정을 걷어 게임을 드러낸다.
        // 게임 화면으로 바로 디졸브하지 않는다(2026-09-08 사용자 "페이드 아웃이 아니라 페이드 전환이잖아"). 검정 구간이 컷신을 끼울 자리 — 컷신이 붙으면 뒤의 페이드 인은 컷신이 맡는다.
        IEnumerator FadeOutOverlay()
        {
            var root = overlay != null ? overlay.rootVisualElement : null;
            if (root != null)
            {
                var screen = root.Q("load-screen");
                var box = root.Q(className: "load-box");
                var dur = new System.Collections.Generic.List<TimeValue> { new TimeValue(FadeOutSeconds, TimeUnit.Second) };
                if (box != null) { box.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new StylePropertyName("opacity") }; box.style.transitionDuration = dur; box.style.opacity = 0f; }
                if (screen != null) { screen.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new StylePropertyName("background-color") }; screen.style.transitionDuration = dur; screen.style.backgroundColor = Color.black; }
                if (clearCam != null) clearCam.backgroundColor = Color.black;
                root.pickingMode = PickingMode.Ignore;
                yield return Wait(FadeOutSeconds + 0.1f);
                yield return Wait(BlackHoldSeconds);

                if (pendingIntro)   // 인트로 컷신 — 검정 위에서 틀고 끝나면 다시 검정(새 게임만). 파일이 없으면 건너뛴다.
                {
                    pendingIntro = false;
                    yield return CutscenePlayer.Play(this, root.Q("cutscene"), CutscenePlayer.IntroPath);
                    yield return Wait(BlackHoldSeconds);
                }

                if (clearCam != null) clearCam.enabled = false;   // 이제 게임 카메라가 밑에서 그린다
                root.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new StylePropertyName("opacity") };
                root.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new TimeValue(FadeInSeconds, TimeUnit.Second) };
                root.style.opacity = 0f;
                yield return Wait(FadeInSeconds + 0.1f);
            }
            HideOverlay();
        }

        static IEnumerator Wait(float seconds)
        {
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) yield return null;
        }

        // ── 팩 ───────────────────────────────────────────────────

        IEnumerator EnsurePackRoutine()
        {
            string pack = PackLoader.CurrentPack;
            if (SimHost.Database == null)
            {
                SimHost.DatabaseLoader = () => PackLoader.Load(pack);
                PackAssets.Clear();
            }
            Report(0f, "PACK");
            var db = SimHost.Database;
            if (db == null) { Fail($"팩 '{pack}'을 읽지 못했습니다 — 콘솔을 보세요."); yield break; }
            if (db.Errors.Count > 0) { Fail($"팩 '{pack}': 정의 오류 {db.Errors.Count}건 — 콘솔을 보세요."); yield break; }

            var task = PackAssets.PreloadAsync(db);
            while (!task.IsCompleted)
            {
                var (done, total) = PackAssets.Progress;
                Report(total > 0 ? (float)done / total : 0f, PackAssets.Current ?? "PACK");
                yield return null;
            }
            if (task.IsFaulted) { Debug.LogException(task.Exception); Fail("팩 자원을 읽지 못했습니다 — 콘솔을 보세요."); yield break; }
            PackReady = true;
        }

        // ── 오버레이 ─────────────────────────────────────────────

        void ShowOverlay(string phase)
        {
            Phase = phase;
            if (overlay == null)
            {
                var ps = Resources.Load<PanelSettings>("Builtin/LoadingPanelSettings");
                var tree = Resources.Load<VisualTreeAsset>("Builtin/LoadingOverlay");
                if (ps == null || tree == null) { Debug.LogError("[AppFlow] Resources/Builtin 의 LoadingPanelSettings 또는 LoadingOverlay 가 없습니다 — 오버레이 없이 진행합니다."); return; }
                overlay = gameObject.AddComponent<UIDocument>();
                overlay.panelSettings = ps;
                overlay.visualTreeAsset = tree;
                overlay.sortingOrder = 500;
            }
            overlay.enabled = true;
            if (overlay.rootVisualElement != null)   // 페이드 아웃 뒤 다시 켤 때 즉시·불투명
            {
                var r = overlay.rootVisualElement;
                var zero = new System.Collections.Generic.List<TimeValue> { new TimeValue(0f, TimeUnit.Second) };
                r.style.transitionDuration = zero; r.style.opacity = 1f; r.pickingMode = PickingMode.Position;
                var screen = r.Q("load-screen"); if (screen != null) { screen.style.transitionDuration = zero; screen.style.backgroundColor = StyleKeyword.Null; }
                var box = r.Q(className: "load-box"); if (box != null) { box.style.transitionDuration = zero; box.style.opacity = 1f; }
            }
            if (clearCam == null)
            {
                clearCam = gameObject.AddComponent<Camera>();
                clearCam.clearFlags = CameraClearFlags.SolidColor;
                clearCam.backgroundColor = new Color(7f / 255f, 13f / 255f, 26f / 255f);   // title.uss .load-screen 바탕
                clearCam.cullingMask = 0;
                clearCam.depth = -100;
                clearCam.useOcclusionCulling = false;
                clearCam.allowHDR = false; clearCam.allowMSAA = false;
            }
            clearCam.backgroundColor = new Color(7f / 255f, 13f / 255f, 26f / 255f);   // 검정 페이드 뒤 되돌림
            clearCam.enabled = true;
            var root = overlay.rootVisualElement;
            if (root == null) return;
            ovTitle = root.Q<Label>("load-title");
            ovPct = root.Q<Label>("load-pct");
            ovMsg = root.Q<Label>("load-msg");
            var host = root.Q("load-bar");
            if (host != null && host.childCount == 0) { ovBar = new HoloBar(); host.Add(ovBar); }
            root.style.display = DisplayStyle.Flex;
            Report(Progress, Current);
        }

        void HideOverlay()
        {
            if (overlay == null) return;
            var root = overlay.rootVisualElement;
            if (root != null) root.style.display = DisplayStyle.None;
            overlay.enabled = false;
            if (clearCam != null) clearCam.enabled = false;
        }

        void Report(float progress, string what)
        {
            Progress = Mathf.Clamp01(progress);
            Current = what ?? "";
            if (overlay == null || !overlay.enabled) return;
            if (ovTitle != null) ovTitle.text = Phase;
            if (ovPct != null) ovPct.text = Mathf.RoundToInt(Progress * 100f) + "%";
            if (ovBar != null) ovBar.Progress = Progress;
            if (ovMsg != null) { ovMsg.text = Current.ToUpperInvariant(); ovMsg.EnableInClassList("load-msg--error", Failed); }
        }

        void Fail(string message)
        {
            Failed = true;
            Current = message;
            Debug.LogError("[AppFlow] " + message, this);
            Report(Progress, message);
        }

        // ── 도우미 ───────────────────────────────────────────────

        static void SetActive(GameObject[] roots, bool active)
        {
            if (roots == null) return;
            foreach (var r in roots) if (r != null) r.SetActive(active);
        }

        static World FindWorld(GameObject[] roots)
        {
            if (roots == null) return null;
            foreach (var r in roots)
            {
                if (r == null) continue;
                var w = r.GetComponentInChildren<World>(true);
                if (w != null) return w;
            }
            return null;
        }
    }
}
