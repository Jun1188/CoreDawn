using UnityEngine.SceneManagement;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 씬 전환 게이트 — 목표 씬(World)을 <b>타이틀 씬을 거쳐</b> 연다. 타이틀 씬의 <see cref="TitleBootstrap"/>이 심을 버리고 팩 정의·자원을 다 읽은 뒤 목표 씬을 연다.
    /// 옛 Boot 씬(로딩 게이트 전용 씬)을 타이틀에 합친 것(2026-09-07 사용자 "boot 랑 title 합쳐") — 첫 부팅의 로딩이 한 번만 뜨고, 새 게임·불러오기는
    /// 타이틀 안에서 "WORLD GENERATING" 을 띄운 채 World 를 연다.
    /// <para>들어오는 길: SaveManager 의 새 게임·불러오기, GameBootstrap 의 "게임 씬을 바로 재생" 라운드트립. 이미 타이틀 안이면 씬을 다시 열지 않고 게이트 단계로 바로 간다.
    /// <paramref name="pack"/>을 주면 그 팩으로 정의·자원을 다시 읽는다(타이틀에서 데이터팩 선택 — 후속).</para>
    /// </summary>
    public static class SceneGate
    {
        public const string TitleScene = "Title";

        static string pendingScene;
        static string pendingPack;
        static bool pendingReload;

        public static void Enter(string targetScene, string pack = null)
        {
            pendingScene = targetScene;
            pendingPack = pack;
            pendingReload = pack != null;
            UnityEngine.Debug.Log($"[SceneGate] Enter '{targetScene}' (pack={pack ?? "-"}, titleAlive={TitleBootstrap.Instance != null})");

            var boot = TitleBootstrap.Instance;
            if (boot != null && boot.isActiveAndEnabled) { boot.Go(); return; }
            SceneManager.LoadScene(TitleScene, LoadSceneMode.Single);
        }

        /// <summary>대기 중인 목표를 꺼내고 비운다. 없으면 scene = null(타이틀 메뉴 모드).</summary>
        internal static void Take(out string scene, out string pack, out bool reload)
        {
            scene = pendingScene; pack = pendingPack; reload = pendingReload;
            pendingScene = null; pendingPack = null; pendingReload = false;
        }
    }
}
