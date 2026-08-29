using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>체력. Create → <see cref="HealthModule"/>.</summary>
    public sealed class HealthModuleDef : EntityModuleDef
    {
        [JsonProperty("maxHp")] public float MaxHp = 100f;

        public override EntityModule Create(Entity entity) => new HealthModule(System.Math.Max(1f, MaxHp));
    }
}
