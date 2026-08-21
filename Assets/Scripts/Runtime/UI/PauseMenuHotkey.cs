using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ESC(Cancel) → 일시정지 메뉴 열기 — 입력 파이프라인의 Fallback 리시버.
///
/// "아무도 소비하지 않은 Cancel = 열린 창 없음"이 이 파이프라인의 규약이므로, 여기까지
/// 흘러온 Cancel은 일시정지를 여는 게 맞다 (input-pipeline-architecture.md). 닫기는 각
/// 팝업(UIPopup)이 더 높은 우선순위에서 직접 처리한다 — 게임오버 화면이 Cancel을 일부러
/// 흘려보내 그 위에 일시정지가 열리는 흐름도 이 우선순위 구조가 만든다.
///
/// 구 SystemUIManager(uGUI HUD, 삭제됨)가 하던 역할의 승계.
/// GameUI_UITK 프리팹 루트에 붙어 부트스트랩 씬(GameUI.unity)과 함께 산다 —
/// PauseMenuView가 없는 씬(타이틀 등)에서는 이벤트를 소비하지 않고 흘려보낸다.
/// </summary>
public class PauseMenuHotkey : MonoBehaviour, IInputReceiver
{
    bool _registered;

    public int Priority => InputPriority.Fallback;
    public bool IsInputActive => isActiveAndEnabled;

    void Update()
    {
        // InputManager도 스스로 부팅하므로 생성 순서를 가정하지 않는다 — 생길 때까지 재시도
        if (_registered || InputManager.Instance == null) return;
        InputManager.Instance.Register(this);
        _registered = true;
    }

    void OnDestroy()
    {
        if (_registered && InputManager.Instance != null) InputManager.Instance.Unregister(this);
    }

    public bool OnInput(in InputEvent e)
    {
        if (e.Phase != InputActionPhase.Performed || e.Id != InputActionId.Cancel) return false;
        if (!PauseMenuView.ExistsInScene()) return false;   // 일시정지가 없는 씬 — 흘려보낸다

        if (PauseMenuView.IsOpen) PauseMenuView.CloseIfOpen();
        else PauseMenuView.TryOpen();
        return true;
    }
}
