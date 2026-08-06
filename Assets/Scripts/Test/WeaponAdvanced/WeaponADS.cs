using UnityEngine;

/// <summary>
/// 정조준(ADS) 모듈.
///
/// 변경점: <b>더 이상 카메라 FOV를 직접 쓰지 않는다.</b> 여기서는 "얼마나 조준했는지"(<see cref="AimWeight"/>)와
/// "원하는 FOV"(<see cref="AimFov"/>)만 게시하고, 실제 FOV 합성은 <see cref="PlayerCameraRig"/>가
/// 한 곳에서 처리한다 — 이동 속도 FOV·슬라이딩 FOV와 싸우지 않기 위해서다.
///
/// 조준 가중치는 <see cref="PlayerMotionState.AimWeight"/>로도 게시된다. 카메라 틸트/보브/스웨이/반동이
/// 모두 이 값 하나를 보고 억제되므로, "조준 중엔 화면이 차분해진다"는 규칙이 전 모듈에 일관되게 적용된다.
/// </summary>
public class WeaponADS : MonoBehaviour, IWeaponMotionModule
{
    [Tooltip("WeaponController가 세팅. 달리기/슬라이딩 중에는 자동으로 해제된다.")]
    public bool isAiming;

    [Tooltip("조준 전환 속도.")]
    public float aimSpeed = 12f;

    [Tooltip("가늠자를 눈앞 어디에 둘지(m).")]
    public float aimDistance = 0.2f;

    [Tooltip("호환용 참조 — FOV 제어는 PlayerCameraRig가 소유한다.")]
    public new Camera camera;

    [Tooltip("호환용. 기준 FOV는 PlayerCameraRig의 baseFov를 쓴다.")]
    public float defaultFov = 70f;

    [Tooltip("현재 무기의 조준 FOV. WeaponController가 무기 교체 시 세팅한다.")]
    public float targetFov = 50f;

    /// <summary>0=허리, 1=완전 조준. 카메라/무기 모듈 전체의 억제 기준.</summary>
    public float AimWeight { get; private set; }

    /// <summary>PlayerCameraRig가 읽어가는 조준 FOV.</summary>
    public float AimFov => targetFov > 1f ? targetFov : defaultFov;

    /// <summary>달리기·슬라이딩 중이 아니고 조준 입력이 들어와 있는가.</summary>
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

    /// <summary>WeaponManager가 무기 교체 시 새 무기의 sightPoint를 등록한다.</summary>
    public void SetupWeapon(Transform newSightPoint)
    {
        if (newSightPoint == null) return;

        // 순수 오프셋을 구하려면 홀더가 원점에 있어야 한다 — WeaponManager.SwapTo가 잠시 초기화한 상태로 부른다
        Vector3 relativePos = transform.InverseTransformPoint(newSightPoint.position);
        Quaternion relativeRot = Quaternion.Inverse(transform.rotation) * newSightPoint.rotation;

        _targetRotOffset = Quaternion.Inverse(relativeRot);
        Vector3 rotatedSightPos = _targetRotOffset * relativePos;
        _targetPosOffset = new Vector3(0f, 0f, aimDistance) - rotatedSightPos;
    }

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
