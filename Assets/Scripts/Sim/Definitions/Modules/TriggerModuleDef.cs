using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>지뢰 — 밟히면 반경 안에 효과, once면 한 번 쓰고 사라진다.</summary>
    public sealed class TriggerModuleDef : EntityModuleDef
    {
        [JsonProperty("radius")] public float Radius = 2f;
        [JsonProperty("once")] public bool Once = true;
        [JsonProperty("effects")] public List<EffectUse> Effects = new List<EffectUse>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var e in Effects) e.Resolve(db, errors, owner);
        }
    }
}
