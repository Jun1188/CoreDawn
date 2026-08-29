using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 둥지의 교전 규칙 — 뷰의 NestEngagementZone(MonoBehaviour)에서 숫자만 뽑은 것. 두뇌가 추적·리쉬 판정에 쓴다.
    /// 낮에만 반응하는 규칙(DayOnly)은 시스템 시계(MonsterSystem.IsDay)로 판정한다. 5a-2의 NestSpawner 정의로 옮겨 갈 값.
    /// </summary>
    public readonly struct EngagementZone
    {
        public readonly float ChaseRange;
        public readonly float LeashRange;
        public readonly bool DayOnly;

        public EngagementZone(float chaseRange, float leashRange, bool dayOnly)
        {
            ChaseRange = Mathf.Max(0f, chaseRange);
            LeashRange = Mathf.Max(1f, leashRange);
            DayOnly = dayOnly;
        }
    }
}
