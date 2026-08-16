using System;
using System.Collections.Generic;
using UnityEngine;

// 이동 — 순수 C# 클래스. 소유 Entity가 매 프레임 Tick(dt)으로 구동한다.
// 두 가지 모드: 경로 추종(런타임 A*, StartMoving)과 방향 이동(플로우필드, SetDirection).
[Serializable]
public class MovementComponent
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotateSpeed = 720f; // 도/초 — 이동 방향으로 몸 돌리는 속도

    [Header("Crowd (군중 겹침 해소)")]
    [Tooltip("개체 반지름. 두 개체가 반지름 합보다 가까우면 CrowdSystem이 겹친 만큼 떼어놓는다. 0이면 군중 시스템에서 제외.")]
    [SerializeField] private float crowdRadius = 0.4f;

    [Header("Knockback (넉백)")]
    [Tooltip("넉백 감쇠율(초당). 클수록 짧고 굵게 밀린다. 총 밀림 거리는 감쇠율과 무관하게 효과가 정한다.")]
    [SerializeField] private float knockbackDamping = 8f;

    private Transform transform;
    private List<Node> currentPath;
    private int targetIndex;
    private Vector3 flowDirection; // 방향 이동 모드 (플로우필드)

    public bool IsMoving => (currentPath != null && currentPath.Count > 0) || flowDirection != Vector3.zero;
    public float MoveSpeed => moveSpeed;

    /// <summary>군중 겹침 해소용 개체 반지름 — CrowdSystem이 읽는다.</summary>
    public float CrowdRadius => crowdRadius;

    /// <summary>효과 시스템의 이동 속도 배율(감속 등) — Entity.Update가 매 프레임 밀어 넣는다.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    // 실제 이동에 쓰는 속도 — 기본 속도 × 효과 배율
    private float EffectiveSpeed => moveSpeed * SpeedMultiplier;

    public event Action OnDestinationReached;
    public event Action OnPathBlocked;

    public void Initialize(Transform ownerTransform) => transform = ownerTransform;

    public void StartMoving(List<Node> path)
    {
        flowDirection = Vector3.zero;
        if (path == null || path.Count == 0)
        {
            currentPath = null;
            return;
        }
        currentPath = path;
        targetIndex = 0;
    }

    // 플로우필드 방향 이동. 매 프레임 갱신 호출을 전제로 한다 (zero면 정지).
    public void SetDirection(Vector3 direction)
    {
        currentPath = null;
        direction.y = 0f;
        flowDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    public void StopMoving()
    {
        currentPath = null;
        flowDirection = Vector3.zero;
    }

    public void Tick(float deltaTime)
    {
        if (transform == null) return;

        if (currentPath != null) TickPath(deltaTime);
        else if (flowDirection != Vector3.zero) TickDirection(deltaTime);

        // 개체 간 겹침 해소는 여기서 하지 않는다 — 모든 이동이 끝난 뒤 CrowdSystem이
        // 중앙 한 패스(LateUpdate)로 처리한다. 넉백만 개체 소관.
        TickKnockback(deltaTime);
    }

    // ── 넉백 — 효과 시스템(KnockbackEffectSO)이 주입하는 외부 충격 ──
    // 이동과 별개 레이어라 이동 속도 제한·감속의 영향을 받지 않는다.
    // 밀린 결과로 생긴 겹침은 같은 프레임 CrowdSystem이 풀어준다.

    private Vector3 knockback; // 현재 넉백 속도 (지수 감쇠)

    /// <summary>지정 방향으로 총 distance만큼 밀려나게 한다 (감쇠 적분값이 distance가 되도록 초기 속도를 잡는다).</summary>
    public void AddKnockback(Vector3 direction, float distance)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || distance <= 0f) return;
        // 지수 감쇠의 총 이동량 = v0 / 감쇠율 → v0 = 거리 × 감쇠율
        knockback += direction.normalized * (distance * knockbackDamping);
    }

    private void TickKnockback(float deltaTime)
    {
        if (knockback.sqrMagnitude < 0.01f)
        {
            knockback = Vector3.zero;
            return;
        }

        // v·dt(explicit Euler)가 아니라 지수 감쇠의 정확한 적분값 — 총 밀림 거리가
        // 프레임레이트와 무관하게 정확히 지정 거리가 된다 (v·dt는 60fps에서 ~7% 과잉으로 실측됨)
        float decay = Mathf.Exp(-knockbackDamping * deltaTime);
        Vector3 next = transform.position + knockback * ((1f - decay) / knockbackDamping);

        // 건물/장애물 셀로는 밀려 들어가지 않는다 — 벽에 닿으면 넉백 소멸
        if (GridManager.Instance != null)
        {
            Node node = GridManager.Instance.NodeFromWorldPoint(next);
            if (node == null || !GridManager.Instance.IsWalkable(node))
            {
                knockback = Vector3.zero;
                return;
            }
        }

        transform.position = next;
        knockback *= decay;
    }

    private void TickPath(float deltaTime)
    {
        // 이동 도중 다음 웨이포인트에 건물이 설치되면 즉시 멈추고 재탐색 요청
        if (GridManager.Instance != null && !GridManager.Instance.IsWalkable(currentPath[targetIndex]))
        {
            StopMoving();
            OnPathBlocked?.Invoke();
            return;
        }

        Vector3 waypoint = currentPath[targetIndex].worldPosition;
        waypoint.y = transform.position.y; // Y축 높이 보정

        Vector3 flatPosition = new Vector3(transform.position.x, 0, transform.position.z);
        Vector3 flatWaypoint = new Vector3(waypoint.x, 0, waypoint.z);

        if (Vector3.Distance(flatPosition, flatWaypoint) < 0.1f)
        {
            targetIndex++;
            if (targetIndex >= currentPath.Count)
            {
                // 목적지 도착 완료
                StopMoving();
                OnDestinationReached?.Invoke();
                return;
            }
            waypoint = currentPath[targetIndex].worldPosition;
            waypoint.y = transform.position.y;
        }

        Vector3 moveDir = waypoint - transform.position;
        transform.position = Vector3.MoveTowards(transform.position, waypoint, EffectiveSpeed * deltaTime);
        Face(moveDir, deltaTime);
    }

    private void TickDirection(float deltaTime)
    {
        Vector3 next = transform.position + flowDirection * (EffectiveSpeed * deltaTime);

        // 건물/장애물 셀로는 진입하지 않는다 (플로우필드가 목표 건물 셀을 가리킬 수 있음 —
        // 그 앞에서 멈추면 FlowFieldState의 사거리 판정이 공격으로 전환시킨다)
        if (GridManager.Instance != null)
        {
            Node nextNode = GridManager.Instance.NodeFromWorldPoint(next);
            if (nextNode == null || !GridManager.Instance.IsWalkable(nextNode))
            {
                Face(flowDirection, deltaTime);
                return;
            }
        }

        transform.position = next;
        Face(flowDirection, deltaTime);
    }

    private void Face(Vector3 direction, float deltaTime)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion look = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, rotateSpeed * deltaTime);
    }
}
