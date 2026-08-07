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
    }

    protected override void Start()
    {
        base.Start();
        if (TimeManager.Instance != null && TimeManager.Instance.Cycle != null)
        {
            TimeManager.Instance.Cycle.NightStarted += OnNightStarted;
            TimeManager.Instance.Cycle.DayStarted += OnDayStarted;
        }
        
        // 씬 시작 시 보스가 없다면 스폰 (에디터에서 씬 리로드를 안 했을 경우 대비)
        if (spawnPoints != null)
        {
            foreach (var sp in spawnPoints)
            {
                if ((sp.linkedBoss == null || sp.linkedBoss.IsDead) && !sp.isDestroyed && sp.bossPrefab != null && sp.point != null)
                {
                    Vector3 spawnPos = sp.point.position + sp.point.forward * 2f;
                    var go = Instantiate(sp.bossPrefab, spawnPos, sp.point.rotation, transform);
                    SnapBossToGround(go);
                    sp.linkedBoss = go.GetComponent<Monster>();
                    sp.linkedBoss?.SetAsBoss();
                    Debug.Log($"[MonsterNest] 시작 시 누락된 보스 몬스터를 자동 스폰했습니다.");
                }
            }
        }
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

                if (dist <= triggerRange)
                {
                    if (Time.time >= lastDefenseSpawnTime + defenseSpawnCooldown)
                    {
                        lastDefenseSpawnTime = Time.time;
                        if (BattleManager.Instance != null && BattleManager.Instance.Spawner != null)
                        {
                            BattleManager.Instance.Spawner.SpawnNestDefenders(this, player, defenseSpawnAmount);
                        }
                    }
                }
                else if (dist <= warningRange)
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
                    
                    // 보스 복구
                    if (sp.bossPrefab != null && sp.point != null)
                    {
                        Vector3 spawnPos = sp.point.position + sp.point.forward * 2f;
                        var go = Instantiate(sp.bossPrefab, spawnPos, sp.point.rotation, transform);
                        SnapBossToGround(go);
                        sp.linkedBoss = go.GetComponent<Monster>();
                        sp.linkedBoss?.SetAsBoss();
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

        // 밤 시작 시 보스가 없으면 스폰 (파괴되지 않은 스폰포인트에 한해)
        if (!IsDestroyed && spawnPoints != null)
        {
            foreach (var sp in spawnPoints)
            {
                if (!sp.isDestroyed && (sp.linkedBoss == null || sp.linkedBoss.IsDead))
                {
                    if (sp.bossPrefab != null && sp.point != null)
                    {
                        Vector3 spawnPos = sp.point.position + sp.point.forward * 2f;
                        var go = UnityEngine.Object.Instantiate(sp.bossPrefab, spawnPos, sp.point.rotation, transform);
                        SnapBossToGround(go);
                        sp.linkedBoss = go.GetComponent<Monster>();
                        sp.linkedBoss?.SetAsBoss();
                        Debug.Log($"[MonsterNest] 새로운 보스 몬스터가 스폰되었습니다!");
                    }
                }
            }
        }
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
