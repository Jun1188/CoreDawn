/// <summary>
/// 이동 속도를 건드리는 지속 효과가 구현한다 — EffectController가 활성 효과 중
/// 이 값의 최솟값(가장 강한 감속)을 집계해 MoveSpeedMultiplier로 노출한다.
///
/// 곱연산 대신 최솟값인 이유: 감속 필드 두 개가 겹친 자리에서 반감×반감 = 1/4로
/// 폭주하지 않고 "가장 강한 하나"만 적용되는 쪽이 수치 조정이 쉽다.
/// </summary>
public interface IMoveSpeedModifier
{
    float SpeedMultiplier { get; }
}
