using UnityEngine;

/// <summary>
/// 스탯 버프/디버프 — duration 동안 주는 피해·받는 피해에 배율을 건다.
/// 적용은 집계 지점에서 일어난다: 공격 배율은 시전 측이 Power를 계산할 때
/// (CombatComponent·총기·타워), 받는 피해 배율은 Entity.ReceiveDamage에서.
/// </summary>
[CreateAssetMenu(fileName = "Effect_StatModifier", menuName = "Combat/Effect/Stat Modifier")]
public class StatModifierEffectSO : DurationEffectSO, IStatModifier
{
    [Header("배율")]
    [Tooltip("주는 피해 배율. 1.5 = 공격력 50% 증가.")]
    public float attackMultiplier = 1f;

    [Tooltip("받는 피해 배율. 0.8 = 피해 20% 경감(방어 버프), 1.5 = 50% 추가 피해(취약 디버프).")]
    public float incomingDamageMultiplier = 1f;

    public float AttackMultiplier => attackMultiplier;
    public float IncomingDamageMultiplier => incomingDamageMultiplier;
}
