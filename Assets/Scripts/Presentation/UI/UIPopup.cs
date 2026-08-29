using UnityEngine;
using UnityEngine.InputSystem;
using CoreDawn.Inputs;

namespace CoreDawn.UI
{
    /// <summary>
    /// UI 팝업 공통 베이스 — 입력 파이프라인의 Popup 계층 리시버.
    /// (설계: input-pipeline-architecture.md §5)
    ///
    /// 패널 GameObject에 부착하면:
    ///  - 활성화 시 UI 맵 Push → Gameplay 입력(사격/건설/이동 계열 액션) 신호 차단
    ///  - 열린 순서대로 depth 우선순위 부여 → Cancel(ESC)은 최상단 팝업만 닫는다
    ///  - 모달이면 처리하지 않은 입력도 삼켜 하위(HUD/플레이어)로 새지 않는다
    ///  - 마우스 커서 해제·재잠금 (<see cref="ReleasesCursor"/>)
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

        /// <summary>커서를 풀어야 하는 창인가. 마우스를 쓰지 않는 오버레이는 false로 덮어쓴다.</summary>
        protected virtual bool ReleasesCursor => true;

        protected virtual void OnEnable()
        {
            _openCount++;
            _depth = ++_depthCounter;   // 나중에 열린 창이 항상 위
            if (ReleasesCursor) AcquireCursor();
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
            if (ReleasesCursor) ReleaseCursor();         // 아래 가드보다 먼저 — 짝이 어긋나면 커서가 샌다
            if (InputManager.Instance == null) return;   // 앱 종료/씬 전환 순서 가드
            InputManager.Instance.Unregister(this);
            if (_mapToken >= 0)
            {
                InputManager.Instance.PopMap(_mapToken);
                _mapToken = -1;
            }
        }

        // ───────────────────────── 커서 ─────────────────────────
        //
        // 창은 창 위에 겹쳐 뜬다 — 일시정지 메뉴 위의 설정 창, 세이브 슬롯 창이 그렇다.
        // 창마다 닫힐 때 무조건 커서를 다시 잠그면, 위쪽 창을 닫는 순간 아직 떠 있는
        // 아래쪽 창에서 마우스가 사라진다. 그래서 잠그고 푸는 것은 개별 창이 아니라
        // "커서를 쥔 창이 몇 개인가"가 정한다.

        // 계수는 여기서 들지 않고 UICursor에 맡긴다 — <b>커서 소유자는 하나여야 한다.</b>
        //
        // UIPopup을 상속하지 않는 자리도 커서를 쥔다: 씬 경계가 그렇다(게임플레이 진입은
        // UICursor.ResetLocked, 타이틀 진입은 ResetFree). 여기에 따로 계수를 두면 그 리셋이
        // 한쪽만 0으로 만들고 다른 쪽은 남아, 창을 다 닫아도 커서가 안 잠기거나 그 반대가 된다.
        // 같은 버그를 두 갈래에서 각각 고쳤고, 합치면서 계수가 둘이 될 뻔한 자리다.

        /// <summary>커서를 푼다. 이미 다른 창이 풀어 뒀어도 세는 것은 늘어난다.</summary>
        private static void AcquireCursor() => UICursor.Release();

        /// <summary>마지막 한 창이 닫힐 때만 다시 잠근다.</summary>
        private static void ReleaseCursor() => UICursor.Restore();

        /// <summary>
        /// 도메인 리로드를 끈 채로 플레이하면 static이 지난 세션의 값을 그대로 들고 시작한다 —
        /// 쥔 창이 하나도 없는데 세는 것만 남아 있으면 창을 닫아도 커서가 다시 잠기지 않는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _depthCounter = 0;
            _openCount = 0;
            // 커서 계수는 UICursor가 자기 것을 스스로 되돌린다
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
}
