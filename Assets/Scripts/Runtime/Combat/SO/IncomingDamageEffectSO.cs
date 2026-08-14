using UnityEngine;

/// <summary>
/// 받는 피해 채널 — duration 동안 이 엔티티가 받는 피해에 ctx.Value 배율을 건다.
/// value &lt; 1 = 방어 버프, value &gt; 1 = 취약 디버프. 극성은 값이 정하고 코드는 같다.
/// 적용 지점은 피해 수렴점(Entity.ReceiveDamage — EffectController.IncomingDamageMultiplier, 곱 집계).
/// </summary>
[CreateAssetMenu(fileName = "Effect_IncomingDamage", menuName = "Combat/Effect/Incoming Damage")]
public class IncomingDamageEffectSO : DurationEffectSO
{
}
