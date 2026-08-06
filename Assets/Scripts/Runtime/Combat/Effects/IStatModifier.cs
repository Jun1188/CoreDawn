/// <summary>
/// 전투 스탯을 건드리는 지속 효과가 구현한다 — EffectController가 활성 효과의
/// 곱을 집계해 AttackMultiplier / IncomingDamageMultiplier로 노출한다.
///
/// 감속(최솟값)과 달리 곱연산인 이유: 버프는 서로 다른 출처(장비·오라·물약류)가
/// 함께 작용하는 게 자연스럽고, 같은 효과의 자기 중첩은 stacking=Refresh가 이미 막는다.
/// </summary>
public interface IStatModifier
{
    /// <summary>주는 피해 배율 (1 = 변화 없음).</summary>
    float AttackMultiplier { get; }

    /// <summary>받는 피해 배율 (0.8 = 20% 경감, 1.5 = 50% 추가 피해).</summary>
    float IncomingDamageMultiplier { get; }
}
