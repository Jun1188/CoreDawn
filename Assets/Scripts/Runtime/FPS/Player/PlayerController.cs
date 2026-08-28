using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CoreDawn.Entities;
using CoreDawn.Inputs;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Navigation;
using CoreDawn.UI;
using CoreDawn.Worlds;
using CoreDawn.Factory;

namespace CoreDawn.FPS
{
    /// <summary>
    /// 플레이어 아바타 조작 — 입력 파이프라인의 Player 리시버(최하위 우선순위)이자
    /// <b>운동 상태의 유일한 생산자</b>.
    ///
    /// 구조
    ///   입력(폴링+라우팅) → 이동 FSM(<see cref="IPlayerLocomotionState"/>) → 강체 속도
    ///                     → <see cref="PlayerMotionState"/> 게시
    ///                     → PlayerCameraRig / CameraMotionManager / WeaponMotionManager 가 구독
    ///
    /// 이전 구조와의 차이
    ///   - 속도를 매 프레임 통째로 덮어쓰던 <c>rb.linearVelocity = target</c> 폐기.
    ///     가속/마찰/공중 가속으로 <b>운동량</b>이 생기고 유지된다.
    ///   - 접지 판정이 "y속도 ≈ 0" 꼼수에서 스피어캐스트 + 경사각 판정으로 바뀜.
    ///     경사면 위에서는 중력을 끄고 속도를 지면 평면에 눕혀 미끄러지지 않는다.
    ///   - 카메라 피치/FOV/눈높이는 PlayerCameraRig가 전담. 여기서는 요(yaw)만 돌리고
    ///     시점 델타를 게시한다 — 무기 스웨이가 구 입력 시스템(Input.GetAxisRaw)을
    ///     직접 읽던 우회로를 끊기 위함.
    ///   - 앉기/슬라이딩 추가. 콜라이더 높이·눈높이는 DOTween 한 곡선에서 같이 유도되므로
    ///     몸과 시야가 절대 어긋나지 않는다.
    ///
    /// HP/감지/전투는 같은 GO의 Player(엔티티) 컴포넌트 담당. 이 클래스는 조작만.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerController : MonoBehaviour, IInputReceiver, IPlayerMotionProvider
    {
        #region [1. Variables - Inspector Settings]

        [Header("Core Components")]
        public Rigidbody rb;

        [Tooltip("피치(상하 시점)를 받는 노드 = CameraHolder. 조준 방향(forward)의 기준이다.")]
        public Transform playerCamera;

        [Tooltip("비워두면 playerCamera에서 자동으로 찾는다.")]
        [SerializeField] private PlayerCameraRig cameraRig;

        [SerializeField] private CapsuleCollider bodyCollider;

        [Header("Camera & Mouse Settings")]
        public float mouseSensitivity = 1f;
        public float MAX_CAMERA_ROTATION_X = 90f;

        [Header("Movement — 속도")]
        [Tooltip("기본 보행 속도. 모든 모션 모듈의 정규화 기준값이기도 하다.")]
        public float moveSpeed = 5f;
        [SerializeField] private float sprintSpeed = 8.5f;
        [SerializeField] private float crouchSpeed = 2.4f;

        [Header("Movement — 가속 / 관성")]
        [Tooltip("지상 가속도(m/s²). 낮을수록 미끄럽고 무겁다.")]
        [SerializeField] private float groundAccel = 55f;
        [Tooltip("입력이 없을 때의 지상 감속도(m/s²).")]
        [SerializeField] private float groundFriction = 42f;
        [Tooltip("진행 방향의 반대로 입력했을 때 제동 배율. 1이면 방향 전환이 굼뜨다.")]
        [SerializeField] private float counterStrafeBoost = 1.8f;
        [Tooltip("공중 가속도. 이미 가진 속도를 넘겨주지는 않고 방향만 다듬는다.")]
        [SerializeField] private float airAccel = 30f;
        [Tooltip("공중에서 '스스로 만들어낼 수 있는' 최대 속도 배율(보행 속도 기준).")]
        [SerializeField] private float airSpeedCapScale = 1f;
        [Tooltip("공중 저항(m/s²). 0이면 운동량이 완전히 보존된다.")]
        [SerializeField] private float airDrag = 0.4f;

        [Header("Jump")]
        [Tooltip("도약 높이(m). 중력에서 초기 속도를 역산하므로 값이 그대로 높이가 된다.")]
        [SerializeField] private float jumpHeight = 1.3f;
        [Tooltip("낙하 중 중력 배율 — 뜬 순간보다 빨리 떨어져야 무게감이 산다.")]
        [SerializeField] private float fallGravityScale = 1.9f;
        [Tooltip("점프 버튼을 일찍 뗐을 때의 중력 배율 (가변 점프 높이).")]
        [SerializeField] private float lowJumpGravityScale = 2.6f;
        [Tooltip("발판을 벗어난 뒤에도 점프를 받아주는 유예(초).")]
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("착지 직전에 누른 점프를 기억하는 시간(초).")]
        [SerializeField] private float jumpBufferTime = 0.15f;

        [Header("Player Sounds")]
        [SerializeField] private AudioClip[] walkSounds; // 걷기 발소리 배열
        [SerializeField] private AudioClip[] runSounds;  // 달리기 발소리 배열
        [SerializeField] private AudioClip slideSound;    // 슬라이딩 소리
        [SerializeField] private AudioClip jumpSound;
        [SerializeField] private AudioClip landSound;

        [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.4f;
        [SerializeField, Range(0f, 1f)] private float jumpVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] private float landVolume = 0.7f;
        [SerializeField, Range(0f, 1f)] private float slideVolume = 0.6f;

        [Header("Ground Probe")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("발밑 탐지 거리(m). 너무 크면 계단 아래에서 뜬 채로 접지 판정된다.")]
        [SerializeField] private float groundProbeDistance = 0.3f;
        [Tooltip("이 각도를 넘는 면은 지면으로 치지 않는다 — 중력에 밀려 미끄러진다.")]
        [SerializeField] private float maxSlopeAngle = 50f;
        [Tooltip("경사 하강에서 공중에 뜨지 않게 지면으로 눌러주는 속도(m/s).")]
        [SerializeField] private float groundStick = 2.2f;
        [Tooltip("이 속도(m/s)로 떨어지면 착지 충격 강도가 1.0이 된다.")]
        [SerializeField] private float hardLandSpeed = 14f;

        [Header("Crouch / Slide")]
        [SerializeField] private float standHeight = 2f;
        [SerializeField] private float crouchHeight = 1.15f;
        [SerializeField] private float slideHeight = 0.95f;
        [Tooltip("자세 전환에 걸리는 시간(초). 콜라이더와 눈높이가 이 한 곡선을 공유한다.")]
        [SerializeField] private float stanceTransitionTime = 0.18f;
        [Tooltip("이 속력 이상일 때만 슬라이딩이 시작된다.")]
        [SerializeField] private float slideEnterSpeed = 5.6f;
        [Tooltip("슬라이딩 진입 시 얹어주는 속도(m/s).")]
        [SerializeField] private float slideImpulse = 4f;
        [SerializeField] private float slideMaxSpeed = 15f;
        [Tooltip("슬라이딩 감속(m/s²). 내리막 가속과 겨루는 값.")]
        [SerializeField] private float slideFriction = 5.5f;
        [Tooltip("경사에서 얻는 가속(m/s²).")]
        [SerializeField] private float slideSlopeAccel = 20f;
        [SerializeField] private float slideMinSpeed = 3.2f;
        [SerializeField] private float slideMaxDuration = 1.4f;
        [Tooltip("슬라이딩 중 방향을 틀 수 있는 각속도(도/초).")]
        [SerializeField] private float slideSteerRate = 110f;
        [SerializeField] private float slideCooldown = 0.4f;
        [Tooltip("슬라이드 점프의 도약 배율 — 연계 이동 보상.")]
        [SerializeField] private float slideJumpBoost = 1.12f;
        [Tooltip("체크하면 앉기가 토글, 해제하면 누르고 있는 동안만 앉는다.")]
        [SerializeField] private bool crouchIsToggle = false;

        [Header("Gait")]
        [Tooltip("보행 주기(Hz). 카메라 보브와 무기 보브가 이 위상을 공유한다.")]
        [SerializeField] private float stepFrequency = 1.05f;

        [Header("Gun & Combat Settings")]
        public WeaponManager weaponManager;

        // (구 "Inventory Backend" 필드 제거 — 플레이어의 Inventory 컴포넌트는 어떤 UI·시스템도
        //  읽지 않는 유령 컨테이너였다. 인벤토리의 정본은 PlayerInventoryHolder의 핫바/가방.)

        [Header("Death & Respawn")]
        [Tooltip("부활할 코어의 위치. 씬에 있는 코어 오브젝트를 할당하세요.")]
        public float respawnTime = 3f;
        private Player playerEntity;

        [Header("Damage Vignette")]
        private Vignette vignette;
        public Color damageColor = Color.red;
        public float maxVignetteIntensity = 0.5f;

        public bool IsDeadOrDying =>
        _isDead ||
        (playerEntity != null && playerEntity.IsDead);


        private bool _isDead;
        private Tween _respawnTween;
        private Tween _damageVignetteTween;

        #endregion

        #region [2. Variables - Runtime]

        public PlayerMotionState Motion { get; } = new PlayerMotionState();
        public Transform MotionRoot => transform;
        public Rigidbody Rb => rb;

        private PlayerInteractionManager interaction;

        // 이동 FSM
        private readonly GroundedState _grounded = new();
        private readonly CrouchingState _crouching = new();
        private readonly SlidingState _sliding = new();
        private readonly AirborneState _airborne = new();
        private IPlayerLocomotionState _state;

        // 입력 상태
        private Vector2 _moveInput;
        private Vector2 _lookInput;
        private bool _jumpHeld, _crouchHeld, _sprintHeld;
        private float _jumpBufferedAt = -99f;

        // 접지/자세
        private float _coyoteTimer;
        private float _groundLock;        // 점프 직후 접지 재판정 금지 구간
        private float _fallSpeed;         // 착지 충격 계산용 — 접지 직전의 y속도
        private float _groundGap;         // 발바닥↔지면 틈(m). 흡착량 산출용
        private float _slideReadyAt;
        private Vector3 _baseCenter;
        private float _eyeOffsetFromTop;  // 아티스트가 잡아둔 눈 위치를 그대로 보존
        private float _currentHeight;
        private Tween _heightTween;

        // 파생값 계산용
        private Vector3 _prevLocalVelocity;

        #endregion

        #region [3. Tuning Accessors — FSM이 읽는 창구]

        // 조준 중에는 절반 속도로 — FSM이 읽는 접근자에서 곱하므로 모든 지상 상태에 일괄 적용된다.
        // AimWeight가 0~1 연속값이라 조준을 올리고 내리는 동안 속도도 부드럽게 따라간다.
        private float AimSpeedFactor => Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(Motion.AimWeight));

        public float WalkSpeed => moveSpeed * AimSpeedFactor;
        public float SprintSpeed => sprintSpeed * AimSpeedFactor;
        public float CrouchSpeed => crouchSpeed * AimSpeedFactor;
        public float GroundAccel => groundAccel;
        public float GroundFriction => groundFriction;
        public float SlideImpulse => slideImpulse;
        public float SlideMaxSpeed => slideMaxSpeed;
        public float SlideFriction => slideFriction;
        public float SlideSlopeAccel => slideSlopeAccel;
        public float SlideMinSpeed => slideMinSpeed;
        public float SlideMaxDuration => slideMaxDuration;
        public float SlideSteerRate => slideSteerRate;
        public float SlideJumpBoost => slideJumpBoost;

        public bool CrouchHeld => _crouchHeld;
        public bool JumpHeld => _jumpHeld;
        public bool WantsSprint => _sprintHeld && _moveInput.y > 0.2f && Motion.AimWeight < 0.5f;
        public bool CanCoyoteJump => _coyoteTimer > 0f;
        public bool CanEnterSlide =>
            Motion.IsGrounded && Motion.Speed >= slideEnterSpeed && Time.time >= _slideReadyAt;

        /// <summary>월드 기준 이동 의도 방향(수평, 정규화). 입력이 없으면 zero.</summary>
        public Vector3 WishDirWorld
        {
            get
            {
                Vector3 d = transform.right * _moveInput.x + transform.forward * _moveInput.y;
                d.y = 0f;
                return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.zero;
            }
        }

        #endregion

        #region [4. Input Pipeline - IInputReceiver]

        public int Priority => InputPriority.Player;
        public bool IsInputActive => isActiveAndEnabled;

        public bool OnInput(in InputEvent e)
        {
            if (playerEntity == null)
                BindPlayerEntityIfNeeded();

            if (IsDeadOrDying)
                return false;

            // 상태가 먼저 볼 기회를 준다 (탈것/사다리 등 상태 고유 입력이 생길 자리)
            if (_state != null && _state.HandleInput(this, e)) return true;

            switch (e.Id)
            {
                case InputActionId.Jump:
                    if (e.Phase == InputActionPhase.Performed)
                    {
                        _jumpHeld = true;
                        _jumpBufferedAt = Time.time;   // 선입력 — 착지 직전 입력도 살린다
                        return true;
                    }
                    if (e.Phase == InputActionPhase.Canceled) _jumpHeld = false;
                    return false;

                case InputActionId.Crouch:
                    if (e.Phase == InputActionPhase.Performed)
                    {
                        _crouchHeld = crouchIsToggle ? !_crouchHeld : true;
                        return true;
                    }
                    if (e.Phase == InputActionPhase.Canceled && !crouchIsToggle) _crouchHeld = false;
                    return false;

                case InputActionId.Sprint:
                    if (e.Phase == InputActionPhase.Performed) { _sprintHeld = true; return true; }
                    if (e.Phase == InputActionPhase.Canceled) _sprintHeld = false;
                    return false;

                // 손 채굴이 생기면서 E도 눌림 상태형이 됐다 — 뗀 시점을 받아야 홀드가 끊긴다.
                // 누름(Performed)의 처리는 아래 이산 입력 switch에 그대로 남긴다.
                case InputActionId.Interact:
                    if (e.Phase == InputActionPhase.Canceled) { interaction?.EndHold(); return false; }
                    break;
            }

            if (e.Phase != InputActionPhase.Performed) return false;

            switch (e.Id)
            {
                case InputActionId.Interact:
                    TryInteract();
                    return true;

                // 열기만 담당 — 인벤이 열려 있으면 인벤 팝업(상위 우선순위)이 먼저 가로채 닫는다.
                // 어느 UI 체계(UITK/uGUI)로 여는지는 GameScreens의 정책 — 여기는 모른다.
                case InputActionId.ToggleInventory:
                    GameScreens.OpenInventory();
                    return true;

                // 맵(M)도 같은 규칙 — 열려 있으면 맵 팝업이 상위에서 가로채 닫는다.
                case InputActionId.ToggleMap:
                    GameScreens.OpenWorldMap();
                    return true;
            }
            return false;
        }

        #endregion

        #region [5. Unity Lifecycle]

        /// <summary>
        /// 몸통 콜라이더에 씌우는 <b>마찰 0</b> 재질 — 벽이나 적에 몸이 붙어 끼이는 것을 막는다.
        ///
        /// 이 컨트롤러는 이동을 속도로 직접 제어하고 지면 감속도 groundFriction이 따로 하므로,
        /// 콜라이더 마찰은 이동에 아무 도움이 되지 않는다. 하는 일은 벽에 밀착했을 때 몸을
        /// 붙잡아 미끄러져 빠져나오지 못하게 만드는 것뿐이다(재질을 비워두면 Unity 기본값 0.6).
        /// Minimum 조합이라 상대가 어떤 재질이든 접점 마찰은 0이 된다.
        ///
        /// 접지 중에는 중력을 끄므로(<see cref="UpdateGroundState"/>) 비탈에서 미끄러지지 않는다.
        /// </summary>
        private static PhysicsMaterial slipperyBody;

        private static PhysicsMaterial SlipperyBody()
        {
            if (slipperyBody == null)
            {
                slipperyBody = new PhysicsMaterial("Player (Frictionless)")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            return slipperyBody;
        }

        private void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (bodyCollider == null) bodyCollider = GetComponent<CapsuleCollider>();
            if (cameraRig == null && playerCamera != null) cameraRig = playerCamera.GetComponent<PlayerCameraRig>();

            // 자식 카메라가 물리 스텝 사이에서 튀지 않도록 — 스냅 현상의 주범
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            if (bodyCollider != null)
            {
                standHeight = bodyCollider.height;      // 씬에 세팅된 값을 정본으로 삼는다
                _baseCenter = bodyCollider.center;
                _currentHeight = standHeight;
                bodyCollider.sharedMaterial = SlipperyBody();
            }

            if (playerCamera != null)
                _eyeOffsetFromTop = playerCamera.localPosition.y - (_baseCenter.y + standHeight * 0.5f);

            Motion.ReferenceSpeed = moveSpeed;
            Motion.GroundNormal = Vector3.up;

            _state = _grounded;
            _state.Enter(this, PlayerLocomotion.Grounded);
            Motion.Locomotion = PlayerLocomotion.Grounded;

            BindDamageVignette();
        }

        private void Start()
        {
            // 게임플레이 진입 기준 상태 — 이전 씬에서 창이 열린 채 넘어왔을 수 있으므로
            // 커서 소유권 계수도 여기서 0으로 되돌린다 (UICursor 머리말 참고)
            UICursor.ResetLocked();

            if (InputManager.Instance != null) InputManager.Instance.Register(this);
            else Debug.LogError("[PlayerController] 씬에 InputManager가 없습니다.", this);

            interaction = GetComponent<PlayerInteractionManager>();
            if (interaction == null)
                Debug.LogWarning("[PlayerController] PlayerInteractionManager가 없어 E 상호작용이 비활성입니다.", this);

            // 시작 시 핫바 활성 슬롯의 무기를 장착 — 장착 브리지는 핫바 컨트롤러가 유일 소유
            if (HotbarController.Instance != null)
                HotbarController.Instance.EquipFromActiveSlot();


            if(playerEntity == null)
                BindPlayerEntityIfNeeded();
        }

        private void OnEnable()
        {
            _state = _grounded;
            Motion.Locomotion = PlayerLocomotion.Grounded;
            Motion.Stance = PlayerStance.Stand;

            if (InputManager.Instance != null)
                InputManager.Instance.Register(this);

            Motion.Stepped += OnPlayerStepped;
        }

        private void OnDisable()
        {
            ReleaseHeldInputs();
            _heightTween?.Kill();
            _heightTween = null;

            // 접지 중에는 중력을 끄고 다니므로, 이 컴포넌트가 꺼진 채 남으면 몸이 허공에 뜬다.
            // 앉은 상태로 꺼지면 콜라이더도 낮은 채 굳는다 — 둘 다 원상복구하고 나간다.
            if (rb != null) rb.useGravity = true;
            _currentHeight = standHeight;
            Motion.Stance = PlayerStance.Stand;
            Motion.CrouchWeight = 0f;
            Motion.SlideWeight = 0f;
            UpdateStanceGeometry();

            if (InputManager.Instance != null) InputManager.Instance.Unregister(this);

            Motion.Stepped -= OnPlayerStepped; // 해제

            _damageVignetteTween?.Kill();
            _damageVignetteTween = null;

            _respawnTween?.Kill();
            _respawnTween = null;
        }

        private void OnDestroy()
        {
            _heightTween?.Kill();
            Motion.ClearSubscribers();
        }

        private void Update()
        {
            if (playerEntity == null)
            BindPlayerEntityIfNeeded();

            if (_isDead) return;

            if (playerEntity != null && playerEntity.IsDead) return;

            float dt = Time.deltaTime;

            // 연속 입력 폴링 — 소속 맵이 비활성(팝업 열림)이면 0이 읽힌다
            if (InputManager.Instance != null)
            {
                _moveInput = InputManager.Instance.ReadValue<Vector2>(InputActionId.Move);
                _lookInput = InputManager.Instance.ReadValue<Vector2>(InputActionId.Look);
            }

            HandleLook(dt);
            RefreshKinematics(dt);
            UpdateStride(dt);
            UpdateEyeHeight();

            _state?.Tick(this, dt);
        }

        private void FixedUpdate()
        {
            if (_isDead) return;

            if (playerEntity != null && playerEntity.IsDead) return;

            float dt = Time.fixedDeltaTime;

            SyncVelocity();
            UpdateGroundState(dt);
            UpdateStanceGeometry();

            _state?.FixedTick(this, dt);
        }

        #endregion

        #region [6. Look]

        private void HandleLook(float dt)
        {
            float yaw = _lookInput.x * mouseSensitivity * 0.1f;
            float pitch = _lookInput.y * mouseSensitivity * 0.1f;

            // 요는 몸이, 피치는 카메라 리그가 소유한다.
            // Transform만 돌리면 물리 엔진이 <b>자기가 기억하는 회전으로 매 스텝 되돌린다</b> —
            // 좌우로 돌려도 제자리로 끌려오는 증상이 이것이다(FreezeRotation이라 각속도는 0인데도).
            // 피치가 멀쩡한 이유는 그쪽은 Rigidbody가 없는 자식 노드이기 때문이다.
            if (Mathf.Abs(yaw) > 0f)
            {
                transform.Rotate(Vector3.up * yaw, Space.Self);
                if (rb != null) rb.rotation = transform.rotation;
            }

            if (cameraRig != null) cameraRig.ApplyLook(pitch, MAX_CAMERA_ROTATION_X);
            else if (playerCamera != null) ApplyLookFallback(pitch);

            Motion.LookDelta = new Vector2(yaw, pitch);
            // EMA — 프레임 스파이크를 먹어 스웨이가 덜덜거리지 않게
            Motion.LookDeltaSmooth = MotionSpring.Damp(Motion.LookDeltaSmooth, Motion.LookDelta / Mathf.Max(dt, 1e-4f) * 0.016f, 14f, dt);
        }

        // 카메라 리그가 없는 씬(구 프리팹)에서도 최소한 시점은 돌아가야 한다
        /// <summary>카메라 리그 — 시선 상하각의 원본. 세이브가 읽고 되돌린다.</summary>
        public PlayerCameraRig CameraRig => cameraRig;

        /// <summary>
        /// 세이브 복원 전용 — 위치와 몸 방향(요)을 되돌린다.
        ///
        /// 속도를 0으로 만드는 이유: 저장은 달리던 중에도 일어나는데, 남은 속도를 그대로 두면
        /// 불러오자마자 플레이어가 저 혼자 미끄러진다. 시선 상하각은 카메라 리그가 따로 갖고 있다.
        /// </summary>
        public void RestoreTransform(Vector3 position, float yawDegrees)
        {
            var rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            transform.SetPositionAndRotation(position, rotation);

            if (rb == null) return;

            // Rigidbody에도 같은 값을 넣어야 한다. 다음 물리 스텝이 트랜스폼을 리지드바디 값으로
            // 되돌리므로, 위치만 맞추고 회전을 빼먹으면 불러온 직후 시선이 정면(0°)으로 튄다.
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        private float _fallbackPitch;
        private void ApplyLookFallback(float pitchDelta)
        {
            _fallbackPitch = Mathf.Clamp(_fallbackPitch - pitchDelta, -MAX_CAMERA_ROTATION_X, MAX_CAMERA_ROTATION_X);
            playerCamera.localRotation = Quaternion.Euler(_fallbackPitch, 0f, 0f);
        }

        #endregion

        #region [7. Motion State 게시]

        private void SyncVelocity()
        {
            Motion.Velocity = rb.linearVelocity;
            Motion.PlanarVelocity = new Vector3(Motion.Velocity.x, 0f, Motion.Velocity.z);
            Motion.Speed = Motion.PlanarVelocity.magnitude;
        }

        private void RefreshKinematics(float dt)
        {
            SyncVelocity();

            Motion.ReferenceSpeed = Mathf.Max(0.1f, moveSpeed);
            Motion.SpeedRatio = Motion.Speed / Motion.ReferenceSpeed;
            Motion.MoveInput = _moveInput;

            Vector3 local = transform.InverseTransformDirection(Motion.Velocity);
            Vector3 rawAccel = dt > 1e-5f ? (local - _prevLocalVelocity) / dt : Vector3.zero;
            _prevLocalVelocity = local;

            Motion.LocalVelocity = local;
            // 가속도는 원본이 매우 거칠다 — 이 스무딩된 값이 카메라/무기 관성 기울기의 입력
            Motion.LocalAcceleration = MotionSpring.Damp(Motion.LocalAcceleration, rawAccel, 9f, dt);

            Vector3 wish = WishDirWorld;
            Motion.WishDirLocal = wish.sqrMagnitude > 0f ? transform.InverseTransformDirection(wish) : Vector3.zero;
            Motion.Locomotion = _state?.Id ?? PlayerLocomotion.Grounded;
        }

        private void UpdateStride(float dt)
        {
            bool striding = Motion.IsGrounded
                         && Motion.Stance != PlayerStance.Slide
                         && Motion.Speed > 0.35f
                         && _moveInput.sqrMagnitude > 0.01f;

            float targetAmp = striding ? Mathf.Clamp01(Motion.SpeedRatio) : 0f;
            Motion.StrideAmplitude = MotionSpring.Damp(Motion.StrideAmplitude, targetAmp, 8f, dt);

            if (!striding) return;

            float freq = stepFrequency * Mathf.Lerp(0.8f, 1.4f, Mathf.Clamp01(Motion.SpeedRatio));
            if (Motion.Stance == PlayerStance.Crouch) freq *= 0.65f;

            float prev = Motion.StrideCycle;
            float next = prev + dt * freq * Mathf.PI * 2f;

            // 반주기마다 한 발 — 카메라 미세 흔들림/발소리의 공통 트리거
            if (Mathf.FloorToInt(next / Mathf.PI) != Mathf.FloorToInt(prev / Mathf.PI))
                Motion.RaiseStepped(Motion.StrideAmplitude);

            Motion.StrideCycle = Mathf.Repeat(next, Mathf.PI * 2f);
        }

        private void OnPlayerStepped(float amplitude)
        {
            // 1. 달리는 중이면 runSounds, 아니면 walkSounds 선택
            bool isRunning = Motion.SpeedRatio > 1.1f; // 기본 속도보다 빠르면 달리기
            AudioClip[] targetClips = (isRunning && runSounds != null && runSounds.Length > 0) 
                                    ? runSounds 
                                    : walkSounds;

            if (targetClips == null || targetClips.Length == 0) return;

            // 2. 랜덤 클립 선택
            AudioClip clip = targetClips[Random.Range(0, targetClips.Length)];
            float finalVolume = footstepVolume * amplitude;

            // 3. 웅크린 상태일 때는 걷기 소리를 더 작고 죽은 소리로 조절
            if (Motion.Stance == PlayerStance.Crouch)
            {
                finalVolume *= 0.4f; 
            }

            if (SoundManager.Instance != null)
                SoundManager.Instance.Play3DSFX(clip, transform.position, finalVolume);
        }

        #endregion

        #region [8. Ground & Stance]

        private void UpdateGroundState(float dt)
        {
            if (_groundLock > 0f)
            {
                _groundLock -= dt;
                Motion.IsGrounded = false;
                Motion.GroundNormal = Vector3.up;
                Motion.TimeInAir += dt;
                _fallSpeed = rb.linearVelocity.y;
                rb.useGravity = true;
                return;
            }

            bool hit = ProbeGround(out RaycastHit ground);
            float angle = hit ? Vector3.Angle(ground.normal, Vector3.up) : 90f;
            bool grounded = hit && angle <= maxSlopeAngle;

            if (grounded && !Motion.IsGrounded)
            {
                float impact = Mathf.Clamp01(-_fallSpeed / Mathf.Max(1f, hardLandSpeed));
                Motion.TimeSinceLanded = 0f;
                if (impact > 0.02f) 
                {
                    Motion.RaiseLanded(impact);

                    if (landSound != null)
                    {
                        if (SoundManager.Instance != null)
                            SoundManager.Instance.Play3DSFX(landSound, transform.position, landVolume * impact);
                    }
                }
            }

            Motion.IsGrounded = grounded;
            Motion.GroundNormal = grounded ? ground.normal : Vector3.up;
            Motion.GroundSlopeAngle = grounded ? angle : 0f;

            // 접지 중엔 중력을 끈다 — 경사면에서 스스로 미끄러지지 않고, 낙하 속도도 안 쌓인다.
            // 급경사(maxSlopeAngle 초과)는 접지로 치지 않으므로 그대로 중력에 밀려 흘러내린다.
            rb.useGravity = !grounded;

            if (grounded)
            {
                Motion.TimeInAir = 0f;
                Motion.TimeSinceLanded += dt;
                _coyoteTimer = coyoteTime;
                _fallSpeed = 0f;
            }
            else
            {
                Motion.TimeInAir += dt;
                _coyoteTimer -= dt;
                _fallSpeed = rb.linearVelocity.y;
            }
        }

        // 캐스트 결과 버퍼 — 매 FixedUpdate 할당을 피한다
        private readonly RaycastHit[] _groundHits = new RaycastHit[8];
        private readonly Collider[] _overlapBuffer = new Collider[8];

        /// <summary>
        /// 발밑 스피어캐스트. 캐스트 시작점이 자기 콜라이더 안이라 <b>자기 자신을 반드시 걸러야</b> 한다
        /// (레이어 마스크로 거르면 다른 플레이어/오브젝트까지 같이 사라진다).
        /// </summary>
        private bool ProbeGround(out RaycastHit hit)
        {
            hit = default;
            if (bodyCollider == null) return false;

            float r = bodyCollider.radius * 0.9f;
            Vector3 origin = transform.position + bodyCollider.center
                           + Vector3.up * (-bodyCollider.height * 0.5f + bodyCollider.radius);
            float dist = (bodyCollider.radius - r) + groundProbeDistance;

            int count = Physics.SphereCastNonAlloc(origin, r, Vector3.down, _groundHits, dist,
                                                   groundMask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                ref RaycastHit h = ref _groundHits[i];
                if (IsSelf(h.collider)) continue;
                if (h.normal.y <= 0.001f) continue;          // 벽면/천장은 지면이 아니다
                if (h.distance >= best) continue;
                best = h.distance;
                hit = h;
                found = true;
            }

            // 발바닥과 지면 사이의 실제 틈 — 흡착은 딱 이만큼만 한다
            _groundGap = found ? Mathf.Max(0f, best - (bodyCollider.radius - r)) : 0f;
            return found;
        }

        /// <summary>일어설 공간이 있는지 — 앉기 해제/앉은 채 점프의 전제 조건.</summary>
        public bool HasHeadroom()
        {
            if (bodyCollider == null) return true;

            float r = bodyCollider.radius * 0.9f;
            Vector3 bottom = transform.position + bodyCollider.center
                           + Vector3.up * (-bodyCollider.height * 0.5f + bodyCollider.radius);
            Vector3 standTop = transform.position + _baseCenter + Vector3.up * (standHeight * 0.5f - r - 0.02f);
            if (standTop.y <= bottom.y) return true;

            int count = Physics.OverlapCapsuleNonAlloc(bottom, standTop, r, _overlapBuffer,
                                                       groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!IsSelf(_overlapBuffer[i])) return false;

            return true;
        }

        /// <summary>플레이어 자신(또는 자식)의 콜라이더인가.</summary>
        private bool IsSelf(Collider c)
        {
            if (c == null) return true;
            if (c == bodyCollider) return true;
            if (rb != null && c.attachedRigidbody == rb) return true;
            return c.transform.IsChildOf(transform);
        }

        public void SetStance(PlayerStance stance)
        {
            if (Motion.Stance == stance) return;

            PlayerStance prev = Motion.Stance;
            Motion.Stance = stance;

            if (stance == PlayerStance.Slide && slideSound != null)
            {
                if (SoundManager.Instance != null)
                    SoundManager.Instance.Play3DSFX(slideSound, transform.position, slideVolume);
            }

            float target = stance switch
            {
                PlayerStance.Crouch => crouchHeight,
                PlayerStance.Slide => slideHeight,
                _ => standHeight
            };

            // 콜라이더 높이와 눈높이가 같은 곡선을 공유한다 — 몸과 시야가 어긋날 수 없다
            _heightTween?.Kill();
            _heightTween = DOTween.To(() => _currentHeight, v => _currentHeight = v, target, stanceTransitionTime)
                                  .SetEase(stance == PlayerStance.Slide ? Ease.OutQuint : Ease.OutCubic)
                                  .SetLink(gameObject);

            Motion.RaiseStanceChanged(prev, stance);
        }

        private void UpdateStanceGeometry()
        {
            if (bodyCollider == null) return;

            bodyCollider.height = _currentHeight;
            Vector3 c = _baseCenter;
            c.y = _baseCenter.y + (_currentHeight - standHeight) * 0.5f;   // 발바닥 위치 고정
            bodyCollider.center = c;

            Motion.CrouchWeight = Mathf.Clamp01(Mathf.InverseLerp(standHeight, crouchHeight, _currentHeight));
            Motion.SlideWeight = MotionSpring.Damp(Motion.SlideWeight, Motion.IsSliding ? 1f : 0f, 11f, Time.fixedDeltaTime);
        }

        private void UpdateEyeHeight()
        {
            if (cameraRig == null) return;
            float centerY = _baseCenter.y + (_currentHeight - standHeight) * 0.5f;
            cameraRig.SetEyeHeight(centerY + _currentHeight * 0.5f + _eyeOffsetFromTop);
        }

        public void MarkSlideCooldown() => _slideReadyAt = Time.time + slideCooldown;

        #endregion

        #region [9. Movement Primitives — FSM이 호출]

        /// <summary>지상 가감속. 입력이 없으면 마찰로 감속한다.</summary>
        public void ApplyGroundMove(float targetSpeed, float accel, float friction, float dt)
        {
            Vector3 planar = Motion.PlanarVelocity;
            Vector3 wish = WishDirWorld;

            if (wish.sqrMagnitude > 1e-4f)
            {
                float inputMag = Mathf.Clamp01(_moveInput.magnitude);
                Vector3 targetVel = wish * (targetSpeed * inputMag);

                // 진행 방향과 반대로 꺾을수록 제동을 세게 — 즉각적인 방향 전환의 손맛
                float align = planar.sqrMagnitude > 0.04f ? Vector3.Dot(planar.normalized, wish) : 1f;
                float rate = accel * Mathf.Lerp(counterStrafeBoost, 1f, Mathf.InverseLerp(-1f, 1f, align));

                planar = Vector3.MoveTowards(planar, targetVel, rate * dt);
            }
            else
            {
                planar = Vector3.MoveTowards(planar, Vector3.zero, friction * dt);
            }

            SetPlanarVelocityOnSlope(planar);
        }

        /// <summary>수평 속도를 지면 경사에 눕혀 적용한다(속력 보존 + 접지 흡착).</summary>
        public void SetPlanarVelocityOnSlope(Vector3 planar)
        {
            if (!Motion.IsGrounded)
            {
                rb.linearVelocity = new Vector3(planar.x, rb.linearVelocity.y, planar.z);
                return;
            }

            Vector3 onSlope = planar;
            if (Motion.GroundSlopeAngle > 0.5f && planar.sqrMagnitude > 1e-6f)
            {
                Vector3 projected = Vector3.ProjectOnPlane(planar, Motion.GroundNormal);
                if (projected.sqrMagnitude > 1e-6f)
                    onSlope = projected.normalized * planar.magnitude;   // 오르막에서 느려지지 않게 속력 보존
            }

            // 흡착은 "발밑에 실제로 뜬 만큼"만. 평지에서 상시 아래로 미는 힘이 없어야
            // 솔버가 매 스텝 되밀지 않고, 카메라가 미세 진동하지 않는다.
            float stick = 0f;
            if (_groundGap > 0.005f)
                stick = Mathf.Min(_groundGap / Mathf.Max(Time.fixedDeltaTime, 1e-4f), groundStick);

            rb.linearVelocity = onSlope - Vector3.up * stick;
        }

        /// <summary>공중 가속 — 이미 가진 속도는 건드리지 않고 부족한 방향만 채운다.</summary>
        public void ApplyAirMove(float dt)
        {
            Vector3 v = rb.linearVelocity;
            Vector3 planar = new Vector3(v.x, 0f, v.z);
            Vector3 wish = WishDirWorld;

            if (wish.sqrMagnitude > 1e-4f)
            {
                float cap = moveSpeed * airSpeedCapScale;
                float current = Vector3.Dot(planar, wish);
                float add = Mathf.Clamp(cap - current, 0f, airAccel * dt);
                planar += wish * add;
            }

            if (airDrag > 0f) planar = Vector3.MoveTowards(planar, Vector3.zero, airDrag * dt);

            rb.linearVelocity = new Vector3(planar.x, v.y, planar.z);
        }

        /// <summary>추가 중력 — 상승보다 하강이 빠르고, 버튼을 일찍 떼면 낮게 뛴다.</summary>
        public void ApplyAirGravity(float dt)
        {
            Vector3 v = rb.linearVelocity;
            float scale = v.y < 0f ? fallGravityScale : (_jumpHeld ? 1f : lowJumpGravityScale);
            if (scale > 1f) v.y += Physics.gravity.y * (scale - 1f) * dt;
            rb.linearVelocity = v;
        }

        public void PerformJump(float boost = 1f)
        {
            float launch = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * Mathf.Max(0.01f, jumpHeight)) * boost;

            Vector3 v = rb.linearVelocity;
            v.y = launch;
            rb.linearVelocity = v;

            rb.useGravity = true;
            Motion.IsGrounded = false;
            _groundLock = 0.12f;
            _coyoteTimer = 0f;

            Motion.RaiseJumped(launch);

            if (jumpSound != null)
            {
                if (SoundManager.Instance != null)
                    SoundManager.Instance.Play3DSFX(jumpSound, transform.position, jumpVolume);
            }
        }

        /// <summary>선입력 버퍼를 소비한다. true를 반환했을 때만 소비된다.</summary>
        public bool TryConsumeJump()
        {
            if (Time.time - _jumpBufferedAt > jumpBufferTime) return false;
            _jumpBufferedAt = -99f;
            return true;
        }

        public void ChangeState(PlayerLocomotion id)
        {
            IPlayerLocomotionState next = id switch
            {
                PlayerLocomotion.Crouching => _crouching,
                PlayerLocomotion.Sliding => _sliding,
                PlayerLocomotion.Airborne => _airborne,
                _ => _grounded
            };
            if (next == _state) return;

            PlayerLocomotion from = _state?.Id ?? PlayerLocomotion.Grounded;
            _state?.Exit(this, id);
            _state = next;
            Motion.Locomotion = id;
            _state.Enter(this, from);
        }

        #endregion

        #region [10. Interaction & External Hooks]

        /// <summary>
        /// 조준 중인 대상에 상호작용 (E). 판정은 PlayerInteractionManager.Current를 그대로 사용 —
        /// 프롬프트에 보이는 대상과 실행 대상이 항상 일치한다.
        ///
        /// 홀드형 대상(<see cref="IHoldInteractable"/> — 손 채굴)은 누른 순간에 아무 일도 하지 않고
        /// 진행만 시작한다. 같은 키에 탭과 홀드를 함께 얹으면 "눌렀다 뗐을 뿐인데 뭔가 실행됐다"가 된다.
        /// </summary>
        private void TryInteract()
        {
            if (interaction == null) return;
            if (interaction.BeginHold(this)) return;
            interaction.Current?.Interact(this);
        }

        /// <summary>화면이 열리는 순간 수평 관성 제거 — 열림 중 이동 입력은 맵 비활성으로 이미 0</summary>
        public void HaltMomentum()
        {
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            ReleaseHeldInputs();
        }

        /// <summary>
        /// 달리기를 즉시 해제한다 (사격/조준이 호출). Shift를 다시 눌러야 재개된다 —
        /// "총을 내린 채 발사"라는 모순 상태를 만들지 않기 위한 단방향 차단.
        /// </summary>
        public void SuppressSprint() => _sprintHeld = false;

        /// <summary>팝업/비활성 전환 시 눌림 상태가 남지 않게 정리.</summary>
        private void ReleaseHeldInputs()
        {
            _jumpHeld = false;
            _sprintHeld = false;
            _jumpBufferedAt = -99f;
            _moveInput = Vector2.zero;
            _lookInput = Vector2.zero;
            if (!crouchIsToggle) _crouchHeld = false;
        }

        #endregion

    #if UNITY_EDITOR
        private void OnValidate()
        {
            crouchHeight = Mathf.Clamp(crouchHeight, 0.4f, standHeight);
            slideHeight = Mathf.Clamp(slideHeight, 0.4f, crouchHeight);
            stanceTransitionTime = Mathf.Max(0.01f, stanceTransitionTime);
            stepFrequency = Mathf.Max(0.1f, stepFrequency);
        }
    #endif

    #region [Combat & VFX Methods & Respawn]

        private void PlayDamageVignette()
        {

            if (vignette == null)
                return;

            _damageVignetteTween?.Kill();

            vignette.color.Override(damageColor);
            vignette.intensity.Override(0f);

            _damageVignetteTween = DOTween.To(
                () => vignette.intensity.value,
                x => vignette.intensity.Override(x),
                maxVignetteIntensity,
                0.1f
            )
            .SetEase(Ease.OutQuad)
            .OnComplete(() =>
            {
                _damageVignetteTween = DOTween.To(
                    () => vignette.intensity.value,
                    x => vignette.intensity.Override(x),
                    0f,
                    0.3f
                )
                .SetEase(Ease.OutQuad);
            });
        }

        private void RespawnAtCore()
        {

            BindPlayerEntityIfNeeded();

            if (playerEntity == null)
                return;

            Building core = FindCore();

            if (core == null)
                return;


            if (!TryFindCoreRespawnPosition(
                    core,
                    out Vector3 respawnPosition))
                return;


            float respawnYaw = core.RotationSteps * 90f;


            rb.isKinematic = false;
            rb.useGravity = true;


            playerEntity.Revive(respawnPosition);


            RestoreTransform(
                respawnPosition,
                respawnYaw
            );


            ReleaseHeldInputs();
            _isDead = false;


            GameplayHUDView hud =
                FindFirstObjectByType<GameplayHUDView>();

            if (hud != null)
            {
                hud.RefreshPlayerHp();
                hud.HideDeathOverlay();
            }

            WeaponController weaponController =
                GetComponentInChildren<WeaponController>();

            if (weaponController != null)
                weaponController.ForceReleaseInput();
        }
        public void HandlePlayerDamaged(float oldHealth, float newHealth)
        {
            if (newHealth < oldHealth)
            {
                PlayDamageVignette();
            }
        }

        public void HandlePlayerDeath()
        {
            Debug.Log(
                "[PlayerController] ===== DEATH EVENT RECEIVED ====="
            );

            _isDead = true;

            ReleaseHeldInputs();

            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                rb.isKinematic = true;
                rb.useGravity = false;
            }

            _respawnTween?.Kill();

            _respawnTween = DOVirtual.DelayedCall(
                respawnTime,
                RespawnAtCore
            );
        }

        private void BindPlayerEntityIfNeeded()
        {
            if (playerEntity != null)
                return;

            playerEntity = GetComponent<Player>();

        }

        private Building FindCore()
        {
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null) return null;

            foreach (var b in boot.Factory.Buildings)
                if (b.IsCore && !b.IsRemoved) return b;

            return null;
        }
        private bool TryFindCoreRespawnPosition(Building core, out Vector3 respawnPosition)
        {
            respawnPosition = default;

            if (core == null)
                return false;

            GridManager gridManager = GridManager.Instance;

            if (gridManager == null)
            {
                Debug.LogError(
                    "[PlayerController] GridManager.Instance가 없습니다."
                );
                return false;
            }

            Node coreNode = gridManager.NodeFromWorldPoint(core.Owner.Position);

            if (coreNode == null)
            {
                Debug.LogError(
                    "[PlayerController] Core의 월드 위치를 Grid Node로 변환하지 못했습니다."
                );
                return false;
            }

            Vector2Int coreCell = coreNode.gridCoord;

            // 코어에서 가까운 칸부터 탐색
            for (int radius = 3; radius <= 6; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    for (int y = -radius; y <= radius; y++)
                    {
                        // 같은 반경의 사각형 테두리만 검사
                        if (Mathf.Abs(x) != radius &&
                            Mathf.Abs(y) != radius)
                            continue;

                        Vector2Int candidateCell =
                            coreCell + new Vector2Int(x, y);

                        Node candidate =
                            gridManager.GetNode(candidateCell);

                        if (candidate == null)
                            continue;
                        if (gridManager.GetTile(candidateCell) != MapTile.Ground)
                            continue;
                        if (!gridManager.IsWalkable(candidate))
                            continue;

                        respawnPosition = candidate.worldPosition;

                        Vector3 rayOrigin =
                            new(
                                candidate.worldPosition.x,
                                candidate.worldPosition.y + 10f,
                                candidate.worldPosition.z
                            );

                        if (Physics.Raycast(
                                rayOrigin,
                                Vector3.down,
                                out RaycastHit groundHit,
                                20f,
                                groundMask,
                                QueryTriggerInteraction.Ignore))
                        {
                            respawnPosition.y =
                                groundHit.point.y;
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[PlayerController] 부활 셀의 실제 바닥을 찾지 못했습니다: " +
                                $"cell={candidateCell}"
                            );

                            continue;
                        }
                        return true;
                    }
                }
            }

            Debug.LogWarning(
                "[PlayerController] Core 주변에서 부활 가능한 칸을 찾지 못했습니다."
            );

            return false;
        }

        private void BindDamageVignette()
        {
            Volume volume = FindFirstObjectByType<Volume>();

            if (volume == null)
            {
                Debug.LogWarning("[PlayerController] Volume을 찾지 못했습니다.");
                return;
            }

            if (!volume.isGlobal)
            {
                Debug.LogWarning("[PlayerController] 찾은 Volume이 Global Volume이 아닙니다.");
                return;
            }

            if (volume.profile == null)
            {
                Debug.LogWarning("[PlayerController] Global Volume Profile이 없습니다.");
                return;
            }

            if (!volume.profile.TryGet(out Vignette foundVignette))
            {
                Debug.LogWarning("[PlayerController] Global Volume Profile에서 Vignette를 찾지 못했습니다.");
                return;
            }

            vignette = foundVignette;
        }
        #endregion
    }
}
