using UnityEngine;

public class WeaponADS : MonoBehaviour, IWeaponMotionModule
{
    public bool isAiming;
    public float aimSpeed = 12f;
    public float aimDistance = 0.2f;

    private Vector3 _targetPosOffset;
    private Quaternion _targetRotOffset = Quaternion.identity;

    public Vector3 PositionOffset { get; private set; }
    public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

    public new Camera camera;
    public float defaultFov;
    public float targetFov;
    // ★ 수정됨: WeaponManager에서 무기를 스왑할 때 호출하여 새 무기의 sightPoint 등록
    public void SetupWeapon(Transform newSightPoint)
    {
        if (newSightPoint == null) return;

        // 주의: 현재 Sway나 Kickback 때문에 Weapon_Holder가 틀어져 있으면 계산이 망가짐.
        // 순수 오프셋을 구하기 위해 Manager가 계산 시점에 transform을 잠시 초기화했다고 가정 (아래 Manager 코드 참고)
        Vector3 relativePos = transform.InverseTransformPoint(newSightPoint.position);
        Quaternion relativeRot = Quaternion.Inverse(transform.rotation) * newSightPoint.rotation;

        _targetRotOffset = Quaternion.Inverse(relativeRot);
        Vector3 rotatedSightPos = _targetRotOffset * relativePos;
        _targetPosOffset = new Vector3(0, 0, aimDistance) - rotatedSightPos;
    }

    private void Update()
    {
        Vector3 destPos = isAiming ? _targetPosOffset : Vector3.zero;
        Quaternion destRot = isAiming ? _targetRotOffset : Quaternion.identity;

        PositionOffset = Vector3.Lerp(PositionOffset, destPos, Time.deltaTime * aimSpeed);
        RotationOffset = Quaternion.Slerp(RotationOffset, destRot, Time.deltaTime * aimSpeed);

        camera.fieldOfView = Mathf.Lerp(camera.fieldOfView, isAiming ? targetFov : defaultFov, Time.deltaTime * aimSpeed);
    }
}