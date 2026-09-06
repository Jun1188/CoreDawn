using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoreDawn.Combat;
using CoreDawn.Sim;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 부팅 씬(로딩 게이트) — 팩 정의(data.json)와 팩 파일 자원(glb·png)을 <b>전부 읽은 뒤</b> 목표 씬을 연다.
    /// 조립기·마커 입히기·설치 미리보기는 동기라 자원이 먼저 준비돼 있어야 하고, 부팅에서 preload를 기다리면 씬 로드가 프레임 1 뒤로 밀려
    /// Start() 조회가 깨진다 — 그래서 기다리는 자리를 씬 하나로 뗐다.
    /// <para>빌드의 0번 씬이다(2026-09-07, 사용자 지시 "loading 씬을 title 앞으로") — 첫 부팅은 다 읽고 <see cref="DefaultTarget"/>(Title)로.
    /// 게임 안에서는 <see cref="Enter"/>(SaveManager의 새 게임·불러오기)로 다시 지나며, 팩을 바꾸면(타이틀에서 데이터팩 선택 — 후속) <c>pack</c>을 넘긴다:
    /// 정의·자원을 버리고 다시 읽는다. 에디터에서 Boot 씬을 바로 재생해도 Title 로 간다.</para>
    /// 화면은 <see cref="UI.BootScreenView"/>(UITK, 타이틀 레퍼런스의 로딩 상자) — 여기서는 단계 문구(<see cref="Status"/>)와 실패(<see cref="Failed"/>)만 낸다.
    /// </summary>
    public sealed class BootScene : MonoBehaviour
    {
        public const string SceneName = "Boot";
        public const string DefaultTarget = "Title";

        /// <summary>READY 를 잠깐 보여주고 넘어간다(레퍼런스 300ms).</summary>
        const int ReadyHoldMs = 300;

        static string pendingScene;
        static string pendingPack;
        static bool pendingReload;

        /// <summary>지금 단계 문구 — 화면이 그대로 보여준다(자원 항목은 PackAssets.Current 가 대신한다).</summary>
        public string Status { get; private set; } = "INIT";
        public bool Failed { get; private set; }
        /// <summary>이번에 여는 씬 — 타이틀이면 "LOADING", 그 밖(World)이면 화면이 "WORLD GENERATING" 으로 바꿔 보여준다(2026-09-07 사용자 지시).</summary>
        public string Target { get; private set; } = DefaultTarget;
        public bool ToTitle => Target == DefaultTarget;

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
            Target = target;
            string pack = pendingPack ?? PackLoader.CurrentPack;
            bool reload = pendingReload || SimHost.Database == null;
            pendingScene = null; pendingPack = null; pendingReload = false;

            // 심은 씬 하나의 것 — 이 게이트를 지나면 옛 월드(엔티티 등록부 + 전투 시스템)는 통째로 버린다.
            // 뷰·부트스트랩이 OnDestroy 에서 자기 엔티티를 빼던 방식은 소유가 뒤집힌 데다(정본은 심) 하나만 빠져도
            // 유령이 남았다 — 광맥이 재로드마다 두 배, 플레이어 재사용으로 시작 아이템 이중 지급(2026-09-04).
            SimRunner.Reset();
            SimHost.Reset();

            Status = "팩 정의";
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

            Status = $"팩 '{pack}' 자원";
            await PackAssets.PreloadAsync(db);
            if (this == null) return;   // 씬이 먼저 닫힘

            // 타이틀로 갈 때는 READY 를 잠깐 보여주고, World 로 갈 때는 "WORLD GENERATING" 화면인 채로 씬을 연다 —
            // 월드 생성(지형·광맥·둥지 굳히기)은 World 씬 로드 안에서 동기로 돌아 그동안 이 마지막 프레임이 남는다
            Status = ToTitle ? "READY" : $"SCENE {System.IO.Path.GetFileNameWithoutExtension(target)}";
            await Task.Delay(ReadyHoldMs);
            if (this == null) return;
            if (ToTitle) Status = $"'{target}' 여는 중";
            await Task.Yield();
            SceneManager.LoadScene(target, LoadSceneMode.Single);
        }

        void Fail(string message)
        {
            Failed = true;
            Status = message;
            Debug.LogError("[BootScene] " + message, this);
        }
    }
}
