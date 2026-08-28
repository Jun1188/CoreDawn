using UnityEngine;
using CoreDawn.Entities;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 효과 적용 한 건의 맥락 — 누가(Source), 얼마의 크기로(Value), 어디를(HitPoint),
    /// 어느 방향에서(HitDirection) 때렸는가.
    ///
    /// Value는 시전측 데이터(EffectEntry.value × 시전측 배율)에서 오고, 해석은 효과가 한다:
    /// 피해량(Damage)·밀려나는 거리(Knockback)·이동속도 배율(MoveSpeed) 등.
    /// 효과 SO는 공유 에셋이라 시전자별 수치를 담을 수 없다는 게 이 구조의 핵심 제약 —
    /// 정의(에셋)는 행동과 형태만 갖고, 크기는 전부 맥락으로 흘러 들어온다.
    /// </summary>
    public readonly struct EffectContext
    {
        /// <summary>시전자. 출처 없는 피해(환경·구 TakeDamage 경로)면 null.</summary>
        public readonly EntityView Source;

        /// <summary>이 효과의 크기 — EffectEntry.value × 시전측 배율. 해석은 효과가 한다.</summary>
        public readonly float Value;

        /// <summary>명중 지점 (타격 이펙트·방사형 넉백용).</summary>
        public readonly Vector3 HitPoint;

        /// <summary>
        /// 공격이 <b>날아온 방향</b>(정규화). 총알·히트스캔의 진행 방향, 근접은 시전자→대상 방향.
        /// 방향을 알 수 없는 전달 방식(폭발·오라)에서는 <see cref="Vector3.zero"/>다.
        ///
        /// HitPoint만으로는 이걸 대신할 수 없다: 명중점에서 대상 중심으로 미는 방식은
        /// 몸통 왼쪽에 맞으면 왼쪽으로, 오른쪽에 맞으면 오른쪽으로 밀어낸다 — 정면에서 쏴도
        /// 대상이 옆으로 튀는 이유가 그것이었다. 총알은 맞은 자리가 아니라 날아온 방향으로 민다.
        /// </summary>
        public readonly Vector3 HitDirection;

        public EffectContext(EntityView source, float value, Vector3 hitPoint = default, Vector3 hitDirection = default)
        {
            Source = source;
            Value = value;
            HitPoint = hitPoint;
            HitDirection = hitDirection;
        }
    }
}
