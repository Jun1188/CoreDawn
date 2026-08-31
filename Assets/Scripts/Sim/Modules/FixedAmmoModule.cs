using System;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 고정 탄(심 모듈) — <see cref="IAmmoSource"/>의 무한판. 항상 쏠 수 있고 아무것도 소비하지 않는다.
    /// 지뢰의 장약·연료 없는 오라가 이걸 단다. 탄 아이템이 없으므로 round는 null — 뷰는 탄 프리팹 없이 연출만 건너뛴다
    /// (오라·기폭은 심이 효과를 직접 걸어 뷰가 필요 없다).
    /// </summary>
    public sealed class FixedAmmoModule : EntityModule, IAmmoSource
    {
        public FixedAmmoModuleDef Def { get; }

        public FixedAmmoModule(FixedAmmoModuleDef def) { Def = def ?? throw new ArgumentNullException(nameof(def)); }

        AmmoModuleDef Ammo => Def.Ammo ?? Def.Build();

        public bool HasAmmo => true;

        public bool TryPeek(out AmmoModuleDef ammo, out ItemDef round) { ammo = Ammo; round = null; return true; }

        public bool TryTake(out AmmoModuleDef ammo, out ItemDef round) { ammo = Ammo; round = null; return true; }

        public Effect[] Bake(AmmoModuleDef ammo)
        {
            var effects = EffectUse.ToEffects(ammo.Effects);
            var mine = Owner?.Get<EffectsModule>();
            return mine != null ? mine.BakeOutgoing(effects) : effects;
        }

        // 소비가 없으니 발화할 일도 없다
        event Action<ItemDef> IAmmoSource.Consumed { add { } remove { } }
    }
}
