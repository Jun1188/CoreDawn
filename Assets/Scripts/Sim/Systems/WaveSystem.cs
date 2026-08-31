using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>웨이브 몬스터의 종류 — 점수(버스트)인가, 진입로의 지루함 방지 무리인가.</summary>
    public enum WaveSpawnKind { Burst, Trickle }

    /// <summary>
    /// 밤 웨이브 — 심 시스템. 밤 시작에 점수(<see cref="WaveRuleDef.ScoreFor"/>)를 정하고, 살아 있는 둥지 중 무작위 몇 개의 스폰 포인트에서
    /// 버스트(뭉텅이)마다 점수 조각을 명단(cost·weight)으로 소진해 몬스터를 세운다. 버스트 몬스터는 자극 버프(영구)를 받는다.
    /// 진입로(entrances)에서는 점수 몬스터가 정해진 비율만큼 잡힐 때까지 기본 몹 무리가 주기마다 나온다(점수·자극과 무관).
    /// 더 나올 것도(점수) 나온 것도(살아 있는 점수 몬스터) 없으면 밤이 끝난다(<see cref="NightCleared"/>).
    /// 둥지는 리스폰하지 않는다 — 파괴된 둥지에서는 다시 웨이브가 나오지 않고, 자극만 남긴다.
    /// 난수는 자기 시드(xorshift)라 세이브·재현이 된다. 시계는 자기 것(Now) — 러너가 Tick으로 올린다.
    /// 뷰(프리팹)는 <see cref="Spawned"/>를 듣고 붙인다 — 이 시스템은 프리팹을 모른다.
    /// </summary>
    public sealed class WaveSystem
    {
        readonly EntityWorld world;
        readonly MonsterSystem monsters;
        readonly WaveRuleDef rule;

        public WaveRuleDef Rule => rule;

        public bool Active { get; private set; }
        public int Day { get; private set; }
        public int Gate { get; private set; }
        public float Now { get; private set; }
        /// <summary>이 밤의 점수(총 예산).</summary>
        public float Score { get; private set; }
        /// <summary>아직 쓰지 않은 점수.</summary>
        public float Remaining { get; private set; }
        public int Bursts { get; private set; }
        public int BurstsDone { get; private set; }
        public float NextBurstAt { get; private set; }
        public float NextTrickleAt { get; private set; }
        /// <summary>현재 자극 — 파괴된 둥지 수로 규칙이 정한다(growth^n). 밤·낮 가리지 않고 살아 있는 값.</summary>
        public float Stimuli { get { int total = 0, destroyed = 0; foreach (var x in AllNests()) { total++; if (x.IsDestroyed) destroyed++; } return rule.StimuliFor(destroyed, total); } }
        float appliedStimuli = 1f;      // 마지막으로 몬스터들에게 건 자극 — 바뀌면 재적용
        bool spawningTrickle;           // 진입로 무리를 세우는 동안 true — 자극을 안 건다
        Effect[] bakedBuffs = Array.Empty<Effect>(); float bakedFor = -1f;

        readonly List<(Entity nest, int point)> selected = new List<(Entity, int)>();
        readonly List<Vector3> entrances = new List<Vector3>();
        readonly List<Entity> burstMonsters = new List<Entity>();
        readonly List<Entity> trickleMonsters = new List<Entity>();
        readonly List<Entity> tickBuffer = new List<Entity>();

        /// <summary>이 밤에 점수로 나온 수 · 그중 잡힌 수.</summary>
        public int SpawnedCount { get; private set; }
        public int KilledCount { get; private set; }
        public int ScoreAlive { get { int n = 0; foreach (var e in burstMonsters) if (e.IsAlive) n++; return n; } }
        public int Alive { get { int n = ScoreAlive; foreach (var e in trickleMonsters) if (e.IsAlive) n++; return n; } }
        public float KilledFraction => SpawnedCount <= 0 ? 1f : (float)KilledCount / SpawnedCount;
        public IReadOnlyList<Entity> BurstMonsters => burstMonsters;
        public IReadOnlyList<Entity> TrickleMonsters => trickleMonsters;
        public IReadOnlyList<(Entity nest, int point)> SelectedPoints => selected;

        /// <summary>(엔티티, 종류) — 뷰가 프리팹을 붙인다.</summary>
        public event Action<Entity, WaveSpawnKind> Spawned;
        /// <summary>(자리, 마리 수) — 버스트 연출용.</summary>
        public event Action<Vector3, int> BurstSpawned;
        /// <summary>(일차, 점수, 잡은 수) — 밤 시작.</summary>
        public event Action<int, float> NightStarted;
        /// <summary>(잡은 수, 나온 수) — 진행 갱신.</summary>
        public event Action<int, int> Progress;
        /// <summary>(일차, 잡은 수) — 더 나올 것도 나온 것도 없다.</summary>
        public event Action<int, int> NightCleared;

        uint rngState = 0x9E3779B9u;

        public WaveSystem(EntityWorld world, MonsterSystem monsters, WaveRuleDef rule)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.monsters = monsters ?? throw new ArgumentNullException(nameof(monsters));
            this.rule = rule ?? throw new ArgumentNullException(nameof(rule));
            monsters.Spawned += OnMonsterSpawned;   // 자극은 이 세계의 모든 몬스터(둥지 보스·낮 방어자·버스트)에 — 진입로 무리만 예외
        }

        // ── 난수 (xorshift32 — 세이브 가능한 한 정수) ──
        public uint RngState { get => rngState; set => rngState = value == 0 ? 0x9E3779B9u : value; }
        uint NextU() { uint x = rngState; x ^= x << 13; x ^= x >> 17; x ^= x << 5; rngState = x; return x; }
        float NextF() => (NextU() & 0xFFFFFF) / 16777216f;                 // [0,1)
        int NextInt(int minInclusive, int maxExclusive) => maxExclusive <= minInclusive ? minInclusive : minInclusive + (int)(NextU() % (uint)(maxExclusive - minInclusive));

        // ── 둥지 ──
        /// <summary>월드의 둥지 전부(파괴된 것 포함 — 총량의 분모) — 순서는 엔티티 id 순으로 고정(재현성).</summary>
        public List<NestModule> AllNests()
        {
            var list = new List<NestModule>();
            foreach (var e in world.All) { var n = e.Get<NestModule>(); if (n != null) list.Add(n); }
            list.Sort((a, b) => string.CompareOrdinal(a.Owner.Id.ToString(), b.Owner.Id.ToString()));
            return list;
        }

        public int DestroyedNests() { int n = 0; foreach (var x in AllNests()) if (x.IsDestroyed) n++; return n; }

        public float CurrentScore(int day, int gate, out int living, out int total)
        {
            var nests = AllNests();
            total = nests.Count; living = 0;
            foreach (var n in nests) if (!n.IsDestroyed) living++;
            return rule.ScoreFor(day, gate, living, total);
        }

        // ── 밤 ──

        /// <summary>밤 시작 — 점수를 정하고 둥지·스폰 포인트를 고른다. 버스트 수·간격은 규칙(목표 밤 길이)에서, 진입로는 뷰(맵)가 준다.</summary>
        public void StartNight(int day, int gate, IReadOnlyList<Vector3> nightEntrances, uint seed)
        {
            EndNight();
            RngState = seed;
            Day = day; Gate = gate; Now = 0f;
            Score = Remaining = CurrentScore(day, gate, out int living, out int total);
            entrances.Clear();
            if (nightEntrances != null) entrances.AddRange(nightEntrances);

            // 살아 있는 둥지 중 무작위 개수 — 그 둥지들의 살아 있는 스폰 포인트가 이 밤의 출구
            var alive = new List<NestModule>();
            foreach (var n in AllNests()) if (!n.IsDestroyed) alive.Add(n);
            int max = rule.NestsPerNightMax > 0 ? Math.Min(rule.NestsPerNightMax, alive.Count) : alive.Count;
            int min = Math.Max(1, Math.Min(rule.NestsPerNightMin, max));
            int count = alive.Count == 0 ? 0 : NextInt(min, max + 1);
            Shuffle(alive);
            for (int i = 0; i < count; i++)
                for (int p = 0; p < alive[i].Points.Count; p++)
                    if (!alive[i].Points[p].IsDestroyed) selected.Add((alive[i].Owner, p));

            Bursts = Math.Max(1, rule.BurstsPerNight);
            BurstsDone = 0;
            NextBurstAt = 0f;                       // 첫 버스트는 즉시
            NextTrickleAt = rule.Trickle.Interval;  // 진입로 무리는 한 주기 뒤부터
            SpawnedCount = KilledCount = 0;
            Active = true;
            NightStarted?.Invoke(day, Score);
            if (Score <= 0f || selected.Count == 0) { Remaining = 0f; TryClear(); }
        }

        /// <summary>아침 — 이 밤의 몬스터를 전부 치우고 멈춘다.</summary>
        public void EndNight()
        {
            Active = false;
            foreach (var e in burstMonsters) monsters.Despawn(e);
            foreach (var e in trickleMonsters) monsters.Despawn(e);
            burstMonsters.Clear(); trickleMonsters.Clear(); selected.Clear(); entrances.Clear();
            Remaining = 0f;
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            // 둥지가 더 부서졌으면(자극 상승) 살아 있는 몬스터 전부에 다시 건다 — 남은 둥지의 보스·방어자도 같이 강해진다
            float stim = Stimuli;
            if (stim != appliedStimuli) { appliedStimuli = stim; ReapplyStimulus(); }
            if (!Active) return;
            Now += dt;

            // 밤 도중 부서진 둥지의 출구는 뺀다 — 파괴 = 스폰원 소멸
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                var n = selected[i].nest.Get<NestModule>();
                if (n == null || n.IsDestroyed || selected[i].point >= n.Points.Count || n.Points[selected[i].point].IsDestroyed) selected.RemoveAt(i);
            }

            if (BurstsDone < Bursts && Now >= NextBurstAt)
            {
                if (selected.Count == 0) { Remaining = 0f; BurstsDone = Bursts; }   // 출구가 없으면 남은 점수는 쓸 수 없다
                else Burst();
                NextBurstAt = Now + rule.BurstInterval;
            }

            if (rule.Trickle.Monster != null && entrances.Count > 0 && KilledFraction < rule.Trickle.UntilKilledFraction && Now >= NextTrickleAt)
            {
                var at = entrances[NextInt(0, entrances.Count)];
                spawningTrickle = true;
                try { for (int i = 0; i < rule.Trickle.Group; i++) SpawnOne(rule.Trickle.Monster, Scatter(at, rule.BurstSpread), WaveSpawnKind.Trickle); }
                finally { spawningTrickle = false; }
                NextTrickleAt = Now + rule.Trickle.Interval;
            }

            TryClear();
        }

        void Burst()
        {
            int left = Bursts - BurstsDone;
            float slice = left <= 1 ? Remaining : Remaining / left;
            var (nest, point) = selected[NextInt(0, selected.Count)];
            Vector3 at = nest.Get<NestModule>().Points[point].Position;

            int count = 0;
            float budget = slice;
            var eligible = new List<WaveRuleDef.RosterEntry>();
            while (true)
            {
                eligible.Clear();
                float total = 0f;
                foreach (var r in rule.Roster)
                    if (r.Eligible(Day, Gate) && r.Cost <= budget + 0.0001f) { eligible.Add(r); total += r.Weight; }
                if (eligible.Count == 0) break;
                float pick = NextF() * total;
                var chosen = eligible[eligible.Count - 1];
                foreach (var r in eligible) { pick -= r.Weight; if (pick <= 0f) { chosen = r; break; } }
                budget -= chosen.Cost;
                SpawnOne(chosen.Monster, Scatter(at, rule.BurstSpread), WaveSpawnKind.Burst);   // 자극 버프는 OnMonsterSpawned가 건다
                count++;
            }
            Remaining = Math.Max(0f, Remaining - (slice - budget));   // 못 쓴 조각(가장 싼 몹보다 적은 나머지)은 다음 버스트로
            BurstsDone++;
            if (BurstsDone >= Bursts) Remaining = 0f;                  // 마지막 버스트 뒤 잔돈은 버린다
            BurstSpawned?.Invoke(at, count);
            Progress?.Invoke(KilledCount, SpawnedCount);
        }

        // ── 자극 버프 ──

        Effect[] BakeStimulusBuffs(float stimuli)
        {
            if (stimuli == bakedFor) return bakedBuffs;
            bakedFor = stimuli;
            if (stimuli <= 1f || rule.StimulusBuffs.Count == 0) return bakedBuffs = Array.Empty<Effect>();
            var list = new List<Effect>();
            foreach (var b in rule.StimulusBuffs)
                if (b.Spec != null) list.Add(new Effect(b.Spec, b.ValueAt(stimuli)));
            return bakedBuffs = list.ToArray();
        }

        void OnMonsterSpawned(Entity e) { if (!spawningTrickle) ApplyStimulus(e, Stimuli); }

        void ApplyStimulus(Entity e, float stimuli)
        {
            var buffs = BakeStimulusBuffs(stimuli);
            if (buffs.Length > 0) e.Get<EffectsModule>()?.Apply(buffs, null, e.Position);   // 비중첩 효과 — 같은 정의는 값이 갱신된다
        }

        void RemoveStimulus(Entity e)
        {
            var fx = e.Get<EffectsModule>(); if (fx == null) return;
            foreach (var b in rule.StimulusBuffs) if (b.Spec != null) fx.Remove(b.Spec);
        }

        void ReapplyStimulus()
        {
            float stim = appliedStimuli;
            foreach (var e in world.All)
            {
                if (!e.IsAlive || e.Get<MonsterBrainModule>() == null || trickleMonsters.Contains(e)) continue;
                ApplyStimulus(e, stim);
            }
        }

        Entity SpawnOne(EntityDef def, Vector3 at, WaveSpawnKind kind)
        {
            var e = monsters.Spawn(def, at, Vector3.forward);
            if (kind == WaveSpawnKind.Burst) { burstMonsters.Add(e); SpawnedCount++; e.Died += OnBurstMonsterDied; }
            else trickleMonsters.Add(e);
            Spawned?.Invoke(e, kind);
            return e;
        }

        void OnBurstMonsterDied(Entity e)
        {
            KilledCount++;
            Progress?.Invoke(KilledCount, SpawnedCount);
            TryClear();
        }

        /// <summary>세이브 복원 — 되살린 몬스터를 이 밤의 명단에 다시 넣는다(뷰가 세운 엔티티).</summary>
        public void Register(Entity e, WaveSpawnKind kind)
        {
            if (e == null) return;
            if (kind == WaveSpawnKind.Burst) { burstMonsters.Add(e); SpawnedCount++; e.Died += OnBurstMonsterDied; }
            else { trickleMonsters.Add(e); RemoveStimulus(e); }   // 뷰가 세울 때 걸린 자극을 뗀다 — 진입로 무리는 자극을 안 받는다
        }

        void TryClear()
        {
            if (!Active) return;
            bool nothingToCome = BurstsDone >= Bursts || Remaining <= 0f || selected.Count == 0;
            if (nothingToCome && ScoreAlive == 0)
            {
                Active = false;
                NightCleared?.Invoke(Day, KilledCount);
            }
        }

        Vector3 Scatter(Vector3 at, float radius)
        {
            float a = NextF() * Mathf.PI * 2f, r = NextF() * radius;
            return at + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = NextInt(0, i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }

        // ── 세이브 ──
        public sealed class State
        {
            public bool Active; public int Day, Gate; public float Now, Score, Remaining; public int Bursts, BurstsDone;
            public float NextBurstAt, NextTrickleAt; public int Spawned, Killed; public uint Rng;
            public List<(string nest, int point)> Selected = new List<(string, int)>();
            public List<Vector3> Entrances = new List<Vector3>();
        }

        public State Capture() => new State
        {
            Active = Active, Day = Day, Gate = Gate, Now = Now, Score = Score, Remaining = Remaining, Bursts = Bursts, BurstsDone = BurstsDone,
            NextBurstAt = NextBurstAt, NextTrickleAt = NextTrickleAt, Spawned = SpawnedCount, Killed = KilledCount, Rng = rngState,
            Selected = selected.ConvertAll(s => (s.nest.Id.ToString(), s.point)), Entrances = new List<Vector3>(entrances),
        };

        /// <summary>복원 — 몬스터 명단은 뷰가 되살린 뒤 <see cref="Register"/>로 다시 채운다. 점수 몬스터 수(Spawned·Killed)는 저장값이 정본.</summary>
        public void Restore(State s, Func<string, Entity> nestById)
        {
            EndNight();
            Active = s.Active; Day = s.Day; Gate = s.Gate; Now = s.Now; Score = s.Score; Remaining = s.Remaining; Bursts = s.Bursts; BurstsDone = s.BurstsDone;
            NextBurstAt = s.NextBurstAt; NextTrickleAt = s.NextTrickleAt; SpawnedCount = s.Spawned; KilledCount = s.Killed; RngState = s.Rng;
            foreach (var (id, point) in s.Selected) { var e = nestById(id); if (e != null) selected.Add((e, point)); }
            entrances.AddRange(s.Entrances);
        }

        /// <summary>복원 뒤 — Register로 넣은 만큼 SpawnedCount가 늘어난 것을 저장값으로 되돌린다.</summary>
        public void FinishRestore(int spawned, int killed) { SpawnedCount = spawned; KilledCount = killed; }
    }
}
