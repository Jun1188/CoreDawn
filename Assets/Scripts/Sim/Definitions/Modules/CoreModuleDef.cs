using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>코어 — 티어 진행·보호막·HP 보너스. 마커가 아니라 진짜 모듈이다: "코어인가"는 이 모듈의 존재로 판정한다.</summary>
    public sealed class CoreModuleDef : EntityModuleDef
    {
        [JsonProperty("tiers")] public List<CoreTierDef> Tiers = new List<CoreTierDef>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var t in Tiers)
                foreach (var r in t.Requirements) r.Resolve(db, errors, owner);
        }
    }

    public sealed class CoreTierDef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("requirements")] public List<ItemAmount> Requirements = new List<ItemAmount>();
        [JsonProperty("unlocks")] public List<string> Unlocks = new List<string>();
        [JsonProperty("maxHpBonus")] public float MaxHpBonus;
        [JsonProperty("isFinal")] public bool IsFinal;
    }
}
