using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.Tests
{
    /// <summary>
    /// 플레이어 무기(WeaponModule) 특성화 테스트 — 씬·GameObject·플레이모드 없이 심만으로 돈다.
    ///
    /// 검증 대상: 빈 탄창은 자동 재장전(소지품에서 실소비) · 방아쇠는 탄을 소비하고 연사 간격을 지키며 효과에 배율을 굽는다 ·
    /// 샷건은 방아쇠 한 번에 펠릿 수만큼 · 탄종 전환은 장전 탄을 돌려준다 · 근접(무한)은 재장전 없이 늘 쏜다 · 총을 내리면 재장전 취소 ·
    /// 세이브 복원(총별 탄창).
    /// 실행: eval `CoreDawn.Tests.WeaponTests.RunAll(out var r)`.
    /// </summary>
    public static class WeaponTests
    {
        static readonly List<(string name, bool pass, string detail)> _results = new();
        static readonly List<string> _fails = new();
        static SimWorld _sim;
        static EntityWorld _world;
        static PlayerSystem _players;
        static Entity _player;
        static WeaponModule _weapon;
        static ItemContainer _bag;
        static ItemDef _basicAmmo, _denseAmmo, _grenade;

        public static bool RunAll(out string report)
        {
            _results.Clear();
            _basicAmmo = PackItem("coredawn:item/basic_ammo");   // 피해 10
            _denseAmmo = PackItem("coredawn:item/dense_ammo");
            _grenade   = PackItem("coredawn:item/grenade");

            Run("1. 빈 탄창은 소지품에서 실소비로 자동 재장전된다",       S1_AutoReload);
            Run("2. 방아쇠는 탄을 소비하고 연사 간격을 지키며 배율을 굽는다", S2_Fire);
            Run("3. 샷건은 방아쇠 한 번에 펠릿 수만큼 소비한다",           S3_Pellets);
            Run("4. 탄종 전환은 장전 탄을 소지품에 돌려주고 새 탄으로 채운다", S4_SwitchAmmo);
            Run("5. 근접(무한)은 재장전 없이 늘 쏜다",                     S5_Melee);
            Run("6. 총을 내리거나 바꾸면 재장전이 취소된다",               S6_CancelOnSwap);
            Run("7. 세이브 복원 — 총별 탄창",                              S7_Save);

            int passed = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var (name, pass, detail) in _results)
            {
                if (pass) passed++;
                sb.AppendLine($"  {(pass ? "PASS" : "FAIL")}  {name}");
                if (!pass) sb.AppendLine("        " + detail.Replace("\n", "\n        "));
            }
            report = $"[WeaponTests] {passed}/{_results.Count} 통과\n" + sb;
            return passed == _results.Count;
        }

        static void Run(string name, Action scenario)
        {
            _sim = new SimWorld(); _world = _sim.Entities;
            _players = new PlayerSystem(_sim);
            var def = new EntityDef { Id = "test:entity/player", DisplayName = "Player", Faction = Faction.Player };
            def.Modules.Add(new HealthModuleDef { MaxHp = 300f });
            def.Modules.Add(new EffectsModuleDef());
            def.Modules.Add(new InventoryModuleDef { Main = 10, Hotbar = 3, StackCap = 0 });
            def.Modules.Add(new WeaponModuleDef());
            _player = _players.Spawn(def, Vector3.zero);
            _weapon = _player.Get<WeaponModule>();
            _bag = _player.Get<InventoryModule>().Main;
            _fails.Clear();
            try { scenario(); }
            catch (Exception e) { _fails.Add("예외 발생:\n" + e); }
            _results.Add((name, _fails.Count == 0, string.Join("\n", _fails)));
            _players.Dispose();
        }

        // ─── 시나리오 ────────────────────────────────────────

        static void S1_AutoReload()
        {
            var rifle = Gun("rifle", magSize: 5, fireInterval: 0.1f, reload: 1f, _basicAmmo);
            Load(_basicAmmo, 12);
            _weapon.Equip(rifle, _players.Now);
            Expect(_weapon.Current != null && _weapon.Current.Loaded == 0, "새 총은 빈 탄창으로 시작");
            Expect(!_weapon.TryFire(_players.Now, out _), "빈 탄창은 못 쏨");
            Expect(_weapon.Reloading, "빈 탄창에 방아쇠 → 재장전 시작");
            Tick(0.5f);
            Expect(_weapon.Reloading && _weapon.ReloadProgress(_players.Now) > 0.4f && _weapon.ReloadProgress(_players.Now) < 0.6f, $"0.5초: 진행 중 ({_weapon.ReloadProgress(_players.Now):F2})");
            Tick(0.6f);
            Expect(!_weapon.Reloading && _weapon.Current.Loaded == 5, $"1초 뒤 탄창 5 (실제 {_weapon.Current.Loaded})");
            Expect(_bag.CountOf(_basicAmmo) == 7, $"소지품 12 → 7 (실제 {_bag.CountOf(_basicAmmo)})");

            // 소지품이 모자라면 있는 만큼만, 아예 없으면 재장전이 시작되지 않는다
            _bag.TryConsume(_basicAmmo, 5);
            _weapon.Current.Loaded = 0;
            Tick(0.1f);
            Expect(_weapon.Reloading, "빈 탄창 → 자동 재장전(소지품 2)");
            Tick(1.1f);
            Expect(_weapon.Current.Loaded == 2 && _bag.CountOf(_basicAmmo) == 0, $"있는 만큼만 채움: 2 (실제 {_weapon.Current.Loaded}, 소지품 {_bag.CountOf(_basicAmmo)})");
            for (int i = 0; i < 2; i++) { Tick(0.2f); _weapon.TryFire(_players.Now, out _); }
            Tick(0.1f);
            Expect(_weapon.Current.Loaded == 0 && !_weapon.Reloading, "소지품에 탄이 없으면 재장전이 시작되지 않는다");
        }

        static void S2_Fire()
        {
            var rifle = Gun("rifle", magSize: 5, fireInterval: 0.2f, reload: 1f, _basicAmmo, damageMultiplier: 1.5f);
            Load(_basicAmmo, 20);
            _weapon.Equip(rifle, _players.Now);
            _weapon.TryStartReload(_players.Now); Tick(1.1f);
            var shots = new List<WeaponShot>(); _weapon.Fired += s => shots.Add(s);

            Expect(_weapon.TryFire(_players.Now, out var first), "장전된 탄창은 쏨");
            Expect(!_weapon.TryFire(_players.Now, out _), "연사 간격 안에는 못 쏨");
            Tick(0.25f);
            Expect(_weapon.TryFire(_players.Now, out _), "간격이 지나면 쏨");
            Expect(_weapon.Current.Loaded == 3, $"두 발 쏘면 탄창 3 (실제 {_weapon.Current.Loaded})");
            Expect(shots.Count == 2 && first.Pellets == 1 && first.Round == _basicAmmo && !first.Hitscan, "발사 결정: 펠릿 1·탄 basic·투사체");
            Expect(first.Effects.Length == 1 && Mathf.Approximately(first.Effects[0].Value, 15f), $"피해 10 × 배율 1.5 = 15 (실제 {(first.Effects.Length > 0 ? first.Effects[0].Value : -1f)})");
            Expect(Mathf.Approximately(first.Range, 100f) && first.Ammo == _basicAmmo.Get<AmmoModuleDef>(), "사거리·탄 성질은 정의의 것");

            // 마지막 탄을 쏘면 알아서 재장전
            for (int i = 0; i < 3; i++) { Tick(0.25f); _weapon.TryFire(_players.Now, out _); }
            Expect(_weapon.Current.Loaded == 0 && _weapon.Reloading, $"다 쏘면 자동 재장전 (탄창 {_weapon.Current.Loaded}, 재장전 {_weapon.Reloading})");
        }

        static void S3_Pellets()
        {
            var shotgun = Gun("shotgun", magSize: 8, fireInterval: 0.8f, reload: 1f, _basicAmmo, pellets: 6);
            Load(_basicAmmo, 20);
            _weapon.Equip(shotgun, _players.Now);
            _weapon.TryStartReload(_players.Now); Tick(1.1f);
            Expect(_weapon.TryFire(_players.Now, out var s1) && s1.Pellets == 6 && _weapon.Current.Loaded == 2, $"펠릿 6 소비 → 탄창 2 (실제 {_weapon.Current.Loaded})");
            Tick(0.9f);
            Expect(_weapon.TryFire(_players.Now, out var s2) && s2.Pellets == 2 && _weapon.Current.Loaded == 0, $"모자라면 남은 만큼(2)만 (실제 펠릿 {s2.Pellets})");
        }

        static void S4_SwitchAmmo()
        {
            var rifle = Gun("rifle", magSize: 5, fireInterval: 0.1f, reload: 0.5f, _basicAmmo, _denseAmmo);
            Load(_basicAmmo, 5); Load(_denseAmmo, 3);
            _weapon.Equip(rifle, _players.Now);
            _weapon.TryStartReload(_players.Now); Tick(0.6f);
            Expect(_weapon.Current.Loaded == 5 && _bag.CountOf(_basicAmmo) == 0, "기본탄 5 장전");
            Expect(_weapon.TrySwitchAmmo(_players.Now), "다른 탄종이 있으면 전환");
            Expect(_weapon.Current.Round == _denseAmmo && _bag.CountOf(_basicAmmo) == 5, $"장전돼 있던 기본탄 5가 소지품으로 돌아옴 (실제 {_bag.CountOf(_basicAmmo)})");
            Expect(_weapon.Reloading, "새 탄종으로 재장전 시작");
            Tick(0.6f);
            Expect(_weapon.Current.Loaded == 3 && _bag.CountOf(_denseAmmo) == 0, $"고밀도탄 3 장전 (실제 {_weapon.Current.Loaded})");
            _bag.TryConsume(_basicAmmo, 5);
            Expect(!_weapon.TrySwitchAmmo(_players.Now), "다른 탄종이 소지품에 없으면 전환 실패");
        }

        static void S5_Melee()
        {
            var cutter = Gun("cutter", magSize: 1, fireInterval: 0.5f, reload: 1f, _basicAmmo, unlimited: true);
            _weapon.Equip(cutter, _players.Now);
            Expect(_weapon.Current.Loaded == 1, "근접은 처음부터 가득");
            int fired = 0; _weapon.Fired += _ => fired++;
            for (int i = 0; i < 5; i++) { _weapon.TryFire(_players.Now, out _); Tick(0.6f); }
            Expect(fired == 5, $"5번 휘두름 (실제 {fired})");
            Expect(_weapon.Current.Loaded == 1 && !_weapon.Reloading, "탄창이 줄지 않고 재장전도 없다");
            Expect(!_weapon.TryStartReload(_players.Now), "근접은 재장전을 시작하지 않는다");
        }

        static void S6_CancelOnSwap()
        {
            var rifle = Gun("rifle", magSize: 5, fireInterval: 0.1f, reload: 1f, _basicAmmo);
            var pistol = Gun("pistol", magSize: 7, fireInterval: 0.3f, reload: 1f, _basicAmmo);
            Load(_basicAmmo, 20);
            int ended = 0; bool lastCompleted = true; _weapon.ReloadEnded += (g, ok) => { ended++; lastCompleted = ok; };
            _weapon.Equip(rifle, _players.Now);
            _weapon.TryStartReload(_players.Now); Tick(0.3f);
            _weapon.Equip(pistol, _players.Now);
            Expect(!_weapon.Reloading && ended == 1 && !lastCompleted, "총을 바꾸면 재장전 취소(완료 아님)");
            Expect(_weapon.MagazineOf(rifle).Loaded == 0, "취소된 재장전은 탄을 채우지 않는다");
            Expect(_bag.CountOf(_basicAmmo) == 20, "취소된 재장전은 소지품도 건드리지 않는다");
            Tick(0.1f);
            Expect(_weapon.Reloading && ReferenceEquals(_weapon.Equipped, pistol), "든 총(권총)의 빈 탄창이 자동 재장전");
            _weapon.Equip(null, _players.Now);
            Expect(!_weapon.Reloading && _weapon.Equipped == null, "맨손이면 취소");
        }

        static void S7_Save()
        {
            var rifle = Gun("rifle", magSize: 5, fireInterval: 0.1f, reload: 0.5f, _basicAmmo, _denseAmmo);
            var launcher = Gun("launcher", magSize: 1, fireInterval: 1f, reload: 0.5f, _grenade);
            Load(_basicAmmo, 10); Load(_grenade, 2);
            _weapon.Equip(rifle, _players.Now); _weapon.TryStartReload(_players.Now); Tick(0.6f);
            _weapon.Equip(launcher, _players.Now); _weapon.TryStartReload(_players.Now); Tick(0.6f);
            var mags = _weapon.Magazines.Select(m => (m.Gun, m.Round, m.Loaded)).ToList();
            Expect(mags.Count == 2, $"총 둘의 탄창 (실제 {mags.Count})");

            // 새 소지자에 복원
            var fresh = new WeaponModule();
            var e2 = _world.Create(Faction.Player, Vector3.zero); e2.Add(new EffectsModule()); e2.Add(fresh);
            foreach (var (g, r, n) in mags) fresh.RestoreMagazine(g, r, n);
            Expect(fresh.MagazineOf(rifle).Loaded == 5 && fresh.MagazineOf(rifle).Round == _basicAmmo, "소총 5/기본탄 복원");
            Expect(fresh.MagazineOf(launcher).Loaded == 1 && fresh.MagazineOf(launcher).Round == _grenade, "유탄발사기 1/유탄 복원");
            fresh.RestoreMagazine(rifle, _basicAmmo, 99);
            Expect(fresh.MagazineOf(rifle).Loaded == 5, "탄창 크기를 넘는 저장값은 잘린다");
        }

        // ─── 헬퍼 ────────────────────────────────────────────

        static void Expect(bool condition, string message) { if (!condition) _fails.Add(message); }

        static void Tick(float seconds)
        {
            int n = Mathf.CeilToInt(seconds / 0.05f);
            _sim.Step(n);   // 월드 스텝(20Hz) — 플레이어 시스템은 등록돼 있다
        }

        static void Load(ItemDef item, int n)
        {
            if (!_bag.TryAdd(item, n)) throw new Exception($"소지품에 {item.Id} ×{n}을 넣지 못했습니다");
        }

        static GunDef Gun(string name, int magSize, float fireInterval, float reload, params ItemDef[] ammo)
            => Gun(name, magSize, fireInterval, reload, ammo, 1f, 1, false);

        static GunDef Gun(string name, int magSize, float fireInterval, float reload, ItemDef ammo, float damageMultiplier = 1f, int pellets = 1, bool unlimited = false)
            => Gun(name, magSize, fireInterval, reload, new[] { ammo }, damageMultiplier, pellets, unlimited);

        static GunDef Gun(string name, int magSize, float fireInterval, float reload, ItemDef[] ammo, float damageMultiplier, int pellets, bool unlimited)
        {
            var g = new GunDef { Id = "test:gun/" + name, DisplayName = name, Fire = new GunFire { Interval = fireInterval, DamageMultiplier = damageMultiplier, Pellets = pellets, Range = 100f },
                                 Ammo = new GunAmmo { MagSize = magSize, ReloadTime = reload, Unlimited = unlimited } };
            foreach (var a in ammo) { g.Ammo.Filter.Add(a.Id); g.AmmoFilter.Add(a); }
            return g;
        }

        static ItemDef PackItem(string id)
        {
            if (SimHost.Database == null) SimHost.DatabaseLoader = () => PackLoader.Load();
            var item = SimHost.Database?.Item(id);
            if (item == null) throw new Exception($"팩 아이템 '{id}'를 찾지 못했습니다 — StreamingAssets/packs/coredawn/data.json 확인");
            return item;
        }
    }
}
