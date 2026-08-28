using UnityEngine;
using CoreDawn.Entities;

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
    /// 지속 효과의 베이스 — Apply가 즉시 실행 대신 대상의 EffectController에 등록한다.
    ///
    /// SO는 공유 에셋이므로 진행 상태(남은 시간 등)를 여기 두면 모든 대상이 상태를 공유해버린다.
    /// 상태는 EffectController의 ActiveEffect 인스턴스가 갖고,
    /// 컨트롤러가 시점마다 OnStart / OnTick / OnEnd를 호출해준다.
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

        public sealed override void Apply(EntityView target, in EffectContext ctx)
            => target.Effects.Add(this, ctx);

        public virtual void OnStart(EntityView target, in EffectContext ctx) { }
        public virtual void OnTick(EntityView target, in EffectContext ctx) { }
        public virtual void OnEnd(EntityView target) { }
    }
}
