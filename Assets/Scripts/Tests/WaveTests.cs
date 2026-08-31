using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.Tests
{
    /// <summary>
    /// 밤 웨이브(WaveSystem) 특성화 테스트 — 씬·GameObject·플레이모드 없이 심만으로 돈다.
    ///
    /// 검증 대상: 점수식(일차+게이트 합, 자극 배율, 살아 있는 둥지 비율, 상한) · 밤 시작에 살아 있는 둥지만 고른다 · 버스트가 밤 길이에 맞춰
    /// 나뉘고 명단 cost로 점수를 소진한다(뭉텅이) · 명단의 일차·게이트 조건과 가중 추첨 · 자극 버프는 버스트 몬스터에만 ·
    /// 진입로 무리는 점수 몬스터가 잡힐 때까지 주기마다 · 더 나올 것도 나온 것도 없으면 밤이 끝난다 · 둥지 전멸이면 점수 0 · 세이브 복원.
    /// 실행: eval `CoreDawn.Tests.WaveTests.RunAll(out var r)`.
    /// </summary>
    public static class WaveTests
    {
        static readonly List<(string name, bool pass, string detail)> _results = new();
        static readonly List<string> _fails = new();
        static EntityWorld _world;
        static MonsterSystem _monsters;
        static WaveSystem _waves;
        static WaveRuleDef _rule;
        static EntityDef _basic, _spitter, _boss;
        static EffectSpec _attackUp, _damageTaken;

        public static bool RunAll(out string report)
        {
            _results.Clear();
            _basic = PackEntity("coredawn:entity/basic"); _spitter = PackEntity("coredawn:entity/spitter"); _boss = PackEntity("coredawn:entity/boss");
            _attackUp = SimHost.Database.Effect("coredawn:effect/attack_up"); _damageTaken = SimHost.Database.Effect("coredawn:effect/damage_taken");

            Run("1. 점수식 — (일차+게이트) × (살아 있는 몫 + 자극 강화분)",      S1_Score);
            Run("2. 밤 시작 — 살아 있는 둥지의 살아 있는 자리만 출구",           S2_Selection);
            Run("3. 버스트 — 밤 길이로 나누고 명단 cost로 점수를 뭉텅이로 소진", S3_Bursts);
            Run("4. 명단 — 일차·게이트 조건과 가중 추첨(합이 분모)",             S4_Roster);
            Run("5. 자극 버프는 버스트 몬스터에만, 진입로 무리는 안 받는다",       S5_StimulusBuffs);
            Run("6. 진입로 무리 — 점수 몬스터가 90% 잡힐 때까지 주기마다",        S6_Trickle);
            Run("7. 더 나올 것도 나온 것도 없으면 밤이 끝난다",                   S7_Clear);
            Run("8. 둥지가 전부 파괴되면 점수 0 — 즉시 종료",                     S8_NoNests);
            Run("9. 세이브 복원",                                                S9_Restore);
            Run("10. 자극은 둥지가 세우는 몬스터에도 — 파괴가 늘면 살아 있는 몬스터에 다시", S10_StimulusEverywhere);

            int passed = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var (name, pass, detail) in _results)
            {
                if (pass) passed++;
                sb.AppendLine($"  {(pass ? "PASS" : "FAIL")}  {name}");
                if (!pass) sb.AppendLine("        " + detail.Replace("\n", "\n        "));
            }
            report = $"[WaveTests] {passed}/{_results.Count} 통과\n" + sb;
            return passed == _results.Count;
        }

        static void Run(string name, Action scenario)
        {
            _world = new EntityWorld();
            _monsters = new MonsterSystem(_world, null);
            _rule = Rule();
            _waves = new WaveSystem(_world, _monsters, _rule);
            _fails.Clear();
            try { scenario(); }
            catch (Exception e) { _fails.Add("예외 발생:\n" + e); }
            _results.Add((name, _fails.Count == 0, string.Join("\n", _fails)));
            _monsters.Dispose();
        }

        // ─── 시나리오 ────────────────────────────────────────

        static void S1_Score()
        {
            Expect(Approx(_rule.ScoreFor(1, 0, 5, 5), 40f), $"1일·게이트 0·5/5 = 40 (실제 {_rule.ScoreFor(1, 0, 5, 5)})");
            Expect(Approx(_rule.ScoreFor(3, 1, 5, 5), 200f), $"3일·게이트 1 = 120+80 = 200 (합연산) (실제 {_rule.ScoreFor(3, 1, 5, 5)})");
            Expect(Approx(_rule.ScoreFor(3, 1, 3, 5), 200f * (0.6f + 2f * Mathf.Pow(0.4f, 4) + 0.1f * 0.4f)), $"둘 파괴: 200 × (3/5 + 2·0.4⁴ + 0.1·0.4) = 138.24 (실제 {_rule.ScoreFor(3, 1, 3, 5)})");
            Expect(_rule.ScoreFor(1, 0, 4, 5) < _rule.ScoreFor(1, 0, 5, 5), $"첫 파괴엔 총량이 준다 (40 → {_rule.ScoreFor(1, 0, 4, 5)})");
            Expect(_rule.ScoreFor(1, 0, 1, 5) > _rule.ScoreFor(1, 0, 2, 5), $"마지막 둥지는 둘 남았을 때보다 세다 ({_rule.ScoreFor(1, 0, 2, 5)} → {_rule.ScoreFor(1, 0, 1, 5)})");
            Expect(Approx(_rule.StimuliFor(4, 5), (0.2f + 2f * Mathf.Pow(0.8f, 4) + 0.08f) / 0.2f), $"마지막 둥지의 자극 = 총량 ÷ 살아 있는 몫 = 5.5 (실제 {_rule.StimuliFor(4, 5)})");
            Expect(_rule.ScoreFor(5, 2, 0, 5) == 0f, "살아 있는 둥지가 없으면 0");
        }

        static void S2_Selection()
        {
            var nests = Nests(4, pointsEach: 2);
            nests[3].RestoreState(true);                   // 파괴된 둥지
            nests[1].RestorePoint(0, true);                // 살아 있는 둥지의 파괴된 자리
            Night(2, 0, 120f, null, seed: 7);
            Expect(_waves.Active, "밤 시작");
            Expect(Approx(_waves.Score, 80f * (0.75f + 2f * Mathf.Pow(0.25f, 4) + 0.1f * 0.25f)), $"점수 = 80 × (3/4 + 2·0.25⁴ + 0.025) = 62.6 (실제 {_waves.Score})");
            Expect(_waves.SelectedPoints.Count >= 1 && _waves.SelectedPoints.All(s => !s.nest.Get<NestModule>().IsDestroyed), "파괴된 둥지는 출구가 아니다");
            Expect(_waves.SelectedPoints.All(s => !s.nest.Get<NestModule>().Points[s.point].IsDestroyed), "파괴된 자리는 출구가 아니다");
            int distinctNests = _waves.SelectedPoints.Select(s => s.nest).Distinct().Count();
            Expect(distinctNests >= 1 && distinctNests <= 3, $"살아 있는 3개 중 무작위 개수 (실제 {distinctNests})");
        }

        // 옛 "밤 길이" 인자 — 규칙의 목표 밤 길이·버스트 수(30초당 1)로 옮겨 같은 버스트 수·간격을 만든다
        static void Night(int day, int gate, float length, IReadOnlyList<Vector3> entrances, uint seed)
        {
            _rule.TargetNightLength = length; _rule.BurstsPerNight = Math.Max(1, (int)(length / 30f));
            _waves.StartNight(day, gate, entrances, seed);
        }

        static void S3_Bursts()
        {
            Nests(5, pointsEach: 1);
            var bursts = new List<(Vector3 at, int count)>(); _waves.BurstSpawned += (at, n) => bursts.Add((at, n));
            Night(5, 2, 120f, null, seed: 3);   // 점수 = 200+160 = 360, 버스트 = 120/30 = 4
            Expect(_waves.Bursts == 4 && Approx(_waves.Score, 360f), $"버스트 4 · 점수 360 (실제 {_waves.Bursts}·{_waves.Score})");
            Tick(0.05f);
            Expect(bursts.Count == 1 && bursts[0].count > 0, $"첫 버스트는 즉시, 뭉텅이 (실제 {bursts.Count}회, {(bursts.Count > 0 ? bursts[0].count : 0)}마리)");
            Expect(_waves.Alive == bursts[0].count, "버스트 사이에는 아무것도 안 나온다(뭉텅이)");
            Tick(29f);
            Expect(bursts.Count == 1, "간격 전에는 다음 버스트가 없다");
            Tick(61f);
            Expect(bursts.Count == 3, $"30초마다 (실제 {bursts.Count})");
            Tick(30f);
            Expect(bursts.Count == 4 && _waves.BurstsDone == 4 && _waves.Remaining == 0f, $"버스트 4번이면 점수를 다 썼다 (남은 {_waves.Remaining})");
            float spent = 0f; foreach (var e in _waves.BurstMonsters) spent += CostOf(e);
            Expect(Approx(spent, 360f, 10f), $"쓴 cost ≈ 점수 360 (실제 {spent})");
            Expect(_waves.BurstMonsters.All(e => e.IsAlive && e.Faction == Faction.Monster), "심 몬스터 엔티티가 서 있다");
        }

        static void S4_Roster()
        {
            Nests(5, 1);
            Night(1, 0, 30f, null, seed: 11);   // 1일: spitter(minDay 3)·boss(minGate 1) 불가 → basic만. 밤 30초 = 버스트 1 → 40점을 한 번에
            Tick(0.05f);
            Expect(_waves.BurstMonsters.Count == 4 && _waves.BurstMonsters.All(e => DefOf(e) == _basic), $"1일 40점 = basic 4 (실제 {_waves.BurstMonsters.Count})");

            // 5일·게이트 2: 셋 다 가능 — 가중치 70/25/5 비율로 섞인다(시드 고정, 표본 크게)
            _monsters.Dispose(); _world = new EntityWorld(); _monsters = new MonsterSystem(_world, null); _waves = new WaveSystem(_world, _monsters, _rule);
            Nests(5, 1);
            _rule.DayPoints = 400f; _rule.GatePoints = 0f;   // 5일 → 2000점
            Night(5, 2, 30f, null, seed: 5);
            Tick(0.05f);
            int nb = _waves.BurstMonsters.Count(e => DefOf(e) == _basic), ns = _waves.BurstMonsters.Count(e => DefOf(e) == _spitter), nbo = _waves.BurstMonsters.Count(e => DefOf(e) == _boss);
            Expect(nb > ns && ns > nbo && nbo >= 1, $"가중 추첨: basic > spitter > boss ≥ 1 (실제 {nb}/{ns}/{nbo})");
            float spent = nb * 10f + ns * 20f + nbo * 60f;
            Expect(Approx(spent, 2000f, 10f), $"cost 합 ≈ 2000 (실제 {spent})");
        }

        static void S5_StimulusBuffs()
        {
            var nests = Nests(4, 1);
            nests[0].RestoreState(true); nests[1].RestoreState(true);   // 둘 파괴(2/4) → 총량 0.5 + 2·0.5⁴ + 0.05 = 0.675, 자극 = 0.675/0.5 = 1.35
            float s2 = _rule.StimuliFor(2, 4);
            Night(2, 0, 60f, new[] { new Vector3(50, 0, 50) }, seed: 9);
            Expect(Approx(_waves.Stimuli, 1.35f) && Approx(s2, 1.35f), $"자극 = 1.35 (실제 {_waves.Stimuli})");
            Tick(0.05f);
            var burst = _waves.BurstMonsters[0]; var fx = burst.Get<EffectsModule>();
            Expect(fx != null && fx.Has(_attackUp) && fx.Has(_damageTaken), "버스트 몬스터에 공격력·받는 피해 버프");
            Expect(Approx(fx.AttackMultiplierFor(_damageTaken == null ? null : SimHost.Database.Effect("coredawn:effect/damage")), 1f + 0.25f * (s2 - 1f)), $"공격력 1 + 0.25×0.35 = 1.0875 (실제 {fx.AttackMultiplierFor(SimHost.Database.Effect("coredawn:effect/damage"))})");
            Expect(Approx(fx.IncomingDamageMultiplier, 1f - 0.15f * (s2 - 1f)), $"받는 피해 1 − 0.15×0.35 = 0.9475 (실제 {fx.IncomingDamageMultiplier})");
            Tick(20.1f);
            Expect(_waves.TrickleMonsters.Count == 3 && _waves.TrickleMonsters.All(e => !e.Get<EffectsModule>().Has(_attackUp)), $"진입로 무리 3에는 버프 없음 (실제 {_waves.TrickleMonsters.Count})");
        }

        static void S6_Trickle()
        {
            Nests(5, 1);
            Night(1, 0, 60f, new[] { new Vector3(50, 0, 50) }, seed: 2);
            Tick(0.05f);
            Expect(_waves.TrickleMonsters.Count == 0, "시작 직후엔 무리 없음(한 주기 뒤부터)");
            Tick(20.1f);
            Expect(_waves.TrickleMonsters.Count == 3, $"20초마다 3마리 (실제 {_waves.TrickleMonsters.Count})");
            Tick(20f);
            Expect(_waves.TrickleMonsters.Count == 6, $"두 번째 무리 (실제 {_waves.TrickleMonsters.Count})");
            // 점수 몬스터 4 중 4를 잡으면(100%) 무리가 멈춘다
            foreach (var e in _waves.BurstMonsters.ToList()) e.Health.Kill();
            Tick(20f);
            Expect(_waves.TrickleMonsters.Count == 6, "90% 이상 잡히면 무리가 더 안 나온다");
        }

        static void S7_Clear()
        {
            Nests(5, 1);
            int clearedDay = -1, clearedKilled = -1; _waves.NightCleared += (d, k) => { clearedDay = d; clearedKilled = k; };
            Night(1, 0, 30f, null, seed: 4);   // 버스트 1 → 40점 = basic 4
            Tick(0.05f);
            Expect(_waves.Active && _waves.Remaining == 0f && _waves.BurstsDone == 1, "다 스폰했지만 살아 있으니 밤은 계속");
            var list = _waves.BurstMonsters.ToList();
            for (int i = 0; i < list.Count - 1; i++) list[i].Health.Kill();
            Tick(0.05f);
            Expect(_waves.Active && _waves.KilledCount == 3, "하나 남아 있으면 계속");
            list[^1].Health.Kill();
            Expect(!_waves.Active && clearedDay == 1 && clearedKilled == 4, $"전멸 → 밤 끝 (day {clearedDay}, killed {clearedKilled})");
        }

        static void S8_NoNests()
        {
            var nests = Nests(3, 1);
            foreach (var n in nests) n.RestoreState(true);
            bool cleared = false; _waves.NightCleared += (_, __) => cleared = true;
            Night(4, 1, 60f, new[] { Vector3.zero }, seed: 1);
            Expect(_waves.Score == 0f && cleared && !_waves.Active, "둥지 전멸 → 점수 0 → 즉시 종료(진입로 무리도 없다)");
            Tick(25f);
            Expect(_waves.TrickleMonsters.Count == 0, "끝난 밤에는 무리도 없다");
        }

        static void S10_StimulusEverywhere()
        {
            var nests = Nests(4, 1);
            var boss = _monsters.Spawn(_boss, new Vector3(10, 0, 10), Vector3.forward);   // 둥지 보스처럼 뷰/둥지가 세운 몬스터 — 밤이 아니어도
            var fx = boss.Get<EffectsModule>();
            Expect(fx != null && Approx(fx.IncomingDamageMultiplier, 1f), "파괴 0 → 자극 1, 버프 없음");
            float s1 = _rule.StimuliFor(1, 4), s2 = _rule.StimuliFor(2, 4);   // 1/4 파괴 → 1.044, 2/4 → 1.35
            nests[0].RestoreState(true);                     // 둥지 하나 파괴
            _waves.Tick(0.05f);                              // 밤이 아니어도 자극 변화는 틱이 잡는다
            Expect(Approx(fx.IncomingDamageMultiplier, 1f - 0.15f * (s1 - 1f), 0.001f), $"파괴 1 → 받는 피해 {1f - 0.15f * (s1 - 1f):0.000} (실제 {fx.IncomingDamageMultiplier})");
            nests[1].RestoreState(true);                     // 둘 파괴 → 자극 1.35, 같은 정의는 값이 갱신(중첩 아님)
            _waves.Tick(0.05f);
            Expect(Approx(fx.IncomingDamageMultiplier, 1f - 0.15f * (s2 - 1f), 0.001f), $"파괴 2 → 0.9475로 갱신 (실제 {fx.IncomingDamageMultiplier})");
            Expect(fx.ActiveCount == 2, $"버프는 정의당 하나(2개) (실제 {fx.ActiveCount})");
            var later = _monsters.Spawn(_basic, new Vector3(12, 0, 10), Vector3.forward);   // 이후 세운 몬스터도 현재 자극으로
            Expect(Approx(later.Get<EffectsModule>().IncomingDamageMultiplier, 1f - 0.15f * (s2 - 1f), 0.001f), "나중에 세운 몬스터도 현재 자극");
            _waves.Register(later, WaveSpawnKind.Trickle);   // 복원된 진입로 무리로 등록되면 자극을 뗀다
            Expect(Approx(later.Get<EffectsModule>().IncomingDamageMultiplier, 1f), "진입로 무리로 등록 → 자극 회수");
        }

        static void S9_Restore()
        {
            Nests(5, 1);
            Night(3, 1, 120f, new[] { new Vector3(50, 0, 50) }, seed: 8);
            Tick(31f);   // 버스트 2회
            var state = _waves.Capture();
            int spawned = _waves.SpawnedCount, remainingBursts = _waves.Bursts - _waves.BurstsDone; float remaining = _waves.Remaining;
            var alive = _waves.BurstMonsters.ToList();

            var fresh = new WaveSystem(_world, _monsters, _rule);
            fresh.Restore(state, id => _world.All.FirstOrDefault(e => e.Id.ToString() == id));
            foreach (var e in alive) fresh.Register(e, WaveSpawnKind.Burst);
            fresh.FinishRestore(state.Spawned, state.Killed);
            Expect(fresh.Active && fresh.SpawnedCount == spawned && Approx(fresh.Remaining, remaining) && fresh.Bursts - fresh.BurstsDone == remainingBursts, "진행 상태 복원");
            Expect(fresh.SelectedPoints.Count == _waves.SelectedPoints.Count && fresh.RngState == _waves.RngState, "출구·난수 상태 복원");
            int before = fresh.BurstMonsters.Count;
            fresh.Tick(30f);
            Expect(fresh.BurstMonsters.Count > before, "복원된 밤이 이어서 버스트한다");
        }

        // ─── 헬퍼 ────────────────────────────────────────────

        static void Expect(bool condition, string message) { if (!condition) _fails.Add(message); }
        static bool Approx(float a, float b, float eps = 0.01f) => Math.Abs(a - b) <= eps;

        static void Tick(float seconds)
        {
            int n = Mathf.CeilToInt(seconds / 0.05f);
            for (int i = 0; i < n; i++) _waves.Tick(seconds / n);
        }

        static WaveRuleDef Rule()
        {
            var r = new WaveRuleDef { Id = "test:wave/rule", DayPoints = 40f, GatePoints = 80f, StimulusAmplitude = 2f, StimulusExponent = 4f, StimulusLinear = 0.1f,
                                      NestsPerNightMin = 1, NestsPerNightMax = 0, TargetNightLength = 120f, BurstsPerNight = 4, BurstSpread = 2f };
            r.StimulusBuffs.Add(new WaveRuleDef.StimulusBuff { EffectId = _attackUp.Id, Spec = _attackUp, Base = 1f, PerStimulus = 0.25f, Min = 0.1f, Max = 10f });
            r.StimulusBuffs.Add(new WaveRuleDef.StimulusBuff { EffectId = _damageTaken.Id, Spec = _damageTaken, Base = 1f, PerStimulus = -0.15f, Min = 0.25f, Max = 1f });
            r.Roster.Add(new WaveRuleDef.RosterEntry { MonsterId = _basic.Id, Monster = _basic, Cost = 10f, Weight = 70f, MinDay = 1, MinGate = 0 });
            r.Roster.Add(new WaveRuleDef.RosterEntry { MonsterId = _spitter.Id, Monster = _spitter, Cost = 20f, Weight = 25f, MinDay = 3, MinGate = 0 });
            r.Roster.Add(new WaveRuleDef.RosterEntry { MonsterId = _boss.Id, Monster = _boss, Cost = 60f, Weight = 5f, MinDay = 4, MinGate = 1 });
            r.Trickle = new WaveRuleDef.TrickleRule { MonsterId = _basic.Id, Monster = _basic, Group = 3, Interval = 20f, UntilKilledFraction = 0.9f };
            return r;
        }

        static List<NestModule> Nests(int count, int pointsEach)
        {
            var list = new List<NestModule>();
            for (int i = 0; i < count; i++)
            {
                var def = new EntityDef { Id = "test:entity/nest" + i, Faction = Faction.Monster };
                def.Modules.Add(new HealthModuleDef { MaxHp = 500f }); def.Modules.Add(new EffectsModuleDef()); def.Modules.Add(new NestModuleDef());
                var e = _world.Create(def.Faction, new Vector3(i * 20f, 0, 0)); def.Assemble(e);
                var m = e.Get<NestModule>();
                var pts = new List<(Vector3, bool)>();
                for (int p = 0; p < pointsEach; p++) pts.Add((e.Position + new Vector3(0, 0, 4 + p * 4), true));
                m.ConfigurePoints(pts);
                list.Add(m);
            }
            return list;
        }

        static EntityDef DefOf(Entity e)
        {
            float hp = e.Health.MaxHealth;
            return Approx(hp, 500f) ? _boss : Approx(hp, 60f) ? _spitter : _basic;
        }
        static float CostOf(Entity e) => DefOf(e) == _boss ? 60f : DefOf(e) == _spitter ? 20f : 10f;

        static EntityDef PackEntity(string id)
        {
            if (SimHost.Database == null) SimHost.DatabaseLoader = () => PackLoader.Load();
            var def = SimHost.Database?.Entity(id);
            if (def == null) throw new Exception($"팩 엔티티 '{id}'를 찾지 못했습니다");
            return def;
        }
    }
}
