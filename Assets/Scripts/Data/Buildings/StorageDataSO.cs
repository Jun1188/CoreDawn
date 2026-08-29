using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.UI;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>저장소. 받은 아이템을 보관하고 연결된 하류로 내보낸다. 용량은 버퍼 크기로 설정.</summary>
    [CreateAssetMenu(fileName = "NewStorage", menuName = "Factory/Buildings/Storage")]
    public class StorageDataSO : BuildingDataSO
    {
        public override IBuildingBehavior CreateBehavior(BuildingModule building)
            => new StorageBehavior(building);
    }

    // ─── 행동 ──────────────────────────────────────────────────────

}
