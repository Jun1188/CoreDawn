using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Factory 시스템 특성화 테스트 하네스.
/// 재설계 전후의 "올바른 동작"을 박제해, 내부를 갈아엎어도 회귀를 잡아낸다.
///
/// 사용법: 아무 씬에나 빈 GameObject를 만들고 이 컴포넌트를 붙인 뒤 플레이.
///         심/뷰 분리 덕에 씬·GameObject·프레임 없이 FactorySim을 직접 생성해
///         동기로 돌린다 — 전체 스위트가 첫 프레임에 즉시 완료된다.
///
/// NUnit(Test Runner)이 아닌 이유: 테스트 asmdef는 Assembly-CSharp을 참조할 수
/// 없는데 Runtime↔Test 코드가 상호 참조 중이라 어셈블리 분리가 불가.
/// (심은 이미 plain C#이라, 어셈블리 정리가 되면 그대로 EditMode NUnit 이전 가능)
/// </summary>
public class FactoryScenarioTests : MonoBehaviour
{
    readonly List<(string name, bool pass, string detail)> _results = new();
    readonly List<string> _fails = new();               // 실행 중인 시나리오의 실패 메시지
    readonly List<ScriptableObject> _createdSOs = new();

    FactorySim _sim;
    ItemDataSO _ore, _ingot;

    // ─── 실행 루프 ──────────────────────────────────────────────

    int count;

    void Start()
    {
        _ore   = MakeItem("TestOre",   ItemType.Ore);
        _ingot = MakeItem("TestIngot", ItemType.Ingot);

        count = 0;

        Run("1. 기본 체인 운반",              S1_BasicChain);
        Run("2. 설치 순서 무관 (stall 데드락)", S2_OrderIndependence);
        Run("3. 막힌 체인 무유실·정지",        S3_StallNoLoss);
        Run("4. 중간 철거 분할·복구",          S4_DemolishSplit);
        Run("5. 회전 배치 연결",              S5_RotatedChain);
        Run("6. 어셈블러 조합 체인",           S6_AssemblerChain);
        Run("7. 커브 벨트 코너 체인",          S7_CurvedChain);
        Run("8. 분배기 라운드로빈",            S8_SplitterRoundRobin);
        Run("9. 합류기 두 소스 합류",          S9_MergerTwoSources);
        Run("10. 분배기 필터 분배",            S10_SplitterFilter);
        Run("11. 분배기 필터 다중 아이템",      S11_SplitterMultiItemFilter);
        Run("12. 한 아이템 → 두 출구 분배",     S12_SplitterItemToTwoOutlets);
        Run("13. 막은 출구 건너뛰기",           S13_SplitterBlockedOutlet);

        foreach (var so in _createdSOs) DestroyImmediate(so);
        _createdSOs.Clear();

        int passed = 0;
        foreach (var r in _results) if (r.pass) passed++;
        Debug.Log($"[FactoryScenarioTests] 완료: {passed}/{_results.Count} 통과");
        foreach (var r in _results)
            if (!r.pass) Debug.LogError($"[FAIL] {r.name}\n{r.detail}");
    }

    /// <summary>시나리오 1개를 격리 실행. 예외도 실패로 기록하고 다음으로 넘어간다.</summary>
    void Run(string name, Action scenario)
    {
        count++;

        // 시나리오마다 새 심 — plain C#이라 싱글톤 정리·프레임 대기가 필요 없다
        _sim = new FactorySim(tps: 10f);
        _sim.GetResourceAt = _ => _ore;
        _beltSO = null;   // 벨트 SO도 시나리오별로 새로
        _fails.Clear();

        try { scenario(); }
        catch (Exception e) { _fails.Add("예외 발생:\n" + e); }

        _results.Add((name, _fails.Count == 0, string.Join("\n", _fails)));
        _sim = null;
    }

    // ─── 시나리오 ──────────────────────────────────────────────

    /// <summary>마이너→벨트×2→저장소: 아이템이 끝까지 운반된다.</summary>
    void S1_BasicChain()
    {
        Place(Miner(), 0, 0);
        PlaceBelt(1, 0, 0, BeltShape.Straight);
        PlaceBelt(2, 0, 0, BeltShape.Straight);
        var store = Place(Storage(), 3, 0);

        RunSim(4f);
        Expect(StoredCount(store, _ore) >= 1,
            $"저장소에 아이템이 도착해야 함 (실제: {StoredCount(store, _ore)}개)");
    }

    /// <summary>마이너를 먼저 설치해 stall시킨 뒤 벨트를 연결해도 흐른다. (데드락 회귀 테스트)</summary>
    void S2_OrderIndependence()
    {
        var miner = Place(Miner(outBuf: 2), 0, 0);

        RunSim(1.5f); // 버퍼(2)가 차고 stall될 시간
        int stalled = miner.Output.CountOf(_ore);
        Expect(stalled == 2, $"출력이 막히면 버퍼 상한(2)에서 생산이 멈춰야 함 (실제: {stalled}개)");

        PlaceBelt(1, 0, 0, BeltShape.Straight);
        var store = Place(Storage(), 2, 0);

        RunSim(3f);
        Expect(StoredCount(store, _ore) >= 1,
            $"벨트 연결 후 stall이 풀려 아이템이 흘러야 함 (실제 저장소: {StoredCount(store, _ore)}개)");
    }

    /// <summary>출구 없는 체인: 가득 차면 생산이 멈추고, 총량이 더 늘지도 사라지지도 않는다.</summary>
    void S3_StallNoLoss()
    {
        var miner = Place(Miner(outBuf: 2), 0, 0);
        var belt  = PlaceBelt(1, 0, 0, BeltShape.Straight);

        RunSim(6f); // 모든 버퍼·벨트가 가득 찰 시간
        int total1 = SystemTotal(miner, belt);

        RunSim(2f);
        int total2 = SystemTotal(miner, belt);

        Expect(total1 == total2, $"가득 찬 뒤에는 총량이 변하면 안 됨 (증발/과잉생산): {total1} → {total2}");
        Expect(total2 <= 2 + 10 + 2, $"총량이 버퍼 상한을 넘으면 안 됨 (실제: {total2})");
    }

    /// <summary>벨트 중간 철거 → 세그먼트 2분할, 재설치 → 흐름 복구.</summary>
    void S4_DemolishSplit()
    {
        Place(Miner(), 0, 0);
        var b1 = PlaceBelt(1, 0, 0, BeltShape.Straight);
        var b2 = PlaceBelt(2, 0, 0, BeltShape.Straight);
        var b3 = PlaceBelt(3, 0, 0, BeltShape.Straight);
        var store = Place(Storage(), 4, 0);

        RunSim(2f);
        _sim.Remove(b2);

        var s1 = _sim.Belts.GetSegment(b1);
        var s3 = _sim.Belts.GetSegment(b3);
        Expect(s1 != null && s3 != null && s1 != s3, "철거 후 상류/하류가 별도 세그먼트로 나뉘어야 함");

        int before = StoredCount(store, _ore);
        PlaceBelt(2, 0, 0, BeltShape.Straight);
        RunSim(4f);
        Expect(StoredCount(store, _ore) > before,
            $"재설치 후 흐름이 복구돼야 함 (저장소: {before} → {StoredCount(store, _ore)}개)");
    }

    /// <summary>회전 배치(남향 체인)에서도 포트가 연결된다.</summary>
    void S5_RotatedChain()
    {
        Place(Miner(), 0, 0, rot: 1);            // 출력 East → South
        PlaceBelt(0, -1, 1, BeltShape.Straight); // 입력 North, 출력 South
        var store = Place(Storage(), 0, -2, rot: 1);

        RunSim(4f);
        Expect(StoredCount(store, _ore) >= 1,
            $"회전된 체인에서도 아이템이 도착해야 함 (실제: {StoredCount(store, _ore)}개)");
    }

    /// <summary>마이너→벨트→어셈블러(2광석=1주괴)→벨트→저장소.</summary>
    void S6_AssemblerChain()
    {
        var recipe = MakeRecipe(_ore, 2, _ingot, 1, craftTime: 0.3f);

        Place(Miner(), 0, 0);
        PlaceBelt(1, 0, 0, BeltShape.Straight);
        Place(Assembler(recipe), 2, 0);
        PlaceBelt(3, 0, 0, BeltShape.Straight);
        var store = Place(Storage(), 4, 0);

        RunSim(6f);
        Expect(StoredCount(store, _ingot) >= 1,
            $"조합된 주괴가 저장소에 도착해야 함 (실제: {StoredCount(store, _ingot)}개)");
    }

    /// <summary>
    /// L커브로 북쪽으로 꺾이는 체인: 마이너→벨트(동)→커브(동→북)→벨트(북)→저장소.
    /// 커브 포함 전체가 하나의 세그먼트로 병합되고 아이템이 도착해야 한다.
    /// </summary>
    void S7_CurvedChain()
    {
        Place(Miner(), 0, 0);
        var b1 = PlaceBelt(1, 0, 0, BeltShape.Straight);    // 동쪽으로
        var b2 = PlaceBelt(2, 0, 3, BeltShape.CurveL);      // 서쪽에서 받아 북쪽으로
        var b3 = PlaceBelt(2, 1, 3, BeltShape.Straight);    // 북쪽으로
        var store = Place(Storage(), 2, 2, rot: 3);         // 남쪽(벨트)에서 받음

        var seg = _sim.Belts.GetSegment(b1);
        Expect(seg != null && seg == _sim.Belts.GetSegment(b2) && seg == _sim.Belts.GetSegment(b3),
            "커브를 포함한 같은 종류 벨트는 하나의 세그먼트로 병합돼야 함");

        RunSim(5f);
        Expect(StoredCount(store, _ore) >= 1,
            $"커브 체인에서도 아이템이 도착해야 함 (실제: {StoredCount(store, _ore)}개)");
    }

    /// <summary>마이너→분배기→저장소 2개: 양쪽에 고르게 분배된다.</summary>
    void S8_SplitterRoundRobin()
    {
        Place(Miner(), 0, 0);
        Place(Splitter(), 1, 0);
        var storeA = Place(Storage(), 2, 0);           // 동쪽 출구
        var storeB = Place(Storage(), 1, 1, rot: 3);   // 북쪽 출구 (남쪽에서 받음)

        RunSim(6f);
        int a = StoredCount(storeA, _ore);
        int b = StoredCount(storeB, _ore);
        Expect(a >= 2 && b >= 2, $"양쪽 출구 모두에 아이템이 가야 함 (A:{a}, B:{b})");
        Expect(Mathf.Abs(a - b) <= 2, $"라운드로빈이면 양쪽이 비슷해야 함 (A:{a}, B:{b})");
    }

    /// <summary>마이너 2개(광석/주괴)→합류기→저장소: 두 소스가 모두 통과한다.</summary>
    void S9_MergerTwoSources()
    {
        // 위치별 자원: (1,1)의 마이너만 주괴를 캔다
        _sim.GetResourceAt = pos => pos == new Vector2Int(1, 1) ? _ingot : _ore;

        Place(Miner(), 0, 0);                          // 서쪽에서 광석
        Place(Miner(), 1, 1, rot: 1);                  // 북쪽에서 주괴 (출력 South)
        Place(Merger(), 1, 0);
        // 두 종류를 받으므로 2슬롯. 저장소는 받은 것을 보관함(입력 버퍼)에 그대로 쌓으므로
        // 1슬롯이면 먼저 온 종류가 그 칸을 차지해 다른 종류가 못 들어온다.
        var store = Place(Storage(slots: 2), 2, 0);

        RunSim(6f);
        Expect(StoredCount(store, _ore) >= 1 && StoredCount(store, _ingot) >= 1,
            $"두 소스 모두 합류기를 통과해야 함 (광석:{StoredCount(store, _ore)}, 주괴:{StoredCount(store, _ingot)})");
    }

    /// <summary>
    /// 분배기 필터: 주괴는 북쪽 전용 출구로만 가고, 광석은 필터 출구에 못 들어간다.
    /// 혼합 라인(광석+주괴) → 분배기(북쪽=주괴 필터) → 동쪽/북쪽 저장소 순수성 검증.
    /// </summary>
    void S10_SplitterFilter()
    {
        // 위치별 자원: (1,1)의 마이너만 주괴를 캔다 (S9와 동일한 혼합 라인 구성)
        _sim.GetResourceAt = pos => pos == new Vector2Int(1, 1) ? _ingot : _ore;

        Place(Miner(), 0, 0);                          // 서쪽에서 광석
        Place(Miner(), 1, 1, rot: 1);                  // 북쪽에서 주괴 (출력 South)
        Place(Merger(), 1, 0);
        var splitter = Place(Splitter(), 2, 0);
        var storeA = Place(Storage(), 3, 0);           // 동쪽 출구 (무필터)
        var storeB = Place(Storage(), 2, 1, rot: 3);   // 북쪽 출구 (주괴 전용)

        ((SplitterBehavior)splitter.Behavior).AddFilter(Direction.North, _ingot);

        RunSim(8f);
        int aOre = StoredCount(storeA, _ore),  aIngot = StoredCount(storeA, _ingot);
        int bOre = StoredCount(storeB, _ore),  bIngot = StoredCount(storeB, _ingot);

        Expect(bIngot >= 1, $"주괴는 필터 출구(북쪽)로 흘러야 함 (실제: {bIngot}개)");
        Expect(bOre == 0,   $"무필터 아이템(광석)은 전용 출구에 못 들어가야 함 (북쪽 광석: {bOre}개)");
        Expect(aOre >= 1,   $"광석은 무필터 출구(동쪽)로 흘러야 함 (실제: {aOre}개)");
        Expect(aIngot == 0, $"필터 아이템(주괴)은 지정 출구로만 가야 함 (동쪽 주괴: {aIngot}개)");
    }

    /// <summary>
    /// 분배기 필터 다중 아이템: 한 출구(북쪽)에 광석·주괴 둘 다 지정 —
    /// 두 종류 모두 북쪽으로만 가고 무필터 출구(동쪽)는 비어 있어야 한다.
    /// 아이템은 심에 직접 주입 (마이너 체인 없이 분배기 단독 검증).
    /// </summary>
    void S11_SplitterMultiItemFilter()
    {
        var splitter = Place(Splitter(), 1, 0);
        var storeA = Place(Storage(), 2, 0);                    // 동쪽 출구 (무필터)
        var storeB = Place(Storage(slots: 2), 1, 1, rot: 3);    // 북쪽 출구 — 두 종류를 담으므로 2슬롯

        var behavior = (SplitterBehavior)splitter.Behavior;
        behavior.AddFilter(Direction.North, _ore);
        behavior.AddFilter(Direction.North, _ingot);

        // 입력 버퍼가 작아(1슬롯) 번갈아 주입하며 흘려보낸다
        for (int i = 0; i < 3; i++)
        {
            splitter.Input.TryAdd(_ore);
            _sim.MarkDirty(splitter);
            RunSim(0.5f);
            splitter.Input.TryAdd(_ingot);
            _sim.MarkDirty(splitter);
            RunSim(0.5f);
        }

        int bOre = StoredCount(storeB, _ore), bIngot = StoredCount(storeB, _ingot);
        int aTotal = StoredCount(storeA, _ore) + StoredCount(storeA, _ingot);

        Expect(bOre >= 2 && bIngot >= 2,
            $"두 필터 아이템 모두 북쪽 전용 출구로 가야 함 (광석:{bOre}, 주괴:{bIngot})");
        Expect(aTotal == 0, $"무필터 출구(동쪽)에는 아무것도 없어야 함 (실제: {aTotal}개)");
    }

    /// <summary>
    /// 한 아이템을 두 출구에 지정: 광석을 북쪽·동쪽 둘 다 허용하면 그 둘 사이에서 나뉘어야 한다.
    /// 같은 물건을 두 라인에 나눠 먹이는 배치를 위한 것 — 예전에는 아이템당 방향이 1개였다.
    /// </summary>
    void S12_SplitterItemToTwoOutlets()
    {
        var splitter = Place(Splitter(), 1, 0);
        var storeA = Place(Storage(), 2, 0);                 // 동쪽
        var storeB = Place(Storage(), 1, 1, rot: 3);         // 북쪽

        var behavior = (SplitterBehavior)splitter.Behavior;
        behavior.AddFilter(Direction.North, _ore);
        behavior.AddFilter(Direction.East, _ore);

        for (int i = 0; i < 6; i++)
        {
            splitter.Input.TryAdd(_ore);
            _sim.MarkDirty(splitter);
            RunSim(0.5f);
        }

        int a = StoredCount(storeA, _ore), b = StoredCount(storeB, _ore);
        Expect(a > 0 && b > 0, $"두 출구 모두로 나뉘어야 함 (동:{a}, 북:{b})");
        Expect(Mathf.Abs(a - b) <= 2, $"두 출구에 고르게 나뉘어야 함 (동:{a}, 북:{b})");
        Expect(behavior.HasPassed(_ore), "지나간 아이템으로 기록돼야 함");
    }

    /// <summary>
    /// 막은 출구: 북쪽을 막으면 그쪽으로는 가지 않고 남은 출구로 전부 넘어가야 한다.
    /// 막을 때 그 출구의 허용 목록도 함께 비워진다.
    /// </summary>
    void S13_SplitterBlockedOutlet()
    {
        var splitter = Place(Splitter(), 1, 0);
        var storeA = Place(Storage(), 2, 0);                 // 동쪽
        var storeB = Place(Storage(), 1, 1, rot: 3);         // 북쪽 — 막을 대상

        var behavior = (SplitterBehavior)splitter.Behavior;
        behavior.AddFilter(Direction.North, _ore);           // 지정해 둔 뒤
        behavior.SetBlocked(Direction.North, true);          // 막으면 지정도 사라져야 한다

        Expect(behavior.AllowedAt(Direction.North).Count == 0, "막은 출구의 허용 목록은 비워져야 함");
        Expect(behavior.StateOf(Direction.North) == OutletState.Blocked, "상태가 Blocked 여야 함");

        for (int i = 0; i < 4; i++)
        {
            splitter.Input.TryAdd(_ore);
            _sim.MarkDirty(splitter);
            RunSim(0.5f);
        }

        Expect(StoredCount(storeB, _ore) == 0, "막은 출구로는 아무것도 가면 안 됨");
        Expect(StoredCount(storeA, _ore) == 4, $"남은 출구로 전부 넘어가야 함 (실제: {StoredCount(storeA, _ore)}개)");
    }

    // ─── 검증/구동 헬퍼 ─────────────────────────────────────────

    void Expect(bool condition, string message)
    {
        if (!condition) _fails.Add(message);
    }

    /// <summary>시뮬레이션을 simSeconds만큼 동기로 진행 (프레임 대기 없음).</summary>
    void RunSim(float simSeconds)
    {
        int ticks = Mathf.CeilToInt(simSeconds / 0.1f);
        for (int i = 0; i < ticks; i++)
            _sim.Advance(0.1f);
    }

    static int StoredCount(Building store, ItemDataSO item)
        => store.Input.CountOf(item) + store.Output.CountOf(item);

    /// <summary>막힌 체인 검증용: 마이너 출력 + 벨트 입력 버퍼 + 벨트 위 아이템 총합.</summary>
    int SystemTotal(Building miner, Building belt)
    {
        int total = miner.Output.CountOf(_ore) + belt.Input.CountOf(_ore);
        var seg = _sim.Belts.GetSegment(belt);
        if (seg != null) total += seg.Items.Count;
        return total;
    }

    // ─── 배치/SO 생성 헬퍼 (심 직접 호출 — 뷰/GameObject 불필요) ──

    Building Place(BuildingDataSO so, int x, int y, int rot = 0)
        => _sim.Place(so, new Vector2Int(x, y), rot);

    Building PlaceBelt(int x, int y, int rot, BeltShape shape)
        => _sim.Place(Belt(), new Vector2Int(x, y), rot, BeltDataSO.BuildPorts(shape, rot));

    BuildingDataSO Miner(float ptime = 0.2f, int outBuf = 5)
    {
        // stackCap = outBuf → 출력 버퍼가 정확히 outBuf개에서 가득 참 (stall 시나리오용)
        var so = MakeBuilding<MinerDataSO>("TestMiner",
            new[] { Port(false, Direction.East) }, stackCap: outBuf);
        // 채굴 시간 = 광맥 기준(훅 없으면 1초) ÷ 배율 → ptime초를 원하면 배율은 1/ptime
        so.speedMultiplier = 1f / Mathf.Max(0.01f, ptime);
        return so;
    }

    // 벨트는 시나리오 안에서 단일 SO를 공유 — 병합 가드가 "같은 에셋"만 병합하므로
    BeltDataSO _beltSO;
    BuildingDataSO Belt() =>
        _beltSO != null ? _beltSO : _beltSO =
            MakeBuilding<BeltDataSO>("TestBelt",
                new[] { Port(true, Direction.West), Port(false, Direction.East) }, stackCap: 10);

    /// <param name="slots">담을 아이템 종류 수만큼 필요 (종류당 1슬롯). 기본 1종.</param>
    BuildingDataSO Storage(int slots = 1) =>
        MakeBuilding<StorageDataSO>("TestStorage",
            new[] { Port(true, Direction.West) }, stackCap: 50, slots: slots);

    BuildingDataSO Splitter() =>
        MakeBuilding<SplitterDataSO>("TestSplitter",
            new[] { Port(true, Direction.West), Port(false, Direction.East),
                    Port(false, Direction.North), Port(false, Direction.South) }, stackCap: 1);

    BuildingDataSO Merger() =>
        MakeBuilding<MergerDataSO>("TestMerger",
            new[] { Port(true, Direction.West), Port(true, Direction.North),
                    Port(true, Direction.South), Port(false, Direction.East) }, stackCap: 1);

    BuildingDataSO Assembler(RecipeDataSO recipe)
    {
        var so = MakeBuilding<AssemblerDataSO>("TestAssembler",
            new[] { Port(true, Direction.West), Port(false, Direction.East) }, stackCap: 10);
        so.availableRecipes = new[] { recipe };
        return so;
    }

    static PortDefinition Port(bool isInput, Direction dir) =>
        new() { IsInput = isInput, Direction = dir, LocalOffset = Vector2Int.zero };

    T MakeBuilding<T>(string name, PortDefinition[] ports, int stackCap = 10, int slots = 1)
        where T : BuildingDataSO
    {
        var so = ScriptableObject.CreateInstance<T>();
        so.name           = name;
        so.size           = Vector2Int.one;
        so.ports          = ports;
        so.inputSlots     = slots;
        so.outputSlots    = slots;      // 여러 종류를 담는 시나리오는 slots를 늘릴 것 (종류당 1슬롯)
        so.bufferStackCap = stackCap;   // 슬롯당 stackCap = "최대 n개" 의미
        _createdSOs.Add(so);
        return so;
    }

    ItemDataSO MakeItem(string name, ItemType type)
    {
        var so = ScriptableObject.CreateInstance<ItemDataSO>();
        so.displayName = name;
        so.type = type;
        _createdSOs.Add(so);
        return so;
    }

    RecipeDataSO MakeRecipe(ItemDataSO input, int inAmount, ItemDataSO output, int outAmount, float craftTime)
    {
        var so = ScriptableObject.CreateInstance<RecipeDataSO>();
        so.name      = "TestRecipe";
        so.inputs    = new[] { new RecipeDataSO.Slot { item = input,  amount = inAmount } };
        so.outputs   = new[] { new RecipeDataSO.Slot { item = output, amount = outAmount } };
        so.craftTime = craftTime;
        _createdSOs.Add(so);
        return so;
    }

    // ─── 결과 표시 ─────────────────────────────────────────────

    void OnGUI()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Factory 특성화 테스트  ({_results.Count}/{count})");
        foreach (var (name, pass, detail) in _results)
        {
            sb.AppendLine($"{(pass ? "PASS" : "FAIL")}  {name}");
            if (!pass) sb.AppendLine($"      {detail.Replace("\n", "\n      ")}");
        }
        GUI.TextArea(new Rect(20, 20, 520, 300), sb.ToString());
    }
}
