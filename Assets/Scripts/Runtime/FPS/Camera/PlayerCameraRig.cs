using DG.Tweening;
using UnityEngine;

/// <summary>
/// 카메라 리그 — <b>CameraHolder</b>에 붙는다. 시선의 "골격"만 담당한다.
///
///   Player            요(yaw)                    ← PlayerController
///   └ CameraHolder    피치 + 눈높이 + FOV        ← 이 컴포넌트
///     └ RecoilHolder  절차적 오프셋 합성          ← CameraMotionManager
///       ├ Main Camera 충격 흔들림                ← CameraShakeManager
///       └ Weapon_Holder 무기 모션                ← WeaponMotionManager
///
/// FOV 소유권을 여기로 일원화한 것이 요점이다. 이전에는 WeaponADS가 카메라 FOV를 직접
/// 덮어써서 이동 속도 기반 FOV나 슬라이딩 연출을 얹을 자리가 없었다. 이제 ADS는
/// "원하는 FOV와 가중치"만 게시하고, 합성은 여기서 한 번에 한다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerCameraRig : MonoBehaviour
{
    [Header("References")]
    [Tooltip("비우면 자식에서 자동으로 찾는다.")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("비우면 자식에서 자동으로 찾는다. ADS FOV 요청을 읽어온다.")]
    [SerializeField] private WeaponADS ads;

    [Header("FOV")]
    [Tooltip("0이면 시작 시 카메라의 현재 FOV를 기준값으로 채택한다.")]
    [SerializeField] private float baseFov = 0f;
    [Tooltip("보행 속도를 넘어선 만큼 벌어지는 추가 FOV(도). 속도감의 핵심.")]
    [SerializeField] private float speedFovGain = 9f;
    [Tooltip("슬라이딩 중 추가로 벌어지는 FOV(도).")]
    [SerializeField] private float slideFovGain = 10f;
    [Tooltip("FOV 추종 속도.")]
    [SerializeField] private float fovSharpness = 7f;
    [Tooltip("점프/착지 순간 FOV를 한 번 튕겨주는 크기(도).")]
    [SerializeField] private float fovPunchOnLand = 5f;
    [SerializeField] private float fovPunchOnSlide = 6f;

    [Header("Pitch")]
    [Tooltip("피치 입력을 스프링으로 부드럽게 따라간다. 0이면 즉시 반응(로우 인풋).")]
    [SerializeField] private float pitchSmoothing = 0f;

    /// <summary>현재 피치(도). 위를 보면 음수 — Unity 관례 그대로.</summary>
    public float Pitch => _pitch;
    public Camera Camera => targetCamera;

    private IPlayerMotionProvider _provider;
    private PlayerMotionState Motion => _provider?.Motion;

    private float _pitch;
    private float _displayPitch;
    private float _eyeHeight;
    private bool _eyeInitialized;

    private float _fov;
    private float _fovPunch;
    private Tween _fovPunchTween;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = GetComponentInChildren<Camera>(true);
        if (ads == null) ads = GetComponentInChildren<WeaponADS>(true);

        if (baseFov <= 0f) baseFov = targetCamera != null ? targetCamera.fieldOfView : 70f;
        _fov = baseFov;

        _eyeHeight = transform.localPosition.y;
        _provider = GetComponentInParent<IPlayerMotionProvider>();
    }

    private void OnEnable()
    {
        if (Motion == null) return;
        Motion.Landed += OnLanded;
        Motion.SlideStarted += OnSlideStarted;
    }

    private void OnDisable()
    {
        if (Motion != null)
        {
            Motion.Landed -= OnLanded;
            Motion.SlideStarted -= OnSlideStarted;
        }
        _fovPunchTween?.Kill();
        _fovPunchTween = null;
    }

    /// <summary>PlayerController가 매 프레임 호출. pitchDelta는 "위로 본 각도"(입력 부호 그대로).</summary>
    public void ApplyLook(float pitchDelta, float maxPitch)
    {
        _pitch = Mathf.Clamp(_pitch - pitchDelta, -maxPitch, maxPitch);
    }

    /// <summary>자세 전환 곡선에서 유도된 눈높이(로컬 Y). 콜라이더와 같은 값에서 나온다.</summary>
    public void SetEyeHeight(float localY)
    {
        _eyeHeight = localY;
        _eyeInitialized = true;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;

        // ── 피치 ──
        _displayPitch = pitchSmoothing > 0f
            ? MotionSpring.Damp(_displayPitch, _pitch, pitchSmoothing, dt)
            : _pitch;
        transform.localRotation = Quaternion.Euler(_displayPitch, 0f, 0f);

        // ── 눈높이 ──
        if (_eyeInitialized)
        {
            Vector3 p = transform.localPosition;
            p.y = _eyeHeight;
            transform.localPosition = p;
        }

        // ── FOV ──
        UpdateFov(dt);
    }

    private void UpdateFov(float dt)
    {
        if (targetCamera == null) return;

        float target = baseFov + _fovPunch;

        if (Motion != null)
        {
            // 보행 속도를 넘어선 초과분만 FOV로 환산 — 걷는 동안엔 화면이 흔들리지 않는다
            float over = Mathf.Clamp01((Motion.Speed - Motion.ReferenceSpeed) / Mathf.Max(1f, Motion.ReferenceSpeed));
            target += over * speedFovGain + Motion.SlideWeight * slideFovGain;

            if (ads != null && Motion.AimWeight > 0.001f)
                target = Mathf.Lerp(target, ads.AimFov, Motion.AimWeight);   // 조준이 최우선
        }

        _fov = MotionSpring.Damp(_fov, target, fovSharpness, dt);
        targetCamera.fieldOfView = _fov;
    }

    // ── 이산 사건 연출 (DOTween) ─────────────────────────────────────────
    private void OnLanded(float impact) => PunchFov(-fovPunchOnLand * impact, 0.09f, 0.22f);
    private void OnSlideStarted(float entrySpeed) => PunchFov(fovPunchOnSlide, 0.07f, 0.3f);

    private void PunchFov(float amount, float attack, float release)
    {
        if (Mathf.Abs(amount) < 0.01f) return;

        _fovPunchTween?.Kill();
        _fovPunchTween = DOTween.Sequence()
            .Append(DOTween.To(() => _fovPunch, v => _fovPunch = v, amount, attack).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => _fovPunch, v => _fovPunch = v, 0f, release).SetEase(Ease.OutSine))
            .SetLink(gameObject);
    }
}
