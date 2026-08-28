using UnityEngine;
using CoreDawn.DayTime;

namespace CoreDawn.Entities
{
    /// <summary>
    /// Optional, scene-local policy for a nest. Nests without this component retain
    /// their legacy behaviour, so older scenes and prefab instances are unaffected.
    /// </summary>
    public sealed class NestEngagementZone : MonoBehaviour
    {
        [Min(0f)] [SerializeField] private float minimumRange = 7f;
        [Min(0f)] [SerializeField] private float maximumRange = 15f;
        [Min(0f)] [SerializeField] private float chaseRange = 25f;
        [Min(0f)] [SerializeField] private float leashRange = 30f;
        [SerializeField] private bool dayOnly = true;

        public float MinimumRange => minimumRange;
        public float MaximumRange => Mathf.Max(minimumRange, maximumRange);
        public float ChaseRange => Mathf.Max(MaximumRange, chaseRange);
        public float LeashRange => Mathf.Max(1f, leashRange);

        public bool IsActivePhase => !dayOnly || TimeManager.Instance == null || TimeManager.Instance.Phase == DayPhase.Day;

        /// <summary>맵 데이터로 세울 때 쓴다 — 같은 프리팹이라도 둥지마다 사나움이 다를 수 있다.</summary>
        public void Configure(float min, float max, float chase, float leash, bool dayOnlyRule)
        {
            minimumRange = Mathf.Max(0f, min);
            maximumRange = Mathf.Max(minimumRange, max);
            chaseRange = Mathf.Max(maximumRange, chase);
            leashRange = Mathf.Max(1f, leash);
            dayOnly = dayOnlyRule;
        }

        public bool CanSpawnFor(Vector3 nestPosition, Vector3 targetPosition)
        {
            if (!IsActivePhase) return false;
            float distance = Vector3.Distance(nestPosition, targetPosition);
            return distance >= MinimumRange && distance <= MaximumRange;
        }

        public bool CanChase(Vector3 nestPosition, Vector3 targetPosition)
        {
            if (!IsActivePhase) return false;
            float distance = Vector3.Distance(nestPosition, targetPosition);
            // minimumRange는 추가 스폰 금지 거리일 뿐이다. 이미 교전한 몬스터가
            // 플레이어가 둥지 안쪽으로 들어왔다는 이유로 추적을 포기하면 안 된다.
            return distance <= ChaseRange;
        }
    }
}
