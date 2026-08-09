using UnityEngine;

/// <summary>
/// 무기 절차적 오프셋 합성기 — Weapon_Holder에 붙는다.
/// 같은 GameObject의 모든 <see cref="IWeaponMotionModule"/> 오프셋을 LateUpdate에 합산한다.
/// 카메라 쪽 <see cref="CameraMotionManager"/>와 동일한 계약이다.
/// 모듈이 transform을 직접 만지면 서로 덮어쓰므로, 쓰기는 여기 한 곳뿐이다.
/// </summary>
[DefaultExecutionOrder(50)]   // 모듈들의 Update가 끝난 뒤 합성
[DisallowMultipleComponent]
public class WeaponMotionManager : MonoBehaviour
{
    [Tooltip("합성 결과 전체 배율. 0이면 무기 절차적 모션 정지(디버그/컷신용).")]
    [Range(0f, 1f)] public float globalScale = 1f;

    [Header("안전 상한")]
    [SerializeField] private float maxPositionOffset = 0.5f;

    private Vector3 _originPos;
    private Quaternion _originRot;
    private IWeaponMotionModule[] _modules;

    private void Awake()
    {
        _originPos = transform.localPosition;
        _originRot = transform.localRotation;
        RebuildModules();
    }

    /// <summary>런타임에 모듈을 붙이거나 뗐다면 호출.</summary>
    public void RebuildModules() => _modules = GetComponents<IWeaponMotionModule>();

    private void LateUpdate()
    {
        if (_modules == null) return;

        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;

        foreach (var module in _modules)
        {
            // 인스펙터에서 체크를 끄면 그 모듈만 즉시 격리된다
            if (module is Behaviour b && !b.isActiveAndEnabled) continue;
            pos += module.PositionOffset;
            rot *= module.RotationOffset;
        }

        pos = Vector3.ClampMagnitude(pos * globalScale, maxPositionOffset);
        if (globalScale < 1f) rot = Quaternion.Slerp(Quaternion.identity, rot, globalScale);

        transform.localPosition = _originPos + pos;
        transform.localRotation = _originRot * rot;
    }
}
