using UnityEngine;
using UnityEngine.InputSystem;
using CoreDawn.Inputs;
using CoreDawn.Placement;
using CoreDawn.Pings;
using CoreDawn.UI;
using Ping = CoreDawn.Pings.Ping;   // UnityEngine.Ping과 충돌

namespace CoreDawn.Inputs
{
    /// <summary>
    /// T — 바라본 대상을 찍는다. 입력을 핑으로 바꾸는 것만 하고 표현은 <see cref="PingService"/> 구독자에게 맡긴다.
    ///
    /// T는 건설 모드에서 벨트 모양 변경(CycleShape)에도 쓰인다. 두 액션이 같은 키에 물려 있어 둘 다 발화하는데,
    /// 그쪽은 BuildTool 우선순위가 자기 이벤트를 소비하고, 여기는 건설 모드면 아예 반응하지 않는다 —
    /// 벨트 모양을 바꾸려다 벨트에 핑이 찍히면 안 된다. 창이 떠 있을 때도 마찬가지(커서가 조준이 아니다).
    ///
    /// 카메라는 GameBootstrap이 꽂는다 — Combat 씬은 플레이어를 모른다.
    /// </summary>
    public class PlayerPingInput : MonoBehaviour, IInputReceiver
    {
        [Tooltip("이 거리(m)까지 조준한다.")]
        [SerializeField] float range = 60f;

        [Tooltip("핑이 유지되는 시간(초).")]
        [SerializeField] float duration = 3f;

        [Tooltip("조준선에 대상이 없을 때(지형·벽) 그 자리에 위치 핑을 찍는다. 끄면 대상이 있을 때만 찍힌다.")]
        [SerializeField] bool pingGroundWhenNoTarget = false;

        [Tooltip("조준 카메라. Combat이 별도 씬으로 올라오므로 GameBootstrap이 꽂는다.")]
        [SerializeField] Camera aimCamera;

        [Tooltip("찍는 플레이어의 루트 — 이 아래 콜라이더(자기 몸·뷰모델)는 조준에서 뺀다. GameBootstrap이 꽂는다.")]
        [SerializeField] Transform selfRoot;

        bool registered;

        public int Priority => InputPriority.Player;
        public bool IsInputActive => isActiveAndEnabled;

        /// <summary>조준 카메라·자기 루트 주입. 인스펙터 배선이 이미 있으면 덮지 않는다 (PlacementSystem.Inject와 같은 규칙).</summary>
        public void Inject(Camera camera, Transform playerRoot)
        {
            if (aimCamera == null) aimCamera = camera;
            if (selfRoot == null) selfRoot = playerRoot;
        }

        void Update()
        {
            // InputManager는 GameBootstrap이 Systems 씬으로 얹으므로 우리보다 늦게 생길 수 있다
            if (registered || InputManager.Instance == null) return;
            InputManager.Instance.Register(this);
            registered = true;
        }

        void OnDisable()
        {
            if (!registered) return;
            InputManager.Instance?.Unregister(this);
            registered = false;
        }

        public bool OnInput(in InputEvent e)
        {
            if (e.Id != InputActionId.Ping || e.Phase != InputActionPhase.Performed) return false;

            // 건설 모드의 T는 벨트 모양 변경이고, 창이 떠 있으면 커서가 조준이 아니다
            if (PlacementSystem.BuildModeActive || UIPopup.AnyOpen) return false;

            if (PingTargeting.TryFindAimed(aimCamera, selfRoot, range, out var target, out var point))
            {
                PingService.Raise(target.PingRoot, PingKind.Look, PingSource.LocalPlayer, duration, target.PingLabel);
                return true;
            }

            if (pingGroundWhenNoTarget && aimCamera != null)
            {
                PingService.RaiseAt(point, PingKind.Marker, PingSource.LocalPlayer, duration);
                return true;
            }

            return false;
        }
    }
}
