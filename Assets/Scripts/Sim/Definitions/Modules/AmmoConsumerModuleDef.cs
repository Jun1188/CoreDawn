using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>탄약 소비 — 받는 탄약 종류와 피해 배율. 발사기(Turret·AuraEmitter·Trigger)가 공유하는 "발사 문"의 값.</summary>
    public sealed class AmmoConsumerModuleDef : EntityModuleDef
    {
        [JsonProperty("ammoFilter")] public List<string> AmmoFilterIds = new List<string>();
        [JsonProperty("damageMultiplier")] public float DamageMultiplier = 1f;

        [JsonIgnore] public List<ItemDef> AmmoFilter { get; } = new List<ItemDef>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            AmmoFilter.Clear();
            foreach (var id in AmmoFilterIds)
            {
                var i = db.ResolveItem(id, errors, owner);
                if (i == null) continue;
                if (i.Get<AmmoModuleDef>() == null) { errors.Add($"{owner}: ammoFilter '{id}'은(는) 탄약(Ammo 모듈)이 아닙니다"); continue; }
                AmmoFilter.Add(i);
            }
        }

        public override EntityModule Create(Entity entity) => new AmmoConsumerModule(this);
    }
}
