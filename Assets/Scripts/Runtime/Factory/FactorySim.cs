using System;
using System.Collections.Generic;
using UnityEngine;

// ================================================================
//  FactorySim.cs
//  공장 시뮬레이션의 루트 — plain C#, Unity 씬/컴포넌트 의존 없음
//
//  포함:
//    FactorySim — 시계 + Dirty Queue + Wake 예약 + 배치/제거 진입점
//    GridIndex  — 좌표 → Building O(1) 조회
//
//  Unity와의 접점은 FactoryBootstrap(드라이버)과 BuildingEntity(씬 표현)뿐.
//  씬 없이 생성해 Advance()를 직접 호출하면 헤드리스로 돌릴 수 있다 (테스트용).
// ================================================================

/// <summary>좌표 → Building O(1) 조회. 배치 로직은 없다 — FactorySim이 채운다.</summary>
public class GridIndex
{
    readonly Dictionary<Vector2Int, Building> _grid = new();

    public void Add(Vector2Int cell, Building b) => _grid[cell] = b;
    public void Remove(Vector2Int cell) => _grid.Remove(cell);
    public Building GetAt(Vector2Int cell) => _grid.TryGetValue(cell, out var b) ? b : null;
    public bool IsOccupied(Vector2Int cell) => _grid.ContainsKey(cell);
}

/// <summary>
/// Dirty Queue + Wake 예약 기반 이벤트 주도 시뮬레이션.
///
/// 핵심 아이디어:
///   건물 10,000개 중 100개만 현재 활성 → 100번만 Tick() 호출.
///   큐에 없는 건물은 완전 무시.
///
/// 건물을 깨우는 두 가지 경로:
///   MarkDirty(b)        — "지금 변화가 생겼다" (아이템 수신, 상류/하류 상태 변화, 새 연결)
///   ScheduleWake(b, t)  — "t초 후에 깨워라"   (채굴/조합 타이머의 완료 시점 예약)
/// </summary>
public class FactorySim
{
    public readonly GridIndex          Grid;
    public readonly BuildingGraph      Graph;
    public readonly BeltSegmentManager Belts;

    /// <summary>시뮬레이션 누적 시간(초). 틱마다 틱 간격씩 증가한다.</summary>
    public float Now { get; private set; }

    /// <summary>
    /// 세이브 복원 전용 — 심 시계를 저장 시점으로 되돌린다. 건물을 배치하기 전에 호출할 것.
    ///
    /// 이게 없으면 채굴/조합 타이머가 전부 어긋난다. 각 행동은 완료 시각을 이 시계 기준
    /// 절대값(_readyAt)으로 들고 있어서, 시계만 0으로 되돌아가면 예약이 아득한 미래가 되어
    /// 공장 전체가 멈춘 것처럼 보인다.
    /// </summary>
    public void RestoreClock(float now) => Now = now;

    /// <summary>마이너가 채굴 대상을 결정하는 서비스 포인트 (ResourceGrid 등에서 주입).</summary>
    public Func<Vector2Int, ItemDataSO> GetResourceAt;

    /// <summary>
    /// 마이너가 채굴 1회분을 실제로 꺼내가는 서비스 포인트 (셀, 요청 개수) → 실제로 꺼낸 개수.
    /// 0이면 광맥 재고가 비었다는 뜻 — 마이너는 생산하지 않고 다음 주기에 재시도한다.
    /// 주입하지 않으면(null) 재고 개념 없이 무한 채굴 — 기존 동작 그대로.
    /// </summary>
    public Func<Vector2Int, int, int> TryExtractResourceAt;

    /// <summary>
    /// 이 칸의 광맥에서 1개를 캐는 데 걸리는 기준 시간(초) — 배율 1인 채굴기 기준.
    /// 실제 시간 = 이 값 ÷ MinerDataSO.speedMultiplier.
    /// 주입하지 않으면(null) 1초 — 광맥마다 난이도가 없던 기존 동작.
    /// </summary>
    public Func<Vector2Int, float> GetExtractIntervalAt;

    readonly Queue<Building>   _queue = new();
    readonly HashSet<Building> _inQ   = new(); // 중복 등록 방지 O(1)

    // wake 예약 — (깨울 시각, 건물) 이진 min-heap. index 0 = 가장 이른 예약.
    readonly List<(float time, Building b)> _wake = new();

    readonly float _interval;
    readonly int   _maxCatchUpTicks;
    float _timer;

    public FactorySim(float tps = 10f, int maxCatchUpTicks = 5)
    {
        _interval        = 1f / Mathf.Max(0.1f, tps);
        _maxCatchUpTicks = Mathf.Max(1, maxCatchUpTicks);
        Grid  = new GridIndex();
        Graph = new BuildingGraph(this);
        Belts = new BeltSegmentManager(this);
    }

    // ── 배치/제거 (외부 진입점 — 뷰 생성은 PlacementBridge가 별도로)

    public Building Place(BuildingDataSO so, Vector2Int origin, int rotSteps = 0,
        PortDefinition[] portOverride = null, BeltShape shape = BeltShape.Straight)
    {
        var b = new Building(this, so, origin, rotSteps, portOverride, shape);

        var size = so.GetRotatedSize(rotSteps);
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                Grid.Add(origin + new Vector2Int(x, y), b);

        Graph.OnPlaced(b);
        MarkDirty(b);
        return b;
    }

    /// <summary>
    /// 건물이 심에서 제거된 직후 1회. 씬 표현(뷰)은 이 통지를 받아 스스로 정리한다 —
    /// 심이 원본이고 뷰가 따라오는 방향을 코드로 고정하는 지점.
    /// 벨트 폐기 통지(Belts.ItemDiscarded)보다 항상 뒤에 온다(그때는 뷰가 아직 살아 있어야 하므로).
    /// </summary>
    public event Action<Building> Removed;

    public void Remove(Building b)
    {
        if (b == null || b.IsRemoved) return;
        b.IsRemoved = true;            // 큐/힙에 남은 참조는 틱에서 걸러진다

        Graph.OnRemoved(b);

        var size = b.Data.GetRotatedSize(b.RotationSteps);
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                Grid.Remove(b.Origin + new Vector2Int(x, y));

        _inQ.Remove(b);

        Removed?.Invoke(b);
    }

    // ── 깨우기

    /// <summary>건물을 다음 틱 처리 대상에 추가. O(1). 중복 호출 안전.</summary>
    public void MarkDirty(Building b)
    {
        if (b == null || b.IsRemoved) return;
        if (_inQ.Add(b)) _queue.Enqueue(b);
    }

    /// <summary>
    /// delay초 후 건물을 깨운다(= MarkDirty). 타이머 완료 시점 예약용.
    /// 같은 건물을 중복 예약해도 안전하다 — 이른 기상은 각 행동이 Now로 걸러낸다.
    /// </summary>
    public void ScheduleWake(Building b, float delay)
    {
        if (b == null || b.IsRemoved) return;
        _wake.Add((Now + delay, b));
        for (int i = _wake.Count - 1; i > 0; )
        {
            int p = (i - 1) / 2;
            if (_wake[p].time <= _wake[i].time) break;
            (_wake[p], _wake[i]) = (_wake[i], _wake[p]);
            i = p;
        }
    }

    void PopWake()
    {
        _wake[0] = _wake[^1];
        _wake.RemoveAt(_wake.Count - 1);
        for (int i = 0; ; )
        {
            int l = 2 * i + 1, r = l + 1, m = i;
            if (l < _wake.Count && _wake[l].time < _wake[m].time) m = l;
            if (r < _wake.Count && _wake[r].time < _wake[m].time) m = r;
            if (m == i) break;
            (_wake[m], _wake[i]) = (_wake[i], _wake[m]);
            i = m;
        }
    }

    // ── 구동

    /// <summary>
    /// 실시간 dt만큼 시뮬레이션을 진행한다 (고정 틱 + 따라잡기 상한).
    /// 드라이버(FactoryBootstrap)가 매 프레임 호출하거나, 테스트가 직접 호출한다.
    /// </summary>
    /// <summary>
    /// 마지막 틱 이후 흐른 시간(초) — 뷰가 틱 사이를 외삽해 부드럽게 그릴 때 사용.
    /// 틱 지연(캐치업 한도 초과) 시에도 한 틱 분량을 넘지 않게 클램프.
    /// </summary>
    public float TickLeftover => Mathf.Min(_timer, _interval);

    public void Advance(float dt)
    {
        _timer += dt;

        // 밀린 틱을 따라잡되, 프레임당 한도를 둬서 저사양에서
        // "틱 몰아치기 → 프레임 더 느려짐 → 더 밀림" 나선을 방지한다.
        int ticks = 0;
        while (_timer >= _interval && ticks < _maxCatchUpTicks)
        {
            _timer -= _interval;
            RunTick();
            ticks++;
        }

        // 한도를 넘긴 빚은 버린다 (다음 프레임에 처리할 1틱분만 유지).
        if (_timer > _interval) _timer = _interval;
    }

    void RunTick()
    {
        Now += _interval;

        // 예약 시각이 된 건물을 큐로 이동
        while (_wake.Count > 0 && _wake[0].time <= Now)
        {
            MarkDirty(_wake[0].b);
            PopWake();
        }

        // 틱 시작 시점의 큐 크기만큼만 처리.
        // 이번 틱에서 새로 MarkDirty된 건물은 다음 틱에 처리된다.
        int count = _queue.Count;
        for (int i = 0; i < count; i++)
        {
            var b = _queue.Dequeue();
            _inQ.Remove(b);
            if (b == null || b.IsRemoved) continue;
            b.Tick(_interval);
        }
    }
}
