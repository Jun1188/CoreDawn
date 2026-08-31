using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CoreDawn.Data;   // Direction — 5a-2f에서 공장과 함께 Sim으로 옮긴다

namespace CoreDawn.Sim
{
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

    /// <summary>
    /// 라우터 — 분배기·합류기가 <b>같은</b> 모듈을 쓴다(구 SplitterBehavior·MergerBehavior).
    /// 합류 = 입력 여럿·출력 하나, 분배 = 입력 하나·출력 여럿일 뿐 규칙은 하나다:
    /// 출구별 필터(허용 목록·차단)와 라운드로빈 커서. 필터는 별개 모듈이 아니라 라우팅 규칙의 일부다 —
    /// "이 출력 포트는 어떤 아이템을 받는가", 비어 있으면 전부.
    ///
    /// 실제로 흘리는 일(연결·포트)은 공장의 것이라 소유자(BuildingModule.PumpRouted)가 하고,
    /// 이 모듈은 규칙과 상태만 가진다 — 어느 출구가 막혔나, 어느 아이템이 어느 방향 전용인가, 다음 순번.
    /// </summary>
    public sealed class RouterModule : EntityModule, ISaveableModule
    {
        public RouterModuleDef Def { get; }
        public RouterModule(RouterModuleDef def) { Def = def; }

        /// <summary>라운드로빈 커서 — 다음에 밀어볼 출력 연결 인덱스. 소유자(펌핑)가 읽고 쓴다.</summary>
        public int Cursor;

        // 필터 — 출구 "방향" 기준 저장: 이웃 설치/철거로 연결 목록이 재구축돼도 유지된다.
        // 방향당 아이템 여러 종, 아이템당 방향도 여러 개. 한 아이템을 두 출구로 보내면
        // 그 둘 사이에서 라운드로빈한다 — 같은 물건을 두 라인에 나눠 먹이는 배치가 흔하다.
        // 판정은 여전히 O(1): 두 딕셔너리가 서로의 역인덱스라 조회가 해시 한 번이다.
        readonly Dictionary<ItemDef, HashSet<Direction>> _dirsByItem = new();
        readonly Dictionary<Direction, HashSet<ItemDef>> _itemsByDir = new();

        // 완전히 막은 출구. 필터가 "이것만 보낸다"라면 이쪽은 "아무것도 안 보낸다"다.
        // 빈 허용 목록으로 대신할 수 없다 — _itemsByDir는 빈 집합을 지우는 것이 불변식이고,
        // 그래야 ContainsKey가 곧 "전용 출구인가"로 쓰인다.
        readonly HashSet<Direction> _blocked = new();

        // 이 라우터를 실제로 지나간 아이템. UI가 "지나가는 중"을 붙여 전체 목록에서 찾는 수고를 던다 —
        // 라인을 잘못 이었을 때도 여기가 비어 있어 드러난다.
        readonly HashSet<ItemDef> _passed = new();

        /// <summary>규칙이 바뀌었다 — 대기 중이던 아이템이 새 규칙으로 흐를 수 있으니 소유자가 깨어난다(구 MarkDirty).</summary>
        public event Action Changed;

        // ── 필터 설정 표면 (UI/상호작용이 호출 — 심 API)

        /// <summary>출구 방향에 아이템을 허용한다. 다른 방향의 지정은 건드리지 않는다 — 둘 다 열어 두면 나눠 흐른다.</summary>
        public void AddFilter(Direction dir, ItemDef item)
        {
            if (item == null) return;

            if (!_dirsByItem.TryGetValue(item, out var dirs)) _dirsByItem[item] = dirs = new HashSet<Direction>();
            if (!dirs.Add(dir)) return;   // 이미 허용돼 있음

            if (!_itemsByDir.TryGetValue(dir, out var set)) _itemsByDir[dir] = set = new HashSet<ItemDef>();
            set.Add(item);
            Changed?.Invoke();
        }

        /// <summary>출구 방향 하나에서만 해제. 다른 방향에 남아 있으면 그쪽으로는 계속 흐른다.</summary>
        public void RemoveFilter(Direction dir, ItemDef item)
        {
            if (item == null || !_dirsByItem.TryGetValue(item, out var dirs)) return;
            if (!dirs.Remove(dir)) return;
            if (dirs.Count == 0) _dirsByItem.Remove(item);

            RemoveFromDir(dir, item);
            Changed?.Invoke();
        }

        /// <summary>아이템의 모든 방향 지정 해제 — 다시 일반 출구들로 흐른다.</summary>
        public void RemoveFilter(ItemDef item)
        {
            if (item == null || !_dirsByItem.TryGetValue(item, out var dirs)) return;
            foreach (var dir in dirs) RemoveFromDir(dir, item);
            _dirsByItem.Remove(item);
            Changed?.Invoke();
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
            Changed?.Invoke();
        }

        void RemoveFromDir(Direction dir, ItemDef item)
        {
            if (!_itemsByDir.TryGetValue(dir, out var set)) return;
            set.Remove(item);
            if (set.Count == 0) _itemsByDir.Remove(dir);   // 빈 집합 제거 — ContainsKey = "전용 출구" 불변식 유지
        }

        // ── 조회 (펌핑·UI)

        /// <summary>이 아이템이 허용된 출구 방향들. 지정이 없으면 빈 목록 = 일반 출구로 흐른다.</summary>
        public IReadOnlyCollection<Direction> DirectionsOf(ItemDef item) =>
            item != null && _dirsByItem.TryGetValue(item, out var dirs)
                ? dirs : (IReadOnlyCollection<Direction>)Array.Empty<Direction>();

        /// <summary>이 출구가 허용하는 아이템들. 비어 있으면 전용 출구가 아니다(일반 출구).</summary>
        public IReadOnlyCollection<ItemDef> AllowedAt(Direction dir) =>
            _itemsByDir.TryGetValue(dir, out var set)
                ? set : (IReadOnlyCollection<ItemDef>)Array.Empty<ItemDef>();

        public bool IsAllowedAt(Direction dir, ItemDef item) =>
            item != null && _dirsByItem.TryGetValue(item, out var dirs) && dirs.Contains(dir);

        /// <summary>이 아이템에 전용 출구 지정이 있는가 — 있으면 지정된 방향으로만, 없으면 일반 출구들로 흐른다.</summary>
        public bool HasAssignedDirs(ItemDef item) =>
            item != null && _dirsByItem.TryGetValue(item, out var dirs) && dirs.Count > 0;

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

            Changed?.Invoke();   // 막힌 채 대기하던 아이템이 다시 흐를 수 있음
        }

        /// <summary>이 라우터를 실제로 지나간 적 있는 아이템인가 — UI의 "지나가는 중" 표시.</summary>
        public bool HasPassed(ItemDef item) => item != null && _passed.Contains(item);

        /// <summary>아이템이 실제로 지나갔다 — 소유자(펌핑)가 배출 성공 시 기록한다.</summary>
        public void MarkPassed(ItemDef item) { if (item != null) _passed.Add(item); }

        /// <summary>출구의 현재 상태 — 화면이 세 가지를 그림으로 구분한다 (SCR-07).</summary>
        public OutletState StateOf(Direction dir) =>
            _blocked.Contains(dir) ? OutletState.Blocked
            : _itemsByDir.ContainsKey(dir) ? OutletState.Only
            : OutletState.All;

        // ── 세이브(ISaveableModule) — 키는 옛 SplitterBehavior 저장과 같다 ──
        //
        // 필터는 사람이 손으로 설정한 값이라 반드시 보존돼야 한다 —
        // 라인을 다시 이어 붙이는 것보다 필터를 다시 찍는 쪽이 훨씬 성가시다.
        // 집합은 순회 순서가 보장되지 않으므로 저장할 때 정렬한다 (같은 상태 → 같은 파일).

        public sealed class SaveState
        {
            [JsonProperty("next")] public int Next;
            [JsonProperty("filters")] public List<DirFilter> Filters = new();
            [JsonProperty("blocked")] public List<Direction> Blocked = new();
            [JsonProperty("passed")] public List<string> Passed = new();

            public sealed class DirFilter
            {
                [JsonProperty("dir")] public Direction Direction;
                [JsonProperty("items")] public List<string> ItemIds = new();
            }
        }

        public object CaptureState()
        {
            var s = new SaveState { Next = Cursor };

            foreach (var kv in _itemsByDir.OrderBy(k => (int)k.Key))
                s.Filters.Add(new SaveState.DirFilter
                {
                    Direction = kv.Key,
                    ItemIds = kv.Value.Select(i => i.Id).OrderBy(id => id).ToList(),
                });

            s.Blocked = _blocked.OrderBy(d => (int)d).ToList();
            s.Passed = _passed.Select(i => i.Id).OrderBy(id => id).ToList();
            return s;
        }

        public void RestoreState(JToken state)
        {
            var s = state?.ToObject<SaveState>();
            if (s == null) return;

            _dirsByItem.Clear();
            _itemsByDir.Clear();
            _blocked.Clear();
            _passed.Clear();

            Cursor = s.Next;

            // AddFilter를 거치면 두 딕셔너리의 역인덱스 불변식이 저절로 지켜진다
            if (s.Filters != null)
                foreach (var f in s.Filters)
                    if (f?.ItemIds != null)
                        foreach (var id in f.ItemIds)
                            AddFilter(f.Direction, FindItem(id));

            if (s.Blocked != null)
                foreach (var d in s.Blocked) _blocked.Add(d);

            if (s.Passed != null)
                foreach (var id in s.Passed)
                {
                    var item = FindItem(id);
                    if (item != null) _passed.Add(item);
                }
        }

        static ItemDef FindItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var def = SimHost.Database?.Item(id);
            if (def == null) UnityEngine.Debug.LogWarning($"[Router] 세이브의 아이템 id \"{id}\"가 팩에 없습니다 — 그 항목은 건너뜁니다.");
            return def;
        }
    }
}
