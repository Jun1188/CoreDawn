using System;
using UnityEngine;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 공격 효과 한 항목 — "무슨 효과를(effect) 얼마나 세게(value)".
    /// 무기·탄약·몬스터·타워의 공격 정의는 전부 이 항목의 목록이다. bare 피해 필드는 없다.
    ///
    /// 역할 분담:
    ///   클래스(EffectSO 하위) = 채널(코드) — 무슨 일이 일어나는가
    ///   에셋                  = 정체성 — 중첩 키·지속시간 같은 형태. 감속과 가속처럼
    ///                           상반 용도는 같은 클래스라도 에셋을 나눠야 Refresh가 안 섞인다
    ///   value                 = 극성과 세기 — 피해량, 넉백 거리(m), 배율(감속 0.5·가속 1.3) 등
    ///                           해석은 효과가 한다. 시전측 배율은 무차별 곱이 아니라 선별적이다:
    ///                           공격 버프는 자기 affects 목록에 든 효과만(BakeOutgoing),
    ///                           포탑 damageMultiplier는 피해형(Damage·DoT)만 증폭한다
    /// </summary>
    [Serializable]
    public struct EffectEntry
    {
        public EffectSO effect;

        [Tooltip("이 효과의 크기 — 피해량·거리·배율 등, 해석은 효과가 한다.")]
        public float value;

        public EffectEntry(EffectSO effect, float value)
        {
            this.effect = effect;
            this.value = value;
        }
    }
}
