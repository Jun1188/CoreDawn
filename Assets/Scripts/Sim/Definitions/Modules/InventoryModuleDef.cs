using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>역할별 아이템 컨테이너 — input·output·main 슬롯 수와 핫바 창 크기. 플레이어·저장고·기계 공용.</summary>
    public sealed class InventoryModuleDef : EntityModuleDef
    {
        [JsonProperty("input")] public int Input;
        [JsonProperty("output")] public int Output;
        /// <summary>플레이어 소지품 전체 칸 수(핫바 포함).</summary>
        [JsonProperty("main")] public int Main;
        /// <summary>main의 앞 몇 칸이 핫바(장착 선택 범위)인가 — 그릇이 아니라 창이다(마크와 같다). 0 = 핫바 없음.</summary>
        [JsonProperty("hotbar")] public int Hotbar;
        /// <summary>슬롯당 상한. 0 = 아이템의 maxStack 그대로.</summary>
        [JsonProperty("stackCap")] public int StackCap;

        public override EntityModule Create(Entity entity) => new InventoryModule(this);
    }
}
