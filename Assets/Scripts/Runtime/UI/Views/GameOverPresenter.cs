using System.Collections;
using UnityEngine;

/// <summary>
/// 게임오버 화면을 띄우는 쪽. GameUI_UITK 루트(항상 활성)에 붙는다.
///
/// <b>왜 패널이 직접 구독하지 않는가</b> — 패널 호스트는 프리팹에서 꺼져 있어
/// OnEnable이 돌지 않는다. 꺼진 오브젝트는 자기가 열려야 할 순간을 알 수 없으므로,
/// 항상 켜져 있는 이 컴포넌트가 대신 듣는다.
///
/// <see cref="BattleManager"/>는 씬마다 새로 생기고 게임오버 신호도 인스턴스 이벤트라,
/// 한 번 찾고 마는 방식으로는 씬을 옮긴 뒤 영영 붙지 않는다. 붙을 때까지 매 프레임
/// 다시 찾는다 — GameplayHUDView가 코어·플레이어 엔티티를 대하는 방식과 같은 규칙이다.
/// </summary>
[DefaultExecutionOrder(100)]
public class GameOverPresenter : MonoBehaviour
{
    [Tooltip("코어가 터진 뒤 화면이 뜨기까지의 시간(초). 폭발과 파괴음을 보고 들을 틈을 준다.")]
    [SerializeField] float presentDelay = 1.5f;

    BattleManager bound;

    void Update()
    {
        // 씬이 바뀌면 이전 BattleManager는 파괴돼 fake-null이 되므로 여기서 자동으로 다시 붙는다
        if (bound == null) TryBind();
    }

    void OnDisable()
    {
        if (bound == null) return;
        bound.GameOver -= OnGameOver;
        bound = null;
    }

    void TryBind()
    {
        var bm = BattleManager.Instance;
        if (bm == null) return;

        bound = bm;
        bm.GameOver += OnGameOver;

        // 이미 끝난 세계에 들어온 경우 — 게임오버 상태로 저장된 세이브를 연 것이다.
        // RestoreGameOver는 사망 연출이 로드할 때마다 다시 돌지 않도록 이벤트를 재발화하지
        // 않으므로(BattleManager), 여기서 직접 확인하지 않으면 UI 없는 죽은 세계가 된다.
        // 연출을 기다릴 이유가 없으니 지연 없이 바로 띄운다.
        if (bm.IsGameOver) GameScreens.OpenGameOver();
    }

    void OnGameOver()
    {
        if (isActiveAndEnabled) StartCoroutine(PresentAfterDelay());
        else GameScreens.OpenGameOver();
    }

    IEnumerator PresentAfterDelay()
    {
        // Realtime이어야 한다 — 대기 중 다른 창이 timeScale을 0으로 만들면 영영 깨어나지 않는다
        yield return new WaitForSecondsRealtime(presentDelay);
        GameScreens.OpenGameOver();
    }
}
