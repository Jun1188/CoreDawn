using UnityEngine;

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
        return distance >= MinimumRange && distance <= ChaseRange;
    }
}
