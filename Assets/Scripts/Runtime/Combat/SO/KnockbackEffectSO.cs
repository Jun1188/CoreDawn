using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 넉백의 방향 기준 — "어느 쪽으로 미는가".
    ///
    /// 클래스를 나누지 않고 필드로 둔 이유: 클래스(EffectSO 하위)는 <b>채널</b>(무슨 일이
    /// 일어나는가)이고 여기서 갈리는 것은 <b>형태</b>다. 둘 다 "대상을 민다"는 같은 일이라
    /// 감속/가속이 <see cref="MoveSpeedEffectSO"/> 하나를 쓰는 것과 같은 자리다.
    /// (EffectEntry 머리말의 역할 분담 참고. 두 거동이 동시에 필요하면 에셋을 나누면 된다.)
    /// </summary>
    public enum KnockbackMode
    {
        /// <summary>공격이 날아온 방향으로 민다 — 총알·히트스캔·근접.</summary>
        Directional,

        /// <summary>명중점에서 바깥으로 민다 — 폭발·오라처럼 한 점에서 퍼지는 공격.</summary>
        Radial,
    }

    /// <summary>
    /// 넉백 — 즉시 효과. 대상을 총 ctx.Value 미터만큼 밀어낸다.
    /// 실제 이동은 MovementComponent의 넉백 임펄스(지수 감쇠)가 수행하고,
    /// 밀린 결과로 생긴 개체 겹침은 같은 프레임 CrowdSystem이 풀어준다.
    /// Movement가 없는 대상(건물·플레이어)에게는 아무 일도 하지 않는다.
    ///
    /// 방향 기준이 <see cref="mode"/>로 갈린다. 예전에는 방사형 하나뿐이었는데, 그러면
    /// 총알에 정면으로 맞아도 <b>몸통 어느 쪽에 맞았는지</b>가 방향을 정한다 — 왼쪽 어깨에
    /// 맞으면 왼쪽으로, 오른쪽에 맞으면 오른쪽으로 튄다. 사격에서 원하는 것은 그게 아니라
    /// 탄이 날아온 방향이다.
    /// </summary>
    [CreateAssetMenu(fileName = "Effect_Knockback", menuName = "Combat/Effect/Knockback")]
    public class KnockbackEffectSO : EffectSO
    {
        [Tooltip("어느 쪽으로 미는가. Directional = 공격이 날아온 방향(총알·근접), " +
                 "Radial = 명중점에서 바깥(폭발·오라).")]
        public KnockbackMode mode = KnockbackMode.Directional;

        public override void Apply(EntityView target, in EffectContext ctx)
        {
            var movement = target.Entity?.Get<Movement>();   // 이동은 심 모듈 — 넉백도 그쪽에 쌓인다
            if (movement == null || ctx.Value <= 0f) return;

            Vector3 dir = ResolveDirection(target, ctx);
            if (dir.sqrMagnitude < 0.0001f) return;   // 방향을 알 수 없으면 포기

            movement.AddKnockback(dir, ctx.Value);    // 정규화·수평 투영은 Movement가 한다
        }

        /// <summary>
        /// 미는 방향을 정한다. Directional이라도 방향이 실려오지 않는 전달 방식(폭발·오라)이
        /// 있으므로 방사형으로 물러나고, 그것마저 0이면(근접처럼 명중점 = 대상 위치)
        /// 시전자 위치로 한 번 더 물러난다. 세 단계 모두 실패할 때만 포기한다.
        /// </summary>
        Vector3 ResolveDirection(EntityView target, in EffectContext ctx)
        {
            if (mode == KnockbackMode.Directional && ctx.HitDirection.sqrMagnitude > 0.0001f)
                return ctx.HitDirection;

            Vector3 fromPoint = target.GetPosition() - ctx.HitPoint;
            fromPoint.y = 0f;
            if (fromPoint.sqrMagnitude > 0.0001f) return fromPoint;

            if (ctx.Source != null) return target.GetPosition() - ctx.Source.GetPosition();
            return Vector3.zero;
        }
    }
}
