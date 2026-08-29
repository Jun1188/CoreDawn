using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>밤 웨이브 — 어느 날부터, 몇 마리, 어떤 몬스터, 어떤 버프(효과)로.</summary>
    public sealed class WaveDef : Def
    {
        [JsonProperty("day")] public int Day = 1;
        [JsonProperty("requiredCoreTier")] public int RequiredCoreTier;
        [JsonProperty("baseAmount")] public int BaseAmount = 4;
        [JsonProperty("maxAliveAmount")] public int MaxAliveAmount = 4;
        [JsonProperty("spawnInterval")] public float SpawnInterval = 2f;
        [JsonProperty("monster")] public string MonsterId;
        [JsonProperty("buffs")] public List<EffectUse> Buffs = new List<EffectUse>();

        [JsonIgnore] public EntityDef Monster { get; private set; }

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            Monster = db.ResolveEntity(MonsterId, errors, Id);
            foreach (var b in Buffs) b.Resolve(db, errors, Id);
        }
    }
}
