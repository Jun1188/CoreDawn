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
    [Tooltip("뷰모델(오버레이) 카메라 — 줌 배율이 같이 적용된다. 비우면 자식에서 자동으로 찾는다.")]
    [SerializeField] private Camera overlayCamera;
    [Tooltip("비우면 자식에서 자동으로 찾는다. ADS 줌 배율을 읽어온다.")]
    [SerializeField] private WeaponADS ads;

    [Header("FOV — 전부 가로(horizontal) 기준")]
    // 세로 기준을 쓰면 울트라와이드에서 시야가 옆으로 폭주하고, 데이터 수치도 화면비에
    // 따라 다르게 느껴진다 — 이 리그의 모든 각도는 가로 기준이고 적용할 때만 세로로 변환한다.
    [Tooltip("가로 기준 FOV(도). 0이면 시작 시 카메라의 현재 값을 가로로 환산해 채택한다.")]
    [SerializeField] private float baseFov = 103f;
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

    private float _overlayBaseFov;   // 오버레이 카메라의 기준 가로 FOV — 줌 배율만 얹는다

    private void Awake()
    {
        if (targetCamera == null) targetCamera = GetComponentInChildren<Camera>(true);
        if (overlayCamera == null)
            foreach (var c in GetComponentsInChildren<Camera>(true))
                if (c != targetCamera) { overlayCamera = c; break; }
        if (ads == null) ads = GetComponentInChildren<WeaponADS>(true);

        if (baseFov <= 0f)
            baseFov = targetCamera != null
                ? Camera.VerticalToHorizontalFieldOfView(targetCamera.fieldOfView, targetCamera.aspect)
                : 103f;
        _fov = baseFov;

        if (overlayCamera != null)
            _overlayBaseFov = Camera.VerticalToHorizontalFieldOfView(overlayCamera.fieldOfView, overlayCamera.aspect);

        _eyeHeight = transform.localPosition.y;
        _provider = GetComponentInParent<IPlayerMotionProvider>();
    }

    /// <summary>가로 FOV에 줌 배율 적용 — 확대는 각도가 아니라 tan 공간에서 일어난다.</summary>
    private static float ApplyZoom(float horizontalFov, float zoom)
    {
        return 2f * Mathf.Atan(Mathf.Tan(horizontalFov * Mathf.Deg2Rad * 0.5f) / Mathf.Max(0.01f, zoom)) * Mathf.Rad2Deg;
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
    /// <summary>
    /// 세이브 복원 전용 — 시선 상하각을 저장된 값으로 되돌린다.
    /// 표시용 각도까지 함께 맞춰야 부드럽게 따라오는 보간이 로드 직후 한 번 휙 돌지 않는다.
    /// </summary>
    public void RestorePitch(float pitch)
    {
        _pitch = pitch;
        _displayPitch = pitch;
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

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
                target = Mathf.Lerp(target, ApplyZoom(baseFov, ads.AimZoom), Motion.AimWeight);   // 조준이 최우선
        }

        _fov = MotionSpring.Damp(_fov, target, fovSharpness, dt);

        // 내부는 가로 기준 — 카메라 API는 세로만 받으므로 화면비로 환산해 적용
        targetCamera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(_fov, targetCamera.aspect);

        // 오버레이(뷰모델) 카메라 — 현재 줌 배율만 뽑아 같은 비율로 조여준다.
        // 자기 기준 FOV(뷰모델 원근감)는 유지한 채 조준 확대만 함께 따라온다.
        if (overlayCamera != null)
        {
            float zoomNow = Mathf.Tan(baseFov * Mathf.Deg2Rad * 0.5f) / Mathf.Tan(_fov * Mathf.Deg2Rad * 0.5f);
            float overlayFov = ApplyZoom(_overlayBaseFov, zoomNow);
            overlayCamera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(overlayFov, overlayCamera.aspect);
        }
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
