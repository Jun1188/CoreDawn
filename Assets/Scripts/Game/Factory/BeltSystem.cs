using System.Collections.Generic;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    // ─── 벨트 시스템 ──────────────────────────────────────────────

    /// <summary>
    /// 벨트 — 엔티티 단위가 아니라 세그먼트(여러 벨트를 합친 것) 단위로 산다(구 BeltSegmentManager + BeltBehavior).
    /// 연결/해제 이벤트를 받아 BeltSegment를 생성·병합·분리하고, 벨트 건물의 틱을 돌린다. (plain C#)
    /// BuildingGraph.RegisterConn() → OnNewConnection() 순으로 호출된다.
    /// Conveyor 정의는 속도·모양 값만 갖는 데이터 전용이라 벨트 엔티티에 모듈이 없다.
    /// </summary>
    public class BeltSystem
    {
        readonly FactorySystem _sim;

        /// <summary>
        /// 벨트 철거로 세그먼트에서 밀려난(폐기될) 아이템 통지 — (제거된 벨트, 아이템).
        /// 심은 월드를 모르므로 여기서 버리기만 하고, 드라이버(FactoryBootstrap)가
        /// 구독해 월드 드롭으로 되살린다.
        /// </summary>
        public event System.Action<BuildingModule, ItemDef> ItemDiscarded;

        readonly Dictionary<BuildingModule, BeltSegment> _map = new();
        readonly List<BeltSegment> _segs = new();

        public IReadOnlyList<BeltSegment> Segments => _segs;

        public BeltSystem(FactorySystem sim) => _sim = sim;

        /// <summary>
        /// 벨트 틱(구 BeltBehavior.Tick) — 입력 버퍼를 세그먼트 입구에 올리고,
        /// 대표 벨트(입구 = 마지막 인덱스)가 세그먼트 전체를 1번만 구동한다. 실제 아이템 이동은 BeltSegment가 담당한다.
        /// </summary>
        public void Tick(BuildingModule belt, float dt)
        {
            var seg = EnsureSegment(belt);  // 항상 세그먼트 존재

            // 입력 버퍼 아이템을 벨트 위로 (입구가 막혔으면 받아준 만큼만 소비).
            // TryAddItem은 세그먼트 입구(pos 0) 삽입 — 생산자로부터 입력을 받는 벨트는
            // 상류 벨트가 없는 벨트뿐이므로(1입력 포트) 항상 자기 세그먼트의 입구다.
            foreach (var (item, count) in belt.Input.Snapshot())
            {
                int moved = 0;
                while (moved < count && seg.TryAddItem(item)) moved++;
                if (moved > 0)
                {
                    belt.Input.TryConsume(item, moved);
                    belt.NotifyUpstream(); // 입력 버퍼에 자리 생김 → 막혀 있던 상류 깨움
                }
            }

            // 대표 벨트(입구 = 마지막 인덱스)가 세그먼트 전체를 1번만 구동
            if (seg.BeltCount > 0 && seg.Belts[^1] == belt)
                seg.Tick(dt);

            // 입구가 막혀 버퍼가 안 비면 다음 틱에 재시도
            if (belt.Input.HasAny)
                _sim.MarkDirty(belt);
        }

        /// <summary>이 벨트의 세그먼트를 보장(없으면 1칸 세그먼트 즉시 생성).</summary>
        public BeltSegment EnsureSegment(BuildingModule belt)
        {
            if (_map.TryGetValue(belt, out var s)) return s;
            var seg = new BeltSegment(_sim);
            if (belt.Def.Get<ConveyorModuleDef>() is { } conveyor)
                seg.SpeedTilesPerSec = conveyor.SpeedTilesPerSec;
            seg.Belts.Add(belt);
            _map[belt] = seg;
            _segs.Add(seg);
            return seg;
        }

        public BeltSegment GetSegment(BuildingModule b) =>
            _map.TryGetValue(b, out var s) ? s : null;

        /// <summary>벨트-벨트 연결 시 병합. From=상류, To=하류.</summary>
        public void OnNewConnection(BuildingConnection c)
        {
            if (!c.From.IsConveyor) return;
            if (!c.To.IsConveyor) return;

            // 세그먼트는 1자 체인만 표현한다. 합류/분배는 전용 건물(비 Transport)이
            // 담당하기로 했으므로, 벨트가 여러 벨트와 이어지는 경우는 병합하지 않는다.
            if (c.From.OutputConnections.Count > 1 || c.To.InputConnections.Count > 1) return;

            // 같은 벨트 종류(동일 SO 에셋)끼리만 병합 — 티어가 다르면(고속 벨트 등)
            // 경계에서 세그먼트가 끊기고, 아이템은 버퍼 push로 넘어간다.
            if (c.From.Def != c.To.Def) return;

            var sf = EnsureSegment(c.From);   // 상류
            var st = EnsureSegment(c.To);     // 하류
            if (sf == st) return;             // 이미 같은 세그먼트(루프) → 무시

            int fromCount = sf.BeltCount;

            // 합쳐진 순서(출구→입구) = To의 벨트들, 그다음 From의 벨트들.
            // sf(From)를 살리고 To의 벨트들을 출구 쪽(앞)에 끼운다.
            for (int i = st.Belts.Count - 1; i >= 0; i--)
            {
                var b = st.Belts[i];
                sf.Belts.Insert(0, b);
                _map[b] = sf;
            }

            // 아이템 이관: From 아이템은 pos 유지, To 아이템은 +fromCount 만큼 밀어 출구 쪽으로
            foreach (var (item, pos) in st.Items)
                sf.AddItemAt(item, pos + fromCount);

            _segs.Remove(st);

            // 병합된 세그먼트에 아이템이 있으면 새 대표(입구) 벨트가 구동을 이어받는다
            if (sf.HasItems) _sim.MarkDirty(sf.Belts[^1]);
        }

        /// <summary>벨트 철거 시 세그먼트를 상류·하류로 정밀 분할. 제거 벨트 위 아이템은 폐기.</summary>
        public void OnBuildingRemoved(BuildingModule b)
        {
            if (!_map.TryGetValue(b, out var seg)) return;

            int k = seg.Belts.IndexOf(b);   // 0 = 출구
            int n = seg.BeltCount;

            // 옛 세그먼트 등록 해제
            _segs.Remove(seg);
            foreach (var belt in seg.Belts) _map.Remove(belt);

            // 하류 조각: Belts[0..k-1], pos 구간 [n-k, n] → pos -= (n-k)
            if (k > 0)
            {
                var d = new BeltSegment(_sim) { SpeedTilesPerSec = seg.SpeedTilesPerSec };
                for (int i = 0; i < k; i++) { d.Belts.Add(seg.Belts[i]); _map[seg.Belts[i]] = d; }
                foreach (var (item, pos) in seg.Items)
                    if (pos >= n - k) d.AddItemAt(item, pos - (n - k));
                _segs.Add(d);
                if (d.HasItems) _sim.MarkDirty(d.Belts[^1]); // 대표만 깨움
            }

            // 상류 조각: Belts[k+1..n-1], pos 구간 [0, n-1-k] → pos 유지
            if (k < n - 1)
            {
                var u = new BeltSegment(_sim) { SpeedTilesPerSec = seg.SpeedTilesPerSec };
                for (int i = k + 1; i < n; i++) { u.Belts.Add(seg.Belts[i]); _map[seg.Belts[i]] = u; }
                foreach (var (item, pos) in seg.Items)
                    if (pos < n - 1 - k) u.AddItemAt(item, pos);
                _segs.Add(u);
                if (u.HasItems) _sim.MarkDirty(u.Belts[^1]); // 대표만 깨움
            }

            // (n-1-k ≤ pos < n-k) 구간 = 제거 벨트 위 아이템 → 세그먼트에서는 폐기, 드라이버에 통지
            foreach (var (item, pos) in seg.Items)
                if (pos >= n - 1 - k && pos < n - k)
                    ItemDiscarded?.Invoke(b, item);
        }
    }
}
