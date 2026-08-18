using UnityEngine;

/// <summary>
/// 카메라 반동 — <see cref="ICameraMotionModule"/>.
///
/// 이전에는 RecoilHolder의 localRotation을 통째로 덮어썼기 때문에 이동 기울기·보브 같은
/// 다른 카메라 모션을 같은 노드에 얹을 수 없었다. 지금은 오프셋만 내놓고
/// <see cref="CameraMotionManager"/>가 합성한다.
///
/// 적분 방식도 Lerp 2단 중첩에서 감쇠 스프링 해석해(<see cref="MotionSpring"/>)로 교체 —
/// 무기 킥백(WeaponKickback)과 같은 수식을 쓰므로 손과 시야의 반동 톤이 일치한다.
/// 호출은 WeaponManager의 발사 팬아웃(OnWeaponFired → FireRecoil)에서 온다.
/// </summary>
public class ProceduralRecoil : MonoBehaviour, ICameraMotionModule
{
    public int MotionOrder => 30;

    [Header("Recoil Spring")]
    [Tooltip("반동 스프링 진동수(Hz). 높을수록 '탕!' 하고 날카롭게 튄다.")]
    public float frequency = 13f;
    [Tooltip("감쇠비. 1 미만이면 살짝 튕겼다 정지.")]
    public float damping = 0.62f;

    [Header("Recovery")]
    [Tooltip("반동이 원점으로 되돌아가는 추가 복원력. 연사 중 누적 각을 서서히 밀어 내린다.")]
    public float returnSpeed = 6f;
    [Tooltip("연사 중 누적 반동 각의 상한(도).")]
    public float maxAccumulated = 12f;

    [Header("ADS")]
    [Tooltip("조준 중 반동 크기 배율.")]
    [Range(0f, 1f)] public float aimScale = 0.55f;

    [Header("Movement Coupling")]
    [Tooltip("이동 속도가 빠를수록 반동이 커지는 비율. 0이면 속도 무관.")]
    [Range(0f, 1f)] public float speedInfluence = 0.35f;

    public Vector3 PositionOffset => Vector3.zero;
    public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

    private Vector3 _value;      // 오일러(도)
    private Vector3 _velocity;

    private IPlayerMotionProvider _provider;
    private PlayerMotionState Motion => _provider?.Motion;

    private void Awake() => _provider = GetComponentInParent<IPlayerMotionProvider>();

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        MotionSpring.Step(ref _value, ref _velocity, MotionSpring.Visible(frequency, dt), damping, dt);

        // 스프링 위에 얹는 완만한 복원 — 연사로 쌓인 각을 서서히 되돌린다
        _value = MotionSpring.Damp(_value, Vector3.zero, returnSpeed, dt);

        if (_value.sqrMagnitude > maxAccumulated * maxAccumulated)
            _value = _value.normalized * maxAccumulated;

        RotationOffset = Quaternion.Euler(_value);
    }

    /// <summary>WeaponManager의 발사 팬아웃이 호출. 화면이 위로 튀고(X), 좌우로 흔들리며(Y), 비틀린다(Z).</summary>
    public void FireRecoil(float recoilX, float recoilY, float recoilZ)
    {
        float scale = 1f;

        var m = Motion;
        if (m != null)
        {
            scale *= Mathf.Lerp(1f, aimScale, Mathf.Clamp01(m.AimWeight));
            // 달리면서 쏘면 더 크게 튄다 — 이동과 사격이 실제로 맞물리는 지점
            scale *= 1f + speedInfluence * Mathf.Clamp01(m.SpeedRatio);
        }

        // 임펄스 역산도 Update와 같은 진동수를 써야 "딱 그만큼 튄다"가 성립한다
        float f = MotionSpring.Visible(frequency, Time.deltaTime);

        _velocity.x += MotionSpring.SolveImpulseVelocity(-recoilX * scale, f, damping);
        _velocity.y += MotionSpring.SolveImpulseVelocity(Random.Range(-recoilY, recoilY) * scale, f, damping);
        _velocity.z += MotionSpring.SolveImpulseVelocity(Random.Range(-recoilZ, recoilZ) * scale, f, damping);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        frequency = Mathf.Max(0.1f, frequency);
        damping = Mathf.Max(0.01f, damping);
        returnSpeed = Mathf.Max(0f, returnSpeed);
        maxAccumulated = Mathf.Max(0.1f, maxAccumulated);
    }
#endif
}
