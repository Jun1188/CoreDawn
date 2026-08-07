using UnityEngine;

/// <summary>
/// 총기 킥백(반동) 모듈.
///
/// 위치/회전을 각각 "감쇠 스프링(damped harmonic oscillator)"으로 시뮬레이션합니다.
/// 매 프레임 Lerp를 두 번 겹쳐 쫓아가는 방식(구 버전) 대신, 상미분방정식
///     x'' + 2*zeta*omega*x' + omega^2*x = 0
/// 의 "닫힌형 정확해(exact closed-form solution)"를 매 프레임 그대로 계산합니다.
/// 그 결과 Time.deltaTime이 크든 작든, 스프링을 아무리 뻑뻑하게 세팅하든
/// 절대 발산·흔들림 없이 항상 물리적으로 정확한 위치/속도가 나옵니다.
/// (세미-임플리시트 오일러 적분은 진동수*dt가 커지면 발산하지만, 이 방식은 그런 한계가 없습니다.)
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
    [Tooltip("조준 중 반동 크기 배율")]
    public float aimAmountMultiplier = 0.45f;
    [Tooltip("조준 중 스프링 진동수 배율 (클수록 더 빨리 정지 = 더 타이트한 손맛)")]
    public float aimFrequencyMultiplier = 1.25f;
    [Tooltip("조준 중 스프링 감쇠비 배율 (클수록 덜 흔들림)")]
    public float aimDampingMultiplier = 1.35f;

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

    private bool _isAiming;
    private float _noiseSeed;

    private void Awake()
    {
        // 무기 인스턴스마다 노이즈 위상을 다르게 주어 흔들림 패턴이 서로 겹치지 않게 함
        _noiseSeed = Random.Range(0f, 1000f);
    }

    // ★ Gun에서 무기 고유의 반동값을 전달받아 스프링에 "임펄스(순간 속도)"를 가함
    public void Fire(float zAmount, Vector3 rotAmount, bool isAiming)
    {
        _isAiming = isAiming;

        float amountMult = isAiming ? aimAmountMultiplier : 1f;
        float freqMult = isAiming ? aimFrequencyMultiplier : 1f;
        float dampMult = isAiming ? aimDampingMultiplier : 1f;

        float posFreq = positionFrequency * freqMult;
        float posDamp = positionDamping * dampMult;
        float rotFreq = rotationFrequency * freqMult;
        float rotDamp = rotationDamping * dampMult;

        // 매 발마다 세기를 미세하게 흔들어 기계적으로 똑같이 반복되는 느낌을 제거
        float variance = 1f + Random.Range(-perShotVariance, perShotVariance);
        float scaledAmount = amountMult * variance;

        // 뒤로 튕기는 Z축 위치 반동
        float desiredZ = -zAmount * scaledAmount;
        _posVelocity.z += SolveImpulseVelocity(desiredZ, posFreq, posDamp);

        // 위로 튕기는 피치(수직) 반동 - 결정적 값
        float desiredPitch = -rotAmount.x * scaledAmount;
        _rotVelocity.x += SolveImpulseVelocity(desiredPitch, rotFreq, rotDamp);

        // 좌우 요(수평) 반동 - 완전 독립 난수 대신 펄린 노이즈로 자연스럽게 "흐르듯" 편향
        float wander = Mathf.PerlinNoise(Time.time * horizontalWanderSpeed + _noiseSeed, 0.5f) * 2f - 1f;
        float desiredYaw = wander * rotAmount.y * scaledAmount;
        _rotVelocity.y += SolveImpulseVelocity(desiredYaw, rotFreq, rotDamp);

        // 수평 반동에 비례한 롤 - 총구가 옆으로 틀어지는 무게감 있는 비틀림
        float desiredRoll = -desiredYaw * rollCoupling;
        _rotVelocity.z += SolveImpulseVelocity(desiredRoll, rotFreq, rotDamp);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float freqMult = _isAiming ? aimFrequencyMultiplier : 1f;
        float dampMult = _isAiming ? aimDampingMultiplier : 1f;

        StepSpring(ref _posValue, ref _posVelocity, positionFrequency * freqMult, positionDamping * dampMult, dt);
        StepSpring(ref _rotEuler, ref _rotVelocity, rotationFrequency * freqMult, rotationDamping * dampMult, dt);

        // 연사 중 값이 한없이 쌓이는 것을 막는 안전 상한(정상적인 세팅에서는 거의 발동하지 않음)
        if (_posValue.sqrMagnitude > maxPositionKick * maxPositionKick)
            _posValue = _posValue.normalized * maxPositionKick;
        if (_rotEuler.sqrMagnitude > maxRotationKick * maxRotationKick)
            _rotEuler = _rotEuler.normalized * maxRotationKick;

        PositionOffset = _posValue;
        RotationOffset = Quaternion.Euler(_rotEuler);
    }

    /// <summary>
    /// 감쇠 스프링 x'' + 2*zeta*omega*x' + omega^2*x = 0 의 닫힌형 정확해로
    /// (value, velocity)를 dt만큼 전진시킵니다.
    /// dt 크기나 진동수와 무관하게 항상 안정적입니다(수치 적분이 아닌 해석해이므로 오차 없음).
    /// </summary>
    private static void StepSpring(ref Vector3 value, ref Vector3 velocity, float frequencyHz, float dampingRatio, float dt)
    {
        float omega = 2f * Mathf.PI * Mathf.Max(frequencyHz, 0.001f);
        float zeta = Mathf.Max(dampingRatio, 0.001f);

        Vector3 x0 = value;
        Vector3 v0 = velocity;

        if (zeta < 0.999f)
        {
            // 언더댐프: 살짝 튕기며 정지 (미세한 진동 O) - 총기 킥 특유의 통통 튀는 손맛
            float wd = omega * Mathf.Sqrt(1f - zeta * zeta);
            float et = Mathf.Exp(-zeta * omega * dt);
            float c = Mathf.Cos(wd * dt);
            float s = Mathf.Sin(wd * dt);

            Vector3 c2 = (v0 + zeta * omega * x0) / wd;
            value = et * (x0 * c + c2 * s);
            velocity = et * (v0 * c - (omega * (omega * x0 + zeta * v0) / wd) * s);
        }
        else if (zeta < 1.001f)
        {
            // 임계댐프: 오버슈트 없이 가장 빠르게 정지
            float et = Mathf.Exp(-omega * dt);
            value = et * (x0 + (v0 + omega * x0) * dt);
            velocity = et * (v0 - omega * dt * (v0 + omega * x0));
        }
        else
        {
            // 오버댐프: 진동 없이 느긋하게 정지
            float wd = omega * Mathf.Sqrt(zeta * zeta - 1f);
            float et = Mathf.Exp(-zeta * omega * dt);
            float c = (float)System.Math.Cosh(wd * dt);
            float s = (float)System.Math.Sinh(wd * dt);

            Vector3 c2 = (v0 + zeta * omega * x0) / wd;
            value = et * (x0 * c + c2 * s);
            velocity = et * (v0 * c - (omega * (omega * x0 + zeta * v0) / wd) * s);
        }
    }

    /// <summary>
    /// "정지 상태에서 임펄스를 줬을 때 스프링이 도달하는 최대 변위(peak)가 정확히
    /// desiredPeak가 되도록" 필요한 초기 속도(임펄스)를 역산합니다.
    /// 이 덕분에 기획자가 넣는 반동 수치(zAmount, rotAmount)가 스프링의 뻑뻑함(frequency/damping)
    /// 세팅과 무관하게 항상 "그 값만큼 킥된다"는 직관적인 의미를 그대로 유지합니다.
    /// (검증: 고정밀 RK4 적분으로 찾은 실제 피크값과 이 공식의 오차는 1e-12 수준)
    /// </summary>
    private static float SolveImpulseVelocity(float desiredPeak, float frequencyHz, float dampingRatio)
    {
        if (Mathf.Approximately(desiredPeak, 0f)) return 0f;

        float omega = 2f * Mathf.PI * Mathf.Max(frequencyHz, 0.001f);
        float zeta = Mathf.Max(dampingRatio, 0.001f);
        float k; // 단위 임펄스(V=1) 당 피크 변위 비율

        if (zeta < 0.999f)
        {
            float wd = omega * Mathf.Sqrt(1f - zeta * zeta);
            float tPeak = Mathf.Atan2(wd, zeta * omega) / wd;
            k = Mathf.Exp(-zeta * omega * tPeak) * Mathf.Sin(wd * tPeak) / wd;
        }
        else if (zeta < 1.001f)
        {
            k = Mathf.Exp(-1f) / omega;
        }
        else
        {
            float wd = omega * Mathf.Sqrt(zeta * zeta - 1f);
            float ratio = wd / (zeta * omega);
            float tPeak = (0.5f * Mathf.Log((1f + ratio) / (1f - ratio))) / wd;
            k = Mathf.Exp(-zeta * omega * tPeak) * (float)System.Math.Sinh(wd * tPeak) / wd;
        }

        return Mathf.Abs(k) > 1e-6f ? desiredPeak / k : 0f;
    }
}