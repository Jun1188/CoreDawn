using UnityEngine;

/// <summary>
/// 탄약 모듈 — 이 아이템이 무기·포탑의 탄으로 소모될 수 있고, 1발 명중 시 무슨 일이
/// 일어나는지를 정의한다 (구 AmmoItemSO의 대체).
///
/// 명중 효과를 탄약이 갖고 포탑이 배율을 갖는 이유: 탄약이 강해지면 그 탄을 쓰는
/// 모든 포탑이 함께 강해져야 한다. 포탑의 damageMultiplier는 피해형(Damage·DoT)
/// 항목에만 곱해진다 — 감속 같은 부가 효과는 그대로.
/// </summary>
public class AmmoModuleSO : ItemModuleSO
{
    [Tooltip("이 탄약 1발이 명중 시 일으키는 일 — 피해도 항목의 하나다: {Damage, 10}.")]
    public EffectEntry[] attackEffects;

    /// <summary>피해 항목들의 value 합 — 툴팁 표기용 (전투 계산엔 쓰지 않는다).</summary>
    public float BaseDamage
    {
        get
        {
            float sum = 0f;
            if (attackEffects != null)
                foreach (var e in attackEffects)
                    if (e.effect is DamageEffectSO) sum += e.value;
            return sum;
        }
    }
}
