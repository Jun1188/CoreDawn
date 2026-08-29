using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>
    /// 합류기. 여러 입력 연결의 아이템을 하나의 출력으로 모은다.
    /// 벨트가 아니므로 세그먼트는 합류기 앞뒤에서 끊긴다 (팀 합의: 분기/합류 = 전용 건물).
    ///
    /// 입력 공정성: 상류들이 입력 버퍼(1칸)를 선착순으로 채운다 — 막힌 벨트는
    /// 매 틱 재시도하므로 대체로 번갈아 들어오지만 엄밀한 공정성은 아님.
    /// TODO(공정성): 굶는 라인이 눈에 띄면 입력 연결별 라운드로빈 수용으로 개선.
    /// </summary>
    [CreateAssetMenu(fileName = "NewMerger", menuName = "Factory/Buildings/Merger")]
    public class MergerDataSO : BuildingDataSO
    {
        public override IBuildingBehavior CreateBehavior(BuildingModule building)
            => new MergerBehavior(building);
    }

    // ─── 행동 ──────────────────────────────────────────────────────

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
