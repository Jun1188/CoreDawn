using UnityEngine;

/// <summary>
/// 정조준(ADS) 연출 모듈 — 가늠자(sightPoint)가 카메라 중앙에 오도록 오프셋을 계산하고,
/// "얼마나 조준했는지"(<see cref="AimWeight"/>)와 "원하는 FOV"(<see cref="AimFov"/>)를 게시한다.
///
/// <b>카메라 FOV를 직접 쓰지 않는다</b> — 실제 FOV 합성은 <see cref="PlayerCameraRig"/>가
/// 한 곳에서 처리한다 (이동 속도 FOV·슬라이딩 FOV와 싸우지 않기 위해서).
/// 조준 가중치는 <see cref="PlayerMotionState.AimWeight"/>로도 게시되어 카메라
/// 틸트/보브/스웨이/반동이 전부 이 값 하나를 보고 억제된다.
///
/// 조준 상태의 원본은 WeaponManager다 — 여기는 SetAiming/SetupWeapon으로 받기만 하고,
/// 밖에서 필드를 직접 찌르는 경로는 없다.
/// </summary>
public class WeaponADS : MonoBehaviour, IWeaponMotionModule
{
    [Tooltip("조준 전환 속도. 클수록 빠르게 정렬된다.")]
    [SerializeField] private float aimSpeed = 12f;

    [Tooltip("조준 시 가늠자와 눈(카메라) 사이 거리(m).")]
    [SerializeField] private float aimDistance = 0.2f;

    /// <summary>0=허리, 1=완전 조준. 카메라/무기 모듈 전체의 억제 기준.</summary>
    public float AimWeight { get; private set; }

    /// <summary>PlayerCameraRig가 읽어가는 조준 FOV — 현재 무기의 zoomFOV(무기 데이터).</summary>
    public float AimFov => zoomFov;

    /// <summary>달리기·슬라이딩 중이 아닌가 — 그때는 조준이 자동 해제된다.</summary>
    public bool IsAimAllowed
    {
        get
        {
            var m = _provider?.Motion;
            if (m == null) return true;
            return !m.IsSprinting && !m.IsSliding;
        }
    }

    public Vector3 PositionOffset { get; private set; }
    public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

    private bool isAiming;
    private float zoomFov = 50f;
    private Vector3 _targetPosOffset;
    private Quaternion _targetRotOffset = Quaternion.identity;

    private IPlayerMotionProvider _provider;

    private void Awake() => _provider = GetComponentInParent<IPlayerMotionProvider>();

    // 무기 홀더가 꺼지면 조준 가중치가 1로 굳어 카메라/스웨이가 잠긴 채 남는다 — 반드시 해제
    private void OnDisable()
    {
        isAiming = false;
        AimWeight = 0f;
        if (_provider?.Motion != null) _provider.Motion.AimWeight = 0f;
    }

    /// <summary>WeaponManager가 무기 스왑 때 호출 — 새 무기의 가늠자와 줌 FOV 등록.</summary>
    public void SetupWeapon(Transform sightPoint, float weaponZoomFov)
    {
        zoomFov = weaponZoomFov;

        // 가늠자가 없는 무기(근접 등) — 이전 무기의 정렬 오프셋을 물려받으면 안 된다
        if (sightPoint == null)
        {
            _targetPosOffset = Vector3.zero;
            _targetRotOffset = Quaternion.identity;
            return;
        }

        // 순수 오프셋을 구하려면 홀더가 원점에 있어야 한다 — WeaponManager.SwapTo가 잠시 초기화한 상태로 부른다
        Vector3 relativePos = transform.InverseTransformPoint(sightPoint.position);
        Quaternion relativeRot = Quaternion.Inverse(transform.rotation) * sightPoint.rotation;

        _targetRotOffset = Quaternion.Inverse(relativeRot);
        Vector3 rotatedSightPos = _targetRotOffset * relativePos;
        _targetPosOffset = new Vector3(0f, 0f, aimDistance) - rotatedSightPos;
    }

    /// <summary>WeaponManager가 호출 — 조준 상태 반영.</summary>
    public void SetAiming(bool aiming) => isAiming = aiming;

    private void Update()
    {
        float dt = Time.deltaTime;

        bool aiming = isAiming && IsAimAllowed;
        AimWeight = MotionSpring.Damp(AimWeight, aiming ? 1f : 0f, aimSpeed, dt);
        if (AimWeight < 0.0005f) AimWeight = 0f;

        // 모든 모듈이 참조하는 단일 소스로 게시
        var m = _provider?.Motion;
        if (m != null) m.AimWeight = AimWeight;

        PositionOffset = Vector3.Lerp(Vector3.zero, _targetPosOffset, AimWeight);
        RotationOffset = Quaternion.Slerp(Quaternion.identity, _targetRotOffset, AimWeight);
    }
}
