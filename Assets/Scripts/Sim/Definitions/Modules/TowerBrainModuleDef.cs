using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>타워 두뇌 — 표적·조준·발사 시점. 효과는 탄(AmmoConsumer)이 정한다.</summary>
    public sealed class TowerBrainModuleDef : EntityModuleDef
    {
        [JsonProperty("range")] public float Range = 8f;          // 칸
        [JsonProperty("minRange")] public float MinRange;
        [JsonProperty("fireRate")] public float FireRate = 1f;    // 발/초
        [JsonProperty("turnSpeed")] public float TurnSpeed = 180f;
        [JsonProperty("aimTolerance")] public float AimTolerance = 5f;
        [JsonProperty("preferHighArc")] public bool PreferHighArc;
        [JsonProperty("muzzleHeight")] public float MuzzleHeight = 1f;
    }
}
