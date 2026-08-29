using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    public enum EffectStacking
    {
        /// <summary>같은 효과 재적용 시 남은 시간을 초기화한다 (기본 — 감속 필드 펄스 등).</summary>
        Refresh,
        /// <summary>별개 인스턴스로 중첩한다 (중첩 DoT 등).</summary>
        Stack,
    }

    /// <summary>
    /// 지속 효과의 베이스 — 지속 시간·중첩 규칙·틱 간격. 진행(남은 시간·틱 타이머)은 심 <see cref="Effects"/>의
    /// 활성 인스턴스가 갖는다: SO는 공유 에셋이라 상태를 두면 모든 대상이 공유해 버린다.
    /// </summary>
    public abstract class DurationEffectSO : EffectSO
    {
        [Header("지속")]
        [Tooltip("지속 시간(초). 0 이하 = 영구(대상이 죽을 때까지) — 웨이브 버프 등.")]
        public float duration = 3f;

        [Tooltip("같은 효과가 다시 적용될 때: Refresh = 남은 시간 초기화, Stack = 별개로 중첩.")]
        public EffectStacking stacking = EffectStacking.Refresh;

        /// <summary>틱 간격(초). 0 이하면 틱 없음 (시작/종료만 있는 상태 효과 — 감속 등).</summary>
        public virtual float TickInterval => 0f;

        internal override EffectSpec BuildSpec()
            => new EffectSpec(SpecId, Kind, duration, stacking == EffectStacking.Stack, TickInterval);
    }
}
