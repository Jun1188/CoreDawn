using System;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 받는 피해를 조건으로 막는 문 — 심 모듈. 규칙의 주인이 아직 뷰인 개체(둥지: 보스나 스폰 포인트가 살아 있으면 무적)가
    /// 술어만 심에 꽂는다. 심은 뷰를 모르고 "지금 막는가"만 묻는다.
    /// 피해가 뷰를 거치지 않고 심 안에서 끝나는 지금, 뷰의 ReceiveDamage override로는 이 규칙을 지킬 수 없다.
    /// 규칙이 심으로 오면(5단계 둥지 모듈) 이 문은 사라진다.
    /// </summary>
    public sealed class DamageGate : EntityModule, IDamageInterceptor
    {
        /// <summary>(때린 엔티티) → 막는가. null이면 막지 않는다.</summary>
        public Func<Entity, bool> Blocks { get; set; }

        public float Intercept(float amount, Entity source) => Blocks != null && Blocks(source) ? 0f : amount;
    }
}
