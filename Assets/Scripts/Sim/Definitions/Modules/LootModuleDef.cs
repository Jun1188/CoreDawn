using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>사망 드롭.</summary>
    public sealed class LootModuleDef : EntityModuleDef
    {
        [JsonProperty("drops")] public List<ItemAmount> Drops = new List<ItemAmount>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var d in Drops) d.Resolve(db, errors, owner);
        }
    }
}
