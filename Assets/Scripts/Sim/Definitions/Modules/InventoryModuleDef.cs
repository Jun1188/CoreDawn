using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>역할별 아이템 컨테이너 — input·output·main·hotbar 슬롯 수. 플레이어·저장고·기계 공용.</summary>
    public sealed class InventoryModuleDef : EntityModuleDef
    {
        [JsonProperty("input")] public int Input;
        [JsonProperty("output")] public int Output;
        [JsonProperty("main")] public int Main;
        [JsonProperty("hotbar")] public int Hotbar;
        /// <summary>슬롯당 상한. 0 = 아이템의 maxStack 그대로.</summary>
        [JsonProperty("stackCap")] public int StackCap;

        public override EntityModule Create(Entity entity) => new InventoryModule(this);
    }
}
