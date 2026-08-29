using UnityEngine;
using System.Collections.Generic;
using CoreDawn.Combat;
using CoreDawn.DayTime;
using CoreDawn.Factory;
using CoreDawn.Interaction;
using CoreDawn.Navigation;
using CoreDawn.Save;
using CoreDawn.Worlds;
using CoreDawn.Sim;
using CoreDawn.Data;
using CoreDawn.UI;

namespace CoreDawn.Entities
{
    // 둥지(MonsterNest)는 파괴 가능한 Entity이나,
    // 둥지의 핵(NestCore) 부분은 파괴 불가하며 스폰 포인트 역할을 한다.
    public class MonsterNest : EntityView
    {
        [System.Serializable]
        public class NestSpawnPoint
        {
            public Transform point;
            public MonsterView linkedBoss;
            [Tooltip("이 포인트에 서는 보스의 종류(MonsterDataSO). 비우면 보스 없음. 프리팹·HP·공격은 전부 데이터가 정한다.")]
            public MonsterDataSO bossData;
            [HideInInspector] public bool isDestroyed = false;
            [HideInInspector] public int destroyedDay = -1;
        }

        /// <summary>낮 방어 스폰 한 자리. 종류·HP는 둥지의 defenderMonster(데이터)가 정하므로 위치뿐이다.</summary>
        public readonly struct DefenderSpawnSlot
        {
            public readonly Vector3 position;

            public DefenderSpawnSlot(Vector3 position) => this.position = position;
        }

        [Header("Nest Settings")]
        [Tooltip("둥지 파괴 시 드롭되는 아이템. 비워두면 기본적으로 괴수핵(Item:BeastCore) 드롭.")]
        public ItemDataSO dropItem;

        [Tooltip("둥지에서 몬스터가 생성될 위치들 및 연동 보스")]
        public System.Collections.Generic.List<NestSpawnPoint> spawnPoints;

        [Tooltip("파괴 불가능한 코어(핵) 등 파괴 시에도 유지될 오브젝트들")]
        public GameObject[] indestructibleVisuals;

        [Tooltip("파괴 시 꺼질 외형(구조물 및 콜라이더 포함) 오브젝트들")]
        public GameObject[] destructibleVisuals;

        [Tooltip("둥지의 건물 데이터 — 차지하는 칸과 파괴 규칙(철거 가능·공격 가능)의 출처. " +
                 "WorldPopulator가 BuildingDatabase에서 찾아 꽂는다.")]
        [SerializeField] private NestDataSO data;

        [Tooltip("낮 방어 몬스터·보스전 지원군의 종류. 비우면 MonsterDatabase의 기본 종류.")]
        [SerializeField] private MonsterDataSO defenderMonster;

        /// <summary>방어자 종류 — WaveSpawnManager.SpawnNestDefenders가 읽는다. null = DB 기본.</summary>
        public MonsterDataSO DefenderData => defenderMonster;

        /// <summary>둥지의 건물 데이터를 꽂는다 (WorldPopulator 전용).</summary>
        public void SetData(NestDataSO nestData) => data = nestData;

        [Header("Recovery Settings")]
        [Tooltip("보스 파괴 후 복구되는 기간(일)")]
        [SerializeField] private int bossRecoveryDays = 2;
        [Tooltip("둥지 파괴 후 복구되는 기간(일)")]
        [SerializeField] private int nestRecoveryDays = 3;

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
        [Tooltip("스폰 금지 반경(m) — 플레이어가 이 거리 안이면 보이든 안 보이든 그 포인트는 스폰하지 않는다 " +
                 "(플레이어와 겹쳐 태어나는 것 방지). NestEngagementZone이 붙어 있으면 그쪽 값이 우선한다.")]
        [SerializeField] private float daySpawnMinRange = 2f;
        [Tooltip("스폰 시작 반경(m) — 플레이어가 스폰 포인트에서 이 거리 안으로 들어오면 " +
                 "그 포인트가 낮 방어 스폰을 시작한다. 단, 포인트가 플레이어 화면에 실제로 보이는 동안" +
                 "(시야각 안 + 가림 없음)은 팝인 방지를 위해 그 포인트만 멈춘다. " +
                 "NestEngagementZone이 붙어 있으면 그쪽 값이 우선한다.")]
        [SerializeField] private float daySpawnMaxRange = 15f;

        private bool hasWarned;
        // 음의 무한대 — 플레이어가 스폰 범위에 처음 들어오면 쿨타임 없이 즉시 첫 스폰이 나가야 한다
        // (0으로 두면 플레이 시작 후 쿨타임만큼은 범위에 들어와도 스폰이 안 나간다).
        private float lastDefenseSpawnTime = float.NegativeInfinity;
        private NestEngagementZone engagementZone;

        // 둥지마다 OverlapSphere를 돌리지 않는다 — 플레이어는 하나뿐이라 참조를 캐시해 두고
        // 거리 계산(산술)만으로 감지한다. Monster가 "플레이어가 몬스터를 찾아 콜백"으로
        // 개체별 물리 쿼리를 없앤 것과 같은 방향의 최적화다.
        private PlayerView cachedPlayer;
        private float nextPlayerSearch;   // 플레이어 사망·교체 대비 저빈도 재탐색 타이머

        /// <summary>
        /// 감지는 경고·스폰에 쓰는 모든 반경을 덮어야 한다 — 감지가 좁으면 스폰 판정 자체가 늦는다.
        /// 스폰 포인트는 둥지 중심에서 수십 m 떨어질 수 있으므로(현재 맵은 cellSize 4에
        /// 오프셋 최대 9칸 = 36m), 그 거리만큼 감지를 넓혀야 포인트 근처의 플레이어를 놓치지 않는다.
        /// 칸 크기는 씬마다 다르니 상수로 적지 말고 <see cref="MaxSpawnPointOffset"/>로 실측할 것.
        /// </summary>
        private float SensorRange => Mathf.Max(warningRange, Mathf.Max(triggerRange,
            engagementZone != null ? engagementZone.MaximumRange : daySpawnMaxRange)) + MaxSpawnPointOffset;

        /// <summary>유효한 플레이어 참조. 캐시가 죽으면 1초에 한 번만 다시 찾는다.</summary>
        private PlayerView FindPlayer()
        {
            if (cachedPlayer != null && cachedPlayer.IsValidTarget()) return cachedPlayer;
            if (Time.time < nextPlayerSearch) return null;

            nextPlayerSearch = Time.time + 1f;
            cachedPlayer = FindFirstObjectByType<PlayerView>();
            return cachedPlayer != null && cachedPlayer.IsValidTarget() ? cachedPlayer : null;
        }

        /// <summary>맵·에디터가 배선한 스폰 포인트가 하나라도 있는가 — 파괴 여부와 무관.</summary>
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

        /// <summary>파괴되지 않은 스폰 포인트가 남아 있는가.</summary>
        private bool HasLiveSpawnPoint
        {
            get
            {
                if (spawnPoints != null)
                    foreach (var sp in spawnPoints)
                        if (sp != null && !sp.isDestroyed && sp.point != null) return true;
                return false;
            }
        }

        /// <summary>스폰 포인트가 둥지 중심에서 가장 멀리 떨어진 거리.</summary>
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

        /// <summary>
        /// 플레이어와 가장 가까운 스폰 앵커(둥지 중심 또는 살아 있는 스폰 포인트)의 위치.
        /// 스폰·경고 판정을 둥지 중심으로만 재면, 중심에서 먼 포인트 옆에 서 있는 플레이어를
        /// 한참 못 보다가 중심 반경에 들어서는 순간 갑자기 쏟아내는 것처럼 보인다.
        /// </summary>
        private Vector3 NearestSpawnAnchor(Vector3 playerPos)
        {
            Vector3 best = transform.position;
            float bestDist = Vector3.Distance(best, playerPos);
            if (spawnPoints != null)
                foreach (var sp in spawnPoints)
                {
                    if (sp == null || sp.isDestroyed || sp.point == null) continue;
                    float d = Vector3.Distance(sp.point.position, playerPos);
                    if (d < bestDist) { bestDist = d; best = sp.point.position; }
                }
            return best;
        }

        /// <summary>
        /// 지금 플레이어와 교전 중인(각성한) 보스. 없으면 null.
        /// 둥지가 보스의 상태를 모르면, 제 대장이 두들겨 맞는 동안 쫄몹이 "플레이어가 멀다"는
        /// 이유로 한 마리도 안 나오는 그림이 된다.
        /// </summary>
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

        /// <summary>
        /// 보스 교전 중 지원군이 나올 자리 — 거리 규칙은 전부 무시하고 <b>보스에게 가장 가까운</b>
        /// 살아 있는 스폰 포인트를 쓴다. 다만 그 포인트가 플레이어 화면에 실제로 보이면
        /// 다음으로 가까운 안 보이는 포인트로 넘어간다(눈앞 팝인 방지). 전부 보이면
        /// 플레이어에게서 가장 먼 포인트를 쓴다 — 스폰 자체는 절대 멈추지 않는다.
        /// </summary>
        private List<DefenderSpawnSlot> GetBossReinforcementSlots(MonsterView boss, PlayerView player)
        {
            var result = new List<DefenderSpawnSlot>();
            if (spawnPoints == null || boss == null) return result;

            Vector3 bossPos = boss.transform.position;
            Vector3 playerPos = player != null ? player.transform.position : bossPos;
            Camera eye = Camera.main;

            NestSpawnPoint bestHidden = null;   // 보스에게 가장 가까운, 화면에 안 보이는 포인트
            float bestHiddenDist = float.MaxValue;
            NestSpawnPoint farthestVisible = null;  // 폴백 — 플레이어에게서 가장 먼 포인트
            float farthestVisibleDist = -1f;

            foreach (var sp in spawnPoints)
            {
                if (sp == null || sp.isDestroyed || sp.point == null) continue;

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
            if (chosen != null)
                result.Add(new DefenderSpawnSlot(chosen.point.position));

            return result;
        }

        /// <summary>
        /// 맵 데이터로 세울 때 쓴다. 복구 일수는 인스펙터 전용(private)이라 밖에서 못 넣으므로
        /// 여기로 통로를 낸다 — 부트스트랩이 맵마다 다른 둥지를 만들 수 있어야 한다.
        /// 0 이하는 "프리팹 값을 그대로 둔다"는 뜻이다.
        /// </summary>
        public void Configure(float warning, float trigger, int defenseAmount, float defenseCooldown,
                              int bossDays, int nestDays)
        {
            if (warning > 0f) warningRange = warning;
            if (trigger > 0f) { triggerRange = trigger; daySpawnMaxRange = trigger; }
            if (defenseAmount > 0) defenseSpawnAmount = defenseAmount;
            if (defenseCooldown > 0f) defenseSpawnCooldown = defenseCooldown;
            if (bossDays > 0) bossRecoveryDays = bossDays;
            if (nestDays > 0) nestRecoveryDays = nestDays;
        }


        public bool IsDestroyed { get; private set; }
        private int destroyedDay = -1;

        protected override void Awake()
        {
            base.Awake();
            // 파괴 연출 지연시간 0, Entity의 기본 파괴 시 Destroy 방지용
            SetDeathBehavior(destroy: false, delay: 0f);
        }

        protected override void Start()
        {
            base.Start();
            // 생성 주체는 WorldPopulator다. 그 경로를 안 탄 둥지(옛 씬에 직접 놓인 것)만 여기서 스스로 세운다 — 보이게 경고
            if (Entity == null)
            {
                Debug.LogWarning("[MonsterNest] 심 엔티티 없이 시작했습니다 — WorldPopulator를 거치지 않은 둥지. 데이터 HP로 직접 세웁니다.", this);
                var e = SimHost.World.Create(Faction.Monster, transform.position);   // 둥지는 몬스터 편 — 타워가 노리고 플레이어가 부순다
                e.Add(new HealthModule(Mathf.Max(1f, data != null ? data.maxHp : 500f)));
                e.Add(new EffectsModule());
                AttachEntity(e);
            }

            // 교전 구역은 Awake가 아니라 여기서 찾는다 — WorldPopulator는 Instantiate(→Awake 즉시
            // 실행) <b>뒤에</b> AddComponent<NestEngagementZone>()을 하므로, Awake에서 캐시하면
            // 그 경로에서 영원히 null로 굳는다. 그러면 보스만 zone 없이(=하드코딩 폴백 반경)
            // 배선되고, 매번 GetComponent를 다시 하는 WaveSpawnManager를 타는 방어 몬스터는
            // zone을 받아, 같은 둥지 안에서 규칙이 갈린다.
            engagementZone = GetComponent<NestEngagementZone>();

            if (TimeManager.Instance != null && TimeManager.Instance.Cycle != null)
            {
                TimeManager.Instance.Cycle.NightStarted += OnNightStarted;
                TimeManager.Instance.Cycle.DayStarted += OnDayStarted;
            }

            // 보스는 낮 던전의 고정 전투 대상이다. NestEngagementZone은 방어 몬스터의
            // 출현/추적 거리만 제어하며, 보스의 초기 배치를 막으면 플레이어가 선공할
            // 비선공 보스 자체가 존재하지 않게 된다.
            EnsureBossesSpawned();

            // 코어 머리 위 HP 바 — 둥지가 파괴돼도 남는 코어(indestructibleVisuals[0]) 위에 띄운다.
            Transform core = indestructibleVisuals != null && indestructibleVisuals.Length > 0
                             && indestructibleVisuals[0] != null
                ? indestructibleVisuals[0].transform : transform;
            WorldHealthBar.Attach(this, core, large: true);
        }

        private void SnapBossToGround(GameObject boss)
        {
            if (GridManager.Instance != null)
            {
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
        }

        // 무적 규칙(보스·스폰 포인트가 살아 있는 동안, 데이터가 공격을 거부할 때)은 심의 피해 인터셉터로 건다 —
        // 피해가 뷰를 거치지 않고 심 안에서 끝나므로(투사체 → Effects → Health) 뷰의 ReceiveDamage override로는 지킬 수 없다.
        // 규칙의 주인(스폰 포인트 상태)은 아직 뷰라 술어만 꽂는다 — 둥지가 심으로 가면(5단계) 문도 심 모듈이 된다.
        protected override void OnEntityAttached()
        {
            base.OnEntityAttached();
            if (!Entity.Has<DamageGateModule>())
                Entity.Add(new DamageGateModule()).Blocks = _ => (data != null && !data.isAttackable) || IsInvulnerable();
        }

        private bool IsInvulnerable()
        {
            if (spawnPoints == null) return false;
            foreach (var sp in spawnPoints)
            {
                if (!sp.isDestroyed) return true;
            }
            return false;
        }

        protected override void Update()
        {
            base.Update();

            // 보스 사망 감지 및 스폰포인트 비활성화
            if (spawnPoints != null)
            {
                foreach (var sp in spawnPoints)
                {
                    if (!sp.isDestroyed && sp.linkedBoss != null && sp.linkedBoss.IsDead)
                    {
                        sp.isDestroyed = true;
                        sp.destroyedDay = TimeManager.Instance != null ? TimeManager.Instance.DayNumber : -1;
                        if (sp.point != null) sp.point.gameObject.SetActive(false);
                        Debug.Log($"[MonsterNest] 보스 몬스터가 파괴되어 해당 스폰 포인트가 비활성화 되었습니다. (Day {sp.destroyedDay})");
                    }
                }
            }

            if (IsDestroyed) return;

            // 물리 쿼리 없는 감지 — 캐시한 플레이어와의 거리(산술)로만 판정한다.
            PlayerView player = FindPlayer();

            // 보스가 교전 중이면 거리 규칙 전부(SensorRange·CanSpawnFor·daySpawnMaxRange)를
            // 우회한다. 대장이 맞고 있는데 "플레이어가 둥지에서 멀다"는 이유로 지원군을 안 보내는 건
            // 둥지가 제 보스를 버리는 것과 같다. 자리 선정은 보스 기준(GetBossReinforcementSlots).
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
                        {
                            BattleManager.Instance.Spawner.SpawnNestDefenders(
                                this, player, defenseSpawnAmount,
                                GetBossReinforcementSlots(engagedBoss, player), engagedBoss);
                        }
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
                    {
                        // 포인트가 배선된 둥지는 전부 파괴되면 낮 스폰이 멈춰야 한다 —
                        // 교전 구역은 거리 규칙만 알므로 포인트 생존 여부는 여기서 걸러야 한다.
                        // (안 걸르면 SpawnNestDefenders가 GetAllActiveSpawnPositions의
                        //  둥지 중심 폴백을 타고 계속 스폰한다.)
                        canSpawn = (!HasConfiguredSpawnPoints || HasLiveSpawnPoint)
                                   && engagementZone.CanSpawnFor(anchor, player.transform.position);
                    }
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
                            {
                                BattleManager.Instance.Spawner.SpawnNestDefenders(this, player, defenseSpawnAmount, spawnable);
                            }
                        }
                    }
                    else if ((engagementZone == null || engagementZone.IsActivePhase) && dist <= warningRange)
                    {
                        if (!hasWarned)
                        {
                            hasWarned = true;
                            Debug.Log("[MonsterNest] 둥지 근처에 접근했습니다! 조심하세요.");
                        }
                    }
                    else
                    {
                        hasWarned = false;
                    }
                }
                else
                {
                    hasWarned = false;
                }
            }
            else
            {
                hasWarned = false;
            }
        }

        private void EnsureBossesSpawned()
        {
            if (spawnPoints == null) return;

            // 세이브 복원 중이면 아무것도 만들지 않는다 — 저장된 보스를 곧 되살릴 참이라
            // 여기서 만들면 두 배가 된다. Start와 Update 양쪽에서 불리므로 호출부가 아니라
            // 이 안에서 막아야 복원이 끝나기 전에 Update가 먼저 도는 경우까지 덮인다.
            if (SaveLoadContext.IsRestoring) return;

            foreach (var sp in spawnPoints)
            {
                if (sp.isDestroyed || sp.point == null || sp.bossData == null) continue;
                if (sp.linkedBoss != null && !sp.linkedBoss.IsDead) continue;

                SpawnBossAtPoint(sp);
            }
        }

        // 낮 방어 몬스터와 보스는 같은 에디터 지정 스폰 포인트를 사용한다.
        // 지면 스냅은 Y축 보정만 하므로 수평 위치는 point와 항상 일치한다.
        private void SpawnBossAtPoint(NestSpawnPoint spawnPoint)
        {
            var boss = MonsterSpawner.Spawn(spawnPoint.bossData, spawnPoint.point.position, spawnPoint.point.rotation, transform);
            SnapBossToGround(boss.gameObject);
            spawnPoint.linkedBoss = boss;
            boss.SetAsBoss(engagementZone);
            Debug.Log($"[MonsterNest] 보스를 지정 스폰 포인트에 배치했습니다: {spawnPoint.point.name}");
        }

        protected override void OnDestroy()
        {
            var e = Entity;
            base.OnDestroy();
            // 심이 만들었지만 뷰와 수명을 같이한다(씬 전환) — 종료 중엔 심을 건드리지 않는다
            if (e != null && !e.IsRemoved && !ApplicationQuitting) SimHost.World.Remove(e);
            if (TimeManager.Instance != null && TimeManager.Instance.Cycle != null)
            {
                TimeManager.Instance.Cycle.NightStarted -= OnNightStarted;
                TimeManager.Instance.Cycle.DayStarted -= OnDayStarted;
            }
        }

        protected override void HandleDeath()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;

            if (TimeManager.Instance != null)
            {
                destroyedDay = TimeManager.Instance.DayNumber;
            }

            // 아이템 드롭 처리
            ItemDataSO drop = dropItem;
            if (drop == null)
            {
                var db = ItemDatabaseSO.LoadDefault();
                if (db != null) drop = db.FindById("Item:BeastCore");
            }

            if (drop != null)
            {
                // 위로 약간 던지는 방향
                Vector3 throwDir = (Vector3.up + UnityEngine.Random.insideUnitSphere * 0.2f).normalized;
                DroppedItem.Spawn(drop, 1, transform.position + Vector3.up * 1f, throwDir);
            }

            // 외형 끄기
            foreach (var go in destructibleVisuals)
            {
                if (go != null) go.SetActive(false);
            }

            Debug.Log($"[MonsterNest] 둥지가 파괴되었습니다 (Day {destroyedDay}). 당분간 웨이브가 약화됩니다.");
        }

        private void OnDayStarted(int day)
        {
            if (spawnPoints != null)
            {
                foreach (var sp in spawnPoints)
                {
                    if (sp.isDestroyed && day >= sp.destroyedDay + bossRecoveryDays)
                    {
                        sp.isDestroyed = false;
                        if (sp.point != null) sp.point.gameObject.SetActive(true);

                        // 보스 복구. EngagementZone은 방어 몬스터의 거리 규칙일 뿐,
                        // 보스 재배치를 차단하지 않는다.
                        if (sp.bossData != null && sp.point != null)
                        {
                            SpawnBossAtPoint(sp);
                            Debug.Log($"[MonsterNest] 보스 몬스터와 스폰포인트가 복구되었습니다! (Day {day})");
                        }
                    }
                }
            }
        }

        private void OnNightStarted(int day)
        {
            if (IsDestroyed && day >= destroyedDay + nestRecoveryDays)
            {
                // 이틀 후 복구 (당일 밤 시작 시 복구됨)
                IsDestroyed = false;
                Health.ResetToFull(); // 체력 회복

                foreach (var go in destructibleVisuals)
                {
                    if (go != null) go.SetActive(true);
                }
                Debug.Log($"[MonsterNest] 파괴되었던 둥지가 복구되었습니다 (Day {day}).");
            }

            // 낮 던전은 밤에 보스를 보충하지 않는다. 밤 스폰은 BattleManager가 별도
            // NightSpawnPointProvider(던전 입구)에서 전담한다. EngagementZone이 없는
            // 레거시 둥지만 기존의 밤 보충 동작을 유지한다.
            if (engagementZone == null && !IsDestroyed && spawnPoints != null)
            {
                foreach (var sp in spawnPoints)
                {
                    if (!sp.isDestroyed && (sp.linkedBoss == null || sp.linkedBoss.IsDead))
                    {
                        if (sp.bossData != null && sp.point != null)
                        {
                            SpawnBossAtPoint(sp);
                            Debug.Log($"[MonsterNest] 새로운 보스 몬스터가 스폰되었습니다!");
                        }
                    }
                }
            }
        }

        // ── 세이브 복원 표면 ─────────────────────────────────────────
        //
        // 둥지의 파괴/복구는 "며칠에 부쉈는가"로 굴러간다(destroyedDay + recoveryDays).
        // 그래서 파괴 여부만 저장하면 안 되고 날짜를 함께 남겨야 복구 카운트다운이 이어진다.

        public IReadOnlyList<NestSpawnPoint> Points =>
            spawnPoints ?? (IReadOnlyList<NestSpawnPoint>)System.Array.Empty<NestSpawnPoint>();

        public int DestroyedDay => destroyedDay;
        public bool HasWarned => hasWarned;

        /// <summary>세이브 복원 전용 — 둥지 자체의 파괴 상태와 외형을 되돌린다.</summary>
        public void RestoreSaveState(bool isDestroyed, int day, bool warned)
        {
            IsDestroyed = isDestroyed;
            destroyedDay = day;
            hasWarned = warned;

            // 외형은 파괴 처리(HandleDeath)와 복구(OnNightStarted)가 켜고 끄는 것과 같은 대상이다
            if (destructibleVisuals != null)
                foreach (var go in destructibleVisuals)
                    if (go != null) go.SetActive(!isDestroyed);
        }

        /// <summary>세이브 복원 전용 — 스폰 포인트 한 곳의 파괴 상태를 되돌린다.</summary>
        public void RestoreSpawnPoint(int index, bool destroyed, int day)
        {
            if (spawnPoints == null || index < 0 || index >= spawnPoints.Count) return;

            var sp = spawnPoints[index];
            sp.isDestroyed = destroyed;
            sp.destroyedDay = day;
            if (sp.point != null) sp.point.gameObject.SetActive(!destroyed);
        }

        /// <summary>
        /// 세이브 복원 전용 — 스폰 포인트의 보스를 저장된 위치에 되살린다.
        /// 이미 보스가 붙어 있으면(중복 스폰) 먼저 치운다.
        /// </summary>
        public MonsterView RestoreBoss(int index, Vector3 position, Quaternion rotation)
        {
            if (spawnPoints == null || index < 0 || index >= spawnPoints.Count) return null;

            var sp = spawnPoints[index];
            if (sp.bossData == null || sp.bossData.prefab == null) return null;

            if (sp.linkedBoss != null) Destroy(sp.linkedBoss.gameObject);

            var restored = MonsterSpawner.Spawn(sp.bossData, position, rotation, transform);
            sp.linkedBoss = restored;

            // 평시 스폰(SpawnBossAtPoint)과 같은 계약으로 마무리한다 — 이걸 빠뜨리면 되살아난
            // 보스가 보스 취급을 받지 못하고 교전 구역에도 묶이지 않아, 불러온 게임에서만
            // 다르게 행동하게 된다. 위치만 저장값을 쓰고 나머지는 평시와 동일하다.
            sp.linkedBoss?.SetAsBoss(engagementZone);
            return sp.linkedBoss;
        }

        /// <summary>세이브 복원 전용 — 스폰 포인트에 붙어 있는 보스를 치운다 (저장 당시 죽어 있던 경우).</summary>
        public void ClearBoss(int index)
        {
            if (spawnPoints == null || index < 0 || index >= spawnPoints.Count) return;

            var sp = spawnPoints[index];
            if (sp.linkedBoss != null) Destroy(sp.linkedBoss.gameObject);
            sp.linkedBoss = null;
        }

        /// <summary>
        /// 낮 방어 스폰이 지금 가능한 포인트들.
        ///
        /// 규칙(포인트별):
        ///   · 플레이어가 스폰 시작 반경(daySpawnMaxRange) 안 → 스폰한다
        ///   · 스폰 금지 반경(daySpawnMinRange) 안 → 무조건 멈춘다 (플레이어와 겹쳐 태어남 방지)
        ///   · 포인트가 플레이어 <b>화면에 실제로 보이는 동안</b>(카메라 시야각 안 + 가림 없음)은
        ///     거리와 무관하게 그 포인트만 멈춘다 — 눈앞 팝인 방지.
        ///     화면 밖(옆·뒤)이거나 벽·절벽에 가려져 있으면 계속 나온다.
        /// </summary>
        public List<DefenderSpawnSlot> GetDaySpawnableSlots(PlayerView player)
        {
            var result = new List<DefenderSpawnSlot>();
            if (player == null) return result;
            Vector3 playerPos = player.transform.position;
            Camera eye = Camera.main;   // 플레이어 시점 카메라 — 시야각 판정의 기준

            if (spawnPoints != null)
                foreach (var sp in spawnPoints)
                {
                    if (sp == null || sp.isDestroyed || sp.point == null) continue;

                    Vector3 pos = sp.point.position;
                    float d = Vector3.Distance(pos, playerPos);
                    if (d > daySpawnMaxRange) continue;
                    if (d <= daySpawnMinRange) continue;
                    if (IsOnPlayerScreen(pos, player, eye)) continue;
                    result.Add(new DefenderSpawnSlot(pos));
                }

            // 포인트가 하나도 <b>배선되지 않은</b> 둥지만 둥지 중심을 같은 규칙으로 판정한다
            // (GetAllActiveDefenderSlots의 폴백과 짝). 포인트가 있었는데 전부 파괴된 둥지는
            // 여기로 오면 안 된다 — 보스를 잡아 포인트를 없앤 보상이 낮 스폰 중단이다.
            if (!HasConfiguredSpawnPoints)
            {
                float d = Vector3.Distance(transform.position, playerPos);
                if (d <= daySpawnMaxRange && d > daySpawnMinRange &&
                    !IsOnPlayerScreen(transform.position, player, eye))
                    result.Add(new DefenderSpawnSlot(transform.position));
            }
            return result;
        }

        /// <summary>
        /// 스폰 포인트가 플레이어 <b>화면에 실제로 보이는가</b> — 두 단계로 판정한다:
        ///   ① 카메라 시야각(뷰포트) 안인가 — 옆·뒤에 있으면 화면에 없으니 스폰해도 팝인이 아니다
        ///   ② 시야각 안이라면, 벽·절벽에 가려져 있지는 않은가(포인트 눈높이 → 플레이어 머리 레이캐스트)
        /// 몬스터·플레이어 콜라이더는 벽이 아니므로 마스크에서 뺀다.
        /// </summary>
        private static bool IsOnPlayerScreen(Vector3 spawnPos, PlayerView player, Camera eye)
        {
            Vector3 probe = spawnPos + Vector3.up * 1.2f;   // 스폰될 몬스터의 몸통 높이

            if (eye != null)
            {
                Vector3 vp = eye.WorldToViewportPoint(probe);
                // 뷰포트 살짝 밖까지 여유를 둔다 — 화면 가장자리에서 태어나는 것도 팝인으로 보인다
                if (vp.z <= 0f || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f)
                    return false;   // 시야각 밖
            }

            Vector3 head = player.transform.position + Vector3.up * 1.6f;
            Vector3 dir = head - probe;
            float dist = dir.magnitude;
            if (dist <= 0.5f) return true;   // 사실상 겹친 위치 — 가릴 것이 없다

            int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Monster", "Player", "Character");
            return !Physics.Raycast(probe, dir / dist, dist - 0.3f, mask);
        }

        /// <summary>
        /// 활성화된 모든 스폰 포인트를 낮 방어 스폰 자리(위치 + 포인트별 몬스터 HP)로 반환.
        /// 유효한 포인트가 없으면 둥지 중심을 기본 HP(0 = 프리팹 값)로 돌려준다.
        /// </summary>
        public List<DefenderSpawnSlot> GetAllActiveDefenderSlots()
        {
            var slots = new List<DefenderSpawnSlot>();
            if (spawnPoints != null)
                foreach (var sp in spawnPoints)
                {
                    if (sp == null || sp.isDestroyed || sp.point == null) continue;
                    slots.Add(new DefenderSpawnSlot(sp.point.position));
                }

            if (slots.Count == 0)
                slots.Add(new DefenderSpawnSlot(transform.position));
            return slots;
        }

        /// <summary>
        /// 활성화된 모든 둥지 스폰 포인트 위치 반환
        /// </summary>
        public List<Vector3> GetAllActiveSpawnPositions()
        {
            List<Vector3> positions = new List<Vector3>();
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                var activePoints = spawnPoints.FindAll(sp => !sp.isDestroyed && sp.point != null);
                foreach (var pt in activePoints)
                {
                    positions.Add(pt.point.position);
                }
            }

            // 유효한 스폰 포인트가 없으면 기본 위치 반환
            if (positions.Count == 0)
            {
                positions.Add(transform.position);
            }
            return positions;
        }

        /// <summary>
        /// 웨이브 매니저가 스폰할 위치를 요청할 때 호출 (랜덤한 1개 위치 반환)
        /// </summary>
        public bool TryGetSpawnPosition(out Vector3 position)
        {
            var positions = GetAllActiveSpawnPositions();
            if (positions.Count > 0)
            {
                position = positions[UnityEngine.Random.Range(0, positions.Count)];
                return true;
            }
            position = transform.position;
            return true;
        }
    }
}
