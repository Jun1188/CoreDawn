using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>채굴기 — 밑의 광맥(ResourceDeposit)에서 캔다.</summary>
    public sealed class ExtractorModuleDef : EntityModuleDef
    {
        [JsonProperty("speedMultiplier")] public float SpeedMultiplier = 1f;

        public override EntityModule Create(Entity entity) => new ExtractorModule(this);
    }
}
