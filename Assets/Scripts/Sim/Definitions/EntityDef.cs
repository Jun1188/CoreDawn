using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 엔티티 정의 — 건물·몬스터·둥지·나무·광맥·플레이어가 모두 이 한 스키마다. "무엇인가"는 kind가 아니라 modules[]의 조합이 말한다.
    /// </summary>
    public sealed class EntityDef : Def
    {
        [JsonProperty("faction")] public Faction Faction = Faction.Player;
        [JsonProperty("modules")] public List<EntityModuleDef> Modules = new List<EntityModuleDef>();

        /// <summary>표현 카탈로그가 읽는 몫(모델·프리팹·아이콘 등). 심은 열어 보지 않는다.</summary>
        [JsonProperty("view")] public JObject View;

        public T Get<T>() where T : EntityModuleDef
        {
            foreach (var m in Modules)
                if (m is T t) return t;
            return null;
        }

        public bool Has<T>() where T : EntityModuleDef => Get<T>() != null;

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            foreach (var m in Modules) m.Resolve(db, errors, Id);
        }

        /// <summary>정의대로 모듈을 조립한다 — 정의 순서대로, 정의 1 → 모듈 0/1.</summary>
        public void Assemble(Entity entity)
        {
            entity.Def = this;
            foreach (var m in Modules)
            {
                var module = m.Create(entity);
                if (module != null) entity.Add(module);
            }
        }
    }
}
