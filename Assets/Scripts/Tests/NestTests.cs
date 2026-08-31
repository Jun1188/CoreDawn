using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tests
{
    /// <summary>
    /// 둥지(NestModule) 특성화 테스트 — 씬·GameObject·플레이모드 없이 심만으로 돈다.
    ///
    /// 검증 대상: 스폰 포인트가 살아 있으면 무적 · 보스가 죽으면(엔티티 이벤트) 자리가 영구 파괴되고 전부 죽으면 피해가 통한다 ·
    /// 둥지가 죽으면 파괴 상태이고 복구되지 않는다 · 보스가 필요한 자리만 부른다 · 세이브 복원.
    /// 실행: eval `CoreDawn.Tests.NestTests.RunAll(out var r)`.
    /// </summary>
    public static class NestTests
    {
        static readonly List<(string name, bool pass, string detail)> _results = new();
        static readonly List<string> _fails = new();
        static EntityWorld _world;
        static Entity _nest;
        static NestModule _module;
        static Entity _attacker;

        public static bool RunAll(out string report)
        {
            _results.Clear();
            Run("1. 스폰 포인트가 살아 있으면 둥지는 무적",                S1_Invulnerable);
            Run("2. 보스가 죽으면 자리가 파괴되고 전부 죽으면 피해가 통한다", S2_BossDeathOpensNest);
            Run("3. 둥지 파괴는 영구 — 복구 없음",                          S3_NestDestroyedForever);
            Run("4. 세이브 복원 — 둥지·자리 상태",                          S4_Restore);

            int passed = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var (name, pass, detail) in _results)
            {
                if (pass) passed++;
                sb.AppendLine($"  {(pass ? "PASS" : "FAIL")}  {name}");
                if (!pass) sb.AppendLine("        " + detail.Replace("\n", "\n        "));
            }
            report = $"[NestTests] {passed}/{_results.Count} 통과\n" + sb;
            return passed == _results.Count;
        }

        static void Run(string name, Action scenario)
        {
            _world = new EntityWorld();
            var def = new EntityDef { Id = "test:entity/nest", DisplayName = "Nest", Faction = Faction.Monster };
            def.Modules.Add(new HealthModuleDef { MaxHp = 500f });
            def.Modules.Add(new EffectsModuleDef());
            def.Modules.Add(new NestModuleDef());
            _nest = _world.Create(def.Faction, Vector3.zero);
            def.Assemble(_nest);
            _module = _nest.Get<NestModule>();
            _module.ConfigurePoints(new List<(Vector3, bool)> { (new Vector3(4, 0, 0), true), (new Vector3(-4, 0, 0), true), (new Vector3(0, 0, 4), false) });
            _attacker = _world.Create(Faction.Player, new Vector3(2, 0, 0));
            _fails.Clear();
            try { scenario(); }
            catch (Exception e) { _fails.Add("예외 발생:\n" + e); }
            _results.Add((name, _fails.Count == 0, string.Join("\n", _fails)));
        }

        static void S1_Invulnerable()
        {
            Expect(_module.IsInvulnerable, "자리가 셋 살아 있으면 무적");
            _nest.Health.Damage(100f, _attacker);
            Expect(Mathf.Approximately(_nest.Health.CurrentHealth, 500f), $"피해가 막혀야 함 (실제 HP {_nest.Health.CurrentHealth})");
            var needed = new List<int>(); _module.BossNeeded += i => needed.Add(i);
            _module.RequestMissingBosses();
            Expect(needed.Count == 2 && needed.Contains(0) && needed.Contains(1), $"보스 자리 둘(0·1)만 부른다 (실제 {string.Join(",", needed)})");
            _module.BindBoss(0, Boss()); needed.Clear();
            _module.RequestMissingBosses();
            Expect(needed.Count == 1 && needed[0] == 1, "보스가 서 있는 자리는 다시 부르지 않는다");
        }

        static void S2_BossDeathOpensNest()
        {
            var b0 = Boss(); var b1 = Boss();
            _module.BindBoss(0, b0); _module.BindBoss(1, b1);
            var destroyed = new List<int>(); _module.PointDestroyed += i => destroyed.Add(i);
            b0.Health.Kill();
            Expect(_module.Points[0].IsDestroyed && destroyed.Count == 1 && destroyed[0] == 0, "보스 0 죽음 → 자리 0 파괴, PointDestroyed(0)");
            Expect(_module.IsInvulnerable, "자리 1·2가 남아 있으면 아직 무적");
            b1.Health.Kill();
            Expect(_module.Points[1].IsDestroyed && _module.IsInvulnerable, "보스 없는 자리 2가 살아 있으면 아직 무적(옛 규칙 그대로)");
            _module.RestorePoint(2, true);
            Expect(!_module.IsInvulnerable, "자리가 전부 파괴되면 피해가 통한다");
            _nest.Health.Damage(100f, _attacker);
            Expect(Mathf.Approximately(_nest.Health.CurrentHealth, 400f), $"100 피해 (실제 HP {_nest.Health.CurrentHealth})");
        }

        static void S3_NestDestroyedForever()
        {
            for (int i = 0; i < 3; i++) _module.RestorePoint(i, true);
            bool destroyedEvent = false; _module.Destroyed += () => destroyedEvent = true;
            _nest.Health.Damage(500f, _attacker);
            Expect(_module.IsDestroyed && destroyedEvent && !_nest.IsAlive, "둥지 사망 → 파괴 상태");
            Expect(!_module.HasLivePoint, "파괴된 둥지에 살아 있는 자리가 없다");
            // 복구 API가 없다 — 되살릴 길은 세이브 복원뿐이고 그것도 상태를 그대로 되돌릴 뿐이다
            Expect(typeof(NestModule).GetMethod("OnDayStarted") == null && typeof(NestModule).GetMethod("OnNightStarted") == null, "복구 진입점이 없어야 함");
        }

        static void S4_Restore()
        {
            _module.RestoreState(true);
            _module.RestorePoint(1, true);
            Expect(_module.IsDestroyed, "둥지 상태 복원");
            Expect(_module.Points[1].IsDestroyed && !_module.Points[0].IsDestroyed, "자리 상태 복원");
            var boss = Boss(); _module.BindBoss(0, boss);
            Expect(_module.Points[0].BossAlive, "복원된 보스가 자리에 붙는다");
            _module.ClearBoss(0);
            Expect(!_module.Points[0].BossAlive, "보스를 치운다");
        }

        static void Expect(bool condition, string message) { if (!condition) _fails.Add(message); }

        static Entity Boss()
        {
            var e = _world.Create(Faction.Monster, new Vector3(4, 0, 0));
            e.Add(new HealthModule(200f));
            e.Add(new EffectsModule());
            return e;
        }
    }
}
