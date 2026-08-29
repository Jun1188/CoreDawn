using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Save;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>채굴기. 주기적으로 아이템을 생산해 출력 포트로 내보낸다.</summary>
    [CreateAssetMenu(fileName = "NewMiner", menuName = "Factory/Buildings/Miner")]
    public class MinerDataSO : BuildingDataSO
    {
        [Header("채굴")]
        [Tooltip("채굴 속도 배율. Mk.1 = 1, Mk.2 = 2.\n" +
                 "실제 시간 = 광맥의 extractInterval ÷ 이 값.\n" +
                 "\"얼마나 좋은 채굴기인가\"는 건물이, \"얼마나 캐기 어려운 광맥인가\"는 땅이 갖는다.")]
        public float speedMultiplier = 1f;

        public override IBuildingBehavior CreateBehavior(BuildingModule building)
            => new MinerBehavior(building, this);
    }

    // ─── 행동 ──────────────────────────────────────────────────────

}
