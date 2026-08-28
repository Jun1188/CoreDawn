using UnityEngine;

namespace CoreDawn.FPS
{
    /// <summary>
    /// 무기 스웨이 — <see cref="WeaponMotionManager"/>가 합성하는 <b>연속</b> 모션 모듈.
    ///
    /// ■ 이번 개편의 핵심
    ///   입력을 더 이상 스스로 읽지 않는다. 예전에는 <c>Input.GetAxisRaw("Mouse X")</c>로
    ///   구 입력 시스템을 직접 긁고, <c>Physics.Raycast</c>로 접지를 따로 판정하고,
    ///   Rigidbody를 <c>GetComponentInParent</c>로 찾아 속도를 읽었다 — 게임의 입력 파이프라인과
    ///   플레이어 FSM을 전부 우회하는 세 갈래 뒷문이었다.
    ///   지금은 <see cref="PlayerMotionState"/> 하나만 읽는다. 팝업이 열려 입력이 끊기면
    ///   시점 델타가 0으로 오고, 앉거나 슬라이딩하면 그 상태가 그대로 반영된다.
    ///
    /// ■ 담당 범위
    ///   시점 스웨이 / 이동 관성 / 보행 보브. 자세 포즈와 착지 충격 같은 <b>이산</b> 연출은
    ///   <see cref="WeaponStancePose"/>가 맡는다.
    /// </summary>
    public class HandSway : MonoBehaviour, IWeaponMotionModule
    {
        // ─── Look Sway ────────────────────────────────────────────────────
        [Header("Look Sway (시점)")]
        [Tooltip("시점 이동에 따른 최대 위치 스웨이(m).")]
        public float posSwayAmount = 0.055f;
        [Tooltip("시점 이동에 따른 최대 회전 스웨이(도).")]
        public float rotSwayAmount = 4.5f;
        [Tooltip("스웨이가 포화되는 시점 속도(도/60fps프레임).")]
        public float swaySaturation = 2.5f;
        [Tooltip("스웨이 추종 속도.")]
        public float swaySharpness = 9f;

        // ─── Movement Sway ────────────────────────────────────────────────
        [Header("Movement Sway (이동 관성)")]
        public bool moveSwayEnabled = true;
        [Tooltip("속도에 따른 위치 관성 최대치. (X 좌우, Y 상하, Z 앞뒤)")]
        public Vector3 movePosSway = new Vector3(0.022f, 0.018f, 0.03f);
        [Tooltip("속도에 따른 회전 관성 최대치(도). (X 앞뒤 기울기, Y 좌우 회전, Z 좌우 롤)")]
        public Vector3 moveRotSway = new Vector3(3.2f, 1.4f, 2.6f);
        [Tooltip("가속도에 반응하는 추가 위치 관성 — 출발/정지 순간의 '툭' 하는 맛.")]
        public Vector3 accelPosSway = new Vector3(0.012f, 0.01f, 0.02f);
        [Tooltip("가속도 관성이 포화되는 값(m/s²).")]
        public float accelSaturation = 30f;
        public float moveSwaySharpness = 7f;

        // ─── Bob ──────────────────────────────────────────────────────────
        [Header("Movement Bob")]
        public bool bobEnabled = true;
        [Tooltip("좌우 보브 진폭(m).")]
        public float bobAmountX = 0.011f;
        [Tooltip("상하 보브 진폭(m).")]
        public float bobAmountY = 0.008f;
        [Tooltip("보브에 연동된 롤(도).")]
        public float bobRoll = 1.1f;
        [Tooltip("앉았을 때 보브 배율.")]
        public float crouchBobScale = 0.55f;
        public float bobSharpness = 11f;

        // ─── ADS ──────────────────────────────────────────────────────────
        [Header("ADS 억제")]
        [Tooltip("조준 중 스웨이/보브를 얼마나 잠글지. 1이면 완전히 정지.")]
        [Range(0f, 1f)] public float aimSuppression = 0.85f;

        // ─── IWeaponMotionModule ──────────────────────────────────────────
        public Vector3 PositionOffset { get; private set; }
        public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

        // ─── 내부 상태 ────────────────────────────────────────────────────
        private IPlayerMotionProvider _provider;
        private PlayerMotionState Motion => _provider?.Motion;

        private Vector3 _swayPos;
        private Vector3 _swayEuler;
        private Vector3 _movePos;
        private Vector3 _moveEuler;
        private Vector3 _bobPos;
        private float _bobRollValue;

        private void Awake()
        {
            _provider = GetComponentInParent<IPlayerMotionProvider>();
            if (_provider == null)
                Debug.LogWarning("[HandSway] 상위에 PlayerController(IPlayerMotionProvider)가 없어 스웨이가 정지합니다.", this);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            var m = Motion;
            if (m == null) return;

            float gate = 1f - aimSuppression * Mathf.Clamp01(m.AimWeight);

            UpdateLookSway(m, gate, dt);
            UpdateMoveSway(m, gate, dt);
            UpdateBob(m, gate, dt);

            PositionOffset = _swayPos + _movePos + _bobPos;
            RotationOffset = Quaternion.Euler(_swayEuler + _moveEuler + new Vector3(0f, 0f, _bobRollValue));
        }

        // 시점을 돌리면 무기가 뒤늦게 끌려온다
        private void UpdateLookSway(PlayerMotionState m, float gate, float dt)
        {
            float sat = Mathf.Max(0.01f, swaySaturation);
            float nx = Mathf.Clamp(m.LookDeltaSmooth.x / sat, -1f, 1f);
            float ny = Mathf.Clamp(m.LookDeltaSmooth.y / sat, -1f, 1f);

            Vector3 targetPos = new Vector3(-nx * posSwayAmount, -ny * posSwayAmount, 0f) * gate;
            Vector3 targetEuler = new Vector3(-ny * rotSwayAmount, nx * rotSwayAmount * 0.5f, -nx * rotSwayAmount) * gate;

            _swayPos = MotionSpring.Damp(_swayPos, targetPos, swaySharpness, dt);
            _swayEuler = MotionSpring.Damp(_swayEuler, targetEuler, swaySharpness, dt);
        }

        // 몸이 움직이는 방향/가속도에 무기가 저항한다
        private void UpdateMoveSway(PlayerMotionState m, float gate, float dt)
        {
            if (!moveSwayEnabled)
            {
                _movePos = MotionSpring.Damp(_movePos, Vector3.zero, moveSwaySharpness, dt);
                _moveEuler = MotionSpring.Damp(_moveEuler, Vector3.zero, moveSwaySharpness, dt);
                return;
            }

            float refSpeed = Mathf.Max(0.1f, m.ReferenceSpeed);
            Vector3 vN = new Vector3(
                Mathf.Clamp(m.LocalVelocity.x / refSpeed, -1.5f, 1.5f),
                Mathf.Clamp(m.LocalVelocity.y / refSpeed, -1.5f, 1.5f),
                Mathf.Clamp(m.LocalVelocity.z / refSpeed, -1.5f, 1.5f));

            float sat = Mathf.Max(0.1f, accelSaturation);
            Vector3 aN = new Vector3(
                Mathf.Clamp(m.LocalAcceleration.x / sat, -1f, 1f),
                Mathf.Clamp(m.LocalAcceleration.y / sat, -1f, 1f),
                Mathf.Clamp(m.LocalAcceleration.z / sat, -1f, 1f));

            Vector3 targetPos = new Vector3(
                -vN.x * movePosSway.x - aN.x * accelPosSway.x,
                -vN.y * movePosSway.y - aN.y * accelPosSway.y,
                -vN.z * movePosSway.z - aN.z * accelPosSway.z) * gate;

            Vector3 targetEuler = new Vector3(
                 vN.z * moveRotSway.x,
                -vN.x * moveRotSway.y,
                -vN.x * moveRotSway.z) * gate;

            _movePos = MotionSpring.Damp(_movePos, targetPos, moveSwaySharpness, dt);
            _moveEuler = MotionSpring.Damp(_moveEuler, targetEuler, moveSwaySharpness, dt);
        }

        // 위상은 PlayerController가 만든 공용 StrideCycle — 카메라 보브와 어긋나지 않는다
        private void UpdateBob(PlayerMotionState m, float gate, float dt)
        {
            if (!bobEnabled)
            {
                _bobPos = MotionSpring.Damp(_bobPos, Vector3.zero, bobSharpness, dt);
                _bobRollValue = MotionSpring.Damp(_bobRollValue, 0f, bobSharpness, dt);
                return;
            }

            float scale = m.StrideAmplitude * gate * Mathf.Lerp(1f, crouchBobScale, m.CrouchWeight);
            float c = m.StrideCycle;

            Vector3 target = new Vector3(Mathf.Sin(c) * bobAmountX, Mathf.Sin(c * 2f) * bobAmountY, 0f) * scale;

            _bobPos = MotionSpring.Damp(_bobPos, target, bobSharpness, dt);
            _bobRollValue = MotionSpring.Damp(_bobRollValue, -Mathf.Sin(c) * bobRoll * scale, bobSharpness, dt);
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            posSwayAmount = Mathf.Max(0f, posSwayAmount);
            rotSwayAmount = Mathf.Max(0f, rotSwayAmount);
            swaySaturation = Mathf.Max(0.01f, swaySaturation);
            swaySharpness = Mathf.Max(0.1f, swaySharpness);
            moveSwaySharpness = Mathf.Max(0.1f, moveSwaySharpness);
            bobSharpness = Mathf.Max(0.1f, bobSharpness);
            accelSaturation = Mathf.Max(0.1f, accelSaturation);
        }
    #endif
    }
}
