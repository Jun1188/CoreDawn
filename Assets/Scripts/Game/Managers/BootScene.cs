using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoreDawn.Combat;
using CoreDawn.Sim;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 부팅 씬(로딩 게이트) — 팩 정의(data.json)와 팩 파일 자원(glb·png)을 <b>전부 읽은 뒤</b> 목표 씬(World)을 연다.
    /// 조립기·마커 입히기·설치 미리보기는 동기라 자원이 먼저 준비돼 있어야 하고, 부팅에서 preload를 기다리면 씬 로드가 프레임 1 뒤로 밀려
    /// Start() 조회가 깨진다 — 그래서 기다리는 자리를 씬 하나로 뗐다.
    /// <para>들어오는 길: <see cref="Enter"/>(SaveManager의 새 게임·불러오기). 팩을 바꾸면(타이틀에서 데이터팩 선택 — 후속) <c>pack</c>을 넘긴다:
    /// 정의·자원을 버리고 다시 읽는다. 에디터에서 Boot 씬을 바로 재생하면 기본 팩으로 World를 연다.</para>
    /// 로딩 화면은 임시(OnGUI) — UI 작업 때 UI Toolkit 화면으로 바꾼다.
    /// </summary>
    public sealed class BootScene : MonoBehaviour
    {
        public const string SceneName = "Boot";
        public const string DefaultTarget = "World";

        static string pendingScene;
        static string pendingPack;
        static bool pendingReload;

        string status = "팩을 읽는 중…";
        bool failed;

        /// <summary>목표 씬을 부팅 씬을 거쳐 연다. <paramref name="pack"/>을 주면 그 팩으로 정의·자원을 다시 읽는다(없으면 지금 것을 그대로 쓴다).</summary>
        public static void Enter(string targetScene, string pack = null)
        {
            pendingScene = targetScene;
            pendingPack = pack;
            pendingReload = pack != null;
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
        }

        public static bool IsBootScene(Scene scene) => scene.name == SceneName;

        async void Start()
        {
            string target = pendingScene ?? DefaultTarget;
            string pack = pendingPack ?? PackLoader.CurrentPack;
            bool reload = pendingReload || SimHost.Database == null;
            pendingScene = null; pendingPack = null; pendingReload = false;

            // 심은 씬 하나의 것 — 이 게이트를 지나면 옛 월드(엔티티 등록부 + 전투 시스템)는 통째로 버린다.
            // 뷰·부트스트랩이 OnDestroy 에서 자기 엔티티를 빼던 방식은 소유가 뒤집힌 데다(정본은 심) 하나만 빠져도
            // 유령이 남았다 — 광맥이 재로드마다 두 배, 플레이어 재사용으로 시작 아이템 이중 지급(2026-09-04).
            SimRunner.Reset();
            SimHost.Reset();

            if (reload)
            {
                PackLoader.CurrentPack = pack;
                SimHost.DatabaseLoader = () => PackLoader.Load(pack);
                SimHost.Database = null;
                PackAssets.Clear();
            }
            var db = SimHost.Database;
            if (db == null) { Fail($"팩 '{pack}'을 읽지 못했습니다 — 콘솔을 보세요."); return; }
            if (db.Errors.Count > 0) { Fail($"팩 '{pack}': 정의 오류 {db.Errors.Count}건 — 콘솔을 보세요."); return; }

            status = $"팩 '{pack}' 자원을 읽는 중…";
            await PackAssets.PreloadAsync(db);
            if (this == null) return;   // 씬이 먼저 닫힘
            status = $"'{target}' 여는 중…";
            await Task.Yield();
            SceneManager.LoadScene(target, LoadSceneMode.Single);
        }

        void Fail(string message)
        {
            failed = true;
            status = message;
            Debug.LogError("[BootScene] " + message, this);
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = failed ? new Color(1f, 0.4f, 0.4f) : Color.white;
            var r = new Rect(0f, Screen.height * 0.5f - 40f, Screen.width, 80f);
            GUI.Label(r, status, style);
            var sub = new GUIStyle(style) { fontSize = 16 };
            sub.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            var (done, total) = PackAssets.Progress;
            GUI.Label(new Rect(0f, r.yMax, Screen.width, 30f), total > 0 ? $"{done} / {total}" : "", sub);
        }
    }
}
