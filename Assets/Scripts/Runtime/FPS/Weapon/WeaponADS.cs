using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 정조준(ADS) 연출 모듈 — 가늠자(sightPoint)가 카메라 중앙에 오도록 무기 홀더를
/// 이동·회전시키고 FOV를 줌인한다. 다른 모션 모듈처럼 오프셋만 계산해 매니저에게 준다.
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

    [FormerlySerializedAs("camera")]
    [Tooltip("FOV 줌을 적용할 카메라.")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("비조준 시 시야각. 0이면 시작 시 카메라의 현재 FOV를 쓴다.")]
    [SerializeField] private float defaultFov = 60f;

    public Vector3 PositionOffset { get; private set; }
    public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

    private bool isAiming;
    private float zoomFov = 50f;
    private Vector3 targetPosOffset;
    private Quaternion targetRotOffset = Quaternion.identity;

    private void Awake()
    {
        if (targetCamera != null && defaultFov <= 0f) defaultFov = targetCamera.fieldOfView;
    }

    /// <summary>WeaponManager가 무기 스왑 때 호출 — 새 무기의 가늠자와 줌 FOV 등록.</summary>
    public void SetupWeapon(Transform sightPoint, float weaponZoomFov)
    {
        zoomFov = weaponZoomFov;
        if (sightPoint == null) return;

        // 주의: 홀더가 흔들림(모션 오프셋)으로 틀어진 상태면 계산이 망가진다 —
        // 매니저가 호출 직전에 홀더 transform을 원점으로 되돌려 놓는다 (SwapTo 참고)
        Vector3 relativePos = transform.InverseTransformPoint(sightPoint.position);
        Quaternion relativeRot = Quaternion.Inverse(transform.rotation) * sightPoint.rotation;

        targetRotOffset = Quaternion.Inverse(relativeRot);
        Vector3 rotatedSightPos = targetRotOffset * relativePos;
        targetPosOffset = new Vector3(0f, 0f, aimDistance) - rotatedSightPos;
    }

    /// <summary>WeaponManager가 호출 — 조준 상태 반영.</summary>
    public void SetAiming(bool aiming) => isAiming = aiming;

    private void Update()
    {
        Vector3 destPos = isAiming ? targetPosOffset : Vector3.zero;
        Quaternion destRot = isAiming ? targetRotOffset : Quaternion.identity;

        PositionOffset = Vector3.Lerp(PositionOffset, destPos, Time.deltaTime * aimSpeed);
        RotationOffset = Quaternion.Slerp(RotationOffset, destRot, Time.deltaTime * aimSpeed);

        if (targetCamera != null)
            targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView,
                isAiming ? zoomFov : defaultFov, Time.deltaTime * aimSpeed);
    }
}
