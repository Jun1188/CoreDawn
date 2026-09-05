using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 고정 탄 — 탄창 없이 자기 정의의 탄으로 쏘는 발사기의 탄(무한, 소비 없음). 지뢰의 장약, 연료 없는 오라의 효과.
    /// 키는 탄약 아이템의 Ammo 모듈과 같다(탄속·중력·폭발·수명·관통·효과) — 발사기는 둘을 구분하지 않고 <see cref="IAmmoSource"/>로만 본다.
    /// 아이템을 가리키지 않는다: 고정 데이터의 건물이 남의 아이템 값을 끌어오지 않는다.
    /// </summary>
    public sealed class FixedAmmoModuleDef : EntityModuleDef
    {
        [JsonProperty("speed")] public float Speed = 70f;
        [JsonProperty("gravity")] public float Gravity;
        [JsonProperty("explosionRadius")] public float ExplosionRadius;
        [JsonProperty("lifetime")] public float Lifetime = 3f;
        [JsonProperty("pierce")] public int Pierce;
        [JsonProperty("effects")] public List<EffectUse> Effects = new List<EffectUse>();

        /// <summary>발사기가 읽는 탄 성질 — 로드 뒤 Resolve가 만든다. 코드 조립(테스트)은 <see cref="Build"/>.</summary>
        [JsonIgnore] public AmmoModuleDef Ammo { get; private set; }

        public override void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            foreach (var e in Effects) e.Resolve(db, errors, owner);
            Build();
        }

        public AmmoModuleDef Build()
        {
            Ammo = new AmmoModuleDef { Speed = Speed, Gravity = Gravity, ExplosionRadius = ExplosionRadius, Lifetime = Lifetime, Pierce = Pierce, Effects = Effects };
            return Ammo;
        }

        public override EntityModule Create(Entity entity) => new FixedAmmoModule(this);
    }
}
