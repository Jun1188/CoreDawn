using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Managers;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Tests
{
    /// <summary>
    /// 광맥(ResourceDepositModule) + 공장 색인 + 채굴기 특성화 테스트 — 씬·GameObject·플레이모드 없이 심만으로 돈다.
    ///
    /// 검증 대상: 공장 색인(PlaceDeposit/DepositAt/RemoveDeposit) · 배치 판정(CanPlace: 부분 덮기 금지·자원 혼합 금지) ·
    /// 채굴기가 난이도÷배율 주기로 캔다 · 광맥 밖 채굴기 · 2×2 채굴기의 돌아가며 기록 · 누적 채굴량.
    /// 매장량은 없다(광맥은 바닥나지 않는다).
    ///
    /// 실행: Tools ▸ ResourceNode 테스트 실행 (ResourceNodeTestRunner) 또는 ResourceNodeTestBehaviour.
    /// </summary>
    public static class ResourceNodeTests
    {
        static readonly List<(string name, bool pass, string detail)> _results = new();
        static readonly List<string> _fails = new();
        static FactorySystem _sim;
        static ItemDef _ore, _coal;

        /// <summary>전체 스위트 실행. 반환값 = 전부 통과했는가. report에 사람이 읽는 결과표.</summary>
        public static bool RunAll(out string report)
        {
            _results.Clear();
            // 광맥 정의는 팩 아이템을 캔다 — 실제 팩 아이템을 쓴다(정의는 코드로 조립)
            _ore  = PackItem("coredawn:item/iron_ore");
            _coal = PackItem("coredawn:item/copper_ore");

            Run("1. 공장 색인 — 놓기·조회·해제·겹침 거부",         S1_Index);
            Run("2. 배치 판정 CanPlace (부분 덮기·혼합 금지)",     S2_CanPlace);
            Run("3. 채굴기는 난이도÷배율 주기로 캔다",             S3_MinerRate);
            Run("4. 광맥 밖 채굴기는 아무것도 캐지 않음",          S4_MinerOffDeposit);
            Run("5. 2×2 채굴기는 덮는 광맥 넷에 돌아가며 기록",     S5_BigMinerRoundRobin);
            Run("6. 손 채굴과 채굴기가 같은 누적 채굴량을 쌓는다",  S6_TotalExtracted);

            int passed = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var (name, pass, detail) in _results)
            {
                if (pass) passed++;
                sb.AppendLine($"  {(pass ? "PASS" : "FAIL")}  {name}");
                if (!pass) sb.AppendLine("        " + detail.Replace("\n", "\n        "));
            }
            report = $"[ResourceNodeTests] {passed}/{_results.Count} 통과\n" + sb;
            return passed == _results.Count;
        }

        static void Run(string name, Action scenario)
        {
            _sim = new FactorySystem(new EntityWorld(), GridGeometry.Unit, tps: 10f);
            _fails.Clear();
            try { scenario(); }
            catch (Exception e) { _fails.Add("예외 발생:\n" + e); }
            _results.Add((name, _fails.Count == 0, string.Join("\n", _fails)));
            _sim = null;
        }

        // ─── 시나리오 ────────────────────────────────────────

        static void S1_Index()
        {
            var dep = Deposit(_ore, new Vector2Int(2, 3));
            Expect(dep.Cell == new Vector2Int(2, 3), $"놓인 칸 (2,3) (실제 {dep.Cell})");
            Expect(_sim.DepositAt(new Vector2Int(2, 3)) == dep, "칸으로 조회돼야 함");
            Expect(_sim.DepositAt(new Vector2Int(3, 3)) == null, "옆 칸은 null — 광맥은 한 칸짜리");
            Expect(_sim.ResourceAt(new Vector2Int(2, 3)) == _ore, "ResourceAt이 자원을 돌려줘야 함");
            Expect(dep.Owner.Health == null, "광맥은 Health가 없다(부서지지 않음)");
            Expect(_sim.Deposits.Count == 1, $"광맥 목록 1개 (실제 {_sim.Deposits.Count})");
            bool threw = false;
            try { Deposit(_coal, new Vector2Int(2, 3)); } catch (InvalidOperationException) { threw = true; }
            Expect(threw, "이미 광맥이 있는 칸에 또 놓으면 예외 — 조용히 덮지 않는다");
            _sim.RemoveDeposit(new Vector2Int(2, 3));
            Expect(_sim.DepositAt(new Vector2Int(2, 3)) == null && _sim.Deposits.Count == 0 && (dep.Owner == null || dep.Owner.IsRemoved),
                   "해제하면 색인에서 빠지고 엔티티도 사라져야 함 (유령 방지)");
        }

        static void S2_CanPlace()
        {
            Deposit(_ore,  new Vector2Int(0, 0));
            Deposit(_ore,  new Vector2Int(1, 0));
            Deposit(_coal, new Vector2Int(2, 0));
            var miner   = Miner();
            var storage = Storage();
            Expect(_sim.CanPlace(storage, new Vector2Int(9, 9), Vector2Int.one, out _),
                   "채굴기가 아니면 광맥과 무관하게 통과해야 함");
            Expect(!_sim.CanPlace(storage, new Vector2Int(0, 0), Vector2Int.one, out string r0) && r0 != null,
                   "채굴기가 아닌 건물은 광맥을 덮을 수 없음");
            Expect(_sim.CanPlace(miner, new Vector2Int(0, 0), Vector2Int.one, out _),
                   "광맥 위 채굴기는 통과");
            Expect(!_sim.CanPlace(miner, new Vector2Int(5, 5), Vector2Int.one, out string r1) && !string.IsNullOrEmpty(r1),
                   "광맥 밖 채굴기는 차단 + 사유 문자열");
            Expect(_sim.CanPlace(miner, new Vector2Int(0, 0), new Vector2Int(2, 1), out _),
                   "같은 자원의 광맥 두 칸을 덮으면 2칸 채굴기도 통과");
            Expect(!_sim.CanPlace(miner, new Vector2Int(1, 0), new Vector2Int(2, 1), out string r2) && r2 != null && r2.Contains("다른"),
                   $"서로 다른 자원의 광맥에 걸치면 차단 (사유: {r2 ?? "없음"})");
            Expect(!_sim.CanPlace(miner, new Vector2Int(2, 0), new Vector2Int(2, 1), out string r3) && r3 != null,
                   $"덮는 칸 중 하나라도 광맥이 아니면 차단 — 부분 덮기 금지 (사유: {r3 ?? "없음"})");
        }

        static void S3_MinerRate()
        {
            var dep = Deposit(_ore, new Vector2Int(0, 0), extractInterval: 1f);
            _sim.Place(Miner(ptime: 0.5f), new Vector2Int(0, 0));   // 배율 2 → 0.5초에 1개
            var store = _sim.Place(Storage(), new Vector2Int(1, 0));
            RunSim(3f);
            int n = Stored(store, _ore);
            Expect(n >= 5 && n <= 6, $"3초 ÷ 0.5초 ≈ 6개 (실제 {n}개)");
            Expect(dep.TotalExtracted == n, $"캔 만큼 광맥에 누적돼야 함 (누적 {dep.TotalExtracted}, 저장 {n})");
            var hard = Deposit(_ore, new Vector2Int(0, 2), extractInterval: 2f);   // 난이도 2배
            _sim.Place(Miner(ptime: 0.5f), new Vector2Int(0, 2));
            var store2 = _sim.Place(Storage(), new Vector2Int(1, 2));
            RunSim(3f);
            int m = Stored(store2, _ore);
            Expect(m >= 2 && m <= 3, $"난이도 2배 광맥은 절반 속도 — 3초에 ≈3개 (실제 {m}개)");
        }

        static void S4_MinerOffDeposit()
        {
            Deposit(_ore, new Vector2Int(0, 0));
            var miner = _sim.Place(Miner(ptime: 0.2f), new Vector2Int(5, 5));   // 광맥 밖 — 규칙을 거치지 않는 심 직접 배치
            var store = _sim.Place(Storage(), new Vector2Int(6, 5));
            RunSim(5f);
            Expect(miner.Owner.Get<ExtractorModule>().Deposits.Count == 0 && miner.Owner.Get<ExtractorModule>().Target == null, "덮는 광맥이 없어야 함");
            Expect(Stored(store, _ore) == 0,
                   $"광맥 밖 채굴기는 생산이 없어야 함 (실제 {Stored(store, _ore)}개)");
        }

        /// <summary>2×2 채굴기가 광맥 넷 위에 — 캔 것이 넷에 돌아가며 기록된다.</summary>
        static void S5_BigMinerRoundRobin()
        {
            var deps = new[]
            {
                Deposit(_ore, new Vector2Int(0, 0)), Deposit(_ore, new Vector2Int(1, 0)),
                Deposit(_ore, new Vector2Int(0, 1)), Deposit(_ore, new Vector2Int(1, 1)),
            };
            var miner = _sim.Place(BigMiner(ptime: 0.25f), new Vector2Int(0, 0));   // 2×2, 출력 East (2,0)
            var store = _sim.Place(Storage(), new Vector2Int(2, 0));
            Expect(miner.Owner.Get<ExtractorModule>().Deposits.Count == 4, $"덮는 광맥 4개를 잡아야 함 (실제 {miner.Owner.Get<ExtractorModule>().Deposits.Count})");
            RunSim(2.1f);   // 0.25초에 1개 — 기상이 0.1초 틱에 정렬되므로 6~8개
            int total = Stored(store, _ore);
            Expect(total >= 6 && total <= 8, $"2.1초 ÷ 0.25초 ≈ 6~8개 (실제 {total}개)");
            int min = int.MaxValue, max = 0, sum = 0;
            foreach (var d in deps) { min = Math.Min(min, d.TotalExtracted); max = Math.Max(max, d.TotalExtracted); sum += d.TotalExtracted; }
            Expect(sum == total && max - min <= 1, $"넷에 고르게 기록돼야 함 (합 {sum}, 최소 {min}, 최대 {max})");
        }

        static void S6_TotalExtracted()
        {
            var a = Deposit(_ore, new Vector2Int(0, 0));
            Expect(a.Extract(3) == 3 && a.TotalExtracted == 3, "손 채굴 3개 → 누적 3");
            Expect(a.Extract(0) == 0 && a.TotalExtracted == 3, "0개 요청은 아무것도 하지 않는다");
            _sim.Place(Miner(ptime: 0.5f), new Vector2Int(0, 0));
            _sim.Place(Storage(), new Vector2Int(1, 0));
            RunSim(1.1f);   // 2개
            Expect(a.TotalExtracted == 5, $"채굴기 2개가 같은 누적에 더해져야 함 (실제 {a.TotalExtracted})");
            a.RestoreState(40);
            Expect(a.TotalExtracted == 40, "세이브 복원은 누적을 그대로 놓는다");
        }

        // ─── 헬퍼 ────────────────────────────────────────────

        static void Expect(bool condition, string message)
        {
            if (!condition) _fails.Add(message);
        }

        static void RunSim(float simSeconds)
        {
            int ticks = Mathf.CeilToInt(simSeconds / 0.1f);
            for (int i = 0; i < ticks; i++) _sim.Advance(0.1f);
        }

        static int Stored(BuildingModule store, ItemDef item)
            => store.Input.CountOf(item) + store.Output.CountOf(item);

        /// <summary>광맥 하나를 칸에 놓는다(정의는 코드로 조립 — 팩 json과 같은 타입).</summary>
        static ResourceDepositModule Deposit(ItemDef item, Vector2Int cell, float extractInterval = 1f)
        {
            var def = new EntityDef { Id = $"test:entity/{item.DisplayName.ToLowerInvariant()}_deposit_{cell.x}_{cell.y}", DisplayName = item.DisplayName + " 광맥", Faction = Faction.Neutral };
            def.Modules.Add(new ResourceDepositModuleDef { Resource = item, ExtractInterval = extractInterval });
            return _sim.PlaceDeposit(def, cell).Get<ResourceDepositModule>();
        }

        static ItemDef PackItem(string id)
        {
            if (SimHost.Database == null) SimHost.DatabaseLoader = () => PackLoader.Load();   // 에디터 러너에는 BeforeSceneLoad 등록이 없다
            var item = SimHost.Database?.Item(id);
            if (item == null) throw new Exception($"팩 아이템 '{id}'를 찾지 못했습니다 — StreamingAssets/packs/coredawn/data.json 확인");
            return item;
        }

        // ─── 정의 생성 (FactoryScenarioTests와 같은 방식 — 팩 json과 같은 타입을 코드로 조립) ──
        static EntityDef Miner(float ptime = 0.2f, int outBuf = 5)
            // 채굴 시간 = 광맥 기준(1초) ÷ 배율 → ptime초를 원하면 배율은 1/ptime
            => MakeBuilding("TestMiner", new[] { Port(false, Direction.East) }, stackCap: outBuf, size: 1,
                            extra: new ExtractorModuleDef { SpeedMultiplier = 1f / Mathf.Max(0.01f, ptime) });

        static EntityDef BigMiner(float ptime = 0.2f, int outBuf = 5)
            => MakeBuilding("TestMinerMk2", new[] { new PortDef { X = 1, Y = 0, Dir = "East", IsInput = false } }, stackCap: outBuf, size: 2,
                            extra: new ExtractorModuleDef { SpeedMultiplier = 1f / Mathf.Max(0.01f, ptime) });

        static EntityDef Storage() =>
            MakeBuilding("TestStorage", new[] { Port(true, Direction.West) }, stackCap: 50);

        static PortDef Port(bool isInput, Direction dir) =>
            new() { IsInput = isInput, Dir = dir.ToString(), X = 0, Y = 0 };

        static EntityDef MakeBuilding(string name, PortDef[] ports, int stackCap = 10, int size = 1, params EntityModuleDef[] extra)
        {
            var def = new EntityDef { Id = "test:entity/" + name.ToLowerInvariant(), DisplayName = name, Faction = Faction.Player };
            def.Modules.Add(new BuildingModuleDef { Size = new Vec2i(size, size) });
            def.Modules.Add(new HealthModuleDef { MaxHp = 100f });
            def.Modules.Add(new EffectsModuleDef());
            var portsDef = new PortsModuleDef();
            portsDef.Ports.AddRange(ports);
            def.Modules.Add(portsDef);
            def.Modules.Add(new InventoryModuleDef { Input = 1, Output = 1, StackCap = stackCap });
            def.Modules.AddRange(extra);
            return def;
        }
    }
}
