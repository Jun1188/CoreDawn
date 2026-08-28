using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Navigation;
using CoreDawn.ResourceNodes;
using CoreDawn.Sim;

namespace CoreDawn.Tests
{
    /// <summary>
    /// ResourceNode(광맥) 특성화 테스트.
    ///
    /// 씬·플레이모드 없이 도는 것이 핵심이다 — 심(FactorySystem)은 plain C#이고
    /// 광맥은 에디트 모드에서도 AddComponent로 만들 수 있으므로, 에디터 배치모드에서
    /// 그대로 실행된다. 실행 경로 두 가지:
    ///   ① 에디터 메뉴  Tools/ResourceNode 테스트 실행   (Editor/ResourceNodeTestRunner.cs)
    ///   ② CLI          Unity.exe -batchmode -quit -executeMethod ResourceNodeTestRunner.RunFromCLI
    /// 플레이모드에서 확인하고 싶으면 빈 오브젝트에 <see cref="ResourceNodeTestBehaviour"/>를 붙이면 된다.
    ///
    /// 검증 대상: 생산 주기/상한 · TryExtract · 레지스트리 색인 · 배치 판정(CanPlace) ·
    ///            채굴기↔재고 연동(재고만큼만 캐고 비면 대기).
    /// </summary>
    public static class ResourceNodeTests
    {
        // ─── 결과 수집 ──────────────────────────────────────────────

        static readonly List<(string name, bool pass, string detail)> _results = new();
        static readonly List<string> _fails = new();
        static readonly List<ScriptableObject> _createdSOs = new();
        static readonly List<GameObject> _createdGOs = new();

        static FactorySystem _sim;
        static ItemDataSO _ore, _coal;

        /// <summary>전체 스위트 실행. 반환값 = 전부 통과했는가. report에 사람이 읽는 결과표.</summary>
        public static bool RunAll(out string report)
        {
            _results.Clear();

            _ore  = MakeItem("TestOre",  ItemType.Ore);
            _coal = MakeItem("TestCoal", ItemType.Ore);

            Run("1. 주기마다 amountPerCycle씩 생산",   S1_Production);
            Run("2. maxStock에서 생산 정지·인출 후 재개", S2_StockCap);
            Run("3. TryExtract 부분 인출 / 재고 0",     S3_TryExtract);
            Run("4. 레지스트리 색인·해제 (멀티타일)",    S4_Registry);
            Run("5. 배치 판정 CanPlace",               S5_CanPlace);
            Run("6. 채굴기가 광맥 재고를 꺼내간다",       S6_MinerConsumesStock);
            Run("7. 재고 0이면 대기, 회복되면 재개",      S7_MinerWaitsAndResumes);
            Run("8. 광맥 밖 채굴기는 아무것도 캐지 않음",  S8_MinerOffNode);
            Run("9. 소켓 미주입이면 기존 무한 채굴 (회귀)", S9_NoHookIsLegacyBehavior);
            Run("10. 한 광맥의 채굴기 여러 대가 재고를 나눠 씀", S10_MultipleMinersShareStock);

            Cleanup();

            int passed = 0;
            foreach (var r in _results) if (r.pass) passed++;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[ResourceNodeTests] {passed}/{_results.Count} 통과");
            foreach (var (name, pass, detail) in _results)
            {
                sb.AppendLine($"  {(pass ? "PASS" : "FAIL")}  {name}");
                if (!pass) sb.AppendLine("        " + detail.Replace("\n", "\n        "));
            }
            report = sb.ToString();
            return passed == _results.Count;
        }

        /// <summary>시나리오 1개를 격리 실행 — 새 심 + 빈 레지스트리에서 시작한다.</summary>
        static void Run(string name, Action scenario)
        {
            _fails.Clear();
            ClearNodes();

            _sim = new FactorySystem(new EntityWorld(), GridGeometry.Unit, tps: 10f);
            ResourceNodeRegistry.HookSim(_sim);
            ResourceNodeRegistry.EnforceMinerPlacement = false;   // 철거는 플레이모드 러너의 일

            try { scenario(); }
            catch (Exception e) { _fails.Add("예외 발생:\n" + e); }

            _results.Add((name, _fails.Count == 0, string.Join("\n", _fails)));
            _sim = null;
        }

        // ─── 시나리오 ───────────────────────────────────────────────

        /// <summary>interval마다 amountPerCycle씩 정확히 쌓인다.</summary>
        static void S1_Production()
        {
            var node = Node(_ore, cell: new Vector2Int(0, 0), interval: 1f, amount: 2, max: 100);

            Produce(0f);                       // 첫 정산 = 클럭 동기화 (아직 생산 없음)
            Expect(node.CurrentStock == 0, $"동기화 직후엔 0이어야 함 (실제 {node.CurrentStock})");

            Produce(1f);
            Expect(node.CurrentStock == 2, $"1주기 후 2개 (실제 {node.CurrentStock})");

            Produce(3f);                       // 두 주기가 한 번에 밀려도 둘 다 정산
            Expect(node.CurrentStock == 6, $"3초 후 6개 (실제 {node.CurrentStock})");

            Produce(3.5f);                     // 주기 중간엔 변화 없음
            Expect(node.CurrentStock == 6, $"주기 중간엔 그대로 6개 (실제 {node.CurrentStock})");
        }

        /// <summary>상한에서 멈추고, 꺼내가면 다시 쌓인다.</summary>
        static void S2_StockCap()
        {
            var node = Node(_ore, new Vector2Int(0, 0), interval: 1f, amount: 3, max: 5);

            Produce(0f);
            Produce(10f);
            Expect(node.CurrentStock == 5, $"상한 5에서 멈춰야 함 (실제 {node.CurrentStock})");
            Expect(node.IsFull, "IsFull이어야 함");

            node.TryExtract(5, out _);
            Expect(node.CurrentStock == 0, "전부 꺼내면 0");

            Produce(11f);
            Expect(node.CurrentStock == 3, $"인출 후 다음 주기부터 재개 (실제 {node.CurrentStock})");
        }

        /// <summary>부족하면 있는 만큼, 0이면 false.</summary>
        static void S3_TryExtract()
        {
            var node = Node(_ore, new Vector2Int(0, 0), interval: 999f, max: 100, stock: 5);

            Expect(node.TryExtract(3, out int t1) && t1 == 3 && node.CurrentStock == 2,
                   $"3개 요청 → 3개 (taken {t1}, 남은 {node.CurrentStock})");

            Expect(node.TryExtract(10, out int t2) && t2 == 2 && node.CurrentStock == 0,
                   $"재고보다 많이 요청하면 있는 만큼 (taken {t2}, 남은 {node.CurrentStock})");

            Expect(!node.TryExtract(1, out int t3) && t3 == 0,
                   $"재고 0이면 false + taken 0 (실제 {t3})");

            Expect(node.Extract(1) == 0, "Extract 간편형도 0");
        }

        /// <summary>2x2 광맥이 4칸 전부를 점유하고, 비활성화하면 전부 해제된다.</summary>
        static void S4_Registry()
        {
            var node = Node(_ore, new Vector2Int(2, 3), size: new Vector2Int(2, 2), interval: 999f);

            Expect(node.Origin == new Vector2Int(2, 3),
                   $"풋프린트 중앙 배치 → Origin (2,3) (실제 {node.Origin})");

            foreach (var c in new[] { new Vector2Int(2, 3), new Vector2Int(3, 3),
                                      new Vector2Int(2, 4), new Vector2Int(3, 4) })
                Expect(ResourceNodeRegistry.NodeAt(c) == node, $"셀 {c}가 색인돼야 함");

            Expect(ResourceNodeRegistry.NodeAt(new Vector2Int(4, 3)) == null, "풋프린트 밖은 null");
            Expect(ResourceNodeRegistry.ResourceAt(new Vector2Int(2, 3)) == _ore, "ResourceAt이 자원을 돌려줘야 함");

            // 생명주기 배선(OnEnable/OnDisable)은 플레이모드에서만 검증할 수 있다 —
            // ExecuteAlways가 아닌 MonoBehaviour는 에디트 모드에서 콜백이 돌지 않기 때문.
            if (Application.isPlaying)
            {
                var auto = new GameObject("AutoRegisterNode") { hideFlags = HideFlags.HideAndDontSave };
                _createdGOs.Add(auto);
                auto.transform.position = ResourceNodeRegistry.Grid.GridToWorldCenter(new Vector2Int(7, 7));
                var autoNode = auto.AddComponent<ResourceNode>();   // OnEnable → Register

                Expect(ResourceNodeRegistry.NodeAt(new Vector2Int(7, 7)) == autoNode,
                       "OnEnable만으로 장부에 등록돼야 함");

                auto.SetActive(false);                              // OnDisable → Unregister
                Expect(ResourceNodeRegistry.NodeAt(new Vector2Int(7, 7)) == null,
                       "비활성화하면 장부에서 빠져야 함 (유령 방지)");
            }

            // 해제 경로 자체는 모드와 무관하게 검증 (OnDisable이 부르는 바로 그 호출)
            ResourceNodeRegistry.Unregister(node);
            Expect(ResourceNodeRegistry.NodeAt(new Vector2Int(2, 3)) == null &&
                   ResourceNodeRegistry.NodeAt(new Vector2Int(3, 4)) == null,
                   "해제하면 점유 셀 전부가 장부에서 빠져야 함 (유령 방지)");
        }

        /// <summary>채굴기만 광맥 규칙을 받고, 풋프린트 전체가 같은 광맥이어야 한다.</summary>
        static void S5_CanPlace()
        {
            Node(_ore,  new Vector2Int(0, 0), size: new Vector2Int(2, 1), interval: 999f);
            Node(_coal, new Vector2Int(2, 0), interval: 999f);

            var miner   = Miner();
            var storage = Storage();

            Expect(ResourceNodeRegistry.CanPlace(storage, new Vector2Int(9, 9), Vector2Int.one),
                   "채굴기가 아니면 광맥과 무관하게 통과해야 함");

            Expect(ResourceNodeRegistry.CanPlace(miner, new Vector2Int(0, 0), Vector2Int.one),
                   "광맥 위 채굴기는 통과");

            Expect(!ResourceNodeRegistry.CanPlace(miner, new Vector2Int(5, 5), Vector2Int.one, out string r1)
                   && !string.IsNullOrEmpty(r1),
                   "광맥 밖 채굴기는 차단 + 사유 문자열");

            Expect(!ResourceNodeRegistry.CanPlace(miner, new Vector2Int(1, 0), new Vector2Int(2, 1), out string r2)
                   && r2 != null && r2.Contains("다른 광맥"),
                   $"서로 다른 광맥에 걸치면 차단 (사유: {r2 ?? "없음"})");

            Expect(ResourceNodeRegistry.CanPlace(miner, new Vector2Int(0, 0), new Vector2Int(2, 1)),
                   "같은 광맥 안이면 2칸 채굴기도 통과");
        }

        /// <summary>핵심: 채굴기가 광맥 재고를 꺼내가고, 재고 총량 이상은 캐지 못한다.</summary>
        static void S6_MinerConsumesStock()
        {
            var node = Node(_ore, new Vector2Int(0, 0), interval: 999f, max: 100, stock: 3);

            _sim.Place(Miner(ptime: 0.2f), new Vector2Int(0, 0));
            var store = _sim.Place(Storage(), new Vector2Int(1, 0));

            RunSim(5f);   // 재생산 없음(interval 999) → 재고 3개가 한계

            Expect(node.CurrentStock == 0, $"광맥 재고가 소진돼야 함 (실제 {node.CurrentStock})");
            Expect(Stored(store, _ore) == 3,
                   $"저장고에 정확히 재고만큼만 도착해야 함 (실제 {Stored(store, _ore)}개)");
        }

        /// <summary>재고가 비면 대기하다가, 다시 쌓이면 알아서 재개한다.</summary>
        static void S7_MinerWaitsAndResumes()
        {
            var node = Node(_ore, new Vector2Int(0, 0), interval: 1f, amount: 1, max: 2, stock: 1);

            _sim.Place(Miner(ptime: 0.2f), new Vector2Int(0, 0));
            var store = _sim.Place(Storage(), new Vector2Int(1, 0));

            RunSim(0.5f);                                   // 초기 재고 1개만 채굴
            int early = Stored(store, _ore);
            Expect(early == 1, $"처음엔 재고 1개만 캐야 함 (실제 {early}개)");

            RunSim(4f);                                     // 광맥이 1초마다 1개씩 재생산
            int later = Stored(store, _ore);
            Expect(later > early, $"재고가 회복되면 채굴이 재개돼야 함 ({early} → {later}개)");
            Expect(later <= early + 5,
                   $"생산 속도보다 빨리 캘 수는 없어야 함 (4초 동안 최대 4~5개, 실제 {later - early}개)");
        }

        /// <summary>광맥이 없는 칸의 채굴기는 아예 대상이 없어 아무것도 생산하지 않는다.</summary>
        static void S8_MinerOffNode()
        {
            Node(_ore, new Vector2Int(0, 0), interval: 999f, stock: 50);

            _sim.Place(Miner(ptime: 0.2f), new Vector2Int(5, 5));       // 광맥 밖
            var store = _sim.Place(Storage(), new Vector2Int(6, 5));

            RunSim(5f);
            Expect(Stored(store, _ore) == 0,
                   $"광맥 밖 채굴기는 생산이 없어야 함 (실제 {Stored(store, _ore)}개)");
        }

        /// <summary>
        /// 광맥을 안 쓰는 기존 씬·테스트(FactoryScenarioTests)의 회귀 방지.
        /// TryExtractResourceAt을 주입하지 않으면 채굴기는 재고 개념 없이 계속 캐야 한다.
        /// </summary>
        static void S9_NoHookIsLegacyBehavior()
        {
            _sim.GetResourceAt        = _ => _ore;   // 예전 방식 — 위치와 무관하게 광석
            _sim.TryExtractResourceAt = null;        // 재고 소켓 없음

            _sim.Place(Miner(ptime: 0.2f), new Vector2Int(0, 0));
            var store = _sim.Place(Storage(), new Vector2Int(1, 0));

            RunSim(3f);
            Expect(Stored(store, _ore) >= 10,
                   $"소켓이 없으면 시간에 비례해 계속 생산돼야 함 (3초/0.2초 ≈ 15개, 실제 {Stored(store, _ore)}개)");
        }

        /// <summary>
        /// 멀티타일 광맥 위에 채굴기 두 대를 올리면 재고 하나를 나눠 쓴다 —
        /// 둘이 합쳐 재고 총량을 넘길 수 없어야 한다 (칸마다 재고가 따로 생기면 안 됨).
        /// </summary>
        static void S10_MultipleMinersShareStock()
        {
            var node = Node(_ore, new Vector2Int(0, 0), size: new Vector2Int(2, 2),
                            interval: 999f, max: 100, stock: 4);

            _sim.Place(Miner(ptime: 0.2f), new Vector2Int(0, 0));
            var storeA = _sim.Place(Storage(), new Vector2Int(1, 0));
            _sim.Place(Miner(ptime: 0.2f), new Vector2Int(0, 1));
            var storeB = _sim.Place(Storage(), new Vector2Int(1, 1));

            RunSim(5f);   // 재생산 없음 → 초기 재고 4개가 둘의 총 한계

            int total = Stored(storeA, _ore) + Stored(storeB, _ore);
            Expect(total == 4, $"두 채굴기의 산출 합이 광맥 재고와 같아야 함 (실제 {total}개)");
            Expect(node.CurrentStock == 0, $"광맥 재고가 소진돼야 함 (실제 {node.CurrentStock})");
            Expect(Stored(storeA, _ore) > 0 && Stored(storeB, _ore) > 0,
                   $"둘 다 조금씩은 캐야 함 (A:{Stored(storeA, _ore)}, B:{Stored(storeB, _ore)})");
        }

        // ─── 헬퍼 ──────────────────────────────────────────────────

        static void Expect(bool condition, string message)
        {
            if (!condition) _fails.Add(message);
        }

        /// <summary>심을 simSeconds만큼 진행하며, 매 틱 광맥 생산도 같은 시계로 정산한다(러너와 동일).</summary>
        static void RunSim(float simSeconds)
        {
            int ticks = Mathf.CeilToInt(simSeconds / 0.1f);
            for (int i = 0; i < ticks; i++)
            {
                _sim.Advance(0.1f);
                ResourceNodeRegistry.TickProduction(_sim.Now);
            }
        }

        static void Produce(float now) => ResourceNodeRegistry.TickProduction(now);

        static int Stored(Building store, ItemDataSO item)
            => store.Input.CountOf(item) + store.Output.CountOf(item);

        /// <summary>광맥 하나를 만들어 지정 셀에 정확히 얹는다 (셀 중앙 = 풋프린트 중앙).</summary>
        static ResourceNode Node(ItemDataSO item, Vector2Int cell, Vector2Int size = default,
                                 float interval = 1f, int amount = 1, int max = 20, int stock = 0)
        {
            if (size == default) size = Vector2Int.one;

            var go = new GameObject($"ResourceNode_{item.displayName}_{cell.x}_{cell.y}")
            { hideFlags = HideFlags.HideAndDontSave };
            _createdGOs.Add(go);

            var node = go.AddComponent<ResourceNode>();
            Set(node, "resource", item);
            Set(node, "size", size);
            Set(node, "productionInterval", interval);
            Set(node, "amountPerCycle", amount);
            Set(node, "maxStock", max);
            Set(node, "initialStock", stock);
            Set(node, "currentStock", stock);
            Set(node, "snapToGrid", false);

            // 위치는 필드를 다 채운 뒤에 — 컴포넌트 추가 시 도는 OnValidate의 자동 정렬이
            // 아직 1x1로 알고 있는 상태에서 좌표를 흔드는 것을 피한다.
            node.transform.position = ResourceNodeRegistry.Grid.GetFootprintCenter(cell, size);

            node.Refresh();   // 인스펙터 값이 정해진 뒤 점유 셀을 다시 잡는다
            return node;
        }

        /// <summary>[SerializeField] private 필드를 테스트에서 채운다 (캡슐화는 유지).</summary>
        static void Set(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) throw new Exception($"ResourceNode에 '{field}' 필드가 없습니다 — 테스트를 갱신하세요.");
            f.SetValue(target, value);
        }

        static void ClearNodes()
        {
            foreach (var go in _createdGOs)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _createdGOs.Clear();
        }

        static void Cleanup()
        {
            ClearNodes();
            foreach (var so in _createdSOs)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _createdSOs.Clear();
            ResourceNodeRegistry.EnforceMinerPlacement = true;
        }

        // ─── SO 생성 (FactoryScenarioTests와 같은 방식) ──────────────

        static BuildingDataSO Miner(float ptime = 0.2f, int outBuf = 5)
        {
            var so = MakeBuilding<MinerDataSO>("TestMiner",
                new[] { Port(false, Direction.East) }, stackCap: outBuf);
            // 채굴 시간 = 광맥 기준 ÷ 배율 → ptime초를 원하면 배율은 1/ptime
            so.speedMultiplier = 1f / Mathf.Max(0.01f, ptime);
            return so;
        }

        static BuildingDataSO Storage() =>
            MakeBuilding<StorageDataSO>("TestStorage",
                new[] { Port(true, Direction.West) }, stackCap: 50);

        static PortDefinition Port(bool isInput, Direction dir) =>
            new() { IsInput = isInput, Direction = dir, LocalOffset = Vector2Int.zero };

        static T MakeBuilding<T>(string name, PortDefinition[] ports, int stackCap = 10)
            where T : BuildingDataSO
        {
            var so = ScriptableObject.CreateInstance<T>();
            so.name           = name;
            so.displayName    = name;
            so.size           = Vector2Int.one;
            so.ports          = ports;
            so.inputSlots     = 1;
            so.outputSlots    = 1;
            so.bufferStackCap = stackCap;
            _createdSOs.Add(so);
            return so;
        }

        static ItemDataSO MakeItem(string name, ItemType type)
        {
            var so = ScriptableObject.CreateInstance<ItemDataSO>();
            so.displayName = name;
            so.type = type;
            _createdSOs.Add(so);
            return so;
        }
    }
}
