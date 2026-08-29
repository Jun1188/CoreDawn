using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoreDawn.Sim
{
    /// <summary>아이템 정의. 탄약·무기 성질은 modules[](<see cref="ItemModuleDef"/>)로 붙는다.</summary>
    public sealed class ItemDef : Def
    {
        [JsonProperty("type")] public string Type;       // 분류 이름(Resource·Ammo·Weapon…) — 심은 문자열로만 안다
        [JsonProperty("line")] public string Line;
        [JsonProperty("maxStack")] public int MaxStack = 50;
        [JsonProperty("hideFromMenu")] public bool HideFromMenu;
        [JsonProperty("modules")] public List<ItemModuleDef> Modules = new List<ItemModuleDef>();

        [JsonProperty("view")] public JObject View;

        public T Get<T>() where T : ItemModuleDef
        {
            foreach (var m in Modules)
                if (m is T t) return t;
            return null;
        }

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            foreach (var m in Modules) m.Resolve(db, errors, Id);
        }
    }

    /// <summary>탄약 성질 — 탄속·중력·폭발·수명·관통과 명중 효과.</summary>
    public sealed class AmmoModuleDef : ItemModuleDef
    {
        [JsonProperty("speed")] public float Speed = 70f;
        [JsonProperty("gravity")] public float Gravity;
        [JsonProperty("explosionRadius")] public float ExplosionRadius;
        [JsonProperty("lifetime")] public float Lifetime = 3f;
        [JsonProperty("pierce")] public int Pierce;
        [JsonProperty("effects")] public List<EffectUse> Effects = new List<EffectUse>();

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var e in Effects) e.Resolve(db, errors, owner);
        }
    }

    /// <summary>무기 아이템 — 어떤 총(guns 섹션)인가.</summary>
    public sealed class WeaponModuleDef : ItemModuleDef
    {
        [JsonProperty("gun")] public string GunId;
    }
}
