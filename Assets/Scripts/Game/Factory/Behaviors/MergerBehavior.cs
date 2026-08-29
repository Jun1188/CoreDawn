using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Factory
{
    public class MergerBehavior : IBuildingBehavior
    {
        readonly BuildingModule _b;
        public MergerBehavior(BuildingModule b) => _b = b;
        public void OnAfterPlaced() { }

        public void Tick(float dt)
        {
            // 입력 버퍼 → 출력 버퍼 이동 (출력 여유만큼만)
            foreach (var (item, count) in _b.Input.Snapshot())
            {
                int move = Mathf.Min(count, _b.Output.RoomFor(item));
                if (move <= 0) continue;
                _b.Output.TryAdd(item, move);
                _b.Input.TryConsume(item, move);
                _b.NotifyUpstream(); // 입력 버퍼에 자리 생김 → 막혀 있던 상류 깨움
            }

            _b.FlushOutputs();
        }
    }
}
