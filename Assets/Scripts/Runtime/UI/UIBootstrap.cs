using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 UI(UITK) 자동 탑재 — 씬마다 프리팹을 심는 대신 UI 씬(GameUI.unity)을
/// additive로 얹는다.
///
/// 씬 방식인 이유: 프리팹 인스턴스를 씬마다 두면 새 씬을 만들 때 빠뜨릴 수 있고,
/// 게임플레이 씬 파일에 UI 오브젝트 diff가 섞여 팀 머지가 지저분해진다.
/// 여기서는 씬 파일들이 UI를 전혀 모르고, 로더가 조건에 맞으면 얹는다.
///
/// 조건: 씬에 플레이어가 있고(게임플레이 씬), 아직 UI가 없을 때.
/// Single 로드는 additive로 얹힌 UI 씬도 함께 내리므로 씬 전환마다 다시 검사한다.
/// </summary>
public static class UIBootstrap
{
    const string SceneName = "GameUI";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        // 람다가 아니라 이름 있는 메서드 — 도메인 리로드를 끈 환경(Enter Play Mode Options)에서는
        // static 구독이 플레이를 넘어 살아남으므로, 빼고 다시 걸어야 중복되지 않는다
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryLoad();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) TryLoad();
    }

    static void TryLoad()
    {
        // 이미 있음 — 씬에 직접 심어둔 경우(구 방식)도 여기서 걸려 중복되지 않는다
        if (Object.FindFirstObjectByType<GameplayHUDView>(FindObjectsInactive.Include) != null) return;

        // 플레이어 없는 씬(순수 테스트 씬)에는 얹지 않는다
        if (Object.FindFirstObjectByType<PlayerController>() == null) return;

        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        {
            Debug.LogWarning($"[UIBootstrap] '{SceneName}' 씬이 Build Settings에 없어 UI를 얹지 못함");
            return;
        }

        SceneManager.LoadScene(SceneName, LoadSceneMode.Additive);
    }
}
