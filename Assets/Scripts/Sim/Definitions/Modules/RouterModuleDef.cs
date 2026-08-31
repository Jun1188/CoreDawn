using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>병합기·분배기 — mode = "merge" | "split".</summary>
    public sealed class RouterModuleDef : EntityModuleDef
    {
        [JsonProperty("mode")] public string Mode = "split";

        public override EntityModule Create(Entity entity) => new RouterModule(this);
    }
}
