using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>사망 드롭 — 정의된 드롭 목록과, 그릇(Inventory) 내용물을 함께 떨굴지. 실물 스폰은 게임(LootSpawner)이 Died를 듣고 한다.</summary>
    public sealed class LootModuleDef : EntityModuleDef
    {
        [JsonProperty("drops")] public List<ItemAmount> Drops = new List<ItemAmount>();
        /// <summary>죽을 때 Inventory 모듈의 내용물도 떨군다(건물 기본). 플레이어처럼 소지품을 지키는 쪽은 Loot 자체가 없다.</summary>
        [JsonProperty("dropInventory")] public bool DropInventory = true;

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var d in Drops) d.Resolve(db, errors, owner);
        }

        public override EntityModule Create(Entity entity) => new LootModule(this);
    }
}
