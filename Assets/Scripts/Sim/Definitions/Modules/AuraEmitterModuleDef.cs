using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>오라 — 반경 안의 적에게 주기적으로 효과. 반경·주기는 전달의 것이라 여기, 효과는 탄의 출처(AmmoConsumer 또는 FixedAmmo)가 정한다.</summary>
    public sealed class AuraEmitterModuleDef : EntityModuleDef
    {
        [JsonProperty("radius")] public float Radius = 5f;      // m
        [JsonProperty("interval")] public float Interval = 1f;


        public override EntityModule Create(Entity entity) => new AuraEmitterModule(this);
    }
}
