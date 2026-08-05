using UnityEngine;

// 둥지(MonsterNest)는 파괴 가능한 Entity이나,
// 둥지의 핵(NestCore) 부분은 파괴 불가하며 스폰 포인트 역할을 한다.
public class MonsterNest : Entity
{
    [Header("Nest Settings")]
    [Tooltip("둥지 파괴 시 드롭되는 아이템. 비워두면 기본적으로 괴수핵(Item:BeastCore) 드롭.")]
    public ItemDataSO dropItem;
    
    [Tooltip("둥지에서 몬스터가 생성될 위치들")]
    public Transform[] spawnPoints;

    [Tooltip("파괴 불가능한 코어(핵) 등 파괴 시에도 유지될 오브젝트들")]
    public GameObject[] indestructibleVisuals;

    [Tooltip("파괴 시 꺼질 외형(구조물 및 콜라이더 포함) 오브젝트들")]
    public GameObject[] destructibleVisuals;

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
        if (TimeManager.Instance != null && TimeManager.Instance.Cycle != null)
        {
            TimeManager.Instance.Cycle.NightStarted += OnNightStarted;
        }
    }

    private void OnDestroy()
    {
        if (TimeManager.Instance != null && TimeManager.Instance.Cycle != null)
        {
            TimeManager.Instance.Cycle.NightStarted -= OnNightStarted;
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

    private void OnNightStarted(int day)
    {
        if (IsDestroyed && day >= destroyedDay + 2)
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
    }

    /// <summary>
    /// 웨이브 매니저가 스폰할 위치를 요청할 때 호출
    /// </summary>
    public bool TryGetSpawnPosition(out Vector3 position)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            var pt = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)];
            if (pt != null)
            {
                position = pt.position;
                return true;
            }
        }
        position = transform.position;
        return true;
    }
}
