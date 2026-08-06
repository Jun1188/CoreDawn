using UnityEngine;

/// <summary>
/// 효과 적용 한 건의 맥락 — 누가(Source), 얼마의 기본 수치로(Power), 어디를(HitPoint) 때렸는가.
///
/// 수치(Power)를 여기에 실어 보내는 이유: 발당 피해는 시전측 데이터
/// (GunData.damage, 탄약 피해 × 포탑 배율, CombatComponent.attackDamage)가 정하는데,
/// 효과 SO는 공유 에셋이라 시전자마다 다른 수치를 담을 수 없다.
/// 효과는 Power를 어떻게 쓸지(배율·가산)만 정의한다.
/// </summary>
public readonly struct EffectContext
{
    /// <summary>시전자. 출처 없는 피해(환경·구 TakeDamage 경로)면 null.</summary>
    public readonly Entity Source;

    /// <summary>시전측 기본 수치 — 보통 그 공격의 기본 피해량.</summary>
    public readonly float Power;

    /// <summary>명중 지점 (넉백 방향·타격 이펙트용).</summary>
    public readonly Vector3 HitPoint;

    public EffectContext(Entity source, float power, Vector3 hitPoint = default)
    {
        Source = source;
        Power = power;
        HitPoint = hitPoint;
    }
}
