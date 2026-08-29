using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 컨베이어 벨트. 실제 아이템 이동은 BeltSegment가 담당하고,
    /// 이 행동은 입력 버퍼를 세그먼트에 올리는 것과 세그먼트 구동 대표 역할만 한다.
    /// </summary>
    public class BeltBehavior : IBuildingBehavior
    {
        readonly BuildingModule _b;
        public BeltBehavior(BuildingModule b) => _b = b;
        public void OnAfterPlaced() { }

        public void Tick(float dt)
        {
            var seg = _b.Factory.Belts.EnsureSegment(_b);  // 항상 세그먼트 존재

            // 입력 버퍼 아이템을 벨트 위로 (입구가 막혔으면 받아준 만큼만 소비).
            // TryAddItem은 세그먼트 입구(pos 0) 삽입 — 생산자로부터 입력을 받는 벨트는
            // 상류 벨트가 없는 벨트뿐이므로(1입력 포트) 항상 자기 세그먼트의 입구다.
            foreach (var (item, count) in _b.Input.Snapshot())
            {
                int moved = 0;
                while (moved < count && seg.TryAddItem(item)) moved++;
                if (moved > 0)
                {
                    _b.Input.TryConsume(item, moved);
                    _b.NotifyUpstream(); // 입력 버퍼에 자리 생김 → 막혀 있던 상류 깨움
                }
            }

            // 대표 벨트(입구 = 마지막 인덱스)가 세그먼트 전체를 1번만 구동
            if (seg.BeltCount > 0 && seg.Belts[^1] == _b)
                seg.Tick(dt);

            // 입구가 막혀 버퍼가 안 비면 다음 틱에 재시도
            if (_b.Input.HasAny)
                _b.Factory.MarkDirty(_b);
        }
    }
}
