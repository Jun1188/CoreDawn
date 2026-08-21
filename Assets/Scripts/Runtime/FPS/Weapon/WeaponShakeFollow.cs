using UnityEngine;

/// <summary>
/// 조준 중 무기를 카메라 셰이크에 잠그는 모듈 — Weapon_Holder 에 붙는다.
///
/// 셰이크는 ShakeHolder(카메라의 부모)만 흔든다. 평소(힙)에는 무기가 형제라
/// 셰이크를 안 받아 화면에서 살짝 노는 생동감이 있지만, 조준 중에는 그 어긋남이
/// "가늠자가 눈에서 미끄러지는" 것으로 보인다. 그래서 조준 가중치(AimWeight)만큼
/// 셰이크 오프셋을 그대로 따라간다 — 정조준에서는 카메라와 한 몸, 힙에서는 예전 그대로.
///
/// 실행 순서: CameraShakeManager(0) → 이 모듈(10) → WeaponMotionManager(50),
/// 전부 LateUpdate — 같은 프레임의 셰이크 값을 읽어야 한 프레임 늦게 따라오지 않는다.
/// </summary>
[DefaultExecutionOrder(10)]
public class WeaponShakeFollow : MonoBehaviour, IWeaponMotionModule
{
    public Vector3 PositionOffset { get; private set; }
    public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

    private IPlayerMotionProvider _provider;

    private void Awake() => _provider = GetComponentInParent<IPlayerMotionProvider>();

    private void LateUpdate()
    {
        var shake = CameraShakeManager.Instance;
        float w = _provider?.Motion?.AimWeight ?? 0f;
        if (shake == null || w <= 0.0005f)
        {
            PositionOffset = Vector3.zero;
            RotationOffset = Quaternion.identity;
            return;
        }

        PositionOffset = shake.CurrentPositionOffset * w;
        RotationOffset = Quaternion.Slerp(Quaternion.identity, shake.CurrentRotationOffset, w);
    }

    private void OnDisable()
    {
        PositionOffset = Vector3.zero;
        RotationOffset = Quaternion.identity;
    }
}
