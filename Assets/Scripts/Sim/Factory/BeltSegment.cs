using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 연속으로 연결된 같은 종류의 벨트 체인을 하나의 유닛으로 처리. (plain C#)
    ///
    /// 문제: 벨트 100개 체인 → 100번 Tick, 100번 MarkDirty
    /// 해결: 100개를 BeltSegment 1개로 통합 → 1번 Tick
    ///
    /// 아이템 위치를 float 값으로 추적:
    ///   0.0 = 세그먼트 입구
    ///   N   = 세그먼트 출구 도달 (N = 벨트 수)
    ///
    /// 앞에서부터 처리하면 앞 아이템이 뒤 아이템의 진입을 막는
    /// 실제 컨베이어 벨트 물리를 재현할 수 있다.
    /// </summary>
    public class BeltSegment
    {
        public readonly List<BuildingModule> Belts = new();
        public float SpeedTilesPerSec = 2f;

        public const float Spacing = 0.5f;   // 아이템 간 최소 간격 (뷰의 외삽 클램프도 사용)

        readonly FactorySystem _sim;

        // 불변식: pos 내림차순. index 0 = 출구에 가장 가까움(pos 큼).
        readonly List<(ItemDef item, float pos)> _items = new();

        public BeltSegment(FactorySystem sim) => _sim = sim;

        public bool HasItems => _items.Count > 0;
        public IReadOnlyList<(ItemDef item, float pos)> Items => _items;
        public int BeltCount => Belts.Count;

        /// <summary>입구(pos 0)에 새 아이템 올릴 공간이 있는가. 입구 쪽 = 마지막 인덱스.</summary>
        public bool CanAcceptAtEntry()
            => _items.Count == 0 || _items[^1].pos >= Spacing;

        /// <summary>입구에 아이템 투입. 막혀 있으면 false(버퍼 유지용). O(1).</summary>
        public bool TryAddItem(ItemDef item)
        {
            if (!CanAcceptAtEntry()) return false;
            _items.Add((item, 0f));   // pos 0 = 최소 = 리스트 끝. 정렬 유지.
            return true;
        }

        /// <summary>임의 pos에 삽입(merge/split 이관용). 내림차순 위치 유지.</summary>
        public void AddItemAt(ItemDef item, float pos)
        {
            int i = 0;
            while (i < _items.Count && _items[i].pos > pos) i++;
            _items.Insert(i, (item, pos));
        }

        /// <summary>세이브 복원 전용 — 벨트 위 아이템을 전부 치운다. 저장된 배치를 얹기 직전에 부른다.</summary>
        public void ClearItems() => _items.Clear();

        /// <summary>
        /// index의 아이템을 벨트에서 꺼낸다 — 플레이어가 지나가는 것을 조준해 낚아채는 경로.
        ///
        /// 입구 벨트 하나만 깨우면 충분하다: 그것이 BeltSystem.Tick을 돌리고, 거기서
        /// 입력 버퍼를 세그먼트로 밀면서 NotifyUpstream까지 스스로 부른다. 손으로 뺀 자리는
        /// 벨트를 거치지 않은 변화라 아무도 깨워 주지 않으므로(보관소·설비와 같은 이유)
        /// 여기서 걸어 주지 않으면 뒤 아이템이 다음 입고 때까지 그 자리에 선다.
        /// </summary>
        public bool TryTakeAt(int index, out ItemDef item)
        {
            item = null;
            if (index < 0 || index >= _items.Count) return false;

            item = _items[index].item;
            _items.RemoveAt(index);
            if (BeltCount > 0) _sim.MarkDirty(Belts[^1]);
            return true;
        }

        public void Tick(float dt)
        {
            float advance = SpeedTilesPerSec * dt;

            // 출구 쪽(index 0)부터 처리해야 앞 아이템→뒤 아이템 블로킹이 올바르다.
            for (int i = 0; i < _items.Count; i++)
            {
                var (item, pos) = _items[i];
                float newPos = pos + advance;

                // 바로 앞(출구 쪽 = index i-1)과 0.5칸 간격 유지
                if (i - 1 >= 0)
                    newPos = Mathf.Min(newPos, _items[i - 1].pos - Spacing);

                newPos = Mathf.Max(newPos, pos);   // 역주행 방지

                if (newPos >= BeltCount)
                {
                    var exitBelt = Belts[0];        // ★ index 0 = 출구
                    bool sent = false;
                    foreach (var conn in exitBelt.OutputConnections)
                    {
                        if (!conn.To.Input.TryAdd(item)) continue;
                        _sim.MarkDirty(conn.To);
                        _items.RemoveAt(i);
                        i--;
                        sent = true;
                        break;
                    }
                    if (!sent) _items[i] = (item, BeltCount - 0.01f);
                }
                else
                {
                    _items[i] = (item, newPos);
                }
            }

            // 아이템이 남아 있는 동안은 대표(입구) 벨트만 재등록해 세그먼트를 계속 구동.
            // 세그먼트당 매 틱 1건 — 벨트 수와 무관.
            if (HasItems)
                _sim.MarkDirty(Belts[^1]);
        }
    }
}
