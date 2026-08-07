using UnityEngine;

/// <summary>
/// 무기 모션 합성기 — 같은 오브젝트의 모션 모듈(스웨이·킥백·ADS)들이 각자 계산한
/// 오프셋을 합산해 홀더 transform에 한 번만 쓴다. 모듈이 transform을 직접 만지면
/// 서로 덮어쓰므로, 쓰기는 여기 한 곳뿐이다 (모듈은 읽기 전용 오프셋만 노출).
/// </summary>
public class WeaponMotionManager : MonoBehaviour
{
    private Vector3 originPos;
    private Quaternion originRot;
    private IWeaponMotionModule[] modules; // 인터페이스는 직렬화 불가 — 매번 수집

    private void Awake()
    {
        originPos = transform.localPosition;
        originRot = transform.localRotation;
        modules = GetComponents<IWeaponMotionModule>();
    }

    private void LateUpdate()
    {
        if (modules == null) return;

        Vector3 finalPos = originPos;
        Quaternion finalRot = originRot;

        foreach (var module in modules)
        {
            finalPos += module.PositionOffset;
            finalRot *= module.RotationOffset;
        }

        transform.localPosition = finalPos;
        transform.localRotation = finalRot;
    }
}