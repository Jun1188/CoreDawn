using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// UI 팝업 공통 베이스 — 입력 파이프라인의 Popup 계층 리시버.
/// (설계: input-pipeline-architecture.md §5)
///
/// 패널 GameObject에 부착하면:
///  - 활성화 시 UI 맵 Push → Gameplay 입력(사격/건설/이동 계열 액션) 신호 차단
///  - 열린 순서대로 depth 우선순위 부여 → Cancel(ESC)은 최상단 팝업만 닫는다
///  - 모달이면 처리하지 않은 입력도 삼켜 하위(HUD/플레이어)로 새지 않는다
/// </summary>
public abstract class UIPopup : MonoBehaviour, IInputReceiver
{
    private static int _depthCounter;
    private static int _openCount;
    private int _depth;
    private int _mapToken = -1;

    /// <summary>
    /// 지금 열려 있는 팝업이 하나라도 있는가.
    ///
    /// 창이 떠 있으면 커서는 조작용으로 풀려 있고, 그 커서가 가리키는 월드 지점은
    /// 플레이어가 겨냥한 곳이 아니다. 조준을 근거로 무언가를 보여주는 표시들
    /// (포트 흐름 등)이 이것을 보고 스스로 물러난다.
    /// </summary>
    public static bool AnyOpen => _openCount > 0;

    public int Priority => InputPriority.PopupBase + _depth;
    public bool IsInputActive => gameObject.activeInHierarchy;

    /// <summary>모달이면 처리하지 않은 입력도 전부 삼킨다. 기본값 true.</summary>
    protected virtual bool IsModal => true;

    protected virtual void OnEnable()
    {
        _openCount++;
        _depth = ++_depthCounter;   // 나중에 열린 창이 항상 위
        if (InputManager.Instance == null)
        {
            Debug.LogError("[UIPopup] 씬에 InputManager가 없습니다.", this);
            return;
        }
        InputManager.Instance.Register(this);
        _mapToken = InputManager.Instance.PushMap("UI");
    }

    protected virtual void OnDisable()
    {
        if (_openCount > 0) _openCount--;
        if (InputManager.Instance == null) return;   // 앱 종료/씬 전환 순서 가드
        InputManager.Instance.Unregister(this);
        if (_mapToken >= 0)
        {
            InputManager.Instance.PopMap(_mapToken);
            _mapToken = -1;
        }
    }

    public virtual bool OnInput(in InputEvent e)
    {
        if (e.Phase != InputActionPhase.Performed) return IsModal;

        if (e.Id == InputActionId.Cancel)
        {
            Close();
            return true;   // 최상단 팝업만 닫히는 이유 — 여기서 소비
        }
        return IsModal;
    }

    /// <summary>기본 구현은 GameObject 비활성화. 별도 정리 절차가 있으면 override.</summary>
    public virtual void Close() => gameObject.SetActive(false);
}