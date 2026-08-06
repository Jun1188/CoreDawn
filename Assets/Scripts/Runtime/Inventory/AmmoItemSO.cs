using UnityEngine;

/// <summary>
/// 탄약 아이템 — 무기·포탑이 소모하는 것. <see cref="WeaponItemSO"/>와 같은 패턴.
///
/// 명중 효과를 탄약이 갖고 포탑이 배율을 갖는 이유:
/// 탄약이 강해지면 그 탄을 쓰는 모든 포탑이 함께 강해져야 한다. 포탑마다 고정
/// 수치를 박아두면 새 탄약을 추가할 때마다 모든 포탑 수치를 다시 만져야 한다.
/// 크리스탈 탄약이 감속을 걸고 싶으면 여기 목록에 {Slow, 0.5}를 더하면 된다.
/// </summary>
[CreateAssetMenu(fileName = "NewAmmo", menuName = "Factory/AmmoItem")]
public class AmmoItemSO : ItemDataSO
{
    [Header("Ammo Specific")]
    [Tooltip("이 탄약 1발이 명중 시 일으키는 일 — 피해도 항목의 하나다: {Damage, 10}. " +
             "포탑의 damageMultiplier는 피해형(Damage·DoT) 항목에만 곱해진다 — 감속 같은 부가 효과는 그대로.")]
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
