using UnityEngine;

/// <summary>
/// 효과 적용 한 건의 맥락 — 누가(Source), 얼마의 크기로(Value), 어디를(HitPoint) 때렸는가.
///
/// Value는 시전측 데이터(EffectEntry.value × 시전측 배율)에서 오고, 해석은 효과가 한다:
/// 피해량(Damage)·밀려나는 거리(Knockback)·이동속도 배율(MoveSpeed) 등.
/// 효과 SO는 공유 에셋이라 시전자별 수치를 담을 수 없다는 게 이 구조의 핵심 제약 —
/// 정의(에셋)는 행동과 형태만 갖고, 크기는 전부 맥락으로 흘러 들어온다.
/// </summary>
public readonly struct EffectContext
{
    /// <summary>시전자. 출처 없는 피해(환경·구 TakeDamage 경로)면 null.</summary>
    public readonly Entity Source;

    /// <summary>이 효과의 크기 — EffectEntry.value × 시전측 배율. 해석은 효과가 한다.</summary>
    public readonly float Value;

    /// <summary>명중 지점 (넉백 방향·타격 이펙트용).</summary>
    public readonly Vector3 HitPoint;

    public EffectContext(Entity source, float value, Vector3 hitPoint = default)
    {
        Source = source;
        Value = value;
        HitPoint = hitPoint;
    }
}
