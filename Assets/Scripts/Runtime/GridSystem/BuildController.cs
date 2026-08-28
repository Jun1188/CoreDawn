using UnityEngine;
using UnityEngine.InputSystem;
using CoreDawn.DayTime;
using CoreDawn.Inputs;
using CoreDawn.UI;

namespace CoreDawn.Placement
{
    /// <summary>
    /// 건설 입력 어댑터 — 입력 파이프라인의 BuildTool 리시버.
    /// (설계: Input/input-pipeline-architecture.md §8)
    ///
    /// 입력 해석만 담당하고, 실제 배치/철거/프리뷰는 전부 PlacementSystem에 위임한다.
    /// PlacementSystem은 파이프라인을 모르므로 UI 버튼·테스트 코드가 같은 API를 직접 호출해도 된다.
    /// </summary>
    public class BuildController : MonoBehaviour, IInputReceiver
    {
        [SerializeField] private PlacementSystem placement;

        public int Priority => InputPriority.BuildTool;   // UI보다 아래, 플레이어보다 위
        public bool IsInputActive => isActiveAndEnabled && placement != null;

        void Awake()
        {
            if (placement == null) placement = FindFirstObjectByType<PlacementSystem>();
        }

        void Start()
        {
            // Awake 시점에는 InputManager가 아직 없을 수 있어 Start에서 등록
            if (InputManager.Instance != null) InputManager.Instance.Register(this);
            else Debug.LogError("[BuildController] 씬에 InputManager가 없습니다.", this);
        }

        void OnDisable()
        {
            if (InputManager.Instance != null) InputManager.Instance.Unregister(this);
        }

        // 밤에는 건설 금지 (낮=건설 페이즈, 밤=전투 페이즈). TimeManager 없는 씬은 항상 허용.
        private static bool BuildingAllowed =>
            TimeManager.Instance == null || TimeManager.Instance.IsBuildingAllowed;

        void Update()
        {
            // 건설 모드 도중 밤이 되면 강제 종료
            if (!BuildingAllowed && placement != null && placement.Mode != PlacementSystem.BuildMode.None)
            {
                placement.ExitMode();
                Debug.Log("[BuildController] 밤이 되어 건설 모드를 종료합니다.");
            }
        }

        public bool OnInput(in InputEvent e)
        {
            // 철거만 누름/뗌을 본다 — 클릭 한 번으로 사라지면 벨트 옆 조립기를 실수로 날린다(SCR-06).
            // 나머지 입력은 아래 Performed 가드로 내려간다.
            if (e.Id == InputActionId.Attack && placement != null
                && placement.Mode == PlacementSystem.BuildMode.Demolishing)
            {
                switch (e.Phase)
                {
                    case InputActionPhase.Started:
                        placement.BeginDemolishHold();
                        return true;
                    case InputActionPhase.Canceled:
                        placement.EndDemolishHold();
                        return true;
                    case InputActionPhase.Performed:
                        return true;   // 눌린 순간의 즉시 확정은 삼킨다 — 진행은 Update가 센다
                }
            }

            if (e.Phase != InputActionPhase.Performed) return false;

            // 모드 토글은 대기 상태에서도 받는다 — 단, 밤에는 건설/철거 진입 불가
            switch (e.Id)
            {
                case InputActionId.ToggleBuild:
                case InputActionId.ToggleDemolish:
                    if (!BuildingAllowed)
                    {
                        Debug.Log("[BuildController] 밤에는 건설할 수 없습니다. (아침까지 대기 또는 H로 전환)");
                        return true; // 신호는 소비 — 사격 등으로 새지 않게
                    }
                    if (e.Id == InputActionId.ToggleBuild)
                    {
                        placement.ExitMode();               // 진행 중 모드 정리 후
                        // 건설 메뉴는 UITK(BuildMenuView)뿐이다 — GameUI 씬이 실어 오므로 플레이어가 있는 씬이면 항상 있다.
                        // 구 uGUI 폴백(BuildMenuPopup)은 제거 — 폴백이 있으면 UI 탑재 누락이 조용히 지나간다(GameScreens와 같은 방침).
                        if (!BuildMenuView.TryToggle(placement))
                            Debug.LogWarning("[BuildController] 건설 메뉴(UITK)를 열지 못했습니다 — GameUI 씬이 탑재되지 않았습니다.");
                    }
                    else placement.ToggleDemolishMode();
                    return true;
            }

            if (placement.Mode == PlacementSystem.BuildMode.None) return false;   // 이하는 모드 활성 중에만

            switch (e.Id)
            {
                case InputActionId.Cancel:       // ESC
                case InputActionId.BuildCancel:  // 우클릭 (건설 취소 전용 — Cancel에 묶으면 우클릭이 일시정지로 샘)
                    placement.ExitMode();
                    return true;

                case InputActionId.Rotate:
                    return placement.RotatePreview();     // 배치 모드가 아니면 하류로 통과

                case InputActionId.CycleShape:
                    return placement.CycleBeltShape();    // 벨트가 아니면 하류로 통과

                case InputActionId.Attack:
                    placement.ConfirmAtAim();
                    return true;   // 모드 중 좌클릭은 항상 소비 — 사격으로 새지 않게

                case InputActionId.Reload:
                    return true;   // R키가 Rotate와 겹침 — 모드 중 재장전으로 새지 않게 소비

                case InputActionId.Aim:
                    return true;   // 우클릭이 BuildCancel과 겹침 — 모드 중 조준으로 새지 않게 소비

                case InputActionId.ToggleInventory:
                    return true;   // Global 맵이라 모드 중에도 발화 — 건설/인벤 모드 배타 유지 (나가려면 ESC)
            }
            return false;
        }
    }
}
