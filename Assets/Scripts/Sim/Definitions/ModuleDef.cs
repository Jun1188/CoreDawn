using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 모듈 정의 — json modules[] 항목 하나. "type" 키로 종류를 고르고(명시 표, <see cref="SimSchema"/>) 나머지 키가 값이다.
    /// 정의는 불변·정의당 하나·공유이고, 런타임 모듈은 엔티티당 하나·상태를 가진다(마크의 Item/ItemStack).
    /// </summary>
    public abstract class ModuleDef
    {
        /// <summary>json의 "type" — 컨버터가 채운다.</summary>
        [JsonIgnore] public string TypeName { get; internal set; }

        public virtual void Resolve(SimDatabase db, List<string> errors, string owner) { }
    }

    /// <summary>
    /// 엔티티 모듈 정의. <see cref="Create"/>가 런타임 모듈을 만든다 — <b>정의 하나 → 모듈 0 또는 1개</b>.
    /// 데이터 전용 정의(다른 시스템이 읽기만 하는 것)는 null을 돌려준다. 정체성 마커는 두지 않는다 —
    /// 행동이 있으면 모듈, 없으면 값이다.
    /// </summary>
    public abstract class EntityModuleDef : ModuleDef
    {
        public virtual EntityModule Create(Entity entity) => null;
    }

    /// <summary>아이템 모듈 정의(탄약·무기) — 엔티티가 아니라 아이템 정의에 붙는다.</summary>
    public abstract class ItemModuleDef : ModuleDef { }
}
