using UnityEngine;
using System.Collections.Generic;
using CoreDawn.Factory;
using CoreDawn.Placement;
using CoreDawn.Worlds;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Navigation
{
    // 길찾기를 위한 Node class

    public class GridManager : MonoBehaviour
    {
        public static GridManager Instance { get; private set; }
        [Header("Grid Settings")]
        [Tooltip("정적 장애물이 위치한 레이어 이름. Awake에서 마스크로 변환된다. 런타임 설치 건물은 심의 GridIndex로 별도 처리.")]
        [SerializeField] private string obstacleLayerName = "Obstacle";
        private LayerMask unwalkableMask;
        public float cellSize = 1f;       // GridSystem의 CellSize와 동일한 역할 — 맵이 주입한다(Inject). 굽고 나면 노드 한 변이 된다
        [Tooltip("길찾기 노드 한 변(m). 칸을 이 크기로 나눈다 — 칸 4m·노드 1m면 4등분. 칸 크기가 바뀌어도 노드는 이 크기를 유지한다.")]
        [SerializeField] private float nodeSize = 1f;

        [Header("Map Bounds")]
        [Tooltip("맵(바닥) 콜라이더. 지정하면 이 바운즈로 originPosition과 gridSize를 자동 계산해 " +
                 "그리드가 맵 밖으로 나가지 않도록 한다. 비워두면 아래 수동 값을 사용.")]
        [SerializeField] private Collider mapBounds;
        public Vector2Int gridSize;       // 가로, 세로 셀 개수 (mapBounds 지정 시 자동 계산됨)
        public Vector3 originPosition;    // 그리드의 시작점 (왼쪽 아래) (mapBounds 지정 시 자동 계산됨)
        private Node[,] grid;

        // 길찾기 해상도 세분화 배율 — 월드 칸 하나를 몇 등분해 노드로 쓰는가. Bake에서 계산.
        private int subdiv = 1;

        // 지면 윗면의 월드 y — 스폰 위치 스냅용. mapBounds가 있으면 그 콜라이더의 최상단.
        public float SurfaceY { get; private set; }

        // GridSystem 로직을 래핑할 변수
        private GridSystem gridSystem;
        // 주입된 맵 — 절벽 칸을 통행 불가로 굽는다. 없으면 물리 장애물만으로 판정한다.
        private MapDef map;
        private bool baked;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // 레이어 마스크를 이름으로 해석 — 인스펙터에서 엉뚱한 레이어(예: Bullet)로 설정되는 실수 방지
            unwalkableMask = LayerMask.GetMask(obstacleLayerName);
            if (unwalkableMask == 0)
                Debug.LogWarning($"[GridManager] '{obstacleLayerName}' 레이어를 찾지 못했습니다. " +
                    $"정적 장애물이 전부 walkable로 처리됩니다. Project Settings > Tags and Layers에서 레이어를 확인하세요.");

            // 씬에 직접 두고 인스펙터로 바운즈를 배선한 구성(구 방식)은 여기서 바로 굽는다.
            // 부트스트랩 구성은 월드의 맵을 주입받은 뒤 굽는다 — Inject 참조.
            if (mapBounds != null) Bake();
        }

        /// <summary>
        /// 월드의 맵 주입 — 격자는 지형의 함수지만 GridManager는 시스템이라 별도 씬으로 온다.
        /// <see cref="GameBootstrap"/>이 월드 씬의 <see cref="World"/>에서 맵을 읽어 넘긴다.
        /// 이미 구웠으면(인스펙터 배선된 기존 씬) 무시한다.
        /// </summary>
        public void Inject(MapDef worldMap, Vector3 origin, float tileSize)
        {
            if (baked || worldMap == null) return;

            map = worldMap;
            cellSize = tileSize;
            originPosition = origin;
            gridSize = new Vector2Int(worldMap.width, worldMap.height);
            SurfaceY = origin.y;
            Bake();
        }

        /// <summary>격자를 굽는다 — 한 번만 실행된다.</summary>
        private void Bake()
        {
            if (baked) return;
            baked = true;

            // 길찾기 해상도 세분화 — 월드 칸(예: 4m)이 그대로 노드가 되면 너무 성겨서,
            // 몬스터가 목표와 "같은 칸"에 선 순간 이동이 끝나는데 공격 사거리는 안 닿는다.
            // 칸을 nodeSize(기본 1m)로 등분한다 (칸 4m → 4등분, 1m 칸은 그대로) — 칸 크기는 맵의 값이라 여기서 4를 가정하지 않는다.
            // 맵 타일·심 건물은 여전히 월드 칸 단위다 — 조회 시 subdiv로 되돌려 변환한다.
            subdiv = Mathf.Max(1, Mathf.RoundToInt(cellSize / Mathf.Max(0.01f, nodeSize)));
            cellSize /= subdiv;
            gridSize = new Vector2Int(gridSize.x * subdiv, gridSize.y * subdiv);   // 바운즈 경로는 아래서 새로 계산해 덮는다

            // 맵 주입이 없고 바운즈만 있으면 바운즈로 크기를 잡는다 (구 씬 호환).
            // FloorToInt: 맵에 완전히 포함되는 셀만 그리드에 넣는다 (걸치는 가장자리 셀 제외)
            if (map == null && mapBounds != null)
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
            else if (map != null)
            {
                Debug.Log($"[GridManager] 맵 '{map.Id}'로 그리드 설정: " +
                    $"origin={originPosition}, gridSize={gridSize}, cellSize={cellSize} (칸당 {subdiv}등분)");
            }
            else
            {
                SurfaceY = originPosition.y;
                Debug.LogWarning("[GridManager] 맵도 바운즈도 없어 수동 gridSize/originPosition을 사용합니다. " +
                    "그리드가 맵보다 크면 경로가 맵 밖으로 설정될 수 있습니다.");
            }

            // GridSystem 초기화
            gridSystem = new GridSystem(cellSize, originPosition);
            CreateGrid();
            RefreshAllCosts();   // 길찾기가 볼 비용 한 벌 — 이후로는 바뀐 자리만 고친다
        }

        /// <summary>주입도 바운즈 배선도 없었으면 수동 값으로라도 굽는다 — 격자 없이 남지 않게.</summary>
        void LateUpdate()
        {
            if (!baked) Bake();
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

                    // 통행 불가 조건 두 가지:
                    //   ① 물리 장애물(Obstacle 레이어) — 씬에 놓인 바위·구조물
                    //   ② 맵의 절벽 타일 — 강은 건널 수 있으므로 막지 않는다(비용만 비싸다).
                    bool walkable = !Physics.CheckSphere(worldCenter, cellSize * 0.5f, unwalkableMask)
                                    && (map == null || TileRules.EnterCost(map.TileAt(TileOf(cell))) < TileRules.Blocked);

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

        [Header("진입 비용 (플로우필드 전용)")]
        [Tooltip("건물을 뚫고 지나가는 비용을 HP에 비례시키는 계수. 약한 건물부터 뚫리게 한다.\n" +
                 "지면 10 기준 — 계수 0.5면 벨트(HP60)=30, 울타리(HP250)=125.")]
        [SerializeField] private float buildingCostPerHp = 0.5f;

        [Tooltip("건물 비용 상한. 맵 대각 횡단이 약 1700(121칸)이라 이보다 크면 사실상 통행 불가가 되어 " +
                 "봉쇄가 성립한다 — 유한하게 묶어 둔다. 200 = 20칸 우회할 값어치.")]
        [SerializeField] private int buildingCostCap = 200;

        /// <summary>
        /// 이 칸에 발을 들이는 비용 — <b>길찾기의 정본</b>. 통행 가부도 여기서 파생된다
        /// (<see cref="TileRules.Blocked"/> 이상 = 못 감).
        ///
        /// 질문이 둘이라 <paramref name="passBuildings"/>로 갈린다:
        ///   <b>진격</b>(플로우필드) — "건물을 뚫고 갈 때 얼마?" → 유한 비용.
        ///     무한대로 두면 목표가 아닌 건물(벨트)로 길을 막았을 때 몬스터가 갈 곳을 잃고 굳는다.
        ///     비싸지만 유한하면 "여길 뚫어라"는 방향이 나오고, 도달한 몬스터가 사거리 판정으로 부순다.
        ///     비용이 HP에 비례하므로 약한 곳부터 뚫린다 — 방어 설계가 의미를 갖는다.
        ///   <b>보행</b>(A*·군중·스폰) — "건물을 놔두고 갈 때 얼마?" → 못 간다.
        ///     실제로 걸어갈 수 있는 길이어야 하고, 막히면 ChaseState가 진격 경로와 비교해
        ///     범인 건물을 찾아 부순다 — 그 대비가 성립하려면 여기서 막아야 한다.
        /// </summary>
        public int EnterCost(Vector2Int cell, bool passBuildings = false)
        {
            if (cell.x < 0 || cell.x >= gridSize.x || cell.y < 0 || cell.y >= gridSize.y)
                return TileRules.Blocked;

            var node = grid?[cell.x, cell.y];
            if (node == null || !node.walkable) return TileRules.Blocked;   // 절벽·물리 장애물

            int cost = map != null ? TileRules.EnterCost(map.TileAt(TileOf(cell))) : TileRules.BaseCost;
            if (cost >= TileRules.Blocked) return TileRules.Blocked;

            var building = BuildingAt(node);
            if (building == null || building.Building.Walkable) return cost;   // 밟고 지나가는 건물(지뢰)은 땅이다

            if (!passBuildings) return TileRules.Blocked;

            int hp = Mathf.RoundToInt(building.Def.Get<HealthModuleDef>()?.MaxHp ?? 100f);
            return cost + Mathf.Min(Mathf.RoundToInt(hp * buildingCostPerHp), buildingCostCap);
        }

        // ── 비용 필드 — 플로우필드와 A*가 함께 보는 정본 ─────────────

        private readonly CostField costs = new CostField();

        /// <summary>
        /// 지형·건물 비용 한 벌. 워커 스레드가 이 배열만 보고 길을 찾는다(Unity API 없이).
        /// 갱신은 메인 스레드에서만 — 처음 한 번 전체를 굽고, 이후 바뀐 자리만 다시 칠한다.
        /// </summary>
        public CostField Costs => costs;

        /// <summary>비용 필드 전체를 다시 굽는다 — 격자를 새로 구웠을 때만 필요하다.</summary>
        public void RefreshAllCosts()
        {
            costs.Resize(gridSize);
            RefreshCostCells(0, 0, gridSize.x - 1, gridSize.y - 1);
        }

        /// <summary>
        /// 월드 사각형이 덮는 칸의 비용만 다시 칠한다 — 건물이 놓이거나 부서졌을 때.
        /// 20만 칸을 매번 훑는 대신 바뀐 자리만 고치는 것이 이 함수의 존재 이유다.
        /// 대각 판정이 이웃을 보므로 한 칸 여유를 두고 칠한다.
        /// </summary>
        public void RefreshCostsIn(Vector3 worldMin, Vector3 worldMax)
        {
            if (!costs.IsReady || gridSystem == null) return;

            Vector2Int lo = gridSystem.WorldToGrid(worldMin);
            Vector2Int hi = gridSystem.WorldToGrid(worldMax);

            RefreshCostCells(Mathf.Min(lo.x, hi.x) - 1, Mathf.Min(lo.y, hi.y) - 1,
                             Mathf.Max(lo.x, hi.x) + 1, Mathf.Max(lo.y, hi.y) + 1);
        }

        void RefreshCostCells(int minX, int minY, int maxX, int maxY)
        {
            if (!costs.IsReady) return;

            minX = Mathf.Max(0, minX); minY = Mathf.Max(0, minY);
            maxX = Mathf.Min(gridSize.x - 1, maxX); maxY = Mathf.Min(gridSize.y - 1, maxY);
            if (minX > maxX || minY > maxY) return;

            var sim = FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.Factory : null;

            for (int y = minY; y <= maxY; y++)
            {
                int row = y * gridSize.x;
                int tileY = y / subdiv;

                for (int x = minX; x <= maxX; x++)
                {
                    int i = row + x;
                    var node = grid[x, y];

                    // 아래는 EnterCost와 같은 규칙이되 <b>한 번에 두 답</b>을 낸다 —
                    // 진격/보행을 따로 물으면 타일·건물 조회가 두 배가 되고, 칸이 20만을 넘으면
                    // 그 두 배가 그대로 프레임에 얹힌다.
                    if (node == null || !node.walkable)
                    {
                        costs.EnterCost[i] = TileRules.Blocked;
                        costs.Walkable[i] = false;
                        continue;
                    }

                    int cost = map != null
                        ? TileRules.EnterCost(map.TileAt(new Vector2Int(x / subdiv, tileY)))
                        : TileRules.BaseCost;

                    if (cost >= TileRules.Blocked)
                    {
                        costs.EnterCost[i] = TileRules.Blocked;
                        costs.Walkable[i] = false;
                        continue;
                    }

                    var building = sim != null ? BuildingAt(node) : null;
                    if (building == null || building.Building.Walkable)
                    {
                        costs.EnterCost[i] = cost;
                        costs.Walkable[i] = true;   // 건물이 없으니 걸어서 지나갈 수 있다
                        continue;
                    }

                    int hp = Mathf.RoundToInt(building.Def.Get<HealthModuleDef>()?.MaxHp ?? 100f);
                    costs.EnterCost[i] = cost + Mathf.Min(Mathf.RoundToInt(hp * buildingCostPerHp), buildingCostCap);
                    costs.Walkable[i] = false;      // 뚫고는 가도 걸어서는 못 지나간다
                }
            }

            costs.Touch();
        }

        /// <summary>길찾기 노드 좌표 → 월드 칸(맵 타일) 좌표. 세분화 배율을 되돌린다.</summary>
        private Vector2Int TileOf(Vector2Int cell) => new(cell.x / subdiv, cell.y / subdiv);

        /// <summary>이 칸의 지형 이동 속도 배율 — 강은 느리다. 맵이 없으면 1.</summary>
        public float TerrainSpeed(Vector2Int cell) =>
            map != null ? TileRules.SpeedMultiplier(map.TileAt(TileOf(cell))) : 1f;

        /// <summary>이 칸을 점유한 심 건물 — 좌표계가 달라 월드 좌표를 경유한다. 없으면 null.</summary>
        private BuildingModule BuildingAt(Node node)
        {
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null) return null;

            Vector2Int simCell = simGridSystem != null
                ? simGridSystem.WorldToGrid(node.worldPosition)
                : node.gridCoord;
            return boot.Factory.Grid.GetAt(simCell);
        }

        /// <summary>
        /// 월드 한 칸을 몇 등분해 길찾기 칸으로 쓰는가 (1 = 분할 없음).
        /// "칸 = 10" 단위로 적힌 상수(건물 시드 비용 등)는 월드 칸 기준이라, 길찾기 칸으로 환산할 때
        /// 이 배율을 곱해야 분할 전과 같은 거리 감각을 유지한다.
        /// </summary>
        public int Subdiv => subdiv;

        /// <summary>이 길찾기 칸을 점유한 심 건물. 건물이 없으면 null.</summary>
        public BuildingModule BuildingAt(Vector2Int cell)
        {
            var node = GetNode(cell);
            return node != null ? BuildingAt(node) : null;
        }

        // 정적 장애물(Awake에서 구운 값) + 런타임 설치 건물(심의 GridIndex)을 함께 판정
        /// <summary>
        /// 설 수 있는 칸인가 — <see cref="EnterCost"/>의 얇은 별칭이다(비용이 정본).
        /// 가부만 필요한 곳(A*·군중 분리·스폰 위치)이 읽기 좋으라고 남겨둔 이름이다.
        /// </summary>
        public bool IsWalkable(Node node, bool ignoreBuildings = false) =>
            node != null && EnterCost(node.gridCoord, ignoreBuildings) < TileRules.Blocked;

        public bool IsWalkable(Vector2Int cell, bool ignoreBuildings = false) =>
            EnterCost(cell, ignoreBuildings) < TileRules.Blocked;

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

        public MapTile GetTile(Vector2Int cell)
        {
            return map != null
                ? map.TileAt(TileOf(cell))
                : MapTile.Ground;
        }
        // GetNeighbours 메서드 등은 그대로 유지하거나 gridPos.x, gridPos.y 대신 Vector2Int를 사용하도록 개선 가능
    }
}
