using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>근접 공격 — 사거리·쿨다운·명중 효과.</summary>
    public sealed class AttackModuleDef : EntityModuleDef
    {
        [JsonProperty("range")] public float Range = 1.5f;
        [JsonProperty("cooldown")] public float Cooldown = 2f;
        [JsonProperty("effects")] public List<EffectUse> Effects = new List<EffectUse>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var e in Effects) e.Resolve(db, errors, owner);
        }
    }
}
