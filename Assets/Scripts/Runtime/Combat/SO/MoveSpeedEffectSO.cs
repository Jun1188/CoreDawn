using UnityEngine;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 이동 속도 채널 — duration 동안 이동 속도에 ctx.Value 배율을 건다.
    /// value &lt; 1 = 감속(감속 필드), value &gt; 1 = 가속. 극성은 값이 정하고 코드는 같다.
    /// 감속·가속 용도는 에셋을 나눠야 한다 — 중첩 키(Refresh)가 에셋 단위라, 같은 에셋을
    /// 공유하면 재적용이 서로의 값을 덮어쓴다.
    /// 집계(가장 강한 감속 × 가장 강한 가속)는 EffectController가 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect_MoveSpeed", menuName = "Combat/Effect/Move Speed")]
    public class MoveSpeedEffectSO : DurationEffectSO
    {
    }
}
