using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 기능 부트스트랩 로더 — 씬마다 매니저를 복붙하는 대신, 기능 씬을 additive로 얹는다.
/// (구 UIBootstrap의 일반화 — GameUI가 목록의 한 항목으로 흡수됐다)
///
/// 씬 방식인 이유(UIBootstrap에서 계승): 씬마다 복사본을 두면 새 씬을 만들 때 빠뜨리고,
/// 복사본끼리 어긋나며(실측: UITest·ItemTree·MainScene의 플레이어/매니저 구성이 서로 다름),
/// 게임플레이 씬 파일에 매니저 diff가 섞여 팀 머지가 지저분해진다.
/// 여기서는 씬 파일들이 기능의 존재를 전혀 모르고, 로더가 조건에 맞으면 얹는다.
///
/// 로더가 하나인 이유: RuntimeInitializeOnLoadMethod가 기능마다 흩어지면 실행 순서를
/// 보장할 수 없다 — entries의 순서가 곧 탑재 순서다(의존하는 쪽을 뒤에).
///
/// 멱등성 2중 장치: ① 항목 조건이 대표 타입의 존재를 검사해 불필요한 로드를 피하고,
/// ② 탑재되는 매니저들 각각이 Awake에서 자가 중복 제거(있으면 자폭)하므로 씬에 직접
/// 심어둔 복사본(구 방식)과 공존해도 안전하다. 기존 씬은 고치지 않아도 그대로 돈다 —
/// 씬에서 복사본을 지우는 순간부터 부트스트랩이 대신 얹는 것이 마이그레이션의 전부다.
/// </summary>
public static class GameBootstrap
{
    struct Entry
    {
        public string scene;                 // Build Settings에 등록된 씬 이름
        public System.Func<bool> shouldLoad; // 탑재 조건 — 대표 타입 부재 검사(멱등)
    }

    static readonly Entry[] entries =
    {
        // Systems — 입력·진행·시간. 씬 참조가 없는 순수 매니저만 들어간다.
        new Entry
        {
            scene = "Systems",
            shouldLoad = () => Object.FindFirstObjectByType<InputManager>() == null,
        },

        // Combat — 전투 총괄(BattleManager: 플레이어 엔티티 부착·웨이브 스포너).
        // 그리드·플로우필드·둥지는 맵 소유라 씬에 남는다 — BattleManager가 Awake에서 자동 발견하고,
        // 그리드도 둥지도 없는 씬에서는 스폰이 조용히 쉰다 (아이템 테스트 씬을 오염시키지 않음).
        new Entry
        {
            scene = "Combat",
            shouldLoad = () => Object.FindFirstObjectByType<BattleManager>() == null,
        },

        // GameUI — 전투 HUD·패널 (구 UIBootstrap 항목 그대로)
        new Entry
        {
            scene = "GameUI",
            shouldLoad = () => Object.FindFirstObjectByType<GameplayHUDView>(FindObjectsInactive.Include) == null,
        },
    };

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
        if (mode == LoadSceneMode.Single) TryLoadAll();
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
