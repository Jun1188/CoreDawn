using UnityEngine;
using System.Collections.Generic;

// 둥지(MonsterNest)는 파괴 가능한 Entity이나,
// 둥지의 핵(NestCore) 부분은 파괴 불가하며 스폰 포인트 역할을 한다.
public class MonsterNest : Entity
{
    [System.Serializable]
    public class NestSpawnPoint
    {
        public Transform point;
        public Monster linkedBoss;
        public GameObject bossPrefab;
        [HideInInspector] public bool isDestroyed = false;
        [HideInInspector] public int destroyedDay = -1;
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

    private SensorComponent sensor = new SensorComponent();
    private bool hasWarned;
    private float lastDefenseSpawnTime;
    private NestEngagementZone engagementZone;


    public bool IsDestroyed { get; private set; }
    private int destroyedDay = -1;

    protected override void Awake()
    {
        base.Awake();
        // 파괴 연출 지연시간 0, Entity의 기본 파괴 시 Destroy 방지용
        SetDeathBehavior(destroy: false, delay: 0f);
        
        sensor.Initialize(this);
        sensor.SetTargetLayer("Player", "Character");
        sensor.SetDetectionRange(Mathf.Max(warningRange, triggerRange));
        engagementZone = GetComponent<NestEngagementZone>();
    }

    protected override void Start()
    {
        base.Start();
        if (TimeManager.Instance != null && TimeManager.Instance.Cycle != null)
        {
            TimeManager.Instance.Cycle.NightStarted += OnNightStarted;
            TimeManager.Instance.Cycle.DayStarted += OnDayStarted;
        }
        
        // 보스는 낮 던전의 고정 전투 대상이다. NestEngagementZone은 방어 몬스터의
        // 출현/추적 거리만 제어하며, 보스의 초기 배치를 막으면 플레이어가 선공할
        // 비선공 보스 자체가 존재하지 않게 된다.
        EnsureBossesSpawned();
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

    public override void TakeDamage(float damageAmount)
    {
        if (IsInvulnerable()) 
        {
            // Debug.Log("[MonsterNest] 둥지가 무적 상태입니다! (보스 또는 스폰포인트가 살아있음)");
            return;
        }
        base.TakeDamage(damageAmount);
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

        Entity detectedEntity = sensor.GetClosestTarget(warningRange);
        if (detectedEntity != null && !detectedEntity.IsDead)
        {
            Player player = detectedEntity.GetComponent<Player>();
            if (player != null)
            {
                float dist = Vector3.Distance(transform.position, player.transform.position);

                bool canSpawn = engagementZone != null
                    ? engagementZone.CanSpawnFor(transform.position, player.transform.position)
                    : dist <= triggerRange;

                if (canSpawn)
                {
                    EnsureBossesSpawned();
                    if (Time.time >= lastDefenseSpawnTime + defenseSpawnCooldown)
                    {
                        lastDefenseSpawnTime = Time.time;
                        if (BattleManager.Instance != null && BattleManager.Instance.Spawner != null)
                        {
                            BattleManager.Instance.Spawner.SpawnNestDefenders(this, player, defenseSpawnAmount);
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
            if (sp.isDestroyed || sp.point == null || sp.bossPrefab == null) continue;
            if (sp.linkedBoss != null && !sp.linkedBoss.IsDead) continue;

            SpawnBossAtPoint(sp);
        }
    }

    // 낮 방어 몬스터와 보스는 같은 에디터 지정 스폰 포인트를 사용한다.
    // 지면 스냅은 Y축 보정만 하므로 수평 위치는 point와 항상 일치한다.
    private void SpawnBossAtPoint(NestSpawnPoint spawnPoint)
    {
        var go = Instantiate(spawnPoint.bossPrefab, spawnPoint.point.position,
            spawnPoint.point.rotation, transform);
        SnapBossToGround(go);
        spawnPoint.linkedBoss = go.GetComponent<Monster>();
        spawnPoint.linkedBoss?.SetAsBoss(transform.position, engagementZone);
        Debug.Log($"[MonsterNest] 보스를 지정 스폰 포인트에 배치했습니다: {spawnPoint.point.name}");
    }

    private void OnDestroy()
    {
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
                    if (sp.bossPrefab != null && sp.point != null)
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
            Health.Initialize(); // 체력 회복
            
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
                    if (sp.bossPrefab != null && sp.point != null)
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
    public Monster RestoreBoss(int index, Vector3 position, Quaternion rotation)
    {
        if (spawnPoints == null || index < 0 || index >= spawnPoints.Count) return null;

        var sp = spawnPoints[index];
        if (sp.bossPrefab == null) return null;

        if (sp.linkedBoss != null) Destroy(sp.linkedBoss.gameObject);

        var go = Instantiate(sp.bossPrefab, position, rotation, transform);
        sp.linkedBoss = go.GetComponent<Monster>();

        // 평시 스폰(SpawnBossAtPoint)과 같은 계약으로 마무리한다 — 이걸 빠뜨리면 되살아난
        // 보스가 보스 취급을 받지 못하고 교전 구역에도 묶이지 않아, 불러온 게임에서만
        // 다르게 행동하게 된다. 위치만 저장값을 쓰고 나머지는 평시와 동일하다.
        sp.linkedBoss?.SetAsBoss(transform.position, engagementZone);
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
