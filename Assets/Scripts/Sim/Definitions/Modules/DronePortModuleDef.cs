using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>드론 항구(팀원 데이터) — 값만 옮겨 둔다. 런타임은 아직 없다.</summary>
    public sealed class DronePortModuleDef : EntityModuleDef
    {
        [JsonProperty("carryCapacity")] public int CarryCapacity = 10;
        [JsonProperty("droneRange")] public float DroneRange = 20f;
        [JsonProperty("travelSpeed")] public float TravelSpeed = 5f;
    }
}
