using System.Collections.Generic;
using UnityEngine;

// 플로우필드 구동 매니저 — 갱신 스케줄만 담당하고 계산은 FlowField(순수 C#)에 위임한다.
// 갱신 조건 (기획):
//   1. 밤 시작 시 최초 1회 (TimeManager.Cycle.NightStarted)
//   2. 이후 rebuildInterval(1~3초)마다 1회
//   3. 건물 배치/파괴 시 즉시 (BuildingEntity의 OnEnable/OnDisable → MarkDirty)
// 낮에는 몬스터가 없으므로 주기 갱신을 쉰다. TimeManager가 없는 테스트 씬은 항상 갱신.
public class FlowFieldManager : MonoBehaviour
{
    public static FlowFieldManager Instance { get; private set; }

    [Tooltip("주기 갱신 간격(초). 1~3초 권장.")]
    [SerializeField, Range(1f, 3f)] private float rebuildInterval = 2f;

    [Tooltip("타워 목표의 시드 비용. 코어(0)보다 크게 주면 몬스터가 코어를 우선 목표로 삼는다. 10 = 한 칸 거리.")]
    [SerializeField] private int towerGoalCost = 30;

    private readonly FlowField field = new FlowField();
    private readonly List<FlowField.Goal> goalBuffer = new List<FlowField.Goal>();
    private bool dirty;
    private float nextRebuildTime;

    public bool HasField => field.HasField;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // 밤 시작 시 최초 1회 갱신 예약
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.Cycle.NightStarted += _ => RebuildNow();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // 낮에는 주기 갱신을 쉰다 (건물 변화는 MarkDirty로 쌓였다가 밤 시작 갱신에 반영)
        if (TimeManager.Instance != null && TimeManager.Instance.Phase == DayPhase.Day) return;

        if (dirty || Time.time >= nextRebuildTime)
        {
            RebuildNow();
        }
    }

    // 건물 배치/파괴 등 경로 지형이 바뀌었을 때 호출 — 다음 프레임에 재계산
    public void MarkDirty() => dirty = true;

    private void RebuildNow()
    {
        dirty = false;
        nextRebuildTime = Time.time + rebuildInterval;

        if (GridManager.Instance == null)
        {
            field.Clear();
            return;
        }

        CollectGoals();
        field.Rebuild(GridManager.Instance, goalBuffer);
    }

    // 살아있는 건물이 차지한 셀을 목표로 수집. 단 벨트는 제외 —
    // 운송로(벨트)와 그 위의 아이템은 몬스터의 "진격 목표"가 아니다
    // (아이템/DroppedItem은 애초에 BuildingEntity이 아니라 목표 대상이 될 수 없다).
    // 경로를 막는 벨트는 기존대로 FlowFieldState의 사거리 판정으로 부술 수 있다.
    // 멀티타일 건물은 콜라이더 바운즈가 걸친 모든 셀을 시드로 넣는다.
    private void CollectGoals()
    {
        goalBuffer.Clear();
        var grid = GridManager.Instance;

        foreach (var building in BuildingEntity.All)
        {
            if (!building.IsValidTarget()) continue;
            if (building.Data is BeltDataSO) continue; // 벨트만 목표 제외
            int seedCost = building.IsCore ? 0 : towerGoalCost;

            var col = building.GetComponentInChildren<Collider>();
            if (col != null)
            {
                Node min = grid.NodeFromWorldPoint(col.bounds.min);
                Node max = grid.NodeFromWorldPoint(col.bounds.max);
                if (min != null && max != null)
                {
                    for (int x = min.gridCoord.x; x <= max.gridCoord.x; x++)
                    {
                        for (int y = min.gridCoord.y; y <= max.gridCoord.y; y++)
                        {
                            goalBuffer.Add(new FlowField.Goal(new Vector2Int(x, y), seedCost));
                        }
                    }
                    continue;
                }
            }

            // 콜라이더가 없거나 그리드 밖에 걸친 경우 중심 셀 하나만 시드로
            Node node = grid.NodeFromWorldPoint(building.transform.position);
            if (node != null) goalBuffer.Add(new FlowField.Goal(node.gridCoord, seedCost));
        }
    }

    // 현재 위치 셀에서 목표로 가는 방향 벡터. 필드 없음/목표 도달/맵 밖이면 zero.
    public Vector3 GetDirection(Vector3 worldPosition)
    {
        var grid = GridManager.Instance;
        if (grid == null || !field.HasField) return Vector3.zero;

        Node node = grid.NodeFromWorldPoint(worldPosition);
        if (node == null) return Vector3.zero;
        if (!field.TryGetNext(node.gridCoord, out Vector2Int nextCell)) return Vector3.zero;

        Node nextNode = grid.GetNode(nextCell);
        if (nextNode == null) return Vector3.zero;

        Vector3 dir = nextNode.worldPosition - worldPosition;
        dir.y = 0f;
        return dir.normalized;
    }
}
