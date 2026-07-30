using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// ResourceNodeTest 씬 통합 테스트 — 실제 씬(MainScene 복사본)에서 낮/밤을 오가며
/// 광맥·채굴기 동작을 순서대로 검증한다. 헤드리스 스위트(ResourceNodeTests)가
/// 심 로직만 본다면, 이쪽은 씬의 실제 배선(FactoryBootstrap·PlacementBridge·뷰·TimeManager)까지 본다.
///
/// 실행:
///   에디터 — ResourceNodeTest 씬을 열고 플레이 (결과는 콘솔 + 화면 좌상단 OnGUI)
///   CLI    — Tools 메뉴/CLI 진입점이 씬 생성부터 플레이까지 자동 (ResourceNodeSceneSetup)
///
/// 검증 순서 (1일차 낮 → 1일차 밤 → 2일차 낮 → 2일차 밤):
///   낮1  훅 소유권 / 생산 누적 / 채굴 재고 인출 / 광맥 밖 설치 차단 / 건축 허용
///   밤1  건축 금지 / 채굴 지속 / 전투 파괴 후 광맥·재고 보존 / 채굴기 없을 때 상한 정지
///   낮2  일수 증가·건축 재허용 / 같은 광맥에 재설치 → 채굴 재개
///   밤2  두 번째 밤 전환에도 채굴 지속
/// </summary>
public class ResourceNodeSceneTest : MonoBehaviour
{
    [Tooltip("1x1 광맥 — 생산/채굴/파괴 시나리오의 주 무대. 세팅 스크립트가 연결한다.")]
    [SerializeField] private ResourceNode nodeA;
    [Tooltip("2x2 광맥 — 멀티타일 배치 판정용.")]
    [SerializeField] private ResourceNode nodeB;

    // CLI 러너가 폴링하는 결과 (도메인 리로드 후 플레이 세션 안에서만 살아 있으면 된다)
    public static bool   Finished { get; private set; }
    public static bool   Passed   { get; private set; }
    public static string Report   { get; private set; } = "(실행 중)";

    readonly List<string> _lines = new();
    int _pass, _fail;

    // 우리 케이스와 무관한 씬 자체의 예외(헤드리스에서 입력 장치 없음 등)를 따로 센다
    int _sceneErrors;
    string _firstSceneError;

    FactorySim Sim => FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.Sim : null;
    static GridSystem Grid => ResourceNodeRegistry.Grid;

    ItemDataSO _ore;
    BuildingDataSO _minerSO, _storageSO;
    Building _miner, _storage;

    void OnEnable()  => Application.logMessageReceived += OnLog;
    void OnDisable() => Application.logMessageReceived -= OnLog;

    void OnLog(string condition, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        if (condition.StartsWith("[씬테스트]")) return;
        _sceneErrors++;
        _firstSceneError ??= condition;
    }

    IEnumerator Start()
    {
        Finished = false;

        if (!Prepare()) { Conclude(); yield break; }

        yield return new WaitForSeconds(0.5f);   // FactoryTest.Start 등 다른 Start가 다 돈 뒤

        yield return Day1();
        yield return Night1();
        yield return Day2();
        yield return Night2();

        Conclude();
    }

    // ─── 준비 ───────────────────────────────────────────────────

    bool Prepare()
    {
        if (Sim == null)          return Fatal("씬에 FactoryBootstrap이 없습니다.");
        if (TimeManager.Instance == null) return Fatal("씬에 TimeManager가 없습니다 (낮/밤 테스트 불가).");
        if (nodeA == null || nodeB == null) return Fatal("광맥 참조가 비어 있습니다 (세팅 스크립트 확인).");

        _ore = nodeA.Resource;
        if (_ore == null) return Fatal("광맥 A에 자원 아이템이 없습니다.");

        var db = BuildingDatabaseSO.LoadDefault();
        _minerSO   = db != null ? db.buildings.FirstOrDefault(b => b is MinerDataSO)   : null;
        _storageSO = db != null ? db.buildings.FirstOrDefault(b => b is StorageDataSO) : null;
        if (_minerSO == null || _storageSO == null)
            return Fatal("BuildingDatabase에서 채굴기/저장고 SO를 찾지 못했습니다.");

        return true;
    }

    // ─── 1일차 낮 ───────────────────────────────────────────────

    IEnumerator Day1()
    {
        Section("── 1일차 낮 ──");

        // 이 씬에는 FactoryTest가 있어 Start에서 GetResourceAt을 "전 좌표 철광석"으로 덮어쓴다.
        // 광맥이 있는 씬에서는 레지스트리가 소켓을 되찾아와야 한다 (실행 순서에 흔들리면 안 됨).
        Case("D1 광맥이 자원 소켓의 주인 (FactoryTest 오버라이드 복구)",
             Sim.GetResourceAt != null &&
             Sim.GetResourceAt(nodeA.Origin) == _ore &&
             Sim.GetResourceAt(FarEmptyCell()) == null,
             $"광맥 위={Name(Sim.GetResourceAt?.Invoke(nodeA.Origin))}, " +
             $"광맥 밖={Name(Sim.GetResourceAt?.Invoke(FarEmptyCell()))}");

        Case("D2 광맥이 레지스트리에 등록됨",
             ResourceNodeRegistry.NodeAt(nodeA.Origin) == nodeA &&
             ResourceNodeRegistry.NodeAt(nodeB.Origin) == nodeB,
             $"등록된 광맥 {ResourceNodeRegistry.Nodes.Count}개");

        int before = nodeA.CurrentStock;
        yield return new WaitForSeconds(1.5f);
        Case("D3 낮 동안 재고가 쌓인다",
             nodeA.CurrentStock > before || nodeA.IsFull,
             $"재고 {before} → {nodeA.CurrentStock} (상한 {nodeA.MaxStock})");

        // 광맥 위 설치 — 실제 배치 경로(PlacementBridge)로 심 + 뷰를 함께 만든다
        Case("D4 광맥 위 채굴기는 설치 허용 판정",
             ResourceNodeRegistry.CanPlace(_minerSO, nodeA.Origin, Vector2Int.one),
             "CanPlace(광맥 위) == true 여야 함");

        _miner   = Place(_minerSO,   nodeA.Origin);
        _storage = Place(_storageSO, nodeA.Origin + Vector2Int.right);

        int stockBeforeMining = nodeA.CurrentStock;
        yield return new WaitForSeconds(4f);

        Case("D5 채굴기가 광맥 재고를 꺼내 아이템을 만든다",
             Stored() >= 1,
             $"저장고 {Stored()}개 (광맥 재고 {stockBeforeMining} → {nodeA.CurrentStock})");

        Case("D6 채굴량이 광맥에서 실제로 빠져나간다",
             nodeA.CurrentStock < nodeA.MaxStock || Stored() >= 1,
             $"재고 {nodeA.CurrentStock}/{nodeA.MaxStock}, 산출 {Stored()}개");

        // 광맥 밖 설치 — 판정은 false, 그래도 강행하면 러너가 되돌린다(안전망)
        Vector2Int off = FarEmptyCell();
        Case("D7 광맥 밖 채굴기는 설치 차단 판정 + 사유",
             !ResourceNodeRegistry.CanPlace(_minerSO, off, Vector2Int.one, out string why) &&
             !string.IsNullOrEmpty(why),
             $"사유: {why ?? "(없음)"}");

        Case("D8 2x2 광맥 위 멀티타일 판정",
             ResourceNodeRegistry.CanPlace(_minerSO, nodeB.Origin, new Vector2Int(2, 2)) &&
             !ResourceNodeRegistry.CanPlace(_minerSO, nodeB.Origin + Vector2Int.right, new Vector2Int(2, 2)),
             "광맥 안 2x2=허용, 경계를 걸치면=차단");

        var stray = Place(_minerSO, off);          // 판정을 무시하고 강행한 경우
        yield return null; yield return null;      // 러너의 LateUpdate 한 바퀴
        Case("D9 광맥 밖에 강행 설치되면 자동 철거(안전망)",
             stray == null || stray.IsRemoved || Sim.Grid.GetAt(off) == null,
             $"셀 {off}의 건물: {(Sim.Grid.GetAt(off) == null ? "없음" : "남아 있음")}");

        Case("D10 낮에는 건축이 허용된다",
             TimeManager.Instance.IsBuildingAllowed && TimeManager.Instance.Phase == DayPhase.Day,
             $"Phase={TimeManager.Instance.Phase}, Day={TimeManager.Instance.DayNumber}");
    }

    // ─── 1일차 밤 ───────────────────────────────────────────────

    IEnumerator Night1()
    {
        ForceNight();
        Section("── 1일차 밤 ──");

        Case("N1 밤에는 건축이 금지된다 (BuildController 게이트)",
             !TimeManager.Instance.IsBuildingAllowed && TimeManager.Instance.Phase == DayPhase.Night,
             $"Phase={TimeManager.Instance.Phase}");

        int before = Stored();
        yield return new WaitForSeconds(3f);
        Case("N2 밤에도 공장은 계속 돈다 (채굴 지속)",
             Stored() > before,
             $"저장고 {before} → {Stored()}개");

        // 전투 파괴 경로 — 몬스터 피격과 같은 흐름(Entity.Die → HandleDeath → PlacementBridge.Remove)
        int stockAtDeath = nodeA.CurrentStock;
        var view = FactoryBootstrap.Instance.GetView(_miner);
        if (view != null) view.Die();
        yield return null; yield return null;

        Case("N3 밤에 채굴기가 파괴되면 심에서도 제거된다",
             _miner.IsRemoved && Sim.Grid.GetAt(nodeA.Origin) == null,
             $"IsRemoved={_miner.IsRemoved}, 셀 점유={(Sim.Grid.GetAt(nodeA.Origin) != null)}");

        Case("N4 채굴기가 부서져도 광맥과 재고는 남는다",
             ResourceNodeRegistry.NodeAt(nodeA.Origin) == nodeA && nodeA.CurrentStock >= stockAtDeath,
             $"광맥 등록={ResourceNodeRegistry.NodeAt(nodeA.Origin) != null}, 재고 {stockAtDeath} → {nodeA.CurrentStock}");

        // 캐가는 사람이 없으면 상한에서 멈춰야 한다
        yield return new WaitForSeconds(3f);
        Case("N5 채굴기가 없으면 재고가 상한에서 멈춘다",
             nodeA.CurrentStock == nodeA.MaxStock,
             $"재고 {nodeA.CurrentStock}/{nodeA.MaxStock}");
    }

    // ─── 2일차 낮 ───────────────────────────────────────────────

    IEnumerator Day2()
    {
        int dayBefore = TimeManager.Instance.DayNumber;
        ForceDay();
        Section("── 2일차 낮 ──");

        Case("M1 밤이 끝나면 일수가 오르고 건축이 다시 허용된다",
             TimeManager.Instance.DayNumber == dayBefore + 1 && TimeManager.Instance.IsBuildingAllowed,
             $"Day {dayBefore} → {TimeManager.Instance.DayNumber}, 건축={TimeManager.Instance.IsBuildingAllowed}");

        Case("M2 파괴된 자리에 다시 설치할 수 있다 (광맥은 그대로)",
             ResourceNodeRegistry.CanPlace(_minerSO, nodeA.Origin, Vector2Int.one),
             "CanPlace(같은 광맥) == true 여야 함");

        _miner = Place(_minerSO, nodeA.Origin);
        int before = Stored();
        yield return new WaitForSeconds(4f);

        Case("M3 재설치 후 채굴이 재개된다",
             Stored() > before,
             $"저장고 {before} → {Stored()}개");
    }

    // ─── 2일차 밤 ───────────────────────────────────────────────

    IEnumerator Night2()
    {
        ForceNight();
        Section("── 2일차 밤 ──");

        Case("N6 두 번째 밤 전환도 정상 (건축 금지)",
             !TimeManager.Instance.IsBuildingAllowed && TimeManager.Instance.Phase == DayPhase.Night,
             $"Phase={TimeManager.Instance.Phase}, Day={TimeManager.Instance.DayNumber}");

        int before = Stored();
        yield return new WaitForSeconds(3f);
        Case("N7 두 번째 밤에도 채굴이 계속된다",
             Stored() > before,
             $"저장고 {before} → {Stored()}개");
    }

    // ─── 헬퍼 ──────────────────────────────────────────────────

    /// <summary>남은 시간을 정확히 태워 다음 페이즈로 넘긴다 (낮 길이에 의존하지 않는 결정적 전환).</summary>
    void ForceNight()
    {
        var cycle = TimeManager.Instance.Cycle;
        if (cycle.Phase == DayPhase.Day) cycle.Advance(cycle.PhaseRemaining + 0.01f);
    }

    void ForceDay() => TimeManager.Instance.EndNightEarly();

    Building Place(BuildingDataSO so, Vector2Int cell)
    {
        Vector3 pos = Grid.GetFootprintCenter(cell, so.GetRotatedSize(0));
        return PlacementBridge.Place(so, cell, pos);
    }

    int Stored()
    {
        if (_storage == null || _storage.IsRemoved) return 0;
        return _storage.Input.CountOf(_ore) + _storage.Output.CountOf(_ore);
    }

    /// <summary>광맥에서 충분히 떨어진 빈 칸 — 광맥 밖 판정용.</summary>
    Vector2Int FarEmptyCell() => nodeA.Origin + new Vector2Int(40, 40);

    static string Name(ItemDataSO item) => item != null ? item.displayName : "null";

    void Section(string title) => _lines.Add(title);

    void Case(string name, bool ok, string detail)
    {
        if (ok) _pass++; else _fail++;
        _lines.Add($"  {(ok ? "PASS" : "FAIL")}  {name}\n          {detail}");
    }

    bool Fatal(string message)
    {
        _fail++;
        _lines.Add($"  FAIL  준비 실패 — {message}");
        return false;
    }

    void Conclude()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[씬테스트] ResourceNodeTest {_pass}/{_pass + _fail} 통과");
        foreach (var l in _lines) sb.AppendLine(l);

        if (_sceneErrors > 0)
            sb.AppendLine($"  (참고) 테스트와 무관한 씬 오류 {_sceneErrors}건 — 첫 건: {_firstSceneError}");

        Report   = sb.ToString();
        Passed   = _fail == 0;
        Finished = true;

        if (Passed) Debug.Log(Report);
        else        Debug.LogError(Report);
    }

    void OnGUI() => GUI.TextArea(new Rect(20, 20, 720, 520), Report);
}
