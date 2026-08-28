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
            [JsonProperty("destroyedDay")] public int DestroyedDay;
            [JsonProperty("warned")] public bool HasWarned;
            [JsonProperty("hpMax")] public float HpMax;
            [JsonProperty("hpCur")] public float HpCurrent;
            [JsonProperty("points")] public List<PointDto> Points = new();
        }

        public class PointDto
        {
            [JsonProperty("i")] public int Index;
            [JsonProperty("destroyed")] public bool IsDestroyed;
            [JsonProperty("destroyedDay")] public int DestroyedDay;

            /// <summary>null이면 저장 당시 이 자리에 살아있는 보스가 없었다는 뜻.</summary>
            [JsonProperty("boss")] public MonsterDto Boss;
        }

        public class MonsterDto
        {
            /// <summary>종류(MonsterDataSO.Id, 예 "Monster:Spitter"). 없거나 모르는 id면 데이터베이스의 기본 종류로 세운다 — 옛 세이브 호환.</summary>
            [JsonProperty("data")] public string DataId;
            [JsonProperty("pos")] public Vector3 Position;
            [JsonProperty("rot")] public Quaternion Rotation;
            [JsonProperty("hpMax")] public float HpMax;
            [JsonProperty("hpCur")] public float HpCurrent;
            [JsonProperty("boss")] public bool IsBoss;
            [JsonProperty("awake")] public bool HasBeenAttacked;
            [JsonProperty("defender")] public bool IsNestDefender;
            [JsonProperty("defendOrigin")] public Vector3 DefendOrigin;
        }

        public class WaveDto
        {
            [JsonProperty("enabled")] public bool SpawningEnabled;

            /// <summary>다음 스폰까지 남은 시간(초) — 절대 시각은 다음 실행에서 뜻이 없다.</summary>
            [JsonProperty("nextIn")] public float NextSpawnDelay;
        }

        // ── 저장 ──────────────────────────────────────────────────────

        public object Capture()
        {
            var battle = BattleManager.Instance;
            var nests = Object.FindObjectsByType<MonsterNest>(FindObjectsSortMode.None)
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
                    DestroyedDay = nest.DestroyedDay,
                    HasWarned = nest.HasWarned,
                    HpMax = nest.Health.MaxHealth,
                    HpCurrent = nest.Health.CurrentHealth,
                };

                for (int i = 0; i < nest.Points.Count; i++)
                {
                    var sp = nest.Points[i];
                    var boss = sp.linkedBoss;

                    nd.Points.Add(new PointDto
                    {
                        Index = i,
                        IsDestroyed = sp.isDestroyed,
                        DestroyedDay = sp.destroyedDay,
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

                foreach (var m in spawner.Monsters
                            .Where(m => m != null && !m.IsDead && !bosses.Contains(m))
                            .OrderBy(m => m.transform.position.x).ThenBy(m => m.transform.position.z))
                    dto.Monsters.Add(Describe(m));

                dto.Wave = new WaveDto
                {
                    SpawningEnabled = spawner.SpawningEnabled,
                    NextSpawnDelay = spawner.NextSpawnDelay,
                };
            }

            return dto;
        }

        static MonsterDto Describe(MonsterView m) => new()
        {
            DataId = m.Data != null ? m.Data.Id : null,
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
            var byPath = new Dictionary<string, MonsterNest>();
            foreach (var n in Object.FindObjectsByType<MonsterNest>(FindObjectsSortMode.None))
                if (n != null) byPath[SaveScenePath.Of(n)] = n;

            foreach (var saved in dto.Nests)
            {
                if (saved == null) continue;

                if (!byPath.TryGetValue(saved.ScenePath, out var nest))
                {
                    Debug.LogWarning($"[Save] 둥지 '{saved.ScenePath}' 를 씬에서 찾지 못했습니다.");
                    continue;
                }

                nest.RestoreSaveState(saved.IsDestroyed, saved.DestroyedDay, saved.HasWarned);
                if (saved.HpMax > 0f)
                    nest.Health.RestoreState(saved.HpMax, saved.HpCurrent, saved.IsDestroyed);

                foreach (var p in saved.Points)
                {
                    if (p == null) continue;

                    nest.RestoreSpawnPoint(p.Index, p.IsDestroyed, p.DestroyedDay);

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

            // 씬을 새로 열었다면 목록은 비어 있지만, 제자리 왕복 검증에서는 채워져 있다
            spawner.DespawnAll();

            var database = MonsterDatabaseSO.LoadDefault();
            foreach (var saved in dto.Monsters)
            {
                if (saved == null) continue;
                // 저장된 종류로 되살린다. id가 없거나(옛 세이브) 사라진 종류면 null → 스포너가 기본 종류로 세운다
                var data = database != null && !string.IsNullOrEmpty(saved.DataId) ? database.FindById(saved.DataId) : null;
                Apply(spawner.RestoreMonster(saved.Position, saved.Rotation, data), saved);
            }

            if (dto.Wave != null) spawner.RestoreState(dto.Wave.SpawningEnabled, dto.Wave.NextSpawnDelay);
        }

        static void Apply(MonsterView m, MonsterDto saved)
        {
            if (m == null || saved == null) return;

            m.RestoreSaveState(saved.IsBoss, saved.HasBeenAttacked, saved.IsNestDefender, saved.DefendOrigin);
            if (saved.HpMax > 0f) m.Health.RestoreState(saved.HpMax, saved.HpCurrent, isDead: false);
        }
    }
}
