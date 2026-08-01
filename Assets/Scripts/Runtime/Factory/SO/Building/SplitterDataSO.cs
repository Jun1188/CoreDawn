using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 분배기. 입력 1개를 여러 출력 연결에 라운드로빈으로 고르게 나눈다.
/// 막힌 출구는 건너뛴다 (Factorio 스타일) — 한쪽이 막혀도 나머지로 계속 흐름.
/// 벨트가 아니므로 세그먼트는 분배기 앞뒤에서 끊긴다 (팀 합의: 분기/합류 = 전용 건물).
///
/// 필터: 출구 방향별로 아이템 1종을 지정할 수 있다 —
///   지정 아이템은 그 출구로만 나가고, 필터 출구는 다른 아이템을 받지 않는다.
///   나머지 아이템은 무필터 출구들에 라운드로빈. 판정은 아이템당 O(1) (딕셔너리 조회).
/// </summary>
[CreateAssetMenu(fileName = "NewSplitter", menuName = "Factory/Buildings/Splitter")]
public class SplitterDataSO : BuildingDataSO
{
    public override IBuildingBehavior CreateBehavior(Building building)
        => new SplitterBehavior(building);
}

// ─── 행동 ──────────────────────────────────────────────────────

public class SplitterBehavior : IBuildingBehavior
{
    readonly Building _b;
    int _next;   // 라운드로빈 커서 — 다음에 밀어볼 출력 연결 인덱스

    // 필터 — 출구 "방향" 기준 저장: 이웃 설치/철거로 연결 목록이 재구축돼도 유지된다.
    // 방향당 아이템 여러 종 허용, 아이템당 방향은 1개(결정적 라우팅).
    // 판정 O(1): 아이템 → 지정 방향 딕셔너리 조회 / "전용 출구인가" = 방향 키 존재 여부.
    readonly Dictionary<ItemDataSO, Direction> _dirByItem = new();
    readonly Dictionary<Direction, HashSet<ItemDataSO>> _itemsByDir = new();

    public SplitterBehavior(Building b) => _b = b;
    public void OnAfterPlaced() { }

    // ── 필터 설정 표면 (UI/상호작용이 호출 — 심 API)

    /// <summary>출구 방향에 아이템 필터 추가. 아이템이 다른 방향에 걸려 있었다면 옮겨진다.</summary>
    public void AddFilter(Direction dir, ItemDataSO item)
    {
        if (item == null) return;
        if (_dirByItem.TryGetValue(item, out var oldDir))
        {
            if (oldDir == dir) return;
            RemoveFromDir(oldDir, item);
        }

        _dirByItem[item] = dir;
        if (!_itemsByDir.TryGetValue(dir, out var set)) _itemsByDir[dir] = set = new HashSet<ItemDataSO>();
        set.Add(item);
        _b.Sim.MarkDirty(_b);   // 대기 중이던 아이템이 새 규칙으로 흐를 수 있음
    }

    /// <summary>아이템 하나의 필터 해제.</summary>
    public void RemoveFilter(ItemDataSO item)
    {
        if (item == null || !_dirByItem.TryGetValue(item, out var dir)) return;
        _dirByItem.Remove(item);
        RemoveFromDir(dir, item);
        _b.Sim.MarkDirty(_b);
    }

    /// <summary>출구 방향의 필터 전체 해제 (전용 출구 → 일반 출구로 복귀).</summary>
    public void ClearFilter(Direction dir)
    {
        if (!_itemsByDir.TryGetValue(dir, out var set)) return;
        foreach (var item in set) _dirByItem.Remove(item);
        _itemsByDir.Remove(dir);
        _b.Sim.MarkDirty(_b);
    }

    void RemoveFromDir(Direction dir, ItemDataSO item)
    {
        if (!_itemsByDir.TryGetValue(dir, out var set)) return;
        set.Remove(item);
        if (set.Count == 0) _itemsByDir.Remove(dir);   // 빈 집합 제거 — ContainsKey = "전용 출구" 불변식 유지
    }

    /// <summary>아이템별 지정 방향 조회 (UI 표시용).</summary>
    public IReadOnlyDictionary<ItemDataSO, Direction> Filters => _dirByItem;

    // ── 틱 ──

    public void Tick(float dt)
    {
        // 입력 버퍼의 아이템을 출력 연결에 분배 (출력 버퍼 없음)
        foreach (var (item, count) in _b.Input.Snapshot())
        {
            int moved = 0;
            while (moved < count && TryPush(item)) moved++;
            if (moved > 0)
            {
                _b.Input.TryConsume(item, moved);
                _b.NotifyUpstream(); // 입력 버퍼에 자리 생김 → 막혀 있던 상류 깨움
            }
        }
        // 전부 막혔으면 stall — 하류가 소비하면 NotifyUpstream으로 깨어난다
    }

    /// <summary>
    /// 아이템 1개 배출 시도. 필터 지정 아이템은 지정 출구로만,
    /// 나머지는 무필터 출구들에 라운드로빈. 연결 수는 포트 수(≤4)라 상수 순회.
    /// </summary>
    bool TryPush(ItemDataSO item)
    {
        var conns = _b.OutputConnections;
        if (conns.Count == 0) return false;

        // 필터 지정 아이템 → 그 방향의 출구로만 (막혀 있으면 대기)
        if (_dirByItem.TryGetValue(item, out var dir))
        {
            for (int i = 0; i < conns.Count; i++)
            {
                var c = conns[i];
                if (c.FromPort.Direction != dir) continue;
                if (!c.To.Input.TryAdd(item)) return false;   // 지정 출구 막힘 → 이 아이템은 대기
                _b.Sim.MarkDirty(c.To);
                return true;
            }
            return false;   // 지정 방향에 연결 없음 → 대기
        }

        // 무필터 아이템 → 필터 걸린 출구는 건너뛰고 라운드로빈
        for (int i = 0; i < conns.Count; i++)
        {
            var c = conns[(_next + i) % conns.Count];
            if (_itemsByDir.ContainsKey(c.FromPort.Direction)) continue;   // 전용 출구는 통과 금지
            if (!c.To.Input.TryAdd(item)) continue;                       // 막힌 출구는 건너뜀
            _b.Sim.MarkDirty(c.To);
            _next = (_next + i + 1) % conns.Count;
            return true;
        }
        return false;
    }
}
