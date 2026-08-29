using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Save;
using CoreDawn.UI;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
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
        public override IBuildingBehavior CreateBehavior(BuildingModule building)
            => new SplitterBehavior(building);
    }

    // ─── 행동 ──────────────────────────────────────────────────────

    /// <summary>출구 하나의 상태. 화면은 이 셋을 글자가 아니라 그림으로 구분한다 (SCR-07).</summary>
    public enum OutletState
    {
        /// <summary>허용 목록 없음 — 지정되지 않은 아이템들이 라운드로빈으로 흐른다.</summary>
        All,
        /// <summary>허용 목록 있음 — 목록에 든 아이템만 흐른다.</summary>
        Only,
        /// <summary>막힘 — 아무것도 보내지 않고 다음 출구로 넘긴다.</summary>
        Blocked,
    }

    public class SplitterBehavior : IBuildingBehavior, IInteractiveBehavior, ISaveableBehavior
    {
        readonly BuildingModule _b;
        int _next;   // 라운드로빈 커서 — 다음에 밀어볼 출력 연결 인덱스

        /// <summary>필터 UI가 출력 포트 방향을 조회할 때 사용.</summary>
        public BuildingModule Building => _b;

        // E 상호작용 — 필터 설정 팝업 (Storage 보관함과 같은 opt-in 패턴)
        public string InteractPrompt => "필터 설정";
        public void Interact(PlayerController player)
        {
            // 필터 화면은 UITK(SplitterPanelView)뿐 — 구 uGUI 팝업(SplitterFilterPopup)은 제거.
            // 못 열면 그 자리에서 알린다 (GameScreens와 같은 방침: 폴백이 있으면 UI 탑재 누락이 조용히 지나간다)
            if (SplitterPanelView.TryOpen(this)) return;
            Debug.LogWarning("[Splitter] 필터 화면(UITK)을 열지 못했습니다 — GameUI 씬이 탑재되지 않았습니다.");
        }

        // 필터 — 출구 "방향" 기준 저장: 이웃 설치/철거로 연결 목록이 재구축돼도 유지된다.
        // 방향당 아이템 여러 종, 아이템당 방향도 여러 개. 한 아이템을 두 출구로 보내면
        // 그 둘 사이에서 라운드로빈한다 — 같은 물건을 두 라인에 나눠 먹이는 배치가 흔하다.
        // 판정은 여전히 O(1): 두 딕셔너리가 서로의 역인덱스라 조회가 해시 한 번이다.
        readonly Dictionary<ItemDataSO, HashSet<Direction>> _dirsByItem = new();
        readonly Dictionary<Direction, HashSet<ItemDataSO>> _itemsByDir = new();

        // 완전히 막은 출구. 필터가 "이것만 보낸다"라면 이쪽은 "아무것도 안 보낸다"다.
        // 빈 허용 목록으로 대신할 수 없다 — _itemsByDir는 빈 집합을 지우는 것이 불변식이고,
        // 그래야 ContainsKey가 곧 "전용 출구인가"로 쓰인다.
        readonly HashSet<Direction> _blocked = new();

        // 이 분배기를 실제로 지나간 아이템. UI가 "지나가는 중"을 붙여 전체 목록에서 찾는 수고를 던다 —
        // 라인을 잘못 이었을 때도 여기가 비어 있어 드러난다.
        readonly HashSet<ItemDataSO> _passed = new();

        public SplitterBehavior(BuildingModule b) => _b = b;
        public void OnAfterPlaced() { }

        // ── 필터 설정 표면 (UI/상호작용이 호출 — 심 API)

        /// <summary>출구 방향에 아이템을 허용한다. 다른 방향의 지정은 건드리지 않는다 — 둘 다 열어 두면 나눠 흐른다.</summary>
        public void AddFilter(Direction dir, ItemDataSO item)
        {
            if (item == null) return;

            if (!_dirsByItem.TryGetValue(item, out var dirs)) _dirsByItem[item] = dirs = new HashSet<Direction>();
            if (!dirs.Add(dir)) return;   // 이미 허용돼 있음

            if (!_itemsByDir.TryGetValue(dir, out var set)) _itemsByDir[dir] = set = new HashSet<ItemDataSO>();
            set.Add(item);
            _b.Factory.MarkDirty(_b);   // 대기 중이던 아이템이 새 규칙으로 흐를 수 있음
        }

        /// <summary>출구 방향 하나에서만 해제. 다른 방향에 남아 있으면 그쪽으로는 계속 흐른다.</summary>
        public void RemoveFilter(Direction dir, ItemDataSO item)
        {
            if (item == null || !_dirsByItem.TryGetValue(item, out var dirs)) return;
            if (!dirs.Remove(dir)) return;
            if (dirs.Count == 0) _dirsByItem.Remove(item);

            RemoveFromDir(dir, item);
            _b.Factory.MarkDirty(_b);
        }

        /// <summary>아이템의 모든 방향 지정 해제 — 다시 일반 출구들로 흐른다.</summary>
        public void RemoveFilter(ItemDataSO item)
        {
            if (item == null || !_dirsByItem.TryGetValue(item, out var dirs)) return;
            foreach (var dir in dirs) RemoveFromDir(dir, item);
            _dirsByItem.Remove(item);
            _b.Factory.MarkDirty(_b);
        }

        /// <summary>출구 방향의 허용 목록 전체 해제 (전용 출구 → 일반 출구로 복귀).</summary>
        public void ClearFilter(Direction dir)
        {
            if (!_itemsByDir.TryGetValue(dir, out var set)) return;
            foreach (var item in set)
            {
                if (!_dirsByItem.TryGetValue(item, out var dirs)) continue;
                dirs.Remove(dir);
                if (dirs.Count == 0) _dirsByItem.Remove(item);
            }
            _itemsByDir.Remove(dir);
            _b.Factory.MarkDirty(_b);
        }

        void RemoveFromDir(Direction dir, ItemDataSO item)
        {
            if (!_itemsByDir.TryGetValue(dir, out var set)) return;
            set.Remove(item);
            if (set.Count == 0) _itemsByDir.Remove(dir);   // 빈 집합 제거 — ContainsKey = "전용 출구" 불변식 유지
        }

        // ── 조회 (UI 표시용)

        /// <summary>이 아이템이 허용된 출구 방향들. 지정이 없으면 빈 목록 = 일반 출구로 흐른다.</summary>
        public IReadOnlyCollection<Direction> DirectionsOf(ItemDataSO item) =>
            item != null && _dirsByItem.TryGetValue(item, out var dirs)
                ? dirs : (IReadOnlyCollection<Direction>)System.Array.Empty<Direction>();

        /// <summary>이 출구가 허용하는 아이템들. 비어 있으면 전용 출구가 아니다(일반 출구).</summary>
        public IReadOnlyCollection<ItemDataSO> AllowedAt(Direction dir) =>
            _itemsByDir.TryGetValue(dir, out var set)
                ? set : (IReadOnlyCollection<ItemDataSO>)System.Array.Empty<ItemDataSO>();

        public bool IsAllowedAt(Direction dir, ItemDataSO item) =>
            item != null && _dirsByItem.TryGetValue(item, out var dirs) && dirs.Contains(dir);

        /// <summary>이 출구에 허용 목록이 걸려 있는가 — 그렇다면 무지정 아이템은 통과하지 못한다.</summary>
        public bool HasFilter(Direction dir) => _itemsByDir.ContainsKey(dir);

        // ── 출구 막기

        /// <summary>이 출구로는 아무것도 보내지 않는다.</summary>
        public bool IsBlocked(Direction dir) => _blocked.Contains(dir);

        /// <summary>
        /// 출구를 막거나 연다. 막을 때 허용 목록도 함께 비운다 —
        /// "아무것도 안 보낸다"와 "이것만 보낸다"가 같은 출구에 동시에 걸려 있으면
        /// 나중에 여는 순간 잊고 있던 목록이 되살아나 놀라게 된다.
        /// </summary>
        public void SetBlocked(Direction dir, bool blocked)
        {
            if (blocked)
            {
                ClearFilter(dir);
                if (!_blocked.Add(dir)) return;
            }
            else if (!_blocked.Remove(dir)) return;

            _b.Factory.MarkDirty(_b);   // 막힌 채 대기하던 아이템이 다시 흐를 수 있음
        }

        /// <summary>이 분배기를 실제로 지나간 적 있는 아이템인가 — UI의 "지나가는 중" 표시.</summary>
        public bool HasPassed(ItemDataSO item) => item != null && _passed.Contains(item);

        /// <summary>출구의 현재 상태 — 화면이 세 가지를 그림으로 구분한다 (SCR-07).</summary>
        public OutletState StateOf(Direction dir) =>
            _blocked.Contains(dir) ? OutletState.Blocked
            : _itemsByDir.ContainsKey(dir) ? OutletState.Only
            : OutletState.All;

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
        /// 아이템 1개 배출 시도.
        ///
        /// 갈 수 있는 출구를 고르는 규칙만 다르고, 고른 뒤 라운드로빈으로 흘리는 것은 같다:
        ///   지정된 아이템 → 지정된 방향들 중에서
        ///   지정 없는 아이템 → 허용 목록이 없는 출구들 중에서 (전용 출구는 남의 것이므로 통과 금지)
        /// 어느 쪽이든 막은 출구(_blocked)는 후보에서 빠지고, 가득 찬 출구는 건너뛴다.
        ///
        /// 연결 수는 포트 수(≤4)라 상수 순회다.
        /// </summary>
        bool TryPush(ItemDataSO item)
        {
            var conns = _b.OutputConnections;
            if (conns.Count == 0) return false;

            bool assigned = _dirsByItem.TryGetValue(item, out var dirs) && dirs.Count > 0;

            for (int i = 0; i < conns.Count; i++)
            {
                var c = conns[(_next + i) % conns.Count];
                var dir = c.FromPort.Direction;

                if (_blocked.Contains(dir)) continue;                       // 막은 출구 — 다음 출구로 넘긴다
                if (assigned ? !dirs.Contains(dir) : _itemsByDir.ContainsKey(dir)) continue;
                if (!c.To.Input.TryAdd(item)) continue;                     // 가득 찬 출구는 건너뜀

                _b.Factory.MarkDirty(c.To);
                _passed.Add(item);                                          // "지나가는 중" 표시의 근거
                _next = (_next + i + 1) % conns.Count;
                return true;
            }
            return false;   // 갈 곳이 없다 → 대기(stall). 하류가 소비하면 NotifyUpstream으로 깨어난다
        }

        // ── 세이브 ────────────────────────────────────────────────────
        //
        // 필터는 사람이 손으로 설정한 값이라 반드시 보존돼야 한다 —
        // 라인을 다시 이어 붙이는 것보다 필터를 다시 찍는 쪽이 훨씬 성가시다.
        // 집합은 순회 순서가 보장되지 않으므로 저장할 때 정렬한다 (같은 상태 → 같은 파일).

        public class SaveState
        {
            [JsonProperty("next")] public int Next;
            [JsonProperty("filters")] public List<DirFilter> Filters = new();
            [JsonProperty("blocked")] public List<Direction> Blocked = new();
            [JsonProperty("passed")] public List<string> Passed = new();

            public class DirFilter
            {
                [JsonProperty("dir")] public Direction Direction;
                [JsonProperty("items")] public List<string> ItemIds = new();
            }
        }

        public object CaptureState()
        {
            var s = new SaveState { Next = _next };

            foreach (var kv in _itemsByDir.OrderBy(k => (int)k.Key))
                s.Filters.Add(new SaveState.DirFilter
                {
                    Direction = kv.Key,
                    ItemIds = kv.Value.Select(SaveRefs.IdOf).Where(id => id != null).OrderBy(id => id).ToList(),
                });

            s.Blocked = _blocked.OrderBy(d => (int)d).ToList();
            s.Passed = _passed.Select(SaveRefs.IdOf).Where(id => id != null).OrderBy(id => id).ToList();
            return s;
        }

        public void RestoreState(JToken state)
        {
            var s = SaveJson.FromToken<SaveState>(state);
            if (s == null) return;

            _dirsByItem.Clear();
            _itemsByDir.Clear();
            _blocked.Clear();
            _passed.Clear();

            _next = s.Next;

            // AddFilter를 거치면 두 딕셔너리의 역인덱스 불변식이 저절로 지켜진다
            if (s.Filters != null)
                foreach (var f in s.Filters)
                    if (f?.ItemIds != null)
                        foreach (var id in f.ItemIds)
                            AddFilter(f.Direction, SaveRefs.Item(id));

            if (s.Blocked != null)
                foreach (var d in s.Blocked) _blocked.Add(d);

            if (s.Passed != null)
                foreach (var id in s.Passed)
                {
                    var item = SaveRefs.Item(id);
                    if (item != null) _passed.Add(item);
                }
        }
    }
}
