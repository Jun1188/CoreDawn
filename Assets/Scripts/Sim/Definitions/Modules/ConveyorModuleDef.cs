using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>벨트 — 칸/초.</summary>
    public sealed class ConveyorModuleDef : EntityModuleDef
    {
        [JsonProperty("speedTilesPerSec")] public float SpeedTilesPerSec = 1f;
    }
}
