using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 그리드 기반 배치/철거 로직 — 입력을 모른다.
/// 이산 입력(토글/회전/설치 등)은 BuildController(파이프라인 리시버)가 이 API를 호출하고,
/// 배치 UI(빌드 메뉴)·테스트 코드도 같은 API를 직접 호출할 수 있다.
///
/// 모드:
///  - None        : 대기
///  - Placing     : SelectBuilding()으로 진입. 프리뷰 표시, ConfirmAtAim()=설치
///  - Demolishing : EnterDemolishMode()로 진입. 조준 건물 하이라이트, ConfirmAtAim()=철거
///
/// 연속 입력(조준 레이, 호버 하이라이트)만 Update에서 직접 폴링한다 (§7-1).
/// 조준: 커서가 잠겨 있으면(FPS 플레이 중) 화면 중앙 크로스헤어, 아니면 마우스 커서.
/// </summary>
public class PlacementSystem : MonoBehaviour
{
    public enum BuildMode { None, Placing, Demolishing }

    [Header("References")]
    [SerializeField] private Camera cam;
    [SerializeField] private LayerMask groundMask;
    [Tooltip("건물 목록 — 비워두면 Resources의 BuildingDatabase를 자동 사용. 수동 배열 연결은 폐기됨.")]
    [SerializeField] private BuildingDatabaseSO database;

    [Header("Grid")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Vector3 gridOrigin = Vector3.zero;

    // GridManager가 그리드 설정 정합성을 검사할 때 사용
    public float CellSize => cellSize;
    public Vector3 GridOrigin => gridOrigin;

    [Header("Terrain Height")]
    [SerializeField] private float raycastStartHeight = 100f;
    [SerializeField] private float maxSlopeHeightDiff = 0.5f;

    [Header("Preview Materials")]
    [SerializeField] private Material validMat;
    [SerializeField] private Material invalidMat;
    [Tooltip("철거 모드에서 대상 건물에 입힐 하이라이트 머티리얼 (빨강 반투명 추천).")]
    [SerializeField] private Material demolishHighlightMat;

    private GridSystem grid;
    private BuildMode mode = BuildMode.None;

    // 배치 모드 상태
    private BuildingDataSO current;
    private int lastIndex;                  // 배치 토글용 — 마지막 선택 건물 인덱스
    private GameObject preview;
    private List<Renderer> previewRenderers = new();
    private int rotation;
    private BeltShape beltShape;

    // Update(조준 폴링)가 계산하고 OnInput(Attack)이 사용하는 캐시.
    // 입력 이벤트는 프레임 중간에 오므로 마지막 프레임의 판정을 쓴다.
    private bool lastCanPlace;
    private Vector2Int lastOrigin;
    private Vector3 lastPos;

    // 철거 모드 상태
    private Building hovered;                                          // 지금 하이라이트 중인 건물
    private readonly Dictionary<Renderer, Material[]> savedMats = new(); // 원본 머티리얼 백업

    // ── 외부(UI) 조회용
    public BuildMode Mode => mode;
    public BuildingDataSO CurrentBuilding => current;
    public IReadOnlyList<BuildingDataSO> Buildings =>
        database != null ? database.buildings : System.Array.Empty<BuildingDataSO>();
    public BuildingDatabaseSO Database => database;

    void Awake()
    {
        grid = new GridSystem(cellSize, gridOrigin);
        if (cam == null) cam = Camera.main;

        if (database == null) database = BuildingDatabaseSO.LoadDefault();
        if (database == null) Debug.LogError("[PlacementSystem] BuildingDatabase가 없습니다 (Resources/BuildingDatabase).", this);
    }

    void Update()
    {
        // 연속 입력(조준/프리뷰/호버)만 여기서 폴링 — 이산 입력은 BuildController가 API로 호출
        switch (mode)
        {
            case BuildMode.Placing: UpdatePlacing(); break;
            case BuildMode.Demolishing: UpdateDemolishing(); break;
        }
    }

    // ===================== 조작 API (BuildController/배치 UI가 호출) =====================

    /// <summary>배치 모드 토글 — 마지막으로 선택했던 건물로 진입.</summary>
    public void ToggleBuildMode()
    {
        if (mode == BuildMode.Placing) ExitMode();
        else SelectBuildingByIndex(lastIndex);
    }

    /// <summary>철거 모드 토글.</summary>
    public void ToggleDemolishMode()
    {
        if (mode == BuildMode.Demolishing) ExitMode();
        else EnterDemolishMode();
    }

    /// <summary>프리뷰 90° 회전. 배치 모드가 아니면 false(아무 일 없음).</summary>
    public bool RotatePreview()
    {
        if (mode != BuildMode.Placing) return false;
        rotation = (rotation + 1) % 4;
        return true;
    }

    /// <summary>벨트 모양 순환(직선→L→R). 벨트 배치 중이 아니면 false.</summary>
    public bool CycleBeltShape()
    {
        if (mode != BuildMode.Placing || current is not BeltDataSO) return false;
        beltShape = (BeltShape)(((int)beltShape + 1) % 3);
        SpawnPreview();   // 모양이 바뀌면 프리뷰 메시 교체
        return true;
    }

    /// <summary>현재 조준 지점에서 확정 — 배치 모드면 설치, 철거 모드면 철거.</summary>
    public void ConfirmAtAim()
    {
        if (mode == BuildMode.Placing && lastCanPlace)
            Place(lastOrigin, lastPos);
        else if (mode == BuildMode.Demolishing && hovered != null)
            Demolish(hovered);
    }

    // ===================== 조준 헬퍼 (폴링) =====================

    /// <summary>조준 레이 — 커서 잠금(FPS)이면 화면 중앙, 아니면 마우스 커서.</summary>
    Ray AimRay()
    {
        if (Cursor.lockState == CursorLockMode.Locked || Mouse.current == null)
            return cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        return cam.ScreenPointToRay(Mouse.current.position.ReadValue());
    }

    // ===================== 모드 진입/종료 (UI 연동 표면) =====================

    public void SelectBuilding(BuildingDataSO data)
    {
        if (data == null) return;
        ExitMode();
        mode = BuildMode.Placing;
        current = data;
        rotation = 0;
        beltShape = BeltShape.Straight;
        lastCanPlace = false;   // 첫 Update가 판정을 채우기 전 Attack 방지
        SpawnPreview();
    }

    /// <summary>데이터베이스 인덱스로 선택 — 단축키용 (메뉴 UI는 SelectBuilding을 직접 호출).</summary>
    public void SelectBuildingByIndex(int index)
    {
        var list = Buildings;
        if (index < 0 || index >= list.Count) return;
        lastIndex = index;
        SelectBuilding(list[index]);
    }

    public void EnterDemolishMode()
    {
        ExitMode();
        mode = BuildMode.Demolishing;
    }

    /// <summary>현재 모드를 빠져나오며 프리뷰/하이라이트를 정리한다.</summary>
    public void ExitMode()
    {
        if (preview != null) Destroy(preview);
        previewRenderers.Clear();
        current = null;

        ClearHovered();

        mode = BuildMode.None;
    }

    // ===================== 배치 모드 =====================

    private void UpdatePlacing()
    {
        // 지형을 조준하지 못하면 프리뷰를 숨긴다 (허공에 떠 있지 않게)
        if (!TryGetGroundPoint(out Vector3 cursorPoint))
        {
            if (preview != null) preview.SetActive(false);
            lastCanPlace = false;
            return;
        }
        if (preview != null && !preview.activeSelf) preview.SetActive(true);

        Vector2Int origin = grid.WorldToGrid(cursorPoint);
        Vector2Int size = current.GetRotatedSize(rotation);

        bool heightOk = TryGetFootprintHeight(origin, size, out float groundY);

        Vector3 pos = grid.GetFootprintCenter(origin, size);
        pos.y = groundY + SurfaceLift(current, origin);
        preview.transform.position = pos;
        preview.transform.rotation = Quaternion.Euler(0, rotation * 90, 0);

        // 설치 판정 캐시 — OnInput(Attack)이 사용
        // 채굴기는 광맥 위에서만 (광맥이 없는 씬/비채굴기는 항상 통과)
        lastCanPlace = heightOk && CanPlace(origin, size)
                    && ResourceNodeRegistry.CanPlace(current, origin, size);
        lastOrigin   = origin;
        lastPos      = pos;
        SetPreviewColor(lastCanPlace);
    }

    private void Place(Vector2Int origin, Vector3 pos)
    {
        if (current is BeltDataSO belt)
            PlacementBridge.Place(current, origin, pos, rotation,
                BeltDataSO.BuildPorts(beltShape, rotation), belt.PrefabFor(beltShape));
        else
            PlacementBridge.Place(current, origin, pos, rotation);
    }

    // ===================== 철거 모드 =====================

    private void UpdateDemolishing()
    {
        // 건물 몸체 직접 조준 우선, 실패하면 바닥 칸 폴백 (철거는 OnInput(Attack)이 수행)
        SetHovered(TryGetAimedBuilding(out Building target) ? target : null);
    }

    /// <summary>특정 건물을 철거한다. 점유 칸 모두 해제 + 인스턴스 파괴.</summary>
    public void Demolish(Building b)
    {
        if (b == null) return;

        // 하이라이트 대상이면 복원 절차 없이 참조만 비운다 (어차피 곧 파괴됨)
        if (hovered == b)
        {
            hovered = null;
            savedMats.Clear();
        }

        PlacementBridge.Remove(b);
    }

    /// <summary>칸 좌표로 철거 (외부 호출용 편의 오버로드).</summary>
    public void Demolish(Vector2Int cell)
        => Demolish(FactoryBootstrap.Instance.Sim.Grid.GetAt(cell));

    // ---- 하이라이트 적용/복원 ----
    private void SetHovered(Building b)
    {
        if (hovered == b) return;   // 변화 없으면 그대로
        ClearHovered();             // 이전 대상 원복

        hovered = b;
        if (b == null || demolishHighlightMat == null) return;

        var view = FactoryBootstrap.Instance.GetView(b);
        if (view == null) return;

        foreach (var r in view.GetComponentsInChildren<Renderer>())
        {
            savedMats[r] = r.sharedMaterials;               // 원본 백업
            var arr = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = demolishHighlightMat;
            r.sharedMaterials = arr;                        // 하이라이트 입히기
        }
    }

    private void ClearHovered()
    {
        foreach (var kv in savedMats)
            if (kv.Key != null) kv.Key.sharedMaterials = kv.Value; // 원본 복원
        savedMats.Clear();
        hovered = null;
    }

    // ===================== 공용 헬퍼 =====================

    /// <summary>
    /// 조준한 건물 찾기 — 공용 쿼리 (철거 하이라이트가 사용, 이후 기계 UI 열기 등도 여기로).
    /// ① 건물 콜라이더 직접 히트(몸체 조준) ② 실패 시 바닥 칸의 건물 폴백(벨트처럼 낮은 건물 대비).
    /// </summary>
    public bool TryGetAimedBuilding(out Building building)
    {
        building = null;

        // ① 몸체 직접 조준 — 건물은 Default 레이어라 마스크 없이 쏘고 엔티티 컴포넌트로 판별
        if (Physics.Raycast(AimRay(), out RaycastHit bodyHit, 1000f))
        {
            var view = bodyHit.collider.GetComponentInParent<Entities.Building>();
            if (view != null && view.HasSim)   // 심 없는 건물(코어 등)은 철거 대상 아님
            {
                building = view.Sim;
                return true;
            }
        }

        // ② 바닥 칸 폴백
        if (TryGetGroundPoint(out Vector3 cursorPoint))
        {
            Vector2Int cell = grid.WorldToGrid(cursorPoint);
            building = FactoryBootstrap.Instance.Sim.Grid.GetAt(cell);
        }
        return building != null;
    }

    private bool TryGetGroundPoint(out Vector3 point)
    {
        if (Physics.Raycast(AimRay(), out RaycastHit hit, 1000f, groundMask))
        {
            point = hit.point;
            return true;
        }
        point = default;
        return false;
    }

    private bool SampleCellHeight(Vector2Int cell, out float height)
    {
        Vector3 center = grid.GridToWorldCenter(cell);
        Vector3 start = new Vector3(center.x, gridOrigin.y + raycastStartHeight, center.z);
        if (Physics.Raycast(start, Vector3.down, out RaycastHit hit,
                            raycastStartHeight * 2f, groundMask))
        {
            height = hit.point.y;
            return true;
        }
        height = 0f;
        return false;
    }

    // ===================== 표면 위 올려놓기 =====================

    // 프리팹 → "피벗에서 밑면까지 거리". 프리팹마다 고정이라 처음 한 번만 재고 캐시한다.
    private static readonly Dictionary<BuildingDataSO, float> pivotLiftCache = new();

    /// <summary>
    /// 채굴기를 광맥 위에 지을 때만 건물을 표면 위로 들어올린다.
    ///
    /// 왜 필요한가: 건물 프리팹은 피벗이 큐브 중앙이라(Minor = 1x1x1이 localPos y=0)
    /// 표면 높이에 그대로 놓으면 절반이 표면 아래로 들어간다. 광맥은 지면 위로 솟은
    /// 슬래브라서, 그 위에 놓이는 채굴기만큼은 밑면이 광맥 윗면에 닿아야 한다.
    ///
    /// 비용: 광맥 판정은 O(1) 셀 조회 하나이고, 들어올릴 거리는 프리팹마다 상수라 캐시된다.
    /// 이 함수가 도는 UpdatePreview는 어차피 매 프레임 지면 레이캐스트를 던지고 있으므로
    /// 추가 레이캐스트나 센서는 필요 없다.
    /// </summary>
    public static float SurfaceLift(BuildingDataSO so, Vector2Int origin)
    {
        if (so is not MinerDataSO)                     return 0f;
        if (ResourceNodeRegistry.NodeAt(origin) == null) return 0f;

        return PivotLift(so);
    }

    /// <summary>프리팹 피벗에서 렌더러 밑면까지의 거리 (프리팹 로컬 기준, 회전 0 가정).</summary>
    private static float PivotLift(BuildingDataSO so)
    {
        if (so == null || so.prefab == null) return 0f;
        if (pivotLiftCache.TryGetValue(so, out float cached)) return cached;

        float min = float.MaxValue;
        Transform root = so.prefab.transform;

        foreach (var mf in so.prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;

            // 메시 바운즈 8개 꼭짓점을 프리팹 루트 기준으로 변환해 최저점을 찾는다
            Matrix4x4 m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds mb = mf.sharedMesh.bounds;

            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                min = Mathf.Min(min, m.MultiplyPoint3x4(corner).y);
            }
        }

        float lift = min == float.MaxValue ? 0f : -min;
        pivotLiftCache[so] = lift;
        return lift;
    }

    private bool TryGetFootprintHeight(Vector2Int origin, Vector2Int size, out float y)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var cell in GetCells(origin, size))
        {
            if (!SampleCellHeight(cell, out float h)) { y = 0f; return false; }
            if (h < min) min = h;
            if (h > max) max = h;
        }
        y = max;
        return (max - min) <= maxSlopeHeightDiff;
    }

    private static IEnumerable<Vector2Int> GetCells(Vector2Int origin, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
            for (int z = 0; z < size.y; z++)
                yield return origin + new Vector2Int(x, z);
    }

    private static bool CanPlace(Vector2Int origin, Vector2Int size)
        => GetCells(origin, size).All(c => !FactoryBootstrap.Instance.Sim.Grid.IsOccupied(c));

    private void SpawnPreview()
    {
        if (preview != null) Destroy(preview);
        previewRenderers.Clear();

        var prefab = current is BeltDataSO belt ? belt.PrefabFor(beltShape) : current.prefab;
        if (prefab == null)
        {
            preview = new GameObject("Preview (프리팹 없음)");
            return;
        }

        preview = Instantiate(prefab);
        foreach (var col in preview.GetComponentsInChildren<Collider>())
            col.enabled = false;
        previewRenderers = preview.GetComponentsInChildren<Renderer>().ToList();
    }

    private void SetPreviewColor(bool valid)
    {
        Material mat = valid ? validMat : invalidMat;
        if (mat == null) return;
        foreach (var r in previewRenderers)
            r.sharedMaterial = mat;
    }
}