using System;
using System.Collections.Generic;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 탄약 소비 — 심 모듈. "무엇을 받을지"(입력 그릇의 필터)와 "한 발 꺼내기", 그리고 꺼낸 탄의 효과에
    /// 발사기 배율(damageMultiplier)과 소유자의 공격 버프를 구워 최종 효과 목록을 만드는 일까지.
    ///
    /// 발사기(<see cref="TurretModule"/>·<see cref="AuraEmitterModule"/>·<see cref="TriggerModule"/>)가 공유하는 "발사 문"이다:
    /// 언제 쏠지는 저마다 다르지만 "탄이 있는가 · 한 발 꺼낸다 · 효과는 탄이 정한다"는 같다.
    /// 탄창은 엔티티의 Inventory 입력 그릇 — 벨트가 넣는 곳이 곧 탄창이라 공장 배관이 그대로 보급선이 된다.
    /// 탄창 없이 자기 정의의 탄으로 쏘는 건물(지뢰·연료 없는 오라)은 이것 대신 <see cref="FixedAmmoModule"/>을 단다.
    /// 상류 깨우기(NotifyUpstream)는 공장의 일이라 <see cref="Consumed"/>를 듣는 행동이 맡는다.
    /// </summary>
    public sealed class AmmoConsumerModule : EntityModule, IAmmoSource
    {
        public AmmoConsumerModuleDef Def { get; }

        ItemContainer _input;   // 탄창 = Inventory 입력 그릇. 없으면 null(입력 없는 소유자)

        /// <summary>한 발 꺼냈다 — (탄 아이템). 공장 행동이 듣고 상류를 깨운다.</summary>
        public event Action<ItemDef> Consumed;

        public AmmoConsumerModule(AmmoConsumerModuleDef def) { Def = def ?? throw new ArgumentNullException(nameof(def)); }

        protected internal override void OnAttach()
        {
            _input = Owner.Get<InventoryModule>()?.Input;
            // 조립기가 현재 레시피의 재료만 받는 것과 같은 구조 — 거절된 push는 상류에 배압으로 전달된다
            if (_input != null) _input.AcceptFilter = Accepts;
        }

        /// <summary>탄창(입력 그릇). 없으면 null — 지뢰처럼 장전 없이 장약으로만 쏘는 소유자.</summary>
        public ItemContainer Magazine => _input;

        public bool Accepts(ItemDef item) => item != null && Def.AmmoFilter.Contains(item);

        /// <summary>장전된 탄약이 하나라도 있는가. 탄창이 없으면 false.</summary>
        public bool HasAmmo => _input != null && _input.HasAny;

        /// <summary>다음에 나갈 탄 — 소비하지 않고 본다(곡사 여부·탄속을 미리 알아야 조준할 수 있다). 없으면 null.</summary>
        public ItemDef PeekRound()
        {
            if (_input == null) return null;
            foreach (var (item, n) in _input.Snapshot())
                if (n > 0) return item;
            return null;
        }

        /// <summary>한 발 소비. 소비한 탄이 발사의 전부를 정의한다 — 유탄을 먹인 박격포는 포물선 폭발탄을 쏜다.</summary>
        public bool TryConsume(out ItemDef round)
        {
            round = null;
            if (_input == null) return false;
            foreach (var (item, n) in _input.Snapshot())
            {
                if (n <= 0 || !_input.TryConsume(item, 1)) continue;
                round = item;
                Consumed?.Invoke(item);
                return true;
            }
            return false;
        }

        /// <summary>아이템의 탄약 성질. 탄약이 아닌 아이템이 필터에 들어 있으면 정의 오류 — 조용히 넘기지 않는다.</summary>
        public static AmmoModuleDef AmmoOf(ItemDef item)
            => item?.Get<AmmoModuleDef>()
               ?? throw new InvalidOperationException($"'{item?.Id ?? "(null)"}'은(는) 탄약(Ammo 모듈)이 아닙니다 — ammoFilter 정의를 확인하세요");

        // ── IAmmoSource ──
        public bool TryPeek(out AmmoModuleDef ammo, out ItemDef round)
        {
            round = PeekRound();
            ammo = round != null ? AmmoOf(round) : null;
            return ammo != null;
        }

        public bool TryTake(out AmmoModuleDef ammo, out ItemDef round)
        {
            ammo = null;
            if (!TryConsume(out round)) return false;
            ammo = AmmoOf(round);
            return true;
        }

        /// <summary>
        /// 탄의 효과 목록에 발사기 배율(피해형 항목만)과 소유자의 공격 버프를 구워 최종 목록을 만든다.
        /// 배율은 발사 시점에 확정된다 — 탄이 날아가는 동안 버프가 끝나도 발사 때 값이 유지된다.
        /// </summary>
        public Effect[] Bake(AmmoModuleDef ammo)
        {
            var effects = EffectUse.ToEffects(ammo.Effects);
            float m = Def.DamageMultiplier;
            if (Math.Abs(m - 1f) > 0.0001f)
            {
                var scaled = new Effect[effects.Length];
                for (int i = 0; i < effects.Length; i++)
                {
                    var k = effects[i].Spec.Kind;
                    bool damageLike = k == EffectKind.Damage || k == EffectKind.DamageOverTime;
                    scaled[i] = damageLike ? effects[i].WithValue(effects[i].Value * m) : effects[i];
                }
                effects = scaled;
            }
            var mine = Owner?.Get<EffectsModule>();
            return mine != null ? mine.BakeOutgoing(effects) : effects;
        }
    }
}
