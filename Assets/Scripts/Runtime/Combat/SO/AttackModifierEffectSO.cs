using UnityEngine;

/// <summary>
/// 주는 피해(공격) 채널 — duration 동안, 이 엔티티가 내보내는 공격 항목 중
/// <see cref="affects"/>에 든 효과 정의의 value에 ctx.Value 배율을 건다.
/// value &gt; 1 = 강화 버프, value &lt; 1 = 약화 디버프. 극성은 값이 정하고 코드는 같다.
///
/// 무엇을 증폭할지는 코드가 아니라 이 에셋의 목록이 정한다 — "피해만 강화",
/// "화상(DoT)만 강화", "넉백 강화" 같은 변종이 전부 데이터로 만들어진다.
/// 적용은 공격/발사 시점의 베이크(EffectController.BakeOutgoing)에서 일어난다 —
/// 탄이 날아가는 동안 버프가 끝나도 발사 때 배율이 유지된다.
/// </summary>
[CreateAssetMenu(fileName = "Effect_AttackModifier", menuName = "Combat/Effect/Attack Modifier")]
public class AttackModifierEffectSO : DurationEffectSO
{
    [Tooltip("증폭(또는 약화)할 효과 정의들. 비우면 아무것도 건드리지 않는 버프가 된다 — 명시적으로 채울 것.")]
    public EffectSO[] affects;

    public bool Affects(EffectSO effect)
    {
        if (affects == null || effect == null) return false;
        foreach (var e in affects)
            if (e == effect) return true;
        return false;
    }
}
