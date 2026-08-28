using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Inputs;
using CoreDawn.Interaction;
using CoreDawn.Navigation;
using CoreDawn.Pings;
using CoreDawn.Placement;
using CoreDawn.UI;
using CoreDawn.Worlds;

namespace CoreDawn.Managers
{
    using Object = UnityEngine.Object;   // System.Object와의 모호성 해소 (using System 때문)

    /// <summary>
    /// 기능 부트스트랩 — 기능 씬을 얹고(로드), 씬 경계를 넘는 참조를 꽂는다(조립).
    /// (구 UIBootstrap의 일반화 — GameUI가 목록의 한 항목으로 흡수됐다)
    ///
    /// <b>씬 방식인 이유</b>(UIBootstrap에서 계승): 씬마다 복사본을 두면 새 씬을 만들 때 빠뜨리고,
    /// 복사본끼리 어긋나며(실측: UITest·ItemTree·MainScene의 플레이어/매니저 구성이 서로 달랐다),
    /// 게임플레이 씬 파일에 매니저 diff가 섞여 팀 머지가 지저분해진다.
    /// 씬 파일들은 기능의 존재를 전혀 모르고, 로더가 조건에 맞으면 얹는다.
    ///
    /// <b>조립을 여기서 하는 이유</b>: 기능이 별도 씬으로 오면 인스펙터 참조(카메라·그리드 등)가
    /// 끊긴다. 그 구멍을 소비자마다 런타임 탐색(FindObjectByType·Camera.main)으로 메우면
    /// ① 배선을 잊어도 그럭저럭 도는 "조용한 실패"가 생기고 ② 잘못된 대상을 찾아도 알 수 없으며
    /// ③ 초기화 순서가 코드 어디에도 적히지 않는다. 그래서 <b>탐색은 이 파일이 독점</b>하고,
    /// 소비자는 Inject를 받을 뿐 스스로 찾지 않는다 — 무엇이 무엇을 필요로 하는지가 여기 다 있다.
    ///
    /// 조립 시점은 씬 로드 직후(그 씬 오브젝트들의 Awake 뒤, Start 앞)라 안전하다.
    ///
    /// 멱등성 2중 장치: ① 항목 조건이 대표 타입의 존재를 검사해 불필요한 로드를 피하고,
    /// ② 탑재되는 매니저들이 Awake에서 자가 중복 제거(있으면 자폭)하므로 씬에 직접 심어둔
    /// 복사본(구 방식)과 공존해도 안전하다. 기존 씬은 고치지 않아도 그대로 돈다 —
    /// 씬에서 복사본을 지우는 순간부터 부트스트랩이 대신 얹는 것이 마이그레이션의 전부다.
    /// </summary>
    public static class GameBootstrap
    {
        struct Entry
        {
            public string scene;                 // Build Settings에 등록된 씬 이름
            public Func<bool> shouldLoad;        // 탑재 조건 — 대표 타입 부재 검사(멱등)
            public Action assemble;              // 로드 직후 조립 — 씬 경계를 넘는 참조를 꽂는다
        }

        static readonly Entry[] entries =
        {
            // Systems — 입력·진행·시간. 씬 참조가 없는 순수 매니저만 들어간다(조립 불필요).
            new Entry
            {
                scene = "Systems",
                shouldLoad = () => Object.FindFirstObjectByType<InputManager>() == null,
            },

            // Factory — 공장 심 구동·건설(배치/철거)·벨트 아이템 렌더.
            new Entry
            {
                scene = "Factory",
                shouldLoad = () => Object.FindFirstObjectByType<FactoryBootstrap>() == null,
                assemble = AssembleFactory,
            },

            // Combat — 전투 총괄(BattleManager: 플레이어 엔티티 부착·웨이브 스포너).
            new Entry
            {
                scene = "Combat",
                shouldLoad = () => Object.FindFirstObjectByType<BattleManager>() == null,
                assemble = AssembleCombat,
            },

            // GameUI — 전투 HUD·패널 (구 UIBootstrap 항목 그대로)
            new Entry
            {
                scene = "GameUI",
                shouldLoad = () => Object.FindFirstObjectByType<GameplayHUDView>(FindObjectsInactive.Include) == null,
            },
        };

        // ── 조립 ────────────────────────────────────────────────────

        /// <summary>
        /// 배치 시스템에 조준 카메라와 맵을, 상호작용 조준에 벨트 렌더 뷰를 꽂는다.
        /// 카메라는 조준의 기준, 맵은 건설 가능 판정(강·절벽에는 못 짓는다)과 격자 좌표계의 출처다.
        /// </summary>
        static void AssembleFactory()
        {
            var factory = Object.FindFirstObjectByType<FactoryBootstrap>();

            // 벨트 위 아이템은 콜라이더가 없어(렌더 전용) 조준이 렌더 뷰의 좌표를 직접 훑는다.
            // 맵이 없는 씬에서도 벨트는 돌므로 월드 검사보다 앞에 둔다.
            // 뷰는 FactoryBootstrap의 RequireComponent라 이 시점에 반드시 있다.
            var interaction = Object.FindFirstObjectByType<PlayerInteractionManager>();
            if (interaction != null && factory != null)
                interaction.Inject(factory.GetComponent<BeltItemView>());

            var placement = Object.FindFirstObjectByType<PlacementSystem>();
            if (placement == null) return;

            placement.Inject(PlayerCamera());

            var world = ActiveWorld();
            if (world == null || world.Map == null) return;

            placement.Inject(world.Map, world.Origin, world.CellSize);

            // 코어가 어디 서는지도 맵이 정한다 — 자동 설치(Start)보다 먼저 꽂힌다
            if (factory != null) factory.Inject(world.Map.core);
        }

        /// <summary>
        /// 전투 총괄에 길찾기 그리드·플로우필드를, 그리드에는 월드의 맵을 꽂는다.
        /// 격자는 지형의 함수(절벽 = 통행 불가)지만 GridManager 자체는 시스템이라 별도 씬으로 온다.
        /// 맵이 없는 씬(아이템 테스트 등)에서는 스폰만 쉬고 나머지 전투는 정상 동작한다.
        /// </summary>
        static void AssembleCombat()
        {
            var grid = Object.FindFirstObjectByType<GridManager>();
            var world = ActiveWorld();
            if (grid != null && world != null && world.Map != null)
                grid.Inject(world.Map, world.Origin, world.CellSize);

            var battle = Object.FindFirstObjectByType<BattleManager>();
            if (battle == null) return;

            battle.Inject(grid, Object.FindFirstObjectByType<FlowFieldManager>());

            // 몬스터 아웃라인 — 반경 판정의 기준점(플레이어)을 꽂는다. Combat 씬은 플레이어를 모른다
            var outline = Object.FindFirstObjectByType<MonsterOutlineProximity>();
            if (outline != null) outline.Inject(PlayerRoot());

            // 핑 입력 — 조준 카메라와 자기 루트를 꽂는다 (자기 몸·뷰모델은 조준에서 빠져야 한다)
            var ping = Object.FindFirstObjectByType<PlayerPingInput>();
            if (ping != null) ping.Inject(PlayerCamera(), PlayerRoot());

            // 둥지·광맥·밤 진입로는 맵이 정한다. 씬에 미리 놓지 않고 여기서 세우는 이유는
            // 맵을 갈아끼우면 배치도 함께 갈려야 하기 때문이다 — 씬에 박아두면 맵과 배치가
            // 따로 놀다가 "새 맵인데 옛 둥지가 서 있는" 상태가 된다.
            if (world != null && world.Map != null)
                battle.Inject(WorldPopulator.Populate(world, battle.transform));
        }

        /// <summary>이 씬의 월드 — 맵 데이터의 출처. 없으면 맵 없는 구성(테스트 씬)이다.</summary>
        static World ActiveWorld() => Object.FindFirstObjectByType<World>();

        /// <summary>플레이어 루트 트랜스폼 — 거리 판정의 기준점. 없으면 null (몬스터 아웃라인처럼 없어도 해롭지 않은 소비자용).</summary>
        static Transform PlayerRoot()
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            return player != null ? player.transform : null;
        }

        /// <summary>플레이어의 시점 카메라 — 조준·배치의 기준. 없으면 알린다(조용히 넘어가지 않는다).</summary>
        static Camera PlayerCamera()
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            var camera = player != null ? player.GetComponentInChildren<Camera>() : null;
            if (camera == null)
                Debug.LogError("[GameBootstrap] 플레이어 카메라를 찾지 못했습니다 — 조준이 필요한 시스템이 비활성화됩니다.");
            return camera;
        }

        // ── 로드 ────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            // 람다가 아니라 이름 있는 메서드 — 도메인 리로드를 끈 환경(Enter Play Mode Options)에서는
            // static 구독이 플레이를 넘어 살아남으므로, 빼고 다시 걸어야 중복되지 않는다
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryLoadAll();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Single 로드는 additive로 얹힌 기능 씬도 함께 내리므로 씬 전환마다 다시 검사한다
            if (mode == LoadSceneMode.Single)
            {
                TryLoadAll();
                return;
            }

            // 방금 얹힌 것이 우리 기능 씬이면 조립한다 — 그 씬 오브젝트들의 Awake는 끝났고
            // Start는 아직이라, 주입된 참조를 Start에서 바로 쓸 수 있다.
            foreach (var e in entries)
            {
                if (e.scene != scene.name) continue;
                e.assemble?.Invoke();
                return;
            }
        }

        static void TryLoadAll()
        {
            // 플레이어 없는 씬(순수 테스트 씬)에는 아무것도 얹지 않는다 — 테스트를 오염시키지 않기
            if (Object.FindFirstObjectByType<PlayerController>() == null) return;

            foreach (var e in entries)
            {
                if (!e.shouldLoad()) continue;

                if (!Application.CanStreamedLevelBeLoaded(e.scene))
                {
                    Debug.LogWarning($"[GameBootstrap] '{e.scene}' 씬이 Build Settings에 없어 탑재하지 못함");
                    continue;
                }
                SceneManager.LoadScene(e.scene, LoadSceneMode.Additive);
            }
        }
    }
}
