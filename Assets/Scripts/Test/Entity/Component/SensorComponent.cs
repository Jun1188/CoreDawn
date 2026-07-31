using System.Collections.Generic;
using UnityEngine;

// 감지 — 순수 C# 클래스. OverlapSphere로 대상 레이어의 Entity를 찾는다.
// 길찾기 최적화의 핵심: 몬스터 하나하나가 플레이어를 스캔하는 대신,
// 플레이어(1개)가 범위 내 몬스터들을 찾아 콜백(Monster.OnDetectedByPlayer)해준다.
// 타워(Building)도 같은 컴포넌트로 사거리 내 몬스터를 찾는다.
[System.Serializable]
public class SensorComponent
{
    [SerializeField] private float detectionRange = 10f;
    [Tooltip("감지 대상이 위치한 레이어 이름. Initialize에서 마스크로 변환된다. 플레이어/타워의 감지 대상은 몬스터.")]
    [SerializeField] private string targetLayerName = "Monster";
    [SerializeField] private float scanInterval = 0.2f; // 매 프레임 OverlapSphere 방지

    private Entity owner;
    private Transform transform;
    private LayerMask targetLayer;
    private float lastScanTime = float.MinValue;

    // GC 방지용 재사용 버퍼 (메인 스레드 전용)
    private static readonly Collider[] overlapBuffer = new Collider[64];

    public float DetectionRange => detectionRange;
    public float ScanInterval => scanInterval;

    // 런타임 부착 엔티티(EnsurePlayerEntity 등) 전용 — 인스펙터를 못 쓰는 경우 감지 범위 조정
    public void SetDetectionRange(float range) => detectionRange = Mathf.Max(0f, range);

    public void Initialize(Entity owner)
    {
        this.owner = owner;
        transform = owner.transform;

        // 레이어 마스크를 이름으로 해석 — 인스펙터 오설정 방지
        targetLayer = LayerMask.GetMask(targetLayerName);
        if (targetLayer == 0)
            Debug.LogWarning($"[SensorComponent] '{targetLayerName}' 레이어를 찾지 못했습니다. " +
                $"대상 감지가 동작하지 않습니다. Project Settings > Tags and Layers에서 레이어를 확인하세요.");
    }

    // 스캔 주기가 됐을 때만 true를 반환하며 results를 범위 내 유효 Entity로 채운다.
    // (플레이어가 몬스터 감지/해제 콜백을 쏘는 용도)
    public bool TryScan(List<Entity> results)
    {
        if (transform == null || Time.time < lastScanTime + scanInterval) return false;
        lastScanTime = Time.time;

        results.Clear();
        int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRange, overlapBuffer, targetLayer);
        for (int i = 0; i < count; i++)
        {
            // 콜라이더가 자식 모델에 붙어 있는 프리팹 구조를 지원하기 위해 부모까지 탐색
            Entity entity = overlapBuffer[i].GetComponentInParent<Entity>();
            if (entity == null || entity == owner || !entity.IsValidTarget()) continue;
            if (!results.Contains(entity)) results.Add(entity); // 콜라이더 여러 개인 대상 중복 방지
        }
        return true;
    }

    // 범위 내 가장 가까운 유효 Entity (타워 자동 공격 등 단일 대상용, 즉시 스캔)
    public Entity GetClosestTarget(float rangeOverride = -1f)
    {
        if (transform == null) return null;
        float range = rangeOverride > 0f ? rangeOverride : detectionRange;

        int count = Physics.OverlapSphereNonAlloc(transform.position, range, overlapBuffer, targetLayer);
        Entity closest = null;
        float minDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Entity entity = overlapBuffer[i].GetComponentInParent<Entity>();
            if (entity == null || entity == owner || !entity.IsValidTarget()) continue;

            float dist = Vector3.Distance(transform.position, overlapBuffer[i].transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                closest = entity;
            }
        }
        return closest;
    }
}
