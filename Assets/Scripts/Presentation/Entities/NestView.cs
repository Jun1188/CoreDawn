using UnityEngine;
using System.Collections.Generic;
using CoreDawn.Combat;
using CoreDawn.Navigation;
using CoreDawn.Save;
using CoreDawn.Sim;
using CoreDawn.Data;
using CoreDawn.UI;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 둥지의 뷰. 상태(스폰 포인트의 파괴/복구·파괴된 날·무적 규칙·보스 사망 감지)는 심 <see cref="NestModule"/>의 것이고,
    /// 여기는 ① 포인트의 자리(Transform)와 보스 프리팹 세우기(모듈의 BossNeeded에 답한다), ② 외형 켜고 끄기, ③ 주야 이벤트를
    /// 모듈에 전달, ④ 낮 방어 스폰의 시점·자리 판정(플레이어 거리·화면 가림 — PhysX라 아직 뷰)만 맡는다.
    /// 둥지의 핵(indestructibleVisuals)은 파괴 시에도 남아 스폰 포인트 역할을 한다.
    /// </summary>
    public class NestView : EntityView
    {
        [System.Serializable]
        public class NestSpawnPoint
        {
            public Transform point;
            public MonsterView linkedBoss;
            [Tooltip("이 포인트에 서는 보스의 종류(MonsterDataSO). 비우면 보스 없음. 프리팹·HP·공격은 전부 데이터가 정한다.")]
            public MonsterDataSO bossData;
        }

        /// <summary>낮 방어 스폰 한 자리. 종류·HP는 둥지의 defenderMonster(데이터)가 정하므로 위치뿐이다.</summary>
        public readonly struct DefenderSpawnSlot
        {
            public readonly Vector3 position;
            public DefenderSpawnSlot(Vector3 position) => this.position = position;
        }

        [Header("Nest Settings")]
        [Tooltip("둥지에서 몬스터가 생성될 위치들 및 연동 보스")]
        public List<NestSpawnPoint> spawnPoints;

        [Tooltip("파괴 불가능한 코어(핵) 등 파괴 시에도 유지될 오브젝트들")]
        public GameObject[] indestructibleVisuals;

        [Tooltip("파괴 시 꺼질 외형(구조물 및 콜라이더 포함) 오브젝트들")]
        public GameObject[] destructibleVisuals;

        [Tooltip("둥지의 건물 데이터 — 표현 에셋. 규칙(칸·공격 가능)은 팩 정의(Building 모듈)가 정한다.")]
        [SerializeField] private NestDataSO data;

        [Tooltip("낮 방어 몬스터·보스전 지원군의 종류. 비우면 MonsterDatabase의 기본 종류.")]
        [SerializeField] private MonsterDataSO defenderMonster;

        /// <summary>방어자 종류 — WaveSpawnManager.SpawnNestDefenders가 읽는다. null = DB 기본.</summary>
        public MonsterDataSO DefenderData => defenderMonster;

        public void SetData(NestDataSO nestData) => data = nestData;

        [Header("Defense Settings")]
        [Tooltip("플레이어 접근 경고 반경")]
        public float warningRange = 25f;
        [Tooltip("낮이라도 웨이브 몬스터가 방어하러 튀어나오는 진입 반경")]
        public float triggerRange = 15f;
        [Tooltip("방어 몬스터 한 번 스폰 시 마리 수")]
        public int defenseSpawnAmount = 3;
        [Tooltip("방어 몬스터 스폰 쿨타임")]
        public float defenseSpawnCooldown = 10f;
        [Tooltip("보스가 교전 중일 때의 지원군 스폰 쿨타임 — 평시보다 짧아야 압박이 이어진다.")]
        public float bossFightSpawnCooldown = 6f;

        [Header("Day Spawn Culling")]
        [Tooltip("스폰 금지 반경(m) — 플레이어가 이 거리 안이면 그 포인트는 스폰하지 않는다. NestEngagementZone이 있으면 그쪽 값이 우선한다.")]
        [SerializeField] private float daySpawnMinRange = 2f;
        [Tooltip("스폰 시작 반경(m) — 플레이어가 포인트에서 이 거리 안이면 스폰한다(화면에 보이는 동안은 멈춤). NestEngagementZone이 있으면 그쪽 값이 우선한다.")]
        [SerializeField] private float daySpawnMaxRange = 15f;

        private bool hasWarned;
        private float lastDefenseSpawnTime = float.NegativeInfinity;
        private NestEngagementZone engagementZone;
        private PlayerView cachedPlayer;
        private float nextPlayerSearch;
        private bool subscribed;

        /// <summary>심 둥지 모듈 — 상태의 정본. 심이 아직 안 붙었으면 null.</summary>
        public NestModule Module => Entity?.Get<NestModule>();

        public bool IsDestroyed => Module != null && Module.IsDestroyed;
        public bool HasWarned => hasWarned;

        public IReadOnlyList<NestSpawnPoint> Points =>
            spawnPoints ?? (IReadOnlyList<NestSpawnPoint>)System.Array.Empty<NestSpawnPoint>();

        /// <summary>자리 i의 심 상태. 심이 없거나 자리가 없으면 null.</summary>
        public NestPoint PointState(int i)
        {
            var m = Module;
            return m != null && i >= 0 && i < m.Points.Count ? m.Points[i] : null;
        }

        bool IsPointDestroyed(int i) { var p = PointState(i); return p != null && p.IsDestroyed; }

        private float SensorRange => Mathf.Max(warningRange, Mathf.Max(triggerRange,
            engagementZone != null ? engagementZone.MaximumRange : daySpawnMaxRange)) + MaxSpawnPointOffset;

        private PlayerView FindPlayer()
        {
            if (cachedPlayer != null && cachedPlayer.IsValidTarget()) return cachedPlayer;
            if (Time.time < nextPlayerSearch) return null;
            nextPlayerSearch = Time.time + 1f;
            cachedPlayer = FindFirstObjectByType<PlayerView>();
            return cachedPlayer != null && cachedPlayer.IsValidTarget() ? cachedPlayer : null;
        }

        private bool HasConfiguredSpawnPoints
        {
            get
            {
                if (spawnPoints != null)
                    foreach (var sp in spawnPoints)
                        if (sp != null && sp.point != null) return true;
                return false;
            }
        }

        private bool HasLiveSpawnPoint
        {
            get
            {
                if (spawnPoints != null)
                    for (int i = 0; i < spawnPoints.Count; i++)
                        if (spawnPoints[i] != null && spawnPoints[i].point != null && !IsPointDestroyed(i)) return true;
                return false;
            }
        }

        private float MaxSpawnPointOffset
        {
            get
            {
                float max = 0f;
                if (spawnPoints != null)
                    foreach (var sp in spawnPoints)
                        if (sp != null && sp.point != null)
                            max = Mathf.Max(max, Vector3.Distance(transform.position, sp.point.position));
                return max;
            }
        }

        private Vector3 NearestSpawnAnchor(Vector3 playerPos)
        {
            Vector3 best = transform.position;
            float bestDist = Vector3.Distance(best, playerPos);
            if (spawnPoints != null)
                for (int i = 0; i < spawnPoints.Count; i++)
                {
                    var sp = spawnPoints[i];
                    if (sp == null || sp.point == null || IsPointDestroyed(i)) continue;
                    float d = Vector3.Distance(sp.point.position, playerPos);
                    if (d < bestDist) { bestDist = d; best = sp.point.position; }
                }
            return best;
        }

        /// <summary>지금 플레이어와 교전 중인(각성한) 보스. 없으면 null.</summary>
        public MonsterView EngagedBoss
        {
            get
            {
                if (spawnPoints == null) return null;
                foreach (var sp in spawnPoints)
                {
                    if (sp == null || sp.linkedBoss == null) continue;
                    var boss = sp.linkedBoss;
                    if (!boss.IsDead && boss.IsBoss && boss.HasBeenAttacked) return boss;
                }
                return null;
            }
        }

        private List<DefenderSpawnSlot> GetBossReinforcementSlots(MonsterView boss, PlayerView player)
        {
            var result = new List<DefenderSpawnSlot>();
            if (spawnPoints == null || boss == null) return result;

            Vector3 bossPos = boss.transform.position;
            Vector3 playerPos = player != null ? player.transform.position : bossPos;
            Camera eye = Camera.main;

            NestSpawnPoint bestHidden = null; float bestHiddenDist = float.MaxValue;
            NestSpawnPoint farthestVisible = null; float farthestVisibleDist = -1f;

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                var sp = spawnPoints[i];
                if (sp == null || sp.point == null || IsPointDestroyed(i)) continue;
                Vector3 pos = sp.point.position;
                if (player == null || !IsOnPlayerScreen(pos, player, eye))
                {
                    float d = Vector3.Distance(pos, bossPos);
                    if (d < bestHiddenDist) { bestHiddenDist = d; bestHidden = sp; }
                }
                else
                {
                    float d = Vector3.Distance(pos, playerPos);
                    if (d > farthestVisibleDist) { farthestVisibleDist = d; farthestVisible = sp; }
                }
            }

            var chosen = bestHidden ?? farthestVisible;
            if (chosen != null) result.Add(new DefenderSpawnSlot(chosen.point.position));
            return result;
        }

        /// <summary>맵 데이터로 세울 때 쓴다. 0 이하는 "프리팹/정의 값을 그대로 둔다".</summary>
        public void Configure(float warning, float trigger, int defenseAmount, float defenseCooldown)
        {
            if (warning > 0f) warningRange = warning;
            if (trigger > 0f) { triggerRange = trigger; daySpawnMaxRange = trigger; }
            if (defenseAmount > 0) defenseSpawnAmount = defenseAmount;
            if (defenseCooldown > 0f) defenseSpawnCooldown = defenseCooldown;
            SyncModule();
        }

        /// <summary>
        /// 포인트의 자리·보스 유무를 심 모듈에 밀어 넣는다 — 포인트 목록이 바뀔 때마다(맵 배치·프리팹).
        /// 자리의 정본은 아직 뷰(Transform)다: 맵이 정한 오프셋을 WorldPopulator가 Transform으로 세운다.
        /// </summary>
        public void SyncModule()
        {
            var m = Module;
            if (m == null) return;
            var list = new List<(Vector3, bool)>();
            if (spawnPoints != null)
                foreach (var sp in spawnPoints)
                    list.Add((sp != null && sp.point != null ? sp.point.position : transform.position, sp != null && sp.bossData != null));
            m.ConfigurePoints(list);
        }

        protected override void Awake()
        {
            base.Awake();
            SetDeathBehavior(destroy: false, delay: 0f);
        }

        protected override void OnEntityAttached()
        {
            base.OnEntityAttached();
            var m = Module;
            if (m == null)
            {
                Debug.LogError("[NestView] 심 엔티티에 Nest 모듈이 없습니다 — 팩 정의(entities/nest)의 Nest 모듈을 확인하세요.", this);
                return;
            }
            SyncModule();
            if (!subscribed)
            {
                m.BossNeeded += OnBossNeeded;
                m.PointDestroyed += OnPointDestroyed;
                m.Destroyed += OnNestDestroyed;
                subscribed = true;
            }
        }

        protected override void Start()
        {
            base.Start();
            if (Entity == null)
            {
                Debug.LogWarning("[NestView] 심 엔티티 없이 시작했습니다 — WorldPopulator를 거치지 않은 둥지. 팩 정의로 직접 세웁니다.", this);
                var def = SimHost.Database?.Entity("coredawn:entity/nest");
                var e = SimHost.World.Create(Faction.Monster, transform.position);
                if (def != null) def.Assemble(e);
                else { e.Add(new HealthModule(Mathf.Max(1f, data != null ? data.maxHp : 500f))); e.Add(new EffectsModule()); }
                AttachEntity(e);
            }

            // 교전 구역은 Awake가 아니라 여기서 — WorldPopulator가 Instantiate 뒤에 AddComponent한다
            engagementZone = GetComponent<NestEngagementZone>();

            // 보스는 낮 던전의 고정 전투 대상 — 시작 때 세운다(세이브 복원 중이면 저장된 보스가 곧 되살아나므로 만들지 않는다). 복구는 없다
            EnsureBossesSpawned();

            Transform core = indestructibleVisuals != null && indestructibleVisuals.Length > 0 && indestructibleVisuals[0] != null
                ? indestructibleVisuals[0].transform : transform;
            WorldHealthBar.Attach(this, core, large: true);
        }

        protected override void OnDestroy()
        {
            var e = Entity;
            var m = Module;
            if (m != null && subscribed)
            {
                m.BossNeeded -= OnBossNeeded;
                m.PointDestroyed -= OnPointDestroyed;
                m.Destroyed -= OnNestDestroyed;
                subscribed = false;
            }
            base.OnDestroy();
            if (e != null && !e.IsRemoved && !ApplicationQuitting) SimHost.World.Remove(e);
        }

        // ── 심 → 뷰 ──────────────────────────────────────────────

        private void OnBossNeeded(int index)
        {
            if (SaveLoadContext.IsRestoring) return;
            if (spawnPoints == null || index < 0 || index >= spawnPoints.Count) return;
            var sp = spawnPoints[index];
            if (sp == null || sp.point == null || sp.bossData == null) return;
            if (sp.linkedBoss != null && !sp.linkedBoss.IsDead) { Module?.BindBoss(index, sp.linkedBoss.Entity); return; }
            SpawnBossAtPoint(index, sp);
        }

        private void OnPointDestroyed(int index)
        {
            if (spawnPoints != null && index < spawnPoints.Count && spawnPoints[index]?.point != null)
                spawnPoints[index].point.gameObject.SetActive(false);
            Debug.Log($"[NestView] 보스가 죽어 스폰 포인트 {index + 1}이 영구히 비활성화됐습니다.");
        }

        private void OnNestDestroyed()
        {
            SetVisualsDestroyed(true);
            Debug.Log("[NestView] 둥지가 파괴되었습니다 — 다시 서지 않는다. 이 둥지에서는 웨이브가 나오지 않고, 남은 둥지가 자극된다.");
        }

        private void SetVisualsDestroyed(bool destroyed)
        {
            if (destructibleVisuals == null) return;
            foreach (var go in destructibleVisuals)
                if (go != null) go.SetActive(!destroyed);
        }

        // 드롭(괴수핵)은 정의의 Loot 모듈 — 심의 Died를 듣는 LootSpawner가 뿌린다. 파괴 상태는 모듈이 Died에서 정한다.
        protected override void HandleDeath() { }

        // ── 보스 세우기 (프리팹은 아직 뷰의 것) ──────────────────────

        private void EnsureBossesSpawned()
        {
            if (SaveLoadContext.IsRestoring) return;
            Module?.RequestMissingBosses();
        }

        private void SpawnBossAtPoint(int index, NestSpawnPoint spawnPoint)
        {
            var boss = MonsterSpawner.Spawn(spawnPoint.bossData != null ? spawnPoint.bossData.Def : null, spawnPoint.point.position, spawnPoint.point.rotation, transform);
            SnapBossToGround(boss.gameObject);
            spawnPoint.linkedBoss = boss;
            boss.SetAsBoss(engagementZone);
            Module?.BindBoss(index, boss.Entity);
            Debug.Log($"[NestView] 보스를 지정 스폰 포인트에 배치했습니다: {spawnPoint.point.name}");
        }

        private void SnapBossToGround(GameObject boss)
        {
            if (GridManager.Instance == null) return;
            float surfaceY = GridManager.Instance.SurfaceY;
            var col = boss.GetComponentInChildren<Collider>();
            if (col != null)
            {
                UnityEngine.Physics.SyncTransforms();
                float bottom = col.bounds.min.y;
                boss.transform.position += Vector3.up * (surfaceY - bottom + 0.02f);
            }
            else
            {
                var pos = boss.transform.position;
                pos.y = surfaceY;
                boss.transform.position = pos;
            }
        }

        // ── 낮 방어 스폰 판정 (뷰: 플레이어 거리·화면 가림) ───────────

        protected override void Update()
        {
            base.Update();
            if (IsDestroyed) return;

            PlayerView player = FindPlayer();

            if (player != null && !player.IsDead)
            {
                MonsterView engagedBoss = EngagedBoss;
                if (engagedBoss != null)
                {
                    hasWarned = false;
                    if (Time.time >= lastDefenseSpawnTime + bossFightSpawnCooldown)
                    {
                        lastDefenseSpawnTime = Time.time;
                        if (BattleManager.Instance != null && BattleManager.Instance.Spawner != null)
                            BattleManager.Instance.Spawner.SpawnNestDefenders(this, player, defenseSpawnAmount,
                                GetBossReinforcementSlots(engagedBoss, player), engagedBoss);
                    }
                    return;
                }
            }

            if (player != null && !player.IsDead)
            {
                Vector3 anchor = NearestSpawnAnchor(player.transform.position);
                float dist = Vector3.Distance(anchor, player.transform.position);

                if (dist <= SensorRange)
                {
                    List<DefenderSpawnSlot> spawnable = null;
                    bool canSpawn;
                    if (engagementZone != null)
                        canSpawn = (!HasConfiguredSpawnPoints || HasLiveSpawnPoint)
                                   && engagementZone.CanSpawnFor(anchor, player.transform.position);
                    else
                    {
                        spawnable = GetDaySpawnableSlots(player);
                        canSpawn = spawnable.Count > 0;
                    }

                    if (canSpawn)
                    {
                        EnsureBossesSpawned();
                        if (Time.time >= lastDefenseSpawnTime + defenseSpawnCooldown)
                        {
                            lastDefenseSpawnTime = Time.time;
                            if (BattleManager.Instance != null && BattleManager.Instance.Spawner != null)
                                BattleManager.Instance.Spawner.SpawnNestDefenders(this, player, defenseSpawnAmount, spawnable);
                        }
                    }
                    else if ((engagementZone == null || engagementZone.IsActivePhase) && dist <= warningRange)
                    {
                        if (!hasWarned)
                        {
                            hasWarned = true;
                            Debug.Log("[NestView] 둥지 근처에 접근했습니다! 조심하세요.");
                        }
                    }
                    else hasWarned = false;
                }
                else hasWarned = false;
            }
            else hasWarned = false;
        }

        // ── 세이브 복원 표면 ─────────────────────────────────────

        /// <summary>세이브 복원 전용 — 둥지 자체의 파괴 상태와 외형을 되돌린다.</summary>
        public void RestoreSaveState(bool isDestroyed, bool warned)
        {
            Module?.RestoreState(isDestroyed);
            hasWarned = warned;
            SetVisualsDestroyed(isDestroyed);
        }

        /// <summary>세이브 복원 전용 — 스폰 포인트 한 곳의 파괴 상태를 되돌린다.</summary>
        public void RestoreSpawnPoint(int index, bool destroyed)
        {
            Module?.RestorePoint(index, destroyed);
            if (spawnPoints != null && index >= 0 && index < spawnPoints.Count && spawnPoints[index]?.point != null)
                spawnPoints[index].point.gameObject.SetActive(!destroyed);
        }

        /// <summary>세이브 복원 전용 — 스폰 포인트의 보스를 저장된 위치에 되살린다.</summary>
        public MonsterView RestoreBoss(int index, Vector3 position, Quaternion rotation)
        {
            if (spawnPoints == null || index < 0 || index >= spawnPoints.Count) return null;
            var sp = spawnPoints[index];
            if (sp.bossData == null || sp.bossData.Def == null) return null;

            if (sp.linkedBoss != null) Destroy(sp.linkedBoss.gameObject);
            var restored = MonsterSpawner.Spawn(sp.bossData.Def, position, rotation, transform);
            sp.linkedBoss = restored;
            sp.linkedBoss?.SetAsBoss(engagementZone);
            Module?.BindBoss(index, restored?.Entity);
            return sp.linkedBoss;
        }

        /// <summary>세이브 복원 전용 — 스폰 포인트에 붙어 있는 보스를 치운다 (저장 당시 죽어 있던 경우).</summary>
        public void ClearBoss(int index)
        {
            if (spawnPoints == null || index < 0 || index >= spawnPoints.Count) return;
            var sp = spawnPoints[index];
            if (sp.linkedBoss != null) Destroy(sp.linkedBoss.gameObject);
            sp.linkedBoss = null;
            Module?.ClearBoss(index);
        }

        // ── 낮 방어 스폰 자리 ────────────────────────────────────

        public List<DefenderSpawnSlot> GetDaySpawnableSlots(PlayerView player)
        {
            var result = new List<DefenderSpawnSlot>();
            if (player == null) return result;
            Vector3 playerPos = player.transform.position;
            Camera eye = Camera.main;

            if (spawnPoints != null)
                for (int i = 0; i < spawnPoints.Count; i++)
                {
                    var sp = spawnPoints[i];
                    if (sp == null || sp.point == null || IsPointDestroyed(i)) continue;
                    Vector3 pos = sp.point.position;
                    float d = Vector3.Distance(pos, playerPos);
                    if (d > daySpawnMaxRange || d <= daySpawnMinRange) continue;
                    if (IsOnPlayerScreen(pos, player, eye)) continue;
                    result.Add(new DefenderSpawnSlot(pos));
                }

            if (!HasConfiguredSpawnPoints)
            {
                float d = Vector3.Distance(transform.position, playerPos);
                if (d <= daySpawnMaxRange && d > daySpawnMinRange && !IsOnPlayerScreen(transform.position, player, eye))
                    result.Add(new DefenderSpawnSlot(transform.position));
            }
            return result;
        }

        private static bool IsOnPlayerScreen(Vector3 spawnPos, PlayerView player, Camera eye)
        {
            Vector3 probe = spawnPos + Vector3.up * 1.2f;
            if (eye != null)
            {
                Vector3 vp = eye.WorldToViewportPoint(probe);
                if (vp.z <= 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) return false;
            }
            Vector3 head = player.transform.position + Vector3.up * 1.6f;
            Vector3 dir = head - probe;
            float dist = dir.magnitude;
            if (dist <= 0.5f) return true;
            int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Monster", "Player", "Character");
            return !Physics.Raycast(probe, dir / dist, dist - 0.3f, mask);
        }

        public List<DefenderSpawnSlot> GetAllActiveDefenderSlots()
        {
            var slots = new List<DefenderSpawnSlot>();
            if (spawnPoints != null)
                for (int i = 0; i < spawnPoints.Count; i++)
                {
                    var sp = spawnPoints[i];
                    if (sp == null || sp.point == null || IsPointDestroyed(i)) continue;
                    slots.Add(new DefenderSpawnSlot(sp.point.position));
                }
            if (slots.Count == 0) slots.Add(new DefenderSpawnSlot(transform.position));
            return slots;
        }

        public List<Vector3> GetAllActiveSpawnPositions()
        {
            var positions = new List<Vector3>();
            if (spawnPoints != null)
                for (int i = 0; i < spawnPoints.Count; i++)
                {
                    var sp = spawnPoints[i];
                    if (sp == null || sp.point == null || IsPointDestroyed(i)) continue;
                    positions.Add(sp.point.position);
                }
            if (positions.Count == 0) positions.Add(transform.position);
            return positions;
        }

        public bool TryGetSpawnPosition(out Vector3 position)
        {
            var positions = GetAllActiveSpawnPositions();
            position = positions[UnityEngine.Random.Range(0, positions.Count)];
            return true;
        }
    }
}
