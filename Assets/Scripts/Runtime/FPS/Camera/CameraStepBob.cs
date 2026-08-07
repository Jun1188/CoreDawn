using DG.Tweening;
using UnityEngine;

/// <summary>
/// 발걸음 보브 + 착지/점프 충격 모듈.
///
/// 보브 위상은 스스로 만들지 않고 <see cref="PlayerMotionState.StrideCycle"/>을 쓴다 —
/// 무기 쪽 보브와 같은 위상을 공유하므로 손과 시야가 따로 놀지 않는다.
/// 착지 딥/점프 킥은 연속 신호가 아니라 <b>사건</b>이므로 DOTween 시퀀스로 친다.
/// </summary>
public class CameraStepBob : MonoBehaviour, ICameraMotionModule
{
    public int MotionOrder => 20;

    [Header("Walk Bob")]
    [Tooltip("상하 보브 진폭(m).")]
    [SerializeField] private float bobY = 0.022f;
    [Tooltip("좌우 보브 진폭(m).")]
    [SerializeField] private float bobX = 0.016f;
    [Tooltip("좌우 보브에 연동된 롤(도).")]
    [SerializeField] private float bobRoll = 0.55f;
    [Tooltip("앉았을 때 보브 배율.")]
    [SerializeField] private float crouchScale = 0.55f;
    [Tooltip("조준 중 보브 억제 비율.")]
    [Range(0f, 1f)][SerializeField] private float aimSuppression = 0.8f;
    [SerializeField] private float bobSharpness = 12f;

    [Header("Landing Dip")]
    [Tooltip("착지 충격 1.0에서의 최대 하강(m).")]
    [SerializeField] private float landDip = 0.13f;
    [Tooltip("착지 충격 1.0에서의 최대 피치(도).")]
    [SerializeField] private float landPitch = 4.5f;
    [SerializeField] private float landAttack = 0.07f;
    [SerializeField] private float landRelease = 0.28f;

    [Header("Jump Kick")]
    [Tooltip("도약 순간 카메라가 살짝 처지는 양(m) — 몸이 뒤에 남는 느낌.")]
    [SerializeField] private float jumpKick = 0.05f;
    [SerializeField] private float jumpAttack = 0.06f;
    [SerializeField] private float jumpRelease = 0.2f;

    [Header("Footstep Impulse")]
    [Tooltip("발이 닿을 때마다 얹는 미세 충격(m). 0이면 끔.")]
    [SerializeField] private float stepImpulse = 0.006f;
    [SerializeField] private float stepFrequency = 16f;
    [SerializeField] private float stepDamping = 0.55f;

    public Vector3 PositionOffset { get; private set; }
    public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

    private IPlayerMotionProvider _provider;
    private PlayerMotionState Motion => _provider?.Motion;

    private Vector3 _bobPos;
    private float _bobRollValue;

    private float _impactY;      // 착지 딥 / 점프 킥 (DOTween 구동)
    private float _impactPitch;
    private Sequence _impactSeq;

    private float _stepValue, _stepVelocity;   // 발걸음 스프링

    private void Awake() => _provider = GetComponentInParent<IPlayerMotionProvider>();

    private void OnEnable()
    {
        if (Motion == null) return;
        Motion.Landed += OnLanded;
        Motion.Jumped += OnJumped;
        Motion.Stepped += OnStepped;
    }

    private void OnDisable()
    {
        if (Motion != null)
        {
            Motion.Landed -= OnLanded;
            Motion.Jumped -= OnJumped;
            Motion.Stepped -= OnStepped;
        }
        _impactSeq?.Kill();
        _impactSeq = null;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        var m = Motion;
        if (m == null) return;

        float gate = 1f - aimSuppression * Mathf.Clamp01(m.AimWeight);
        float scale = m.StrideAmplitude * gate * Mathf.Lerp(1f, crouchScale, m.CrouchWeight);

        // 8자 보브 — 상하는 좌우의 두 배 주기
        float c = m.StrideCycle;
        Vector3 targetBob = new Vector3(
            Mathf.Sin(c) * bobX,
            Mathf.Sin(c * 2f) * bobY,
            0f
        ) * scale;

        _bobPos = MotionSpring.Damp(_bobPos, targetBob, bobSharpness, dt);
        _bobRollValue = MotionSpring.Damp(_bobRollValue, Mathf.Sin(c) * bobRoll * scale, bobSharpness, dt);

        MotionSpring.Step(ref _stepValue, ref _stepVelocity, stepFrequency, stepDamping, dt);

        PositionOffset = _bobPos + new Vector3(0f, _impactY + _stepValue, 0f);
        RotationOffset = Quaternion.Euler(_impactPitch, 0f, _bobRollValue);
    }

    // ── 사건 처리 ────────────────────────────────────────────────────────
    private void OnLanded(float impact)
    {
        impact = Mathf.Clamp01(impact);
        PunchImpact(-landDip * impact, landPitch * impact, landAttack, landRelease);
    }

    private void OnJumped(float launchSpeed) => PunchImpact(-jumpKick, 0f, jumpAttack, jumpRelease);

    private void OnStepped(float strength)
    {
        if (stepImpulse <= 0f) return;
        _stepVelocity += MotionSpring.SolveImpulseVelocity(-stepImpulse * strength, stepFrequency, stepDamping);
    }

    private void PunchImpact(float y, float pitch, float attack, float release)
    {
        _impactSeq?.Kill();

        var seq = DOTween.Sequence();
        seq.Append(DOTween.To(() => _impactY, v => _impactY = v, y, attack).SetEase(Ease.OutQuad));
        seq.Join(DOTween.To(() => _impactPitch, v => _impactPitch = v, pitch, attack).SetEase(Ease.OutQuad));
        seq.Append(DOTween.To(() => _impactY, v => _impactY = v, 0f, release).SetEase(Ease.OutBack));
        seq.Join(DOTween.To(() => _impactPitch, v => _impactPitch = v, 0f, release).SetEase(Ease.OutBack));
        seq.SetLink(gameObject);

        _impactSeq = seq;
    }
}
