using UnityEngine;

/// <summary>
/// 감속 — duration 동안 이동 속도에 배율을 건다 (감속 필드 타워 등).
/// 실제 적용은 EffectController가 활성 감속의 최솟값을 집계해
/// Entity.Update → MovementComponent.SpeedMultiplier로 밀어 넣는 경로로 이뤄진다.
/// </summary>
[CreateAssetMenu(fileName = "Effect_Slow", menuName = "Combat/Effect/Slow")]
public class SlowEffectSO : DurationEffectSO, IMoveSpeedModifier
{
    [Header("감속")]
    [Range(0f, 1f)]
    [Tooltip("이동 속도 배율. 0.5 = 절반 속도. 여러 감속이 겹치면 가장 강한(작은) 것 하나만 적용된다.")]
    public float speedMultiplier = 0.5f;

    public float SpeedMultiplier => speedMultiplier;
}
