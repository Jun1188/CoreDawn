using UnityEngine;
using System.Collections.Generic;

// 길찾기를 위한 Node class

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }
    [Header("Grid Settings")]
    [Tooltip("정적 장애물이 위치한 레이어 이름. Awake에서 마스크로 변환된다. 런타임 설치 건물은 심의 GridIndex로 별도 처리.")]
    [SerializeField] private string obstacleLayerName = "Obstacle";
    private LayerMask unwalkableMask;
    public float cellSize = 1f;       // GridSystem의 CellSize와 동일한 역할

    [Header("Map Bounds")]
    [Tooltip("맵(바닥) 콜라이더. 지정하면 이 바운즈로 originPosition과 gridSize를 자동 계산해 " +
             "그리드가 맵 밖으로 나가지 않도록 한다. 비워두면 아래 수동 값을 사용.")]
    [SerializeField] private Collider mapBounds;
    public Vector2Int gridSize;       // 가로, 세로 셀 개수 (mapBounds 지정 시 자동 계산됨)
    public Vector3 originPosition;    // 그리드의 시작점 (왼쪽 아래) (mapBounds 지정 시 자동 계산됨)
    private Node[,] grid;

    // 지면 윗면의 월드 y — 스폰 위치 스냅용. mapBounds가 있으면 그 콜라이더의 최상단.
    public float SurfaceY { get; private set; }

    // GridSystem 로직을 래핑할 변수
    private GridSystem gridSystem;
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // 레이어 마스크를 이름으로 해석 — 인스펙터에서 엉뚱한 레이어(예: Bullet)로 설정되는 실수 방지
        unwalkableMask = LayerMask.GetMask(obstacleLayerName);
        if (unwalkableMask == 0)
            Debug.LogWarning($"[GridManager] '{obstacleLayerName}' 레이어를 찾지 못했습니다. " +
                $"정적 장애물이 전부 walkable로 처리됩니다. Project Settings > Tags and Layers에서 레이어를 확인하세요.");

        // 맵 바운즈가 지정되면 그리드 크기/원점을 맵에 맞춘다 → 경로가 맵 밖으로 나가는 것을 방지.
        // FloorToInt: 맵에 완전히 포함되는 셀만 그리드에 넣는다 (걸치는 가장자리 셀 제외)
        if (mapBounds != null)
        {
            Bounds b = mapBounds.bounds;
            SurfaceY = b.max.y;
            originPosition = new Vector3(b.min.x, originPosition.y, b.min.z);
            gridSize = new Vector2Int(
                Mathf.Max(1, Mathf.FloorToInt(b.size.x / cellSize)),
                Mathf.Max(1, Mathf.FloorToInt(b.size.z / cellSize)));
            Debug.Log($"[GridManager] 맵 바운즈({mapBounds.name})로 그리드 자동 설정: " +
                $"origin={originPosition}, gridSize={gridSize}, cellSize={cellSize}");
        }
        else
        {
            SurfaceY = originPosition.y;
            Debug.LogWarning("[GridManager] mapBounds가 비어 있어 수동 gridSize/originPosition을 사용합니다. " +
                "그리드가 맵보다 크면 경로가 맵 밖으로 설정될 수 있습니다.");
        }

        // GridSystem 초기화
        gridSystem = new GridSystem(cellSize, originPosition);
        CreateGrid();
    }
    void CreateGrid()
    {
        grid = new Node[gridSize.x, gridSize.y];
        for (int x = 0; x < gridSize.x; x++)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                // GridSystem을 이용해 각 셀의 중앙 좌표를 쉽게 구함
                Vector2Int cell = new Vector2Int(x, y);
                Vector3 worldCenter = gridSystem.GridToWorldCenter(cell);

                // 장애물 체크 (반지름은 cellSize의 절반)
                bool walkable = !Physics.CheckSphere(worldCenter, cellSize * 0.5f, unwalkableMask);

                grid[x, y] = new Node(walkable, worldCenter, cell);
            }
        }
    }
    // 심(PlacementSystem) 좌표계 변환기 — GridManager와 origin/cellSize가 달라도
    // 월드 좌표를 거쳐 심의 GridIndex 셀로 정확히 변환한다 (MainScene 통합용).
    private GridSystem simGridSystem;

    void Start()
    {
        var placement = FindFirstObjectByType<PlacementSystem>();
        if (placement != null)
        {
            simGridSystem = new GridSystem(placement.CellSize, placement.GridOrigin);
            if (!Mathf.Approximately(placement.CellSize, cellSize) || placement.GridOrigin != originPosition)
                Debug.Log($"[GridManager] PlacementSystem과 그리드 설정이 달라 월드좌표 변환을 사용합니다. " +
                    $"GridManager(cellSize={cellSize}, origin={originPosition}) vs " +
                    $"PlacementSystem(cellSize={placement.CellSize}, origin={placement.GridOrigin}).");
        }
    }

    // 정적 장애물(Awake에서 구운 값) + 런타임 설치 건물(심의 GridIndex)을 함께 판정
    public bool IsWalkable(Node node, bool ignoreBuildings = false)
    {
        if (node == null || !node.walkable) return false;
        if (!ignoreBuildings)
        {
            var boot = FactoryBootstrap.Instance;
            if (boot != null && boot.Sim != null)
            {
                // 심 그리드는 PlacementSystem 좌표계 — 변환기가 있으면 월드 좌표 경유로 셀을 맞춘다
                Vector2Int simCell = simGridSystem != null
                    ? simGridSystem.WorldToGrid(node.worldPosition)
                    : node.gridCoord;
                if (boot.Sim.Grid.IsOccupied(simCell)) return false;
            }
        }
        return true;
    }

    public bool IsWalkable(Vector2Int cell, bool ignoreBuildings = false)
    {
        if (cell.x < 0 || cell.x >= gridSize.x || cell.y < 0 || cell.y >= gridSize.y) return false;
        return IsWalkable(grid[cell.x, cell.y], ignoreBuildings);
    }

    public Node GetNode(Vector2Int cell)
    {
        if (cell.x < 0 || cell.x >= gridSize.x || cell.y < 0 || cell.y >= gridSize.y) return null;
        return grid[cell.x, cell.y];
    }

    // GridSystem을 사용하여 월드 좌표를 노드로 변환
    public Node NodeFromWorldPoint(Vector3 worldPosition)
    {
        Vector2Int gridPos = gridSystem.WorldToGrid(worldPosition);

        // 배열 범위를 벗어나지 않도록 방어 코드
        if (gridPos.x >= 0 && gridPos.x < gridSize.x && gridPos.y >= 0 && gridPos.y < gridSize.y)
        {
            return grid[gridPos.x, gridPos.y];
        }
        return null; // 맵 밖일 경우
    }

    // GridManager.cs 내부에 추가할 메서드
    public List<Node> GetNeighbours(Node node)
    {
        List<Node> neighbours = new List<Node>();

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;

                // 새로 바뀐 gridCoord를 사용
                int checkX = node.gridCoord.x + x;
                int checkY = node.gridCoord.y + y;

                if (checkX >= 0 && checkX < gridSize.x && checkY >= 0 && checkY < gridSize.y)
                {
                    neighbours.Add(grid[checkX, checkY]);
                }
            }
        }
        return neighbours;
    }


    // GetNeighbours 메서드 등은 그대로 유지하거나 gridPos.x, gridPos.y 대신 Vector2Int를 사용하도록 개선 가능
}
