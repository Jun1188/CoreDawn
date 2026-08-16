using UnityEngine;

/// <summary>
/// 피해 — 즉시 효과. 피해량 = ctx.Value (시전측 EffectEntry가 정한다).
/// 정의는 행동만 갖고 수치가 없다 — 모든 총·탄약·몬스터가 에셋 하나를 공유한다.
/// 방어 배율·저항 같은 받는 쪽 규칙은 수렴점(Entity.ReceiveDamage)에 얹는다.
/// </summary>
[CreateAssetMenu(fileName = "Effect_Damage", menuName = "Combat/Effect/Damage")]
public class DamageEffectSO : EffectSO
{
    public override void Apply(Entity target, in EffectContext ctx)
    {
        if (ctx.Value > 0f) target.ReceiveDamage(ctx.Value);
    }
}
