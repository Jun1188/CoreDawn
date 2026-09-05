using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>입출력 포트 — 어느 칸의 어느 면이 입력/출력인가. 벨트·기계·저장고·코어가 쓴다.</summary>
    public sealed class PortsModuleDef : EntityModuleDef
    {
        [JsonProperty("ports")] public List<PortDef> Ports = new List<PortDef>();
    }

    public sealed class PortDef
    {
        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
        [JsonProperty("dir")] public string Dir = "North";   // North·East·South·West
        [JsonProperty("isInput")] public bool IsInput;
    }
}
