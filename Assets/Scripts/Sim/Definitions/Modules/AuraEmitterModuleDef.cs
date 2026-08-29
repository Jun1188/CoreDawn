using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>오라 — 반경 안의 적에게 주기적으로 효과. 반경은 효과가 아니라 전달의 것이라 여기 있다.</summary>
    public sealed class AuraEmitterModuleDef : EntityModuleDef
    {
        [JsonProperty("radius")] public float Radius = 5f;      // 칸
        [JsonProperty("interval")] public float Interval = 1f;
        [JsonProperty("effects")] public List<EffectUse> Effects = new List<EffectUse>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var e in Effects) e.Resolve(db, errors, owner);
        }
    }
}
