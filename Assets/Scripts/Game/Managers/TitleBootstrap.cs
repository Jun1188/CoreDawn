using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoreDawn.Combat;
using CoreDawn.Sim;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 타이틀 씬의 부팅·로딩 게이트(옛 BootScene 을 타이틀에 합친 것, 2026-09-07). 빌드의 0번 씬이 타이틀이다.
    /// <list type="bullet">
    /// <item><b>메뉴 모드</b>(대기 목표 없음): 심을 버리고 팩 정의·팩 파일 자원(glb·png·ogg)을 <b>전부</b> 읽은 뒤 <see cref="Ready"/> — 타이틀 화면이 로딩 상자를 걷고 메뉴를 연다.</item>
    /// <item><b>게이트 모드</b>(<see cref="SceneGate.Enter"/> 로 목표가 있음): 같은 준비를 하고 목표 씬(World)을 연다. 화면은 "WORLD GENERATING".
    /// 조립기·마커 입히기·설치 미리보기는 동기라 자원이 먼저 준비돼 있어야 하고, 목표 씬 안에서 preload 를 기다리면 프레임 1 의 Start() 조회가 깨진다 — 그래서 여기서 다 읽고 연다.</item>
    /// </list>
    /// 심은 씬 하나의 것 — 이 게이트를 지나면 옛 월드(엔티티 등록부 + 전투 시스템)는 통째로 버린다. 뷰·부트스트랩이 OnDestroy 에서 자기 엔티티를 빼던 방식은
    /// 소유가 뒤집힌 데다 하나만 빠져도 유령이 남았다(광맥이 재로드마다 두 배, 시작 아이템 이중 지급 — 2026-09-04).
    /// 목표 씬 로드는 동기(<see cref="SceneManager.LoadScene"/>) — GameBootstrap 이 기능 씬을 게임 씬의 Start 앞에 동기로 얹는 계약이라 비동기 활성화로는 순서가 깨진다(실측: InputManager 조회 실패).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TitleBootstrap : MonoBehaviour
    {
        public static TitleBootstrap Instance { get; private set; }

        /// <summary>"SCENE WORLD" 를 잠깐 보여주고 넘어간다(레퍼런스 300ms).</summary>
        const int HoldMs = 300;

        /// <summary>지금 단계 문구 — 화면이 그대로 보여준다(자원 항목은 PackAssets.Current 가 대신한다).</summary>
        public string Status { get; private set; } = "INIT";
        public bool Failed { get; private set; }
        /// <summary>이번에 여는 씬. null 이면 타이틀 메뉴 모드.</summary>
        public string Target { get; private set; }
        public bool IsGate => Target != null;
        /// <summary>메뉴 모드에서 팩 자원 준비가 끝났다.</summary>
        public bool Ready { get; private set; }

        // 재진입 세대 — 메뉴 모드 Go 가 preload 를 기다리는 사이 새 게임의 Go 가 들어오면 옛 것은 깨어난 뒤 물러난다.
        // (실측 2026-09-07: 둘 다 같은 preload 완료에 깨어나 옛 Go 가 새로 바뀐 Target 을 보고 LoadScene 을 한 번 더 불러 World 가 두 번 열리고 광맥이 겹쳤다)
        int generation;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start() => Go();

        /// <summary>대기 목표를 꺼내 준비 → (목표가 있으면) 씬 열기. 타이틀 안에서 새 게임·불러오기를 누르면 SceneGate 가 다시 부른다.</summary>
        public async void Go()
        {
            int gen = ++generation;
            SceneGate.Take(out var target, out var pack, out bool reloadPack);
            Debug.Log($"[TitleBootstrap] Go target={target ?? "-"} reloadPack={reloadPack}", this);
            Target = target;
            Ready = false; Failed = false;
            pack ??= PackLoader.CurrentPack;
            bool reload = reloadPack || SimHost.Database == null;

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
            if (this == null || gen != generation) return;   // 씬이 먼저 닫혔거나 더 새 Go 가 들어왔다

            if (target == null) { Status = "READY"; Ready = true; return; }

            Status = $"SCENE {System.IO.Path.GetFileNameWithoutExtension(target)}";
            await Task.Delay(HoldMs);
            if (this == null || gen != generation) return;
            await Task.Yield();
            if (this == null || gen != generation) return;
            Debug.Log($"[TitleBootstrap] LoadScene '{target}'", this);
            SceneManager.LoadScene(target, LoadSceneMode.Single);
        }

        void Fail(string message)
        {
            Failed = true;
            Status = message;
            Debug.LogError("[TitleBootstrap] " + message, this);
        }
    }
}
