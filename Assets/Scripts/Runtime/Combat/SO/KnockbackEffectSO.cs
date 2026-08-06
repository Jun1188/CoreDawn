using UnityEngine;

/// <summary>
/// 넉백 — 즉시 효과. 명중 지점(HitPoint)에서 대상 쪽으로 총 ctx.Value 미터만큼 밀어낸다.
/// 실제 이동은 MovementComponent의 넉백 임펄스(지수 감쇠)가 수행하고,
/// 밀린 결과로 생긴 개체 겹침은 같은 프레임 CrowdSystem이 풀어준다.
/// Movement가 없는 대상(건물·플레이어)에게는 아무 일도 하지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "Effect_Knockback", menuName = "Combat/Effect/Knockback")]
public class KnockbackEffectSO : EffectSO
{
    public override void Apply(Entity target, in EffectContext ctx)
    {
        var movement = target.Movement;
        if (movement == null || ctx.Value <= 0f) return;

        Vector3 dir = target.GetPosition() - ctx.HitPoint;
        dir.y = 0f;

        // 근접 공격은 HitPoint가 대상 위치라 방향이 0 — 시전자 위치로 폴백
        if (dir.sqrMagnitude < 0.0001f && ctx.Source != null)
        {
            dir = target.GetPosition() - ctx.Source.GetPosition();
            dir.y = 0f;
        }
        if (dir.sqrMagnitude < 0.0001f) return; // 방향을 알 수 없으면 포기

        movement.AddKnockback(dir, ctx.Value);
    }
}
