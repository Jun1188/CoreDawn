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

    [Header("Separation (몬스터 겹침 방지)")]
    [Tooltip("이 반경 내의 다른 몬스터로부터 밀려난다. 0이면 분리 조향 비활성화.")]
    [SerializeField] private float separationRadius = 0.8f;
    [Tooltip("밀어내는 세기 배율. 이동 속도 대비 최대 절반 속도로만 밀리도록 내부에서 제한된다.")]
    [SerializeField] private float separationWeight = 1.5f;

    private Transform transform;
    private List<Node> currentPath;
    private int targetIndex;
    private Vector3 flowDirection; // 방향 이동 모드 (플로우필드)

    public bool IsMoving => (currentPath != null && currentPath.Count > 0) || flowDirection != Vector3.zero;
    public float MoveSpeed => moveSpeed;

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

        // 이동 모드와 무관하게 항상 적용 — 정지(공격) 중에도 겹친 개체는 서로 밀려난다
        ApplySeparation(deltaTime);
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
        transform.position = Vector3.MoveTowards(transform.position, waypoint, moveSpeed * deltaTime);
        Face(moveDir, deltaTime);
    }

    private void TickDirection(float deltaTime)
    {
        Vector3 next = transform.position + flowDirection * (moveSpeed * deltaTime);

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

    // 분리 조향 — 주변 몬스터로부터 밀려나는 변위를 이동 후 보정으로 적용한다.
    // 물리(Rigidbody) 없이 transform 이동을 유지하면서 개체 겹침만 해소하는 경량 방식.
    private void ApplySeparation(float deltaTime)
    {
        if (separationRadius <= 0f) return;

        if (monsterMask == 0)
        {
            monsterMask = LayerMask.GetMask("Monster");
            if (monsterMask == 0) { separationRadius = 0f; return; } // 레이어 미설정 프로젝트 — 비활성화
        }

        Vector3 pos = transform.position;
        int count = Physics.OverlapSphereNonAlloc(pos, separationRadius, separationBuffer, monsterMask);

        Vector3 push = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            Transform other = separationBuffer[i].transform;
            if (other == transform || other.IsChildOf(transform)) continue; // 자기 자신/자식 콜라이더 제외

            Vector3 away = pos - other.position;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist < 0.0001f)
            {
                // 완전히 겹친 경우 — 인스턴스 ID 기반 고정 방향으로 밀어 좌우 진동 방지
                float angle = (transform.GetInstanceID() % 360) * Mathf.Deg2Rad;
                away = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                dist = 0.0001f;
            }
            // 가까울수록 강하게 (반경 밖 0 ~ 밀착 1 선형)
            push += away / dist * (1f - Mathf.Clamp01(dist / separationRadius));
        }
        if (push == Vector3.zero) return;

        // 본 이동을 압도하지 않도록 최대 이동속도의 절반으로 제한
        Vector3 offset = Vector3.ClampMagnitude(push * (separationWeight * moveSpeed * deltaTime),
                                                moveSpeed * deltaTime * 0.5f);
        Vector3 next = pos + offset;

        // 건물/장애물 셀로는 밀려나지 않는다
        if (GridManager.Instance != null)
        {
            Node nextNode = GridManager.Instance.NodeFromWorldPoint(next);
            if (nextNode == null || !GridManager.Instance.IsWalkable(nextNode)) return;
        }
        transform.position = next;
    }

    private LayerMask monsterMask;

    // GC 방지용 재사용 버퍼 (메인 스레드 전용, SensorComponent와 같은 패턴)
    private static readonly Collider[] separationBuffer = new Collider[32];

    private void Face(Vector3 direction, float deltaTime)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion look = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, rotateSpeed * deltaTime);
    }
}
