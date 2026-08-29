using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>광맥 — 어떤 자원이 얼마나, 얼마 만에 다시 차는가, 손으로 캐면 얼마나.</summary>
    public sealed class ResourceDepositModuleDef : EntityModuleDef
    {
        [JsonProperty("resource")] public string ResourceId;
        [JsonProperty("maxStock")] public int MaxStock = 20;
        [JsonProperty("regenInterval")] public float RegenInterval = 1f;
        [JsonProperty("amountPerCycle")] public int AmountPerCycle = 1;
        [JsonProperty("manualSeconds")] public float ManualSeconds = 3f;
        [JsonProperty("manualYield")] public int ManualYield = 1;

        [JsonIgnore] public ItemDef Resource { get; private set; }

        public override void Resolve(SimDatabase db, List<string> errors, string owner) => Resource = db.ResolveItem(ResourceId, errors, owner);
    }
}
