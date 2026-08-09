using UnityEngine;

/// <summary>
/// 카메라 절차적 오프셋 합성기 — <b>RecoilHolder</b>에 붙는다.
/// 같은 GameObject에 붙은 모든 <see cref="ICameraMotionModule"/>의 오프셋을 매 LateUpdate에 합산한다.
///
/// 이 노드가 생기기 전에는 ProceduralRecoil이 RecoilHolder의 localRotation을 통째로 덮어썼기 때문에
/// 이동 기울기·보브 같은 걸 얹을 자리가 아예 없었다. 이제 반동은 여러 기여자 중 하나일 뿐이다.
/// </summary>
[DefaultExecutionOrder(50)]   // 모듈들의 Update가 끝난 뒤 합성
[DisallowMultipleComponent]
public class CameraMotionManager : MonoBehaviour
{
    [Tooltip("합성 결과 전체에 곱해지는 배율. 0이면 절차적 모션 전부 정지(시네마틱/디버그용).")]
    [Range(0f, 1f)] public float globalScale = 1f;

    [Header("안전 상한")]
    [Tooltip("합산 위치 오프셋의 최대 크기(m).")]
    [SerializeField] private float maxPositionOffset = 0.35f;
    [Tooltip("합산 회전 오프셋의 최대 각도(도).")]
    [SerializeField] private float maxRotationOffset = 30f;

    private Vector3 _originPos;
    private Quaternion _originRot;
    private ICameraMotionModule[] _modules;

    /// <summary>이번 프레임 합성 결과 — 무기 쪽에서 카메라와 위상을 맞출 때 참고한다.</summary>
    public Vector3 CompositePosition { get; private set; }
    public Quaternion CompositeRotation { get; private set; } = Quaternion.identity;

    private void Awake()
    {
        _originPos = transform.localPosition;
        _originRot = transform.localRotation;
        RebuildModules();
    }

    /// <summary>런타임에 모듈을 붙이거나 뗐다면 호출.</summary>
    public void RebuildModules()
    {
        _modules = GetComponents<ICameraMotionModule>();
        System.Array.Sort(_modules, (a, b) => a.MotionOrder.CompareTo(b.MotionOrder));
    }

    private void LateUpdate()
    {
        if (_modules == null) return;

        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;

        for (int i = 0; i < _modules.Length; i++)
        {
            // 모듈이 MonoBehaviour이고 꺼져 있으면 건너뛴다 (인스펙터 체크박스로 즉시 격리 가능)
            if (_modules[i] is Behaviour b && !b.isActiveAndEnabled) continue;
            pos += _modules[i].PositionOffset;
            rot *= _modules[i].RotationOffset;
        }

        pos = Vector3.ClampMagnitude(pos * globalScale, maxPositionOffset);
        if (globalScale < 1f) rot = Quaternion.Slerp(Quaternion.identity, rot, globalScale);
        rot = ClampAngle(rot, maxRotationOffset);

        CompositePosition = pos;
        CompositeRotation = rot;

        transform.localPosition = _originPos + pos;
        transform.localRotation = _originRot * rot;
    }

    private static Quaternion ClampAngle(Quaternion q, float maxDegrees)
    {
        q.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (Mathf.Abs(angle) <= maxDegrees || float.IsNaN(axis.x)) return q;
        return Quaternion.AngleAxis(Mathf.Clamp(angle, -maxDegrees, maxDegrees), axis);
    }
}
