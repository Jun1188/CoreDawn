using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.Tests
{
    /// <summary>
    /// 포탑·오라·지뢰 특성화 테스트 — 씬·GameObject·플레이모드 없이 심만으로 돈다(ResourceNodeTests와 같은 틀).
    ///
    /// 검증 대상: 리드 수식 · 포탑이 사거리 안 적에게 쿨다운 주기로 쏘고 탄을 소비하며 효과에 배율을 굽는다 ·
    /// 탄이 없으면 굶고 탄이 오면 깬다 · 사거리 밖·최소 사거리 안은 쏘지 않는다 · 선회가 발사를 늦춘다 ·
    /// 오라가 반경 안 전원에게 주기마다 연료의 효과를 걸고 연료를 태운다(연료 없는 오라는 정의 효과) · 지뢰는 한 번 터지고 죽는다 ·
    /// 세이브 왕복(쿨다운·방위).
    /// 실행: eval `CoreDawn.Tests.TurretTests.RunAll(out var r)`.
    /// </summary>
    public static class TurretTests
    {
        static readonly List<(string name, bool pass, string detail)> _results = new();
        static readonly List<string> _fails = new();
        static FactorySystem _sim;
        static ItemDef _basicAmmo, _energyCell, _grenade;

        public static bool RunAll(out string report)
        {
            _results.Clear();
            _basicAmmo  = PackItem("coredawn:item/basic_ammo");        // 직사 · 피해 10
            _energyCell = PackItem("coredawn:item/energy_cell_ammo");  // 직사 · 피해 30 + 감속장 0.5
            _grenade    = PackItem("coredawn:item/grenade");           // 곡사 · 피해 70 + 방사 넉백

            Run("1. 리드 수식 — 직사(이차식)·곡사(한 걸음 + 등비 외삽)",                 S1_Ballistics);
            Run("2. 포탑은 사거리 안 적에게 쿨다운 주기로 쏘고 탄을 소비한다", S2_TurretFires);
            Run("3. 탄이 없으면 굶고, 탄이 오면 깬다",                    S3_Starved);
            Run("4. 사거리 밖·최소 사거리 안은 쏘지 않는다",              S4_Range);
            Run("5. 선회 속도가 첫 발을 늦춘다",                          S5_TurnSpeed);
            Run("6. 오라는 반경 안 전원에게 연료의 효과를 걸고 연료를 태운다", S6_Aura);
            Run("7. 지뢰는 고정 탄으로 한 번 터지고 스스로 죽는다",          S7_Mine);
            Run("8. 세이브 왕복 — 쿨다운·방위",                           S8_Save);
            Run("9. 가려진 적은 쏘지 않는다 — 차폐가 풀리면 쏜다",            S9_LineOfSight);

            int passed = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var (name, pass, detail) in _results)
            {
                if (pass) passed++;
                sb.AppendLine($"  {(pass ? "PASS" : "FAIL")}  {name}");
                if (!pass) sb.AppendLine("        " + detail.Replace("\n", "\n        "));
            }
            report = $"[TurretTests] {passed}/{_results.Count} 통과\n" + sb;
            return passed == _results.Count;
        }

        static void Run(string name, Action scenario)
        {
            _sim = new FactorySystem(new SimWorld(), GridGeometry.Unit, tps: 10f);
            SimHost.LineOfSight = (shooter, target, from, to) => !_blocked.Contains(target);   // 헤드리스: 시나리오가 가린 표적만 안 보인다
            _blocked.Clear();
            _fails.Clear();
            try { scenario(); }
            catch (Exception e) { _fails.Add("예외 발생:\n" + e); }
            _results.Add((name, _fails.Count == 0, string.Join("\n", _fails)));
            _sim = null;
        }

        // ─── 시나리오 ────────────────────────────────────────

        static void S1_Ballistics()
        {
            var o = Vector3.zero;
            var d1 = Ballistics.LinearLead(o, new Vector3(10f, 0, 0), new Vector3(0, 0, 3f), 30f, out var i1);
            float t = Mathf.Sqrt(100f / (900f - 9f));
            Expect((i1 - new Vector3(10f, 0, 3f * t)).magnitude < 0.01f, $"교차 이동 만나는 점 (실제 {i1})");
            Expect((d1 * 30f * t - i1).magnitude < 0.01f, "탄이 그 시각에 그 점에 있어야 함");
            Ballistics.LinearLead(o, new Vector3(10f, 0, 0), new Vector3(50f, 0, 0), 30f, out var i4);
            Expect(i4 == new Vector3(10f, 0, 0), "추월 불가면 현재 위치를 겨눔");

            // 곡사: 정지 표적 10m, s=25, g=9.8 — 저각 해로 쏘면 탄이 실제로 표적 근처에 떨어진다(수치 적분)
            var dir = Ballistics.BallisticLead(o, new Vector3(10f, 0, 0), Vector3.zero, 25f, 9.8f, false, out var impact);
            Vector3 p = o, v = dir * 25f; float dt = 0.001f, closest = float.MaxValue;
            for (int i = 0; i < 5000; i++) { p += v * dt; v += Vector3.down * 9.8f * dt; if (p.y < 0f) { closest = Mathf.Abs(p.x - 10f); break; } }
            Expect(closest < 0.2f, $"곡사 탄착 오차 {closest:F2}m");
            Expect((impact - new Vector3(10f, 0, 0)).magnitude < 0.01f, "정지 표적의 탄착점은 표적 자신");

            // 움직이는 표적 — 등비 외삽 리드가 실제로 맞는지 수치 적분으로 확인(표적 속도 3.5·6 m/s, 옆·접근·이탈)
            float worst = 0f;
            foreach (var tv in new[] { new Vector3(0, 0, 3.5f), new Vector3(-3.5f, 0, 0), new Vector3(3.5f, 0, 0), new Vector3(4f, 0, 4.5f) })
            {
                Vector3 tp = new Vector3(12f, 0, 0);
                var d = Ballistics.BallisticLead(o, tp, tv, 25f, 9.8f, false, out _);
                Vector3 pp = o, pv = d * 25f; float time = 0f, dtt = 0.0005f, missBy = -1f;
                for (int i = 0; i < 40000; i++) { pp += pv * dtt; pv += Vector3.down * 9.8f * dtt; time += dtt; if (pp.y <= 0f && time > 0.05f) { var at = tp + tv * time; missBy = Vector2.Distance(new Vector2(pp.x, pp.z), new Vector2(at.x, at.z)); break; } }
                worst = Mathf.Max(worst, missBy);
            }
            Expect(worst >= 0f && worst < 0.05f, $"이동 표적 곡사 탄착 오차 최대 {worst:F3}m (5cm 안)");
        }

        static void S2_TurretFires()
        {
            var turret = _sim.Place(Turret(range: 8f, fireRate: 2f, turnSpeed: 0f, damageMultiplier: 2f, ammo: _basicAmmo), new Vector2Int(0, 0));
            var module = turret.Owner.Get<TurretModule>();
            var shots = new List<TurretShot>();
            module.FireRequested += s => shots.Add(s);
            Load(turret, _basicAmmo, 20);
            var monster = Monster(new Vector3(5f, 0, 0.5f));

            RunSim(3f);
            Expect(shots.Count >= 5 && shots.Count <= 7, $"2발/초 × 3초 ≈ 6발 (실제 {shots.Count})");
            Expect(turret.Input.CountOf(_basicAmmo) == 20 - shots.Count, $"쏜 만큼 탄이 줄어야 함 (남은 {turret.Input.CountOf(_basicAmmo)})");
            Expect(shots.Count > 0 && shots[0].Target == monster, "표적은 그 몬스터");
            Expect(shots.Count > 0 && shots[0].Effects.Length == 1 && Mathf.Approximately(shots[0].Effects[0].Value, 20f),
                   $"피해 10 × 배율 2 = 20 (실제 {(shots.Count > 0 && shots[0].Effects.Length > 0 ? shots[0].Effects[0].Value : -1f)})");
            Expect(shots.Count > 0 && !shots[0].Hitscan && shots[0].Ammo == _basicAmmo.Get<AmmoModuleDef>(), "탄 성질은 소비한 탄의 것");
            Expect(shots.Count > 0 && Vector3.Dot(shots[0].Direction, Vector3.right) > 0.9f, $"발사 방향은 표적 쪽 (실제 {(shots.Count > 0 ? shots[0].Direction : Vector3.zero)})");
            Expect(module.Phase == TurretPhase.Ready, $"정렬 완료 상태여야 함 (실제 {module.Phase})");

            // 표적이 죽으면 다음 표적이 없으니 멈춘다
            monster.Health.Kill();
            int before = shots.Count;
            RunSim(1f);
            Expect(shots.Count == before, "표적이 죽은 뒤에는 쏘지 않음");
            Expect(module.Phase == TurretPhase.Idle, $"표적 없음 = Idle (실제 {module.Phase})");
        }

        static void S3_Starved()
        {
            var turret = _sim.Place(Turret(range: 8f, fireRate: 2f, turnSpeed: 0f, ammo: _basicAmmo), new Vector2Int(0, 0));
            var module = turret.Owner.Get<TurretModule>();
            int shots = 0; module.FireRequested += _ => shots++;
            Monster(new Vector3(4f, 0, 0));

            RunSim(1f);
            Expect(shots == 0 && module.Phase == TurretPhase.Starved, $"탄 없음 = 굶음·0발 (실제 {module.Phase}, {shots}발)");

            Load(turret, _basicAmmo, 3);   // 손 장전 — 그릇 Changed가 깨워야 한다
            RunSim(2f);
            Expect(shots == 3, $"장전한 3발을 다 쏘고 멈춤 (실제 {shots})");
            Expect(module.Phase == TurretPhase.Starved, $"다 쓰면 다시 굶음 (실제 {module.Phase})");
        }

        static readonly HashSet<Entity> _blocked = new();

        static void S9_LineOfSight()
        {
            var turret = _sim.Place(Turret(range: 10f, minRange: 0f, fireRate: 5f, turnSpeed: 0f, ammo: _basicAmmo), new Vector2Int(0, 0));
            var module = turret.Owner.Get<TurretModule>();
            int shots = 0; module.FireRequested += _ => shots++;
            Load(turret, _basicAmmo, 50);
            var hidden = Monster(new Vector3(4f, 0, 0)); _blocked.Add(hidden);   // 가까운 적이 벽 뒤
            var open = Monster(new Vector3(7f, 0, 0));                              // 먼 적은 보인다
            RunSim(1f);
            Expect(module.Target == open && shots >= 3, $"가려진 가까운 적 대신 보이는 먼 적을 쏜다 (표적 {(module.Target == open ? "open" : module.Target == hidden ? "hidden" : "none")}, {shots}발)");
            _blocked.Add(open);
            int before = shots; RunSim(1f);
            Expect(module.Target == null && shots == before, $"둘 다 가려지면 표적을 놓고 쏘지 않는다 (표적 {(module.Target == null ? "없음" : "있음")}, +{shots - before}발)");
            _blocked.Clear();
            RunSim(1f);
            Expect(module.Target == hidden && shots > before, $"차폐가 풀리면 가장 가까운 적을 다시 잡는다 (+{shots - before}발)");
        }

        static void S4_Range()
        {
            var turret = _sim.Place(Turret(range: 5f, minRange: 2f, fireRate: 5f, turnSpeed: 0f, ammo: _basicAmmo), new Vector2Int(0, 0));
            var module = turret.Owner.Get<TurretModule>();
            int shots = 0; module.FireRequested += _ => shots++;
            Load(turret, _basicAmmo, 50);

            var far = Monster(new Vector3(9f, 0, 0));
            RunSim(1f);
            Expect(shots == 0, $"사거리(5) 밖 9m는 쏘지 않음 (실제 {shots})");

            var near = Monster(new Vector3(1f, 0, 0));
            RunSim(1f);
            Expect(shots == 0, $"최소 사거리(2) 안 1m는 쏘지 않음 (실제 {shots})");

            Monster(new Vector3(3.5f, 0, 0));
            RunSim(1f);
            Expect(shots >= 3, $"사거리 안 3.5m는 쏨 (실제 {shots})");
        }

        static void S5_TurnSpeed()
        {
            // 포탑은 처음 +Z(yaw 0)를 본다. 표적은 +X(yaw 90°). 90°/초면 첫 발은 1초쯤 뒤
            var turret = _sim.Place(Turret(range: 8f, fireRate: 10f, turnSpeed: 90f, aimTolerance: 2f, ammo: _basicAmmo), new Vector2Int(0, 0));
            var module = turret.Owner.Get<TurretModule>();
            float firstAt = -1f; module.FireRequested += _ => { if (firstAt < 0f) firstAt = _sim.Now; };
            Load(turret, _basicAmmo, 50);
            Monster(new Vector3(5f, 0, 0.5f));

            RunSim(0.5f);
            Expect(firstAt < 0f && module.Phase == TurretPhase.Aiming, $"0.5초엔 아직 조준 중 (실제 {module.Phase}, 첫 발 {firstAt})");
            RunSim(1.5f);
            Expect(firstAt >= 0.9f && firstAt <= 1.3f, $"첫 발은 약 1초 뒤 (실제 {firstAt:F2}s)");
        }

        static void S6_Aura()
        {
            var slow = _energyCell.Get<AmmoModuleDef>().Effects.Find(e => e.Spec.Kind == EffectKind.MoveSpeed).Spec;
            var tower = _sim.Place(Aura(radius: 3f, interval: 1f, ammo: _energyCell), new Vector2Int(0, 0));
            var module = tower.Owner.Get<AuraEmitterModule>();
            int pulses = 0; module.Pulsed += _ => pulses++;
            Load(tower, _energyCell, 10);
            var inside1 = Monster(new Vector3(2f, 0, 0), hp: 1000f);
            var inside2 = Monster(new Vector3(-1f, 0, 1f), hp: 1000f);
            var outside = Monster(new Vector3(6f, 0, 0), hp: 1000f);

            RunSim(2.55f);
            Expect(pulses == 3, $"1초 주기 × 2.5초 → 3펄스 (실제 {pulses})");
            Expect(tower.Input.CountOf(_energyCell) == 10 - pulses, $"펄스마다 연료 1 (남은 {tower.Input.CountOf(_energyCell)})");
            Expect(inside1.Get<EffectsModule>().Has(slow) && inside2.Get<EffectsModule>().Has(slow), "반경 안 둘 다 감속");
            Expect(!outside.Get<EffectsModule>().Has(slow), "반경 밖은 그대로");
            Expect(inside1.Health.CurrentHealth < 1000f, "연료의 피해도 걸린다");

            // 연료 없는 오라 — 정의의 효과로 펄스한다
            _sim = new FactorySystem(new SimWorld(), GridGeometry.Unit, tps: 10f);
            var def = Aura(radius: 3f, interval: 0.5f, ammo: null);
            def.Modules.Add(Fixed(Use(slow, 0.5f)));   // 연료 없는 오라 — 자기 정의의 고정 탄
            var free = _sim.Place(def, new Vector2Int(0, 0));
            var m = Monster(new Vector3(1f, 0, 0));
            RunSim(0.3f);
            Expect(m.Get<EffectsModule>().Has(slow), "연료 없는 오라는 고정 탄(FixedAmmo)의 효과로 펄스");
            Expect(free.Owner.Get<AuraEmitterModule>().LastHits == 1, "명중 수 1");
        }

        static void S7_Mine()
        {
            var mine = _sim.Place(Mine(radius: 2f, damageMultiplier: 6f), new Vector2Int(0, 0));
            var module = mine.Owner.Get<TriggerModule>();
            int blasts = 0; module.Triggered += b => { blasts++; Expect(b.Effects.Length == 2, $"고정 탄의 효과 둘(피해·넉백) (실제 {b.Effects.Length})"); };
            var far = Monster(new Vector3(5f, 0, 0), hp: 1000f);
            RunSim(0.5f);
            Expect(blasts == 0 && module.Armed, "반경 밖이면 안 터짐");

            var victim = Monster(new Vector3(1f, 0, 0), hp: 1000f);
            RunSim(0.5f);
            Expect(blasts == 1, $"들어오면 한 번 터짐 (실제 {blasts})");
            Expect(Mathf.Approximately(victim.Health.CurrentHealth, 1000f - 420f), $"고정 탄 피해 420 (남은 HP {victim.Health.CurrentHealth})");
            Expect(far.Health.CurrentHealth >= 1000f, "반경 밖은 무사");
            Expect(!module.Armed && mine.IsRemoved && !_sim.Buildings.Contains(mine), "터진 지뢰는 죽어서 공장에서 치워짐");
            RunSim(0.5f);
            Expect(blasts == 1, "두 번 터지지 않음");
        }

        static void S8_Save()
        {
            var turret = _sim.Place(Turret(range: 8f, fireRate: 0.5f, turnSpeed: 0f, ammo: _basicAmmo), new Vector2Int(0, 0));
            var module = turret.Owner.Get<TurretModule>();
            Load(turret, _basicAmmo, 5);
            Monster(new Vector3(5f, 0, 0));
            RunSim(0.3f);
            Expect(module.ReadyAt > 0f, "한 발 쏘고 쿨다운이 걸려 있어야 함");
            var saved = ((ISaveableModule)turret.Owner.Get<TurretModule>()).CaptureState();
            var tok = Newtonsoft.Json.Linq.JToken.FromObject(saved);
            Expect(tok["readyAt"] != null && tok["yaw"] != null, $"세이브 키 readyAt·yaw (실제 {tok})");

            var again = _sim.Place(Turret(range: 8f, fireRate: 0.5f, turnSpeed: 0f, ammo: _basicAmmo), new Vector2Int(4, 4));
            ((ISaveableModule)again.Owner.Get<TurretModule>()).RestoreState(tok); _sim.MarkDirty(again);
            var m2 = again.Owner.Get<TurretModule>();
            Expect(Mathf.Approximately(m2.ReadyAt, module.ReadyAt) && Mathf.Approximately(m2.Yaw, module.Yaw), "복원된 쿨다운·방위가 같아야 함");
        }

        // ─── 헬퍼 ────────────────────────────────────────────

        static void Expect(bool condition, string message) { if (!condition) _fails.Add(message); }

        static void RunSim(float simSeconds)
        {
            int ticks = Mathf.CeilToInt(simSeconds / 0.1f);   // 공장 틱(10Hz) 수 — 월드 스텝(20Hz)은 그 두 배
            _sim.Sim.Step(ticks * (SimWorld.TicksPerSecond / 10));
        }

        static void Load(BuildingModule b, ItemDef item, int n)
        {
            for (int i = 0; i < n; i++)
                if (!b.Input.TryAdd(item)) throw new Exception($"탄창에 {item.Id}를 넣지 못했습니다 (i={i})");
        }

        static Entity Monster(Vector3 pos, float hp = 100f)
        {
            var e = _sim.World.Create(Faction.Monster, pos);
            e.Add(new HealthModule(hp));
            e.Add(new EffectsModule());
            return e;
        }

        static EffectUse Use(EffectSpec spec, float value)
        {
            var u = new EffectUse { EffectId = spec.Id, Value = value };
            u.Resolve(SimHost.Database, new List<string>(), "test");
            return u;
        }

        static ItemDef PackItem(string id)
        {
            if (SimHost.Database == null) SimHost.DatabaseLoader = () => PackLoader.Load();
            var item = SimHost.Database?.Item(id);
            if (item == null) throw new Exception($"팩 아이템 '{id}'를 찾지 못했습니다 — StreamingAssets/packs/coredawn/data.json 확인");
            return item;
        }

        // ─── 정의 생성 (팩 json과 같은 타입을 코드로 조립) ──

        static EntityDef Turret(float range, float fireRate, float turnSpeed, ItemDef ammo, float minRange = 0f,
                                float aimTolerance = 3f, float damageMultiplier = 1f, bool hitscan = false)
        {
            var def = Base("TestTurret", inputSlots: 1);
            def.Modules.Add(new TurretModuleDef { Range = range, MinRange = minRange, FireRate = fireRate, TurnSpeed = turnSpeed,
                                                  AimTolerance = aimTolerance, MuzzleHeight = 1f, Hitscan = hitscan });
            def.Modules.Add(Consumer(ammo, damageMultiplier));
            return def;
        }

        static EntityDef Aura(float radius, float interval, ItemDef ammo)
        {
            var def = Base("TestAura", inputSlots: ammo != null ? 1 : 0);
            def.Modules.Add(new AuraEmitterModuleDef { Radius = radius, Interval = interval });
            if (ammo != null) def.Modules.Add(Consumer(ammo, 1f));
            return def;
        }

        static EntityDef Mine(float radius, float damageMultiplier)
        {
            var def = Base("TestMine", inputSlots: 0);   // 탄창 없음 — 자기 정의의 고정 탄이 터진다(유탄을 모른다)
            def.Modules.Add(new TriggerModuleDef { Radius = radius, Once = true });
            var damage = _grenade.Get<AmmoModuleDef>().Effects.Find(e => e.Spec.Kind == EffectKind.Damage).Spec;
            var knock = _grenade.Get<AmmoModuleDef>().Effects.Find(e => e.Spec.Kind == EffectKind.Knockback).Spec;
            def.Modules.Add(Fixed(Use(damage, 70f * damageMultiplier), Use(knock, 2f)));
            return def;
        }

        static FixedAmmoModuleDef Fixed(params EffectUse[] effects)
        {
            var d = new FixedAmmoModuleDef();
            d.Effects.AddRange(effects);
            d.Build();
            return d;
        }

        static AmmoConsumerModuleDef Consumer(ItemDef ammo, float damageMultiplier)
        {
            var c = new AmmoConsumerModuleDef { DamageMultiplier = damageMultiplier };
            if (ammo != null) { c.AmmoFilterIds.Add(ammo.Id); c.AmmoFilter.Add(ammo); }
            return c;
        }

        static EntityDef Base(string name, int inputSlots)
        {
            var def = new EntityDef { Id = "test:entity/" + name.ToLowerInvariant(), DisplayName = name, Faction = Faction.Player };
            def.Modules.Add(new BuildingModuleDef { Size = new Vec2i(1, 1) });
            def.Modules.Add(new HealthModuleDef { MaxHp = 100f });
            def.Modules.Add(new EffectsModuleDef());
            var ports = new PortsModuleDef();
            if (inputSlots > 0) ports.Ports.Add(new PortDef { X = 0, Y = 0, Dir = "West", IsInput = true });
            def.Modules.Add(ports);
            if (inputSlots > 0) def.Modules.Add(new InventoryModuleDef { Input = inputSlots, Output = 0, StackCap = 100 });
            return def;
        }
    }
}
