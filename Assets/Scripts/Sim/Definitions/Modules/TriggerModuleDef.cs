using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>지뢰 — 밟히면 반경 안에 효과, once면 한 번 쓰고 사라진다.</summary>
    public sealed class TriggerModuleDef : EntityModuleDef
    {
        [JsonProperty("radius")] public float Radius = 2f;   // m
        [JsonProperty("once")] public bool Once = true;
        [JsonProperty("cooldown")] public float Cooldown = 1f;   // once가 아닐 때 다시 터질 때까지의 초


        public override EntityModule Create(Entity entity) => new TriggerModule(this);
    }
}
