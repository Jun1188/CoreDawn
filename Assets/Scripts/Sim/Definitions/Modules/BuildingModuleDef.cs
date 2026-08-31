using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 건물 — 격자 풋프린트와 배치 규칙. 키는 전부 평행이다(배치 정보를 블록으로 묶지 않는다).
    /// 배치 불가(둥지·나무·코어)는 placeable=false. 런타임 모듈(BuildingModule)은 5a-2에서 이 정의로 만든다.
    /// </summary>
    public sealed class BuildingModuleDef : EntityModuleDef
    {
        [JsonProperty("size")] public Vec2i Size = new Vec2i(1, 1);
        [JsonProperty("placeable")] public bool Placeable = true;
        [JsonProperty("isDemolishable")] public bool IsDemolishable = true;
        [JsonProperty("isAttackable")] public bool IsAttackable = true;
        /// <summary>밟고 지나갈 수 있는가 — 길찾기가 이 건물의 칸을 땅으로 본다(지뢰). 배치 격자는 그대로 차지한다(겹쳐 놓지 못한다).</summary>
        [JsonProperty("walkable")] public bool Walkable;
        [JsonProperty("category")] public string Category;
        [JsonProperty("requiredCoreTier")] public int RequiredCoreTier;
        [JsonProperty("threatSeedCost")] public int ThreatSeedCost;
        [JsonProperty("menuOrder")] public int MenuOrder;
        [JsonProperty("cost")] public List<ItemAmount> Cost = new List<ItemAmount>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var c in Cost) c.Resolve(db, errors, owner);
        }
    }
}
