using UnityEngine;

namespace CoreDawn.FPS
{
    /// <summary>
    /// 이동 관성 기울기 — 플레이어의 <b>방향성과 속도</b>를 카메라 각도로 번역하는 모듈.
    ///
    ///  · 좌우 이동  → 반대쪽으로 롤(코너를 파고드는 느낌)
    ///  · 시점 회전  → 회전 방향으로 롤(관성)
    ///  · 전후 가속  → 피치(가속하면 앞으로 숙이고, 급제동하면 들린다)
    ///  · 낙하       → 하강 속도에 비례한 미세 피치
    ///  · 슬라이딩   → 롤·피치를 크게 얹고 진행 방향으로 요를 살짝 물린다
    ///
    /// 모든 항은 <see cref="PlayerMotionState"/>에서만 읽는다 — 이 모듈은 입력도 물리도 모른다.
    /// </summary>
    public class CameraMovementTilt : MonoBehaviour, ICameraMotionModule
    {
        public int MotionOrder => 10;

        [Header("Strafe / Turn Roll")]
        [Tooltip("좌우 이동 최대 롤(도).")]
        [SerializeField] private float strafeRoll = 2.2f;
        [Tooltip("시점을 좌우로 돌릴 때 따라붙는 롤(도/60fps프레임당 1도 기준).")]
        [SerializeField] private float turnRoll = 1.6f;
        [Tooltip("시점 회전 롤이 포화되는 입력 크기(도/프레임).")]
        [SerializeField] private float turnRollSaturation = 2.5f;

        [Header("Acceleration Pitch")]
        [Tooltip("전후 가속에 따른 최대 피치(도). 양수면 가속 시 아래를 본다.")]
        [SerializeField] private float accelPitch = 1.4f;
        [Tooltip("피치가 포화되는 가속도(m/s²).")]
        [SerializeField] private float accelSaturation = 30f;

        [Header("Airborne")]
        [Tooltip("하강 속도에 비례한 피치(도).")]
        [SerializeField] private float fallPitch = 2.0f;
        [SerializeField] private float fallSaturation = 16f;

        [Header("Slide")]
        [Tooltip("슬라이딩 시 추가 롤(도). 진행 방향 기준 안쪽으로 눕는다.")]
        [SerializeField] private float slideRoll = 7f;
        [Tooltip("슬라이딩 시 추가 피치(도). 지면에 가까워진 시야를 강조.")]
        [SerializeField] private float slidePitch = 3.5f;
        [Tooltip("슬라이딩 조향 시 요(yaw)가 물리는 양(도).")]
        [SerializeField] private float slideYaw = 2.5f;

        [Header("Position Lean")]
        [Tooltip("좌우 이동에 따른 카메라 좌우 밀림(m).")]
        [SerializeField] private float lateralLean = 0.02f;
        [Tooltip("전후 가속에 따른 카메라 앞뒤 밀림(m).")]
        [SerializeField] private float forwardLean = 0.025f;

        [Header("Response")]
        [Tooltip("기울기 추종 속도. 높을수록 즉각적이고 날카롭다.")]
        [SerializeField] private float sharpness = 9f;
        [Tooltip("조준(ADS) 중 기울기 억제 비율. 1이면 완전히 잠근다.")]
        [Range(0f, 1f)][SerializeField] private float aimSuppression = 0.75f;

        public Vector3 PositionOffset { get; private set; }
        public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

        private IPlayerMotionProvider _provider;
        private PlayerMotionState Motion => _provider?.Motion;

        private Vector3 _euler;      // 현재 기울기 (pitch, yaw, roll)
        private Vector3 _pos;

        private void Awake() => _provider = GetComponentInParent<IPlayerMotionProvider>();

        private void Update()
        {
            float dt = Time.deltaTime;
            var m = Motion;
            if (m == null) return;

            float gate = 1f - aimSuppression * Mathf.Clamp01(m.AimWeight);
            float refSpeed = Mathf.Max(0.1f, m.ReferenceSpeed);

            // ── 롤: 좌우 이동 + 시점 회전 ──
            float strafeN = Mathf.Clamp(m.LocalVelocity.x / refSpeed, -1.5f, 1.5f);
            float turnN = Mathf.Clamp(m.LookDeltaSmooth.x / turnRollSaturation, -1f, 1f);
            float roll = -strafeN * strafeRoll - turnN * turnRoll;

            // ── 피치: 전후 가속 + 낙하 ──
            float accelN = Mathf.Clamp(m.LocalAcceleration.z / accelSaturation, -1f, 1f);
            float fallN = m.IsGrounded ? 0f : Mathf.Clamp(-m.Velocity.y / fallSaturation, 0f, 1f);
            float pitch = accelN * accelPitch + fallN * fallPitch;

            // ── 슬라이딩 가중 ──
            float slide = Mathf.Clamp01(m.SlideWeight);
            if (slide > 0.001f)
            {
                float steerN = Mathf.Clamp(m.LocalVelocity.x / refSpeed, -1f, 1f);
                float lateralIntent = Mathf.Clamp(m.MoveInput.x, -1f, 1f);
                roll += -(steerN + lateralIntent * 0.6f) * slideRoll * slide;
                pitch += slidePitch * slide;
                _euler.y = MotionSpring.Damp(_euler.y, lateralIntent * slideYaw * slide, sharpness, dt);
            }
            else
            {
                _euler.y = MotionSpring.Damp(_euler.y, 0f, sharpness, dt);
            }

            _euler.x = MotionSpring.Damp(_euler.x, pitch * gate, sharpness, dt);
            _euler.z = MotionSpring.Damp(_euler.z, roll * gate, sharpness, dt);

            // ── 위치 리드 ──
            Vector3 targetPos = new Vector3(
                -strafeN * lateralLean,
                0f,
                -accelN * forwardLean
            ) * gate;
            _pos = MotionSpring.Damp(_pos, targetPos, sharpness * 0.8f, dt);

            PositionOffset = _pos;
            RotationOffset = Quaternion.Euler(_euler);
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            sharpness = Mathf.Max(0.1f, sharpness);
            turnRollSaturation = Mathf.Max(0.01f, turnRollSaturation);
            accelSaturation = Mathf.Max(0.1f, accelSaturation);
            fallSaturation = Mathf.Max(0.1f, fallSaturation);
        }
    #endif
    }
}
