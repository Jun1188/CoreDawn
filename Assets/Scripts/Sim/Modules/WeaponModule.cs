using System;
using System.Collections.Generic;

namespace CoreDawn.Sim
{
    /// <summary>총 하나의 탄창 상태 — 장전된 탄종과 수. 총 정의당 하나(같은 총을 둘 들 일은 없다 — maxStack 1).</summary>
    public sealed class Magazine
    {
        public GunDef Gun { get; }
        public ItemDef Round { get; internal set; }
        public int Loaded { get; internal set; }
        public Magazine(GunDef gun, ItemDef round, int loaded) { Gun = gun; Round = round; Loaded = loaded; }
    }

    /// <summary>
    /// 방아쇠 한 번의 결정 — 심이 정한 것 전부. 뷰는 펠릿 수만큼 탄퍼짐을 굴려 탄을 만들 뿐 아무것도 다시 판단하지 않는다.
    /// 효과는 이미 배율·버프가 구워진 최종 목록이다.
    /// </summary>
    public readonly struct WeaponShot
    {
        public readonly GunDef Gun;
        public readonly ItemDef Round;          // 나간 탄 아이템(뷰의 프리팹·연출 조회용)
        public readonly AmmoModuleDef Ammo;     // 탄의 성질(탄속·중력·폭발·수명·관통)
        public readonly int Pellets;            // 이번 방아쇠에 나가는 탄 수(탄창이 모자라면 남은 만큼)
        public readonly Effect[] Effects;       // 펠릿 하나의 명중 효과(최종)
        public readonly bool Hitscan;
        public readonly float Range;

        public WeaponShot(GunDef gun, ItemDef round, AmmoModuleDef ammo, int pellets, Effect[] effects, bool hitscan, float range)
        { Gun = gun; Round = round; Ammo = ammo; Pellets = pellets; Effects = effects; Hitscan = hitscan; Range = range; }
    }

    /// <summary>
    /// 무기 소지자(플레이어)의 심 모듈 — 총마다의 탄창, 지금 든 총, 연사 쿨다운, 재장전 타이머. "쏠 수 있나 · 이번 방아쇠는 무엇인가"를
    /// 여기서 끝내고 뷰(<c>Gun</c>)에는 <see cref="Fired"/>만 나간다 — 포탑의 <see cref="TurretModule"/>과 같은 "심 승인 → 뷰 발사" 틀.
    ///
    /// 탄의 실소비: 재장전은 소지품(Inventory main)에서 현재 탄종을 실제로 꺼낸다 — 포탑이 벨트 보급 탄을 소비하는 것과 같은 원칙.
    /// 근접무기(GunDef.unlimitedAmmo)는 탄창이 늘 가득이고 재장전이 없다. 소지품이 없는 소유자(팩 없는 테스트 씬)는 추상 탄창 —
    /// 처음부터 가득이고 재장전이 공짜다(옛 Gun의 규약 그대로).
    /// 시계는 플레이어 시스템(<see cref="PlayerSystem.Now"/>) — Step/Tick의 now.
    /// </summary>
    public sealed class WeaponModule : EntityModule
    {
        readonly Dictionary<GunDef, Magazine> _mags = new Dictionary<GunDef, Magazine>();
        ItemContainer _bag;
        bool _bagLooked;

        public GunDef Equipped { get; private set; }
        public Magazine Current { get; private set; }

        /// <summary>다음 발사가 허용되는 시각.</summary>
        public float ReadyAt { get; private set; }
        public bool Reloading { get; private set; }
        public float ReloadStartedAt { get; private set; }
        public float ReloadEndsAt { get; private set; }

        /// <summary>방아쇠가 당겨졌다 — 뷰가 듣고 탄을 만든다. 탄 소비·쿨다운은 이미 끝난 뒤다.</summary>
        public event Action<WeaponShot> Fired;
        /// <summary>재장전 시작 — 연출(소리)용.</summary>
        public event Action<GunDef> ReloadStarted;
        /// <summary>재장전 끝 — (총, 완료했는가). 취소(총을 내림·바꿈)면 false.</summary>
        public event Action<GunDef, bool> ReloadEnded;

        public IEnumerable<Magazine> Magazines => _mags.Values;

        // 소지품은 정의 순서상 이 모듈 뒤에 붙을 수 있어 첫 사용에서 찾는다
        ItemContainer Bag()
        {
            if (_bagLooked) return _bag;
            _bagLooked = true;
            _bag = Owner.Get<InventoryModule>()?.Main;
            return _bag;
        }

        /// <summary>총의 탄창 — 처음 보는 총은 기본 탄종·빈 탄창으로 만든다(근접·소지품 없는 소유자는 가득).</summary>
        public Magazine MagazineOf(GunDef gun)
        {
            if (gun == null) return null;
            if (_mags.TryGetValue(gun, out var m)) return m;
            bool full = gun.Ammo.Unlimited || Bag() == null;
            m = new Magazine(gun, gun.DefaultAmmo, full ? gun.Ammo.MagSize : 0);
            _mags[gun] = m;
            return m;
        }

        /// <summary>총을 든다(null = 맨손). 하던 재장전은 취소된다 — 손에서 내린 순간 장전은 취소다.</summary>
        public void Equip(GunDef gun, float now)
        {
            if (ReferenceEquals(gun, Equipped)) return;
            CancelReload();
            Equipped = gun;
            Current = MagazineOf(gun);
        }

        public bool CanFire(float now)
            => Equipped != null && !Reloading && now >= ReadyAt && (Equipped.Ammo.Unlimited || Current.Loaded > 0);

        /// <summary>
        /// 방아쇠 — 재장전·탄약·연사 간격을 통과하면 탄을 소비하고 <see cref="Fired"/>. 샷건은 방아쇠 한 번에 펠릿 수만큼 소비한다
        /// (탄창이 모자라면 남은 만큼만). 빈 탄창이면 재장전을 시작하고 false.
        /// </summary>
        public bool TryFire(float now, out WeaponShot shot)
        {
            shot = default;
            var gun = Equipped;
            if (gun == null || Reloading || now < ReadyAt) return false;
            var mag = Current;
            if (!gun.Ammo.Unlimited && mag.Loaded <= 0) { TryStartReload(now); return false; }
            if (mag.Round == null) throw new InvalidOperationException($"{gun.Id}: 장전된 탄종이 없습니다 — ammoFilter가 비었습니다");

            int rounds = gun.Ammo.Unlimited ? gun.RoundsPerTrigger : Math.Min(mag.Loaded, gun.RoundsPerTrigger);
            if (!gun.Ammo.Unlimited) mag.Loaded -= rounds;
            ReadyAt = now + gun.Fire.Interval;

            var ammo = mag.Round.Get<AmmoModuleDef>()
                       ?? throw new InvalidOperationException($"'{mag.Round.Id}'은(는) 탄약(Ammo 모듈)이 아닙니다 — {gun.Id}의 ammoFilter를 확인하세요");
            shot = new WeaponShot(gun, mag.Round, ammo, rounds, Bake(gun, ammo), gun.IsHitscan, gun.Fire.Range);
            Fired?.Invoke(shot);

            // 마지막 탄을 쐈으면 방아쇠를 다시 당길 것 없이 알아서 채운다(소지품에 탄이 없으면 조용히 물러난다)
            if (!gun.Ammo.Unlimited && mag.Loaded <= 0) TryStartReload(now);
            return true;
        }

        /// <summary>재장전 시작 — 근접·재장전 중·이미 가득·소지품에 현재 탄종이 없으면 시작하지 않는다.</summary>
        public bool TryStartReload(float now)
        {
            var gun = Equipped;
            if (gun == null || gun.Ammo.Unlimited || Reloading) return false;
            var mag = Current;
            if (mag.Loaded >= gun.Ammo.MagSize) return false;
            var bag = Bag();
            if (bag != null && (mag.Round == null || bag.CountOf(mag.Round) <= 0)) return false;   // 실소비 — 없는 탄으로는 못 채운다
            Reloading = true;
            ReloadStartedAt = now;
            ReloadEndsAt = now + Math.Max(0f, gun.Ammo.ReloadTime);
            ReloadStarted?.Invoke(gun);
            return true;
        }

        public void CancelReload()
        {
            if (!Reloading) return;
            Reloading = false;
            ReloadEnded?.Invoke(Equipped, false);
        }

        public float ReloadProgress(float now)
        {
            if (!Reloading) return 0f;
            float len = ReloadEndsAt - ReloadStartedAt;
            return len <= 0f ? 1f : Math.Min(1f, Math.Max(0f, (now - ReloadStartedAt) / len));
        }

        /// <summary>매 틱 — 재장전 완료(소지품에서 실제로 꺼내 채움)와 빈 탄창의 자동 재장전.</summary>
        public void Tick(float now)
        {
            var gun = Equipped;
            if (gun == null) return;
            if (Reloading)
            {
                if (now < ReloadEndsAt) return;
                Reloading = false;
                var mag = Current;
                int need = gun.Ammo.MagSize - mag.Loaded;
                var bag = Bag();
                if (bag == null) mag.Loaded = gun.Ammo.MagSize;                         // 추상 탄창 — 소지품 없는 씬
                else
                {
                    int take = Math.Min(need, bag.CountOf(mag.Round));
                    if (take > 0 && bag.TryConsume(mag.Round, take)) mag.Loaded += take;
                }
                ReloadEnded?.Invoke(gun, true);
                return;
            }
            // 빈 탄창은 알아서 채운다 — 무기를 들었을 때·쏘고 났을 때 한 번 거는 대신 매 틱 보는 이유는 상태를 잃는 경합(스왑 중 취소)이 없게
            if (!gun.Ammo.Unlimited && Current.Loaded <= 0) TryStartReload(now);
        }

        /// <summary>
        /// 탄종 전환 — ammoFilter 안에서 소지품에 있는 다음 탄종으로 돈다. 장전돼 있던 탄은 소지품으로 돌려주고(들어갈 자리가 없으면
        /// 바꾸지 않는다 — 전환이 탄을 증발시키면 안 된다) 새 탄종으로 재장전을 시작한다. 성공 시 true.
        /// </summary>
        public bool TrySwitchAmmo(float now)
        {
            var gun = Equipped;
            if (gun == null || Reloading || gun.AmmoFilter.Count <= 1) return false;
            var mag = Current;
            var bag = gun.Ammo.Unlimited ? null : Bag();   // 근접은 소지품을 아예 보지 않는다
            int idx = gun.AmmoFilter.IndexOf(mag.Round);
            for (int step = 1; step <= gun.AmmoFilter.Count; step++)
            {
                var candidate = gun.AmmoFilter[(idx + step) % gun.AmmoFilter.Count];
                if (candidate == null || ReferenceEquals(candidate, mag.Round)) continue;
                if (bag != null && bag.CountOf(candidate) <= 0) continue;
                if (bag != null && mag.Loaded > 0 && mag.Round != null)
                {
                    if (!bag.HasRoomFor(mag.Round, mag.Loaded)) return false;
                    bag.TryAdd(mag.Round, mag.Loaded);
                }
                mag.Round = candidate;
                mag.Loaded = gun.Ammo.Unlimited ? gun.Ammo.MagSize : 0;
                TryStartReload(now);
                return true;
            }
            return false;
        }

        /// <summary>소지품에 남은 탄 수. 소지품이 없으면 -1(무한).</summary>
        public int ReserveOf(ItemDef round) => Bag() != null && round != null ? Bag().CountOf(round) : -1;

        Effect[] Bake(GunDef gun, AmmoModuleDef ammo)
        {
            var effects = EffectUse.ToEffects(ammo.Effects);
            float m = gun.Fire.DamageMultiplier;
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

        /// <summary>세이브 복원 — 총의 탄창을 되돌린다. 재장전 중이었다면 취소된 것으로 본다.</summary>
        public void RestoreMagazine(GunDef gun, ItemDef round, int loaded)
        {
            if (gun == null) return;
            var m = MagazineOf(gun);
            m.Round = round ?? gun.DefaultAmmo;
            m.Loaded = Math.Max(0, Math.Min(gun.Ammo.MagSize, loaded));
        }
    }
}
