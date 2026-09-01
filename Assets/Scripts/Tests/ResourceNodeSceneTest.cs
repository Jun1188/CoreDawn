using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.DayTime;
using CoreDawn.Entities;
using CoreDawn.Factory;
using CoreDawn.Placement;
using CoreDawn.ResourceNodes;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Tests
{
    /// <summary>
    /// ResourceNodeTest 씬 통합 테스트 — 실제 씬(MainScene 복사본)에서 낮/밤을 오가며
    /// 광맥·채굴기 동작을 순서대로 검증한다. 헤드리스 스위트(ResourceNodeTests)가
    /// 심 로직만 본다면, 이쪽은 씬의 실제 배선(FactoryBootstrap·PlacementBridge·뷰·TimeManager)까지 본다.
    ///
    /// 실행:
    ///   에디터 — ResourceNodeTest 씬을 열고 플레이 (결과는 콘솔 로그로만 나온다 — 게임 화면을 가리지 않게)
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
        [SerializeField] private ResourceDepositView nodeA;
        [Tooltip("2x2 광맥 — 멀티타일 배치 판정용.")]
        [SerializeField] private ResourceDepositView nodeB;

        // CLI 러너가 폴링하는 결과 (도메인 리로드 후 플레이 세션 안에서만 살아 있으면 된다)
        public static bool   Finished { get; private set; }
        public static bool   Passed   { get; private set; }
        public static string Report   { get; private set; } = "(실행 중)";

        readonly List<string> _lines = new();
        int _pass, _fail;

        // 우리 케이스와 무관한 씬 자체의 예외(헤드리스에서 입력 장치 없음 등)를 따로 센다
        int _sceneErrors;
        string _firstSceneError;

        FactorySystem Factory => FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.Factory : null;
        static GridSystem _grid;
        static GridSystem Grid => _grid ??= (FindFirstObjectByType<PlacementSystem>() is { } p ? new GridSystem(p.CellSize, p.GridOrigin) : new GridSystem(1f, Vector3.zero));

        ItemDef _ore;
        EntityDef _minerDef, _storageDef;
        BuildingModule _miner, _storage;

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
            if (Factory == null)          return Fatal("씬에 FactoryBootstrap이 없습니다.");
            if (TimeManager.Instance == null) return Fatal("씬에 TimeManager가 없습니다 (낮/밤 테스트 불가).");
            if (nodeA == null || nodeB == null) return Fatal("광맥 참조가 비어 있습니다 (세팅 스크립트 확인).");

            _ore = nodeA.Resource;
            if (_ore == null) return Fatal("광맥 A에 자원 아이템이 없습니다.");

            var db = SimHost.Database;
            _minerDef   = db?.Entity(SimDatabase.IdOf(db.Pack, "entity", "miner"));
            _storageDef = db?.Entity(SimDatabase.IdOf(db.Pack, "entity", "storage"));
            if (_minerDef == null || _storageDef == null)
                return Fatal("팩에서 채굴기(miner)/저장고(storage) 정의를 찾지 못했습니다.");

            return true;
        }

        // ─── 1일차 낮 ───────────────────────────────────────────────

        IEnumerator Day1()
        {
            Section("── 1일차 낮 ──");

            Case("D1 공장이 칸의 자원을 안다 (광맥 색인)",
                 Factory.ResourceAt(nodeA.Cell) == _ore &&
                 Factory.ResourceAt(FarEmptyCell()) == null,
                 $"광맥 위={Name(Factory.ResourceAt(nodeA.Cell))}, 광맥 밖={Name(Factory.ResourceAt(FarEmptyCell()))}");

            Case("D2 광맥 뷰가 심의 광맥 엔티티를 들고 있고 공장에 색인됨",
                 nodeA.Deposit != null && nodeB.Deposit != null &&
                 Factory.DepositAt(nodeA.Cell) == nodeA.Deposit && Factory.DepositAt(nodeB.Cell) == nodeB.Deposit,
                 $"공장의 광맥 {Factory.Deposits.Count}개");

            Case("D3 광맥은 바닥나지 않는다 (매장량 없음)",
                 nodeA.Deposit != null && nodeA.Deposit.Extract(1) == 1 && nodeA.Deposit.Extract(1) == 1,
                 $"누적 채굴 {nodeA.TotalExtracted}");

            // 광맥 위 설치 — 실제 배치 경로(PlacementBridge)로 심 + 뷰를 함께 만든다
            Case("D4 광맥 위 채굴기는 설치 허용 판정",
                 Factory.CanPlace(_minerDef, nodeA.Cell, Vector2Int.one, out _),
                 "CanPlace(광맥 위) == true 여야 함");

            _miner   = Place(_minerDef,   nodeA.Cell);
            _storage = Place(_storageDef, nodeA.Cell + Vector2Int.right);

            int extractedBeforeMining = nodeA.TotalExtracted;
            yield return new WaitForSeconds(4f);

            Case("D5 채굴기가 광맥 재고를 꺼내 아이템을 만든다",
                 Stored() >= 1,
                 $"저장고 {Stored()}개 (누적 채굴 {extractedBeforeMining} → {nodeA.TotalExtracted})");

            Case("D6 캔 만큼 광맥의 누적 채굴량이 오른다",
                 nodeA.TotalExtracted > extractedBeforeMining,
                 $"누적 {extractedBeforeMining} → {nodeA.TotalExtracted}, 산출 {Stored()}개");

            // 광맥 밖 설치 — 판정은 false, 그래도 강행하면 러너가 되돌린다(안전망)
            Vector2Int off = FarEmptyCell();
            Case("D7 광맥 밖 채굴기는 설치 차단 판정 + 사유",
                 !Factory.CanPlace(_minerDef, off, Vector2Int.one, out string why) &&
                 !string.IsNullOrEmpty(why),
                 $"사유: {why ?? "(없음)"}");

            Case("D8 멀티타일 채굴기는 덮는 칸 전부가 광맥이어야 한다 (부분 덮기 금지)",
                 Factory.CanPlace(_minerDef, nodeB.Cell, Vector2Int.one, out _) &&
                 !Factory.CanPlace(_minerDef, nodeB.Cell, new Vector2Int(2, 2), out _),
                 "1x1=허용, 광맥 한 칸 위 2x2=차단");

            var stray = Place(_minerDef, off);          // 판정을 무시하고 강행한 경우(심 직접 배치)
            yield return null; yield return null;
            Case("D9 광맥 밖에 강행 설치된 채굴기는 캘 광맥이 없어 아무것도 하지 않는다",
                 stray != null && stray.Owner.Get<ExtractorModule>() is { } sm && sm.Deposits.Count == 0 && sm.Target == null,
                 $"덮는 광맥 {stray?.Owner.Get<ExtractorModule>()?.Deposits.Count ?? -1}개");
            if (stray != null) PlacementBridge.Remove(stray);

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
            int extractedAtDeath = nodeA.TotalExtracted;
            var view = FactoryBootstrap.Instance.GetView(_miner);
            var viewGO = view != null ? view.gameObject : null;
            if (view != null) view.Die();
            yield return null; yield return null;

            Case("N3 밤에 채굴기가 파괴되면 심에서도 제거된다",
                 _miner.IsRemoved && Factory.Grid.GetAt(nodeA.Cell) == null,
                 $"IsRemoved={_miner.IsRemoved}, 셀 점유={(Factory.Grid.GetAt(nodeA.Cell) != null)}");

            // 작업3의 생명주기 계약 ①: 심이 제거되면 씬 껍데기도 사라진다 (FactorySystem.Removed → Bootstrap)
            Case("N3b 심이 제거되면 씬 껍데기와 매핑도 사라진다",
                 viewGO == null && FactoryBootstrap.Instance.GetView(_miner) == null,
                 $"뷰 GameObject={(viewGO == null ? "파괴됨" : "남아 있음")}, " +
                 $"매핑={(FactoryBootstrap.Instance.GetView(_miner) == null ? "해제됨" : "남아 있음")}");

            Case("N4 채굴기가 부서져도 광맥은 남는다",
                 Factory.DepositAt(nodeA.Cell) == nodeA.Deposit && nodeA.TotalExtracted >= extractedAtDeath,
                 $"광맥 색인={Factory.DepositAt(nodeA.Cell) != null}, 누적 {extractedAtDeath} → {nodeA.TotalExtracted}");

            int extractedIdle = nodeA.TotalExtracted;
            yield return new WaitForSeconds(2f);
            Case("N5 채굴기가 없으면 아무것도 캐지 않는다",
                 nodeA.TotalExtracted == extractedIdle,
                 $"누적 {extractedIdle} → {nodeA.TotalExtracted}");
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
                 Factory.CanPlace(_minerDef, nodeA.Cell, Vector2Int.one, out _),
                 "CanPlace(같은 광맥) == true 여야 함");

            _miner = Place(_minerDef, nodeA.Cell);
            int before = Stored();
            yield return new WaitForSeconds(4f);

            Case("M3 재설치 후 채굴이 재개된다",
                 Stored() > before,
                 $"저장고 {before} → {Stored()}개");

            // 작업3의 생명주기 계약 ②: 껍데기만 사라져도 심이 그리드에 유령으로 남지 않는다.
            // (전투·철거를 거치지 않고 GameObject가 직접 파괴되는 경로)
            var orphanCell = nodeB.Cell;
            var orphan = Place(_minerDef, orphanCell);
            yield return null;
            var orphanView = FactoryBootstrap.Instance.GetView(orphan);
            if (orphanView != null) Destroy(orphanView.gameObject);
            yield return null; yield return null;

            Case("M4 껍데기가 직접 파괴되면 심도 함께 정리된다 (유령 방지)",
                 orphan.IsRemoved && Factory.Grid.GetAt(orphanCell) == null,
                 $"IsRemoved={orphan.IsRemoved}, 셀 {orphanCell} 점유={(Factory.Grid.GetAt(orphanCell) != null)}");
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

        /// <summary>
        /// 건물을 셀에 세운다. 높이는 마우스 배치와 같은 규약을 쓴다 —
        /// 그 자리의 표면(지면, 광맥 위라면 광맥 슬래브 윗면) + PlacementSystem.SurfaceLift.
        /// 덕분에 채굴기를 광맥에 지으면 하네스로 지어도 광맥 윗면에 올라앉는다.
        /// </summary>
        BuildingModule Place(EntityDef def, Vector2Int cell)
        {
            Vector2Int size = BuildingPorts.RotatedSize(def, 0);
            Vector3 pos = Grid.GetFootprintCenter(cell, size);
            pos.y = SurfaceTopAt(pos) + PlacementSystem.SurfaceLift(def, cell);

            return PlacementBridge.Place(def, cell, pos);
        }

        /// <summary>표면 y — Ground 레이어를 위에서 훑는다. 광맥 슬래브도 Ground라 광맥 위면 그 윗면이 나온다.</summary>
        static float SurfaceTopAt(Vector3 at)
        {
            int mask = LayerMask.GetMask("Ground");
            if (mask != 0 && Physics.Raycast(at + Vector3.up * 50f, Vector3.down,
                                             out RaycastHit hit, 100f, mask))
                return hit.point.y;
            return at.y;   // 지면 밖(광맥 밖 강행 설치 케이스 등) — 어차피 안전망이 철거한다
        }

        int Stored()
        {
            if (_storage == null || _storage.IsRemoved) return 0;
            return _storage.Input.CountOf(_ore) + _storage.Output.CountOf(_ore);
        }

        /// <summary>광맥에서 충분히 떨어진 빈 칸 — 광맥 밖 판정용.</summary>
        Vector2Int FarEmptyCell() => nodeA.Cell + new Vector2Int(40, 40);

        static string Name(ItemDef item) => item != null ? item.DisplayName : "null";

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

            CleanUpPlacedBuildings();
        }

        /// <summary>
        /// 검증이 세운 건물을 전부 걷어낸다.
        /// 테스트가 끝난 뒤에도 채굴기가 서 있으면 "짓지도 않았는데 자동으로 설치된" 것처럼 보인다 —
        /// 플레이어가 B키 빌드 메뉴로 직접 지을 때까지 광맥 위는 비어 있어야 한다.
        /// 채굴 진행 관찰은 ResourceNodeStatusLog가 씬 전체를 훑어 콘솔로 알려준다.
        /// </summary>
        void CleanUpPlacedBuildings()
        {
            int removed = 0;

            foreach (var b in new[] { _miner, _storage })
            {
                if (b == null || b.IsRemoved) continue;
                PlacementBridge.Remove(b);
                removed++;
            }

            _miner = _storage = null;
            Debug.Log($"[씬테스트] 검증 종료 — 테스트가 세운 건물 {removed}개를 철거했습니다. " +
                      "광맥은 비어 있습니다 (B키 빌드 메뉴에서 채굴기를 광맥 위에 지어 보세요).");
        }
    }
}
