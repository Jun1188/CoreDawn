using UnityEngine;

namespace CoreDawn.FPS
{
    /// <summary>
    /// 총기 킥백(반동) 모듈.
    ///
    /// 위치/회전을 각각 감쇠 스프링으로 시뮬레이션합니다. 스프링 수식 자체는
    /// <see cref="MotionSpring"/>으로 승격되어 카메라 반동(ProceduralRecoil)·발걸음 충격과
    /// 공유됩니다 — 손과 시야의 반동 톤이 같은 곡선에서 나옵니다.
    ///
    /// 조준 보정은 이제 bool이 아니라 <see cref="PlayerMotionState.AimWeight"/>(0~1 연속값)에
    /// 비례합니다. 조준 전환 도중에 쏴도 반동 세기가 뚝 끊기지 않습니다.
    /// </summary>
    public class WeaponKickback : MonoBehaviour, IWeaponMotionModule
    {
        [Header("위치 반동 스프링 (Z축 뒤로 밀림)")]
        [Tooltip("위치 스프링의 진동수(Hz). 높을수록 더 빠르고 딱딱하게 튕깁니다.")]
        public float positionFrequency = 14f;
        [Tooltip("위치 스프링의 감쇠비. 1=오버슈트 없이 깔끔히 정지 / 1보다 작으면 살짝 튕겼다 정지 / 1보다 크면 느긋하게 정지.")]
        public float positionDamping = 0.8f;

        [Header("회전 반동 스프링 (피치/요/롤)")]
        [Tooltip("회전 스프링의 진동수(Hz).")]
        public float rotationFrequency = 11f;
        [Tooltip("회전 스프링의 감쇠비.")]
        public float rotationDamping = 0.6f;

        [Header("조준(ADS) 보정")]
        [Tooltip("조준 중 반동 크기 배율 — 절반으로 눌러 정조준 사격을 보상한다.")]
        public float aimAmountMultiplier = 0.5f;
        [Tooltip("조준 중 스프링 진동수 배율 (클수록 더 빨리 정지 = 더 타이트한 손맛)")]
        public float aimFrequencyMultiplier = 1.25f;
        [Tooltip("조준 중 스프링 감쇠비 배율 (클수록 덜 흔들림)")]
        public float aimDampingMultiplier = 1.35f;
        [Tooltip("조준 중 회전 반동(상하 피치·좌우 요·롤) 배율. 0이면 조준 중엔 뒤로만 밀린다 — " +
                 "가늠자로 겨눈 상태에서 총이 상하좌우로 튀면 조준이 아니라 난사처럼 느껴진다.")]
        [Range(0f, 1f)] public float aimRotationScale = 0f;

        [Header("디테일 (자연스러움)")]
        [Tooltip("사격마다 반동 세기에 주는 미세한 무작위 편차 비율. 기계적으로 똑같이 반복되는 느낌을 없애줍니다.")]
        [Range(0f, 0.3f)]
        public float perShotVariance = 0.08f;
        [Tooltip("수평 반동이 매번 뚝뚝 끊기지 않고 자연스럽게 좌우로 흐르듯 이어지는 속도")]
        public float horizontalWanderSpeed = 3f;
        [Tooltip("수평 반동에 연동되어 총구가 살짝 롤(Z축)되는 비율. 옆으로 튕길 때 무게감 있는 비틀림을 더해줍니다.")]
        [Range(0f, 1f)]
        public float rollCoupling = 0.35f;

        [Header("안전장치")]
        [Tooltip("연사 중 위치 반동 누적이 이 값(m)을 넘지 않도록 제한")]
        public float maxPositionKick = 0.15f;
        [Tooltip("연사 중 회전 반동 누적이 이 값(도)을 넘지 않도록 제한")]
        public float maxRotationKick = 25f;

        public Vector3 PositionOffset { get; private set; }
        public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

        // 스프링 상태(변위+속도). 회전은 오일러(도) 기준으로 적분하고 마지막에만 쿼터니언으로 변환합니다.
        private Vector3 _posValue, _posVelocity;
        private Vector3 _rotEuler, _rotVelocity;

        private float _noiseSeed;

        private IPlayerMotionProvider _provider;
        /// <summary>조준 가중치 0~1. WeaponADS가 게시한 값을 그대로 쓴다.</summary>
        private float AimWeight => _provider?.Motion != null ? Mathf.Clamp01(_provider.Motion.AimWeight) : 0f;

        private void Awake()
        {
            // 무기 인스턴스마다 노이즈 위상을 다르게 주어 흔들림 패턴이 서로 겹치지 않게 함
            _noiseSeed = Random.Range(0f, 1000f);
            _provider = GetComponentInParent<IPlayerMotionProvider>();
        }

        // ★ Gun에서 무기 고유의 반동값을 전달받아 스프링에 "임펄스(순간 속도)"를 가함
        public void Fire(float zAmount, Vector3 rotAmount, bool isAiming)
        {
            // isAiming(bool)은 호출부 호환용 — 실제 보정은 연속 가중치로 한다
            float aim = Mathf.Max(AimWeight, isAiming ? 1f : 0f);

            float amountMult = Mathf.Lerp(1f, aimAmountMultiplier, aim);
            float freqMult = Mathf.Lerp(1f, aimFrequencyMultiplier, aim);
            float dampMult = Mathf.Lerp(1f, aimDampingMultiplier, aim);

            float posFreq = positionFrequency * freqMult;
            float posDamp = positionDamping * dampMult;
            float rotFreq = rotationFrequency * freqMult;
            float rotDamp = rotationDamping * dampMult;

            // 매 발마다 세기를 미세하게 흔들어 기계적으로 똑같이 반복되는 느낌을 제거
            float variance = 1f + Random.Range(-perShotVariance, perShotVariance);
            float scaledAmount = amountMult * variance;

            // 뒤로 튕기는 Z축 위치 반동
            float desiredZ = -zAmount * scaledAmount;
            _posVelocity.z += MotionSpring.SolveImpulseVelocity(desiredZ, posFreq, posDamp);

            // 회전 반동(피치·요·롤)은 조준 중엔 따로 더 깎는다 — 뒤로 밀리는 킥은 남기되
            // 총이 상하좌우로 튀는 것은 조준을 방해한다.
            float rotScale = scaledAmount * Mathf.Lerp(1f, aimRotationScale, aim);

            // 위로 튕기는 피치(수직) 반동 - 결정적 값
            float desiredPitch = -rotAmount.x * rotScale;
            _rotVelocity.x += MotionSpring.SolveImpulseVelocity(desiredPitch, rotFreq, rotDamp);

            // 좌우 요(수평) 반동 - 완전 독립 난수 대신 펄린 노이즈로 자연스럽게 "흐르듯" 편향
            float wander = Mathf.PerlinNoise(Time.time * horizontalWanderSpeed + _noiseSeed, 0.5f) * 2f - 1f;
            float desiredYaw = wander * rotAmount.y * rotScale;
            _rotVelocity.y += MotionSpring.SolveImpulseVelocity(desiredYaw, rotFreq, rotDamp);

            // 수평 반동에 비례한 롤 - 총구가 옆으로 틀어지는 무게감 있는 비틀림
            float desiredRoll = -desiredYaw * rollCoupling;
            _rotVelocity.z += MotionSpring.SolveImpulseVelocity(desiredRoll, rotFreq, rotDamp);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float aim = AimWeight;
            float freqMult = Mathf.Lerp(1f, aimFrequencyMultiplier, aim);
            float dampMult = Mathf.Lerp(1f, aimDampingMultiplier, aim);

            MotionSpring.Step(ref _posValue, ref _posVelocity, positionFrequency * freqMult, positionDamping * dampMult, dt);
            MotionSpring.Step(ref _rotEuler, ref _rotVelocity, rotationFrequency * freqMult, rotationDamping * dampMult, dt);

            // 연사 중 값이 한없이 쌓이는 것을 막는 안전 상한(정상적인 세팅에서는 거의 발동하지 않음)
            if (_posValue.sqrMagnitude > maxPositionKick * maxPositionKick)
                _posValue = _posValue.normalized * maxPositionKick;
            if (_rotEuler.sqrMagnitude > maxRotationKick * maxRotationKick)
                _rotEuler = _rotEuler.normalized * maxRotationKick;

            PositionOffset = _posValue;
            RotationOffset = Quaternion.Euler(_rotEuler);
        }

    }
}
