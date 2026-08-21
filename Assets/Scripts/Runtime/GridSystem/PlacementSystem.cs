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

    [Header("철거")]
    [Tooltip("철거에 필요한 누름 유지 시간(초). 클릭 한 번에 사라지면 옆 건물을 실수로 날린다.")]
    [SerializeField] private float demolishHoldSeconds = 0.4f;

    private GridSystem grid;
    private PortFlowOverlay portFlow;

    // 프리뷰 포트를 다시 만들어야 하는지 판정하는 키 — 위치만 바뀌면 루트만 옮긴다
    private BuildingDataSO flowSo;
    private int flowRot = -1;
    private BeltShape flowShape;
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

    // 홀드 철거 — 누르고 있는 동안만 진행한다
    private bool holdPressed;
    private Building holdTarget;
    private float holdElapsed;

    // ── 외부(UI) 조회용
    public BuildMode Mode => mode;
    public BuildingDataSO CurrentBuilding => current;
    public IReadOnlyList<BuildingDataSO> Buildings =>
        database != null ? database.buildings : System.Array.Empty<BuildingDataSO>();
    public BuildingDatabaseSO Database => database;

    /// <summary>철거 모드에서 지금 조준 중인 건물. 없으면 null (HUD가 카드를 접는다).</summary>
    public Building HoveredBuilding => hovered;

    /// <summary>철거 홀드 진행도 0~1. 누르고 있지 않으면 0.</summary>
    public float DemolishHoldProgress =>
        holdPressed && holdTarget != null && demolishHoldSeconds > 0f
            ? Mathf.Clamp01(holdElapsed / demolishHoldSeconds)
            : 0f;

    /// <summary>철거까지 남은 시간(초). 누르기 전에는 전체 시간을 보여준다.</summary>
    public float DemolishHoldRemaining =>
        Mathf.Max(0f, demolishHoldSeconds - (holdPressed && holdTarget != null ? holdElapsed : 0f));

    void Awake()
    {
        grid = new GridSystem(cellSize, gridOrigin);

        // 미리 붙여두면 인스펙터로 색·반경을 조절할 수 있고, 없으면 알아서 붙는다 (씬 배선 불필요)
        portFlow = GetComponent<PortFlowOverlay>();
        if (portFlow == null) portFlow = gameObject.AddComponent<PortFlowOverlay>();
        portFlow.Configure(cellSize, gridOrigin);

        if (database == null) database = BuildingDatabaseSO.LoadDefault();
        if (database == null) Debug.LogError("[PlacementSystem] BuildingDatabase가 없습니다 (Resources/BuildingDatabase).", this);
    }

    // 주입된 맵 — 강·절벽에는 짓지 못한다. 없으면 지형 높이·점유만으로 판정한다(구 동작).
    private MapDataSO map;

    /// <summary>
    /// 조준 카메라 주입 — 별도 씬(Factory 부트스트랩)으로 얹힐 때는 인스펙터 참조가 씬 경계를
    /// 넘지 못하므로 GameBootstrap이 플레이어 카메라를 꽂아준다. 씬에 직접 둔 경우엔
    /// 인스펙터 배선이 이미 있으므로 호출되지 않는다(주입이 그것을 덮지도 않는다).
    /// </summary>
    public void Inject(Camera aimCamera)
    {
        if (cam == null) cam = aimCamera;
    }

    /// <summary>
    /// 월드의 맵·격자 주입 — 건설 가능 판정(강·절벽)과 좌표계를 맵에 맞춘다.
    /// 길찾기 격자(GridManager)와 <b>같은 원점·같은 칸 크기</b>를 쓰게 하는 것이 핵심이다:
    /// 둘이 어긋나면 건물이 점유한 칸과 몬스터가 막히는 칸이 달라진다.
    /// </summary>
    public void Inject(MapDataSO worldMap, Vector3 origin, float tileSize)
    {
        if (worldMap == null) return;

        map = worldMap;
        cellSize = tileSize;
        gridOrigin = origin;
        grid = new GridSystem(cellSize, gridOrigin);
        if (portFlow != null) portFlow.Configure(cellSize, gridOrigin);
    }

    void Start()
    {
        // 조준 카메라는 배치·철거 판정의 기준이라 없으면 아무것도 할 수 없다.
        // Camera.main으로 조용히 때우지 않는다 — 잘못된 카메라를 집어도 알 수 없기 때문이다.
        if (cam != null) return;

        Debug.LogError("[PlacementSystem] 조준 카메라가 없어 배치 기능을 끕니다. " +
                       "씬에 직접 두었다면 인스펙터에 배선하고, 부트스트랩 구성이면 " +
                       "플레이어(카메라 포함)가 씬에 있는지 확인하세요.", this);
        enabled = false;
    }

    void Update()
    {
        // 연속 입력(조준/프리뷰/호버)만 여기서 폴링 — 이산 입력은 BuildController가 API로 호출
        switch (mode)
        {
            case BuildMode.Placing: UpdatePlacing(); break;
            case BuildMode.Demolishing: UpdateDemolishing(); break;
            // 대기 모드에서는 포트를 그리지 않는다 — 표시는 건설 모드 전용이다.
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

    /// <summary>
    /// 현재 조준 지점에서 확정 — 배치 모드면 설치, 철거 모드면 즉시 철거.
    ///
    /// 실제 플레이 입력에서 철거는 <see cref="BeginDemolishHold"/>/<see cref="EndDemolishHold"/>로
    /// 누르고 있어야 진행된다(SCR-06). 이 즉시 경로는 UI 버튼·테스트처럼
    /// 이미 확인을 거친 호출자를 위해 남겨 둔다.
    /// </summary>
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
        if (GameManager.Instance != null && !GameManager.Instance.IsTierUnlocked(data.requiredCoreTier)) return;
        ExitMode();
        mode = BuildMode.Placing;
        current = data;
        rotation = 0;
        beltShape = BeltShape.Straight;
        lastCanPlace = false;   // 첫 Update가 판정을 채우기 전 Attack 방지
        SpawnPreview();
        portFlow.EnterPlacement();
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
        EndDemolishHold();
        if (portFlow != null) portFlow.Exit();
        flowSo = null;
        flowRot = -1;

        mode = BuildMode.None;
    }

    // ===================== 배치 모드 =====================

    private void UpdatePlacing()
    {
        // 지형을 조준하지 못하면 프리뷰를 숨긴다 (허공에 떠 있지 않게)
        if (!TryGetGroundPoint(out Vector3 cursorPoint))
        {
            if (preview != null) preview.SetActive(false);
            portFlow.HidePreview();
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
        // 재료가 모자라면 프리뷰가 빨갛게 떠서 누르기 전에 알 수 있다
        lastCanPlace = heightOk && CanBuildTerrain(origin, size) && CanPlace(origin, size)
                    && ResourceNodeRegistry.CanPlace(current, origin, size)
                    && BuildCost.CanAfford(current);
        lastOrigin   = origin;
        lastPos      = pos;
        SetPreviewColor(lastCanPlace);

        // 포트 흐름은 지면에 눕는 표시라 건물을 들어올린 만큼(SurfaceLift)은 빼고 지면 높이에 둔다
        bool shapeChanged = flowSo != current || flowRot != rotation || flowShape != beltShape;
        flowSo = current; flowRot = rotation; flowShape = beltShape;
        portFlow.UpdatePreview(PreviewPorts(), origin, groundY, shapeChanged);
    }

    /// <summary>프리뷰 건물의 회전 반영 포트 — 벨트는 모양(직선/L/R)에 따라 달라진다.</summary>
    private PortDefinition[] PreviewPorts()
        => current is BeltDataSO
            ? BeltDataSO.BuildPorts(beltShape, rotation)
            : current.GetRotatedPorts(rotation);

    private void Place(Vector2Int origin, Vector3 pos)
    {
        // 재료를 먼저 깎는다. 실패하면 배치도 하지 않는다 —
        // 프리뷰 판정과 실제 클릭 사이에 인벤토리가 바뀌었을 수 있다.
        if (!BuildCost.TryCharge(current))
        {
            Debug.Log($"[Placement] 재료가 부족해 '{current.name}' 을 지을 수 없습니다.");
            // 왜 아무 일도 안 일어났는지 소리로 알린다 — 로그는 플레이어가 못 본다
            if (SoundManager.Instance != null) SoundManager.Instance.PlayCommonSFX(CommonSFX.Warning);
            return;
        }

        if (current is BeltDataSO belt)
            PlacementBridge.Place(current, origin, pos, rotation,
                BeltDataSO.BuildPorts(beltShape, rotation), belt.PrefabFor(beltShape), beltShape);
        else
            PlacementBridge.Place(current, origin, pos, rotation);

        // 벨트 한 칸까지 포함해 무엇을 짓든 같은 설치음이 난다 — 공장을 짓는 리듬이 손에 붙는다
        if (SoundManager.Instance != null) SoundManager.Instance.PlayCommonSFX(CommonSFX.Construct);

        portFlow.NotifyGridChanged();   // 새 건물이 이웃 포트를 막았을 수 있다
    }

    /// <summary>
    /// 조준 없이 좌표를 지정해 배치한다 — 부트스트랩·세이브 로드·테스트용 표면.
    /// 프리뷰만 건너뛸 뿐 지형 높이·겹침·광맥 판정은 조준 배치와 완전히 같은 규칙을 쓴다
    /// (그리드 수학이 두 벌로 갈라지지 않게 여기 한 곳에 둔다).
    /// </summary>
    /// <param name="shape">벨트 모양. 벨트가 아닌 건물에서는 무시된다.</param>
    /// <returns>배치 성공 여부. 실패 사유는 reason으로 돌려준다.</returns>
    public bool TryPlaceAt(BuildingDataSO so, Vector2Int origin, int rotSteps,
        out Building placed, out string reason, BeltShape shape = BeltShape.Straight)
    {
        placed = null;
        reason = null;

        if (so == null) { reason = "BuildingDataSO가 null"; return false; }

        Vector2Int size = so.GetRotatedSize(rotSteps);

        if (!TryGetFootprintHeight(origin, size, out float groundY))
        {
            reason = $"지형 높이 판정 실패 (바닥이 없거나 경사가 {maxSlopeHeightDiff}를 넘음)";
            return false;
        }
        if (!CanBuildTerrain(origin, size)) { reason = "지을 수 없는 지형 (강·절벽 또는 맵 밖)"; return false; }
        if (!CanPlace(origin, size)) { reason = "이미 점유된 칸"; return false; }
        if (!ResourceNodeRegistry.CanPlace(so, origin, size)) { reason = "광맥 조건 불충족"; return false; }

        Vector3 pos = grid.GetFootprintCenter(origin, size);
        pos.y = groundY + SurfaceLift(so, origin);

        // 조준 배치(Place)와 같은 규칙 — 벨트는 모양에 맞는 포트·커브 메시로 세운다
        placed = so is BeltDataSO belt
            ? PlacementBridge.Place(so, origin, pos, rotSteps,
                BeltDataSO.BuildPorts(shape, rotSteps), belt.PrefabFor(shape), shape)
            : PlacementBridge.Place(so, origin, pos, rotSteps);
        return placed != null;
    }

    // ===================== 철거 모드 =====================

    private void UpdateDemolishing()
    {
        // 건물 몸체 직접 조준 우선, 실패하면 바닥 칸 폴백.
        // 코어는 철거 대상이 아니다 — 기지의 심장을 실수로 밀면 그대로 게임오버다.
        // 조준 단계에서 걸러 하이라이트도, 홀드 카운트도 아예 걸리지 않게 한다.
        SetHovered(TryGetAimedBuilding(out Building target) && !(target.Data is CoreDataSO)
            ? target : null);

        if (!holdPressed) { holdTarget = null; holdElapsed = 0f; return; }

        // 누른 채로 다른 건물을 조준하면 그쪽부터 다시 센다 —
        // 손을 뗐다 다시 누르게 하면 연속 철거가 번거로워진다
        if (holdTarget != hovered) { holdTarget = hovered; holdElapsed = 0f; }
        if (holdTarget == null) return;

        holdElapsed += Time.deltaTime;
        if (holdElapsed < demolishHoldSeconds) return;

        var done = holdTarget;
        holdTarget = null;
        holdElapsed = 0f;
        Demolish(done);
    }

    /// <summary>좌클릭을 누르기 시작했다 — 철거 모드에서만 의미가 있다.</summary>
    public void BeginDemolishHold()
    {
        if (mode != BuildMode.Demolishing) return;
        holdPressed = true;
        holdTarget = hovered;
        holdElapsed = 0f;
    }

    /// <summary>좌클릭을 뗐다. 임계 시간에 못 미쳤으면 아무 일도 일어나지 않는다.</summary>
    public void EndDemolishHold()
    {
        holdPressed = false;
        holdTarget = null;
        holdElapsed = 0f;
    }

    /// <summary>특정 건물을 철거한다. 점유 칸 모두 해제 + 인스턴스 파괴. 코어는 거부한다.</summary>
    public void Demolish(Building b)
    {
        if (b == null) return;
        if (b.Data is CoreDataSO) return;   // 조준 필터를 우회한 외부 호출까지 방어

        // 하이라이트 대상이면 복원 절차 없이 참조만 비운다 (어차피 곧 파괴됨)
        if (hovered == b)
        {
            hovered = null;
            savedMats.Clear();
        }

        // 환급 위치는 뷰가 파괴되기 전에 잡아둔다 — Remove가 GameObject를 없앤다
        var view = FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.GetView(b) : null;
        Vector3 dropAt = view != null ? view.transform.position : Vector3.zero;
        var data = b.Data;

        PlacementBridge.Remove(b);
        BuildCost.Refund(data, dropAt);   // 전액 환급

        // 자진 철거는 전투 파괴와 다른 소리여야 한다 — 파괴는 사고, 철거는 의도다.
        // 뷰가 이미 사라졌으므로 아까 잡아둔 좌표에서 낸다.
        if (SoundManager.Instance != null) SoundManager.Instance.PlayCommonSFX(CommonSFX.Destroy, 0.7f);

        if (portFlow != null) portFlow.NotifyGridChanged();   // 막혀 있던 이웃 포트가 열린다
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

    [Tooltip("건설(배치·철거) 조준 사거리 폴백(m). 씬에 PlayerInteractionManager가 있으면 " +
             "그쪽의 E키 상호작용 사거리를 그대로 쓴다 — 두 감각이 어긋나지 않게.")]
    [SerializeField] private float buildRangeFallback = 4f;

    private PlayerInteractionManager interaction;

    /// <summary>건설 조준의 최대 거리 — E키 상호작용(interactRange)과 같은 값.
    /// 무제한이면 맵 반대편에서도 짓고 부순다.</summary>
    private float BuildRange
    {
        get
        {
            if (interaction == null) interaction = FindFirstObjectByType<PlayerInteractionManager>();
            return interaction != null ? interaction.InteractRange : buildRangeFallback;
        }
    }

    /// <summary>
    /// 조준한 건물 찾기 — 공용 쿼리 (철거 하이라이트가 사용, 이후 기계 UI 열기 등도 여기로).
    /// ① 건물 콜라이더 직접 히트(몸체 조준) ② 실패 시 바닥 칸의 건물 폴백(벨트처럼 낮은 건물 대비).
    /// </summary>
    public bool TryGetAimedBuilding(out Building building)
    {
        building = null;

        // ① 몸체 직접 조준 — 건물은 Default 레이어라 마스크 없이 쏘고 엔티티 컴포넌트로 판별
        if (Physics.Raycast(AimRay(), out RaycastHit bodyHit, BuildRange))
        {
            var view = bodyHit.collider.GetComponentInParent<BuildingEntity>();
            if (view != null && view.HasSim)   // 심 없는 건물(코어 등)은 철거 대상 아님
            {
                building = view.Sim;
                return true;
            }
        }

        // ② 바닥 칸 폴백 — 심이 없는 씬(UI 테스트 등)에서도 안전해야 한다.
        //    포트 흐름 표시가 붙으면서 이 쿼리가 대기 모드에서도 매 프레임 돌기 때문.
        var boot = FactoryBootstrap.Instance;
        if (boot != null && boot.Sim != null && TryGetGroundPoint(out Vector3 cursorPoint))
        {
            Vector2Int cell = grid.WorldToGrid(cursorPoint);
            building = boot.Sim.Grid.GetAt(cell);
        }
        return building != null;
    }

    private bool TryGetGroundPoint(out Vector3 point)
    {
        // 사거리 밖 지면은 조준 실패 — 프리뷰가 숨고 배치·철거가 걸리지 않는다
        if (Physics.Raycast(AimRay(), out RaycastHit hit, BuildRange, groundMask))
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

    /// <summary>
    /// 칸이 비어 있는가 — 이미 놓인 건물과 겹치지 않는지만 본다.
    /// 지형(강·절벽)은 <see cref="CanBuildTerrain"/>이 따로 판정한다.
    /// </summary>
    private static bool CanPlace(Vector2Int origin, Vector2Int size)
        => GetCells(origin, size).All(c => !FactoryBootstrap.Instance.Sim.Grid.IsOccupied(c));

    /// <summary>
    /// 맵 타일이 건설을 허용하는가 — 지면(0)에만 짓는다. 강(1)은 지나갈 수는 있어도 지을 수 없고,
    /// 절벽(2)과 맵 밖은 둘 다 막힌다. 맵이 주입되지 않은 구성에서는 제한하지 않는다.
    /// </summary>
    private bool CanBuildTerrain(Vector2Int origin, Vector2Int size)
        => map == null || map.CanBuildFootprint(origin, size);

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

        StripLogic(preview);

        previewRenderers = preview.GetComponentsInChildren<Renderer>().ToList();
    }

    /// <summary>
    /// 프리뷰는 <b>진짜 건물 프리팹</b>을 그대로 Instantiate한 것이라, 손대지 않으면 살아 움직인다 —
    /// 타워 프리뷰가 커서를 따라다니며 몬스터를 조준하고, 발사음을 내고, 등장 파티클을 터뜨린다.
    /// 게다가 Entity는 OnEnable에서 전역 레지스트리에 자기를 등록해, 아직 짓지도 않은 건물이
    /// 사거리 계산과 사망 처리의 대상이 된다.
    ///
    /// 그래서 유령에게서 논리와 소리를 걷어낸다. Destroy는 프레임 끝에 처리되지만
    /// OnDisable이 레지스트리 등록을 되돌리므로 한 프레임 이상 남지 않는다.
    /// </summary>
    private static void StripLogic(GameObject ghost)
    {
        foreach (var entity in ghost.GetComponentsInChildren<Entity>(true)) Destroy(entity);
        foreach (var visual in ghost.GetComponentsInChildren<TowerVisualController>(true)) Destroy(visual);

        foreach (var animator in ghost.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var audio in ghost.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
        foreach (var ps in ghost.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var emission = ps.emission;
            emission.enabled = false;
        }
    }

    private void SetPreviewColor(bool valid)
    {
        Material mat = valid ? validMat : invalidMat;
        if (mat == null) return;
        foreach (var r in previewRenderers)
            r.sharedMaterial = mat;
    }
}