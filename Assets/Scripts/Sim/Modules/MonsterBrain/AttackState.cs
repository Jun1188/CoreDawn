using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    public sealed class AttackState : IEntityState
    {
        // 추적 복귀 히스테리시스 — 진입(사거리)과 이탈 기준이 같으면 사거리 경계에서 Attack↔Chase가 매 틱 진동한다
        const float ExitRangeBuffer = 1.15f;

        readonly Entity target;
        public Entity Target => target;

        public AttackState(Entity target) => this.target = target;

        public void Enter(MonsterBrainModule b)
        {
            b.Movement?.StopMoving();
            TryAttackTarget(b);
        }

        public void Update(MonsterBrainModule b, float dt)
        {
            if (!MonsterBrainModule.IsValidTarget(target))
            {
                b.SetState(new IdleState());
                return;
            }

            float distance = MonsterBrainModule.DistanceTo(target, b.Owner.Position);
            if (b.Attack == null || distance > b.Attack.Range * ExitRangeBuffer)
            {
                b.SetState(new ChaseState(target));   // 같은 타겟을 유지한 채 재추적
                return;
            }

            // 사거리 안에 있는 동안은 쿨다운이 돌 때마다 계속 공격
            if (b.Attack.CanAttack(b.Now)) TryAttackTarget(b);
        }

        public void Exit(MonsterBrainModule b) { }

        void TryAttackTarget(MonsterBrainModule b)
        {
            if (!MonsterBrainModule.IsValidTarget(target) || b.Attack == null) return;

            // 수평으로만 타겟을 바라본다
            b.Movement?.FaceImmediately(target.Position - b.Owner.Position);
            b.Attack.TryAttack(target, b.Now);
        }
    }
}
