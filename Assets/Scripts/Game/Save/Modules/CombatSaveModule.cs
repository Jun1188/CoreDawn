using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Entities;
namespace CoreDawn.Save
{
    /// <summary>
    /// 전투 — 둥지의 파괴·복구 진행, 보스, 밤에 몰려온 웨이브 몬스터.
    ///
    /// 둥지가 이 모듈의 무게중심이다. 파괴 여부만이 아니라 <b>며칠에 부쉈는지</b>를 함께 저장해야
    /// 복구 카운트다운(destroyedDay + recoveryDays)이 이어진다 — 날짜를 빠뜨리면 불러올 때마다
    /// 복구가 미뤄지거나 즉시 되살아난다.
    ///
    /// 참고: 정상적인 자동 저장은 아침에 일어나고 아침에는 웨이브 몬스터가 전멸(DespawnAll)하므로,
    /// 몬스터 목록이 실제로 채워지는 것은 밤에 손으로 저장했을 때다.
    /// </summary>
    public class CombatSaveModule : ISaveModule
    {
        public string ModuleId => "combat";
        public int Order => 40;

        // ── DTO ──────────────────────────────────────────────────────

        public class Dto
        {
            [JsonProperty("nests")] public List<NestDto> Nests = new();
            [JsonProperty("monsters")] public List<MonsterDto> Monsters = new();
            [JsonProperty("wave")] public WaveDto Wave = new();
        }

        public class NestDto
        {
            [JsonProperty("path")] public string ScenePath;
            [JsonProperty("destroyed")] public bool IsDestroyed;
            [JsonProperty("warned")] public bool HasWarned;
            [JsonProperty("hpMax")] public float HpMax;
            [JsonProperty("hpCur")] public float HpCurrent;
            [JsonProperty("points")] public List<PointDto> Points = new();
        }

        public class PointDto
        {
            [JsonProperty("i")] public int Index;
            [JsonProperty("destroyed")] public bool IsDestroyed;

            /// <summary>null이면 저장 당시 이 자리에 살아있는 보스가 없었다는 뜻.</summary>
            [JsonProperty("boss")] public MonsterDto Boss;
        }

        public class MonsterDto
        {
            /// <summary>종류 — 팩 id(예 "coredawn:entity/spitter", v4부터). 팩에 없는 id는 경고 후 건너뛴다(변환은 SaveMigrations가 한다).</summary>
            [JsonProperty("data")] public string DataId;
            [JsonProperty("pos")] public Vector3 Position;
            [JsonProperty("rot")] public Quaternion Rotation;
            [JsonProperty("hpMax")] public float HpMax;
            [JsonProperty("hpCur")] public float HpCurrent;
            [JsonProperty("boss")] public bool IsBoss;
            [JsonProperty("awake")] public bool HasBeenAttacked;
            [JsonProperty("defender")] public bool IsNestDefender;
            [JsonProperty("defendOrigin")] public Vector3 DefendOrigin;
            /// <summary>이 밤의 웨이브 몬스터인가 — "burst"(점수) | "trickle"(진입로 무리) | null(둥지 방어자·보스).</summary>
            [JsonProperty("wave")] public string WaveKind;
        }

        /// <summary>밤 웨이브 심 상태(WaveSystem.State) — 점수·남은 점수·버스트·출구·난수. 몬스터 명단은 monsters의 wave 표시로 되살린다.</summary>
        public class WaveDto
        {
            [JsonProperty("active")] public bool Active;
            [JsonProperty("day")] public int Day;
            [JsonProperty("gate")] public int Gate;
            [JsonProperty("now")] public float Now;
            [JsonProperty("score")] public float Score;
            [JsonProperty("remaining")] public float Remaining;
            [JsonProperty("bursts")] public int Bursts;
            [JsonProperty("burstsDone")] public int BurstsDone;
            [JsonProperty("nextBurstAt")] public float NextBurstAt;
            [JsonProperty("nextTrickleAt")] public float NextTrickleAt;
            [JsonProperty("spawned")] public int Spawned;
            [JsonProperty("killed")] public int Killed;
            [JsonProperty("rng")] public uint Rng;
            [JsonProperty("exits")] public List<ExitDto> Exits = new();
            [JsonProperty("entrances")] public List<Vector3> Entrances = new();
        }

        public class ExitDto
        {
            /// <summary>둥지의 씬 경로(NestDto와 같은 안정 키). 런타임 UUID가 아니다 —
            /// 구운 둥지의 UUID는 세션마다 새로 나서, UUID로 실으면 새 세션 로드에서 출구가 전부 유실된다(v3에서 교체).</summary>
            [JsonProperty("nest")] public string Nest;
            [JsonProperty("point")] public int Point;
        }

        // ── 저장 ──────────────────────────────────────────────────────

        public object Capture()
        {
            var battle = BattleManager.Instance;
            var nests = Object.FindObjectsByType<NestView>(FindObjectsSortMode.None)
                              .Where(n => n != null)
                              .OrderBy(n => SaveScenePath.Of(n))
                              .ToList();

            if (battle == null && nests.Count == 0) return null;

            var dto = new Dto();

            foreach (var nest in nests)
            {
                var nd = new NestDto
                {
                    ScenePath = SaveScenePath.Of(nest),
                    IsDestroyed = nest.IsDestroyed,
                    HasWarned = nest.HasWarned,
                    HpMax = nest.Health.MaxHealth,
                    HpCurrent = nest.Health.CurrentHealth,
                };

                for (int i = 0; i < nest.Points.Count; i++)
                {
                    var sp = nest.Points[i];
                    var boss = sp.linkedBoss;
                    var state = nest.PointState(i);   // 파괴 여부·날짜의 정본은 심(NestModule)

                    nd.Points.Add(new PointDto
                    {
                        Index = i,
                        IsDestroyed = state != null && state.IsDestroyed,
                        Boss = boss != null && !boss.IsDead ? Describe(boss) : null,
                    });
                }

                dto.Nests.Add(nd);
            }

            if (battle != null)
            {
                var spawner = battle.Spawner;

                // 둥지에 매인 보스는 위에서 이미 적었으므로 여기서는 뺀다 (되살릴 때 두 번 나오면 안 된다)
                var bosses = new HashSet<MonsterView>(
                    nests.SelectMany(n => n.Points).Select(p => p.linkedBoss).Where(b => b != null));

                var waves = battle.Waves;
                foreach (var m in spawner.Monsters
                            .Where(m => m != null && !m.IsDead && !bosses.Contains(m))
                            .OrderBy(m => m.transform.position.x).ThenBy(m => m.transform.position.z))
                {
                    var d = Describe(m);
                    if (waves != null && m.Entity != null)
                        d.WaveKind = waves.BurstMonsters.Contains(m.Entity) ? "burst" : waves.TrickleMonsters.Contains(m.Entity) ? "trickle" : null;
                    dto.Monsters.Add(d);
                }

                if (waves != null)
                {
                    var s = waves.Capture();

                    // 심은 출구를 엔티티 UUID로 말한다 — 저장은 씬 경로로 번역한다(둥지의 안정 키).
                    var pathByUuid = new Dictionary<string, string>();
                    foreach (var n in nests)
                        if (n.Entity != null) pathByUuid[n.Entity.Id.ToString()] = SaveScenePath.Of(n);

                    dto.Wave = new WaveDto
                    {
                        Active = s.Active, Day = s.Day, Gate = s.Gate, Now = s.Now, Score = s.Score, Remaining = s.Remaining,
                        Bursts = s.Bursts, BurstsDone = s.BurstsDone, NextBurstAt = s.NextBurstAt, NextTrickleAt = s.NextTrickleAt,
                        Spawned = s.Spawned, Killed = s.Killed, Rng = s.Rng,
                        Exits = s.Selected.Select(x => new ExitDto
                        {
                            Nest = pathByUuid.TryGetValue(x.nest, out var path) ? path : Unmapped(x.nest),
                            Point = x.point,
                        }).ToList(),
                        Entrances = s.Entrances,
                    };
                }
            }

            return dto;
        }

        static string Unmapped(string uuid)
        {
            Debug.LogWarning($"[Save] 웨이브 출구의 둥지(uuid {uuid})가 씬의 NestView와 매칭되지 않습니다 — 그대로 싣지만 로드에서 버려질 수 있습니다.");
            return uuid;
        }

        static MonsterDto Describe(MonsterView m) => new()
        {
            DataId = m.Entity?.Def != null ? m.Entity.Def.Id : null,
            Position = m.transform.position,
            Rotation = m.transform.rotation,
            HpMax = m.Health.MaxHealth,
            HpCurrent = m.Health.CurrentHealth,
            IsBoss = m.IsBoss,
            HasBeenAttacked = m.HasBeenAttacked,
            IsNestDefender = m.IsNestDefender,
            DefendOrigin = m.DefendOrigin,
        };

        // ── 복원 ──────────────────────────────────────────────────────

        public void Restore(JToken data)
        {
            var dto = SaveJson.FromToken<Dto>(data);
            if (dto == null) return;

            RestoreNests(dto);
            RestoreWaveMonsters(dto);
        }

        static void RestoreNests(Dto dto)
        {
            var byPath = new Dictionary<string, NestView>();
            foreach (var n in Object.FindObjectsByType<NestView>(FindObjectsSortMode.None))
                if (n != null) byPath[SaveScenePath.Of(n)] = n;

            foreach (var saved in dto.Nests)
            {
                if (saved == null) continue;

                if (!byPath.TryGetValue(saved.ScenePath, out var nest))
                {
                    Debug.LogWarning($"[Save] 둥지 '{saved.ScenePath}' 를 씬에서 찾지 못했습니다.");
                    continue;
                }

                nest.RestoreSaveState(saved.IsDestroyed, saved.HasWarned);
                if (saved.HpMax > 0f)
                    nest.Health.RestoreState(saved.HpMax, saved.HpCurrent, saved.IsDestroyed);

                foreach (var p in saved.Points)
                {
                    if (p == null) continue;

                    nest.RestoreSpawnPoint(p.Index, p.IsDestroyed);

                    if (p.Boss == null) { nest.ClearBoss(p.Index); continue; }

                    var boss = nest.RestoreBoss(p.Index, p.Boss.Position, p.Boss.Rotation);
                    Apply(boss, p.Boss);
                }
            }
        }

        static void RestoreWaveMonsters(Dto dto)
        {
            var battle = BattleManager.Instance;
            if (battle == null) return;

            var spawner = battle.Spawner;
            var waves = battle.Waves;

            // 씬을 새로 열었다면 목록은 비어 있지만, 제자리 왕복 검증에서는 채워져 있다 — 심의 웨이브 몬스터도 함께
            waves?.EndNight();
            spawner.DespawnAll();

            // 웨이브 상태를 먼저 — 몬스터를 되살리며 명단에 다시 넣는다
            if (waves != null && dto.Wave != null)
            {
                var w = dto.Wave;
                var s = new CoreDawn.Sim.WaveSystem.State
                {
                    Active = w.Active, Day = w.Day, Gate = w.Gate, Now = w.Now, Score = w.Score, Remaining = w.Remaining,
                    Bursts = w.Bursts, BurstsDone = w.BurstsDone, NextBurstAt = w.NextBurstAt, NextTrickleAt = w.NextTrickleAt,
                    Spawned = w.Spawned, Killed = w.Killed, Rng = w.Rng,
                    Selected = w.Exits.Select(x => (x.Nest, x.Point)).ToList(), Entrances = w.Entrances,
                };
                // 출구는 씬 경로로 실려 있다(저장 쪽 번역) — 이 세션의 둥지 엔티티로 되돌린다
                var nestsByPath = new Dictionary<string, CoreDawn.Sim.Entity>();
                foreach (var n in Object.FindObjectsByType<NestView>(FindObjectsSortMode.None))
                    if (n != null && n.Entity != null) nestsByPath[SaveScenePath.Of(n)] = n.Entity;
                waves.Restore(s, path =>
                {
                    if (nestsByPath.TryGetValue(path, out var e)) return e;
                    Debug.LogWarning($"[Save] 웨이브 출구의 둥지 '{path}'를 씬에서 찾지 못했습니다 — 그 출구는 버립니다.");
                    return null;
                });
            }

            var db = CoreDawn.Sim.SimHost.Database;
            foreach (var saved in dto.Monsters)
            {
                if (saved == null) continue;
                // 저장된 종류(팩 id)로 되살린다 — 없는 id는 조용히 다른 종류로 바꾸지 않고 소리 내고 건너뛴다
                var def = db != null && !string.IsNullOrEmpty(saved.DataId) ? db.Entity(saved.DataId) : null;
                if (def == null)
                {
                    Debug.LogWarning($"[Save] 몬스터 정의 '{saved.DataId}'가 팩에 없습니다 — 그 몬스터는 복원되지 않습니다.");
                    continue;
                }
                var view = spawner.RestoreMonster(saved.Position, saved.Rotation, def);
                Apply(view, saved);
                if (waves != null && view != null && view.Entity != null && !string.IsNullOrEmpty(saved.WaveKind))
                    waves.Register(view.Entity, saved.WaveKind == "trickle" ? CoreDawn.Sim.WaveSpawnKind.Trickle : CoreDawn.Sim.WaveSpawnKind.Burst);
            }
            if (waves != null && dto.Wave != null) waves.FinishRestore(dto.Wave.Spawned, dto.Wave.Killed);
        }

        static void Apply(MonsterView m, MonsterDto saved)
        {
            if (m == null || saved == null) return;

            m.RestoreSaveState(saved.IsBoss, saved.HasBeenAttacked, saved.IsNestDefender, saved.DefendOrigin);
            if (saved.HpMax > 0f) m.Health.RestoreState(saved.HpMax, saved.HpCurrent, isDead: false);
        }
    }
}
