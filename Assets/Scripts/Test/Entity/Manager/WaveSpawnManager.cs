using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class WaveSpawnManager
{
    [Tooltip("스폰할 몬스터 프리팹. 비워두면 테스트용 캡슐 몬스터를 생성한다.")]
    [SerializeField] private GameObject monsterPrefab;

    [Tooltip("스폰 높이 보정")]
    [SerializeField] private float spawnHeight = 0f;

    private GridManager grid;
    private Transform parent;
    private bool spawningEnabled;
    private float nextSpawnTime;
    private readonly List<Monster> monsters = new List<Monster>();
    private readonly List<Monster> nightWaveMonsters = new List<Monster>();

    private bool quantityBasedMode;
    private bool quantityWaveActive;
    private bool quantityWaveCompleted;
    private int targetSpawnAmount;
    private int spawnedThisWave;
    private int lastNotifiedDefeated = -1;
    private int lastNotifiedSpawned = -1;

    private List<WaveDataSO> waves = new List<WaveDataSO>();
    private WaveDataSO currentWave;

    // 파괴되지 않은 둥지 캐시
    private List<MonsterNest> activeNests = new List<MonsterNest>();
    private readonly List<Transform> nightSpawnPoints = new List<Transform>();
    private bool useExplicitNightSpawnPoints;

    public IReadOnlyList<Monster> Monsters => monsters;
    public bool SpawningEnabled => spawningEnabled;
    public bool QuantityBasedMode => quantityBasedMode;
    public bool IsQuantityWaveActive => quantityWaveActive;
    public bool IsQuantityWaveCompleted => quantityWaveCompleted;
    public int TargetSpawnAmount => targetSpawnAmount;
    public int SpawnedThisWave => spawnedThisWave;
    public int DefeatedThisWave => Mathf.Clamp(spawnedThisWave - NightWaveAliveCount, 0, spawnedThisWave);
    public int RemainingThisWave => Mathf.Max(0, targetSpawnAmount - DefeatedThisWave);

    public event Action<int> QuantityWaveStarted;
    public event Action<int, int, int> QuantityWaveProgressChanged;
    public event Action<int> QuantityWaveCompleted;

    public int AliveCount
    {
        get
        {
            int count = 0;
            foreach (var m in monsters)
                if (m != null && !m.IsDead) count++;
            return count;
        }
    }

    public int MaxAlive => currentWave != null ? currentWave.maxAliveAmount : 4;
    private int BaseAmount => currentWave != null ? currentWave.baseAmount : 4;
    private float SpawnInterval => currentWave != null ? currentWave.spawnInterval : 2f;

    private int NightWaveAliveCount
    {
        get
        {
            int count = 0;
            foreach (var monster in nightWaveMonsters)
                if (monster != null && !monster.IsDead) count++;
            return count;
        }
    }

    public void SetQuantityBasedMode(bool enabled)
    {
        quantityBasedMode = enabled;
        if (!enabled)
        {
            quantityWaveActive = false;
            quantityWaveCompleted = false;
            nightWaveMonsters.Clear();
        }
    }

    public void Initialize(GridManager grid, Transform parent)
    {
        Initialize(grid, parent, null);
    }

    public void Initialize(GridManager grid, Transform parent, IReadOnlyList<Transform> explicitNightSpawnPoints)
    {
        this.grid = grid;
        this.parent = parent;
        nightSpawnPoints.Clear();
        useExplicitNightSpawnPoints = explicitNightSpawnPoints != null;
        if (explicitNightSpawnPoints != null)
        {
            for (int i = 0; i < explicitNightSpawnPoints.Count; i++)
                if (explicitNightSpawnPoints[i] != null) nightSpawnPoints.Add(explicitNightSpawnPoints[i]);
        }
        if (grid == null)
            Debug.LogWarning("[WaveSpawnManager] GridManager가 없습니다. 둥지 또는 바닥에 스폰이 실패할 수 있습니다.");

        LoadWaves();
    }

    private void LoadWaves()
    {
        waves.Clear();
#if UNITY_EDITOR
        var guids = UnityEditor.AssetDatabase.FindAssets("t:WaveDataSO");
        foreach(var guid in guids)
        {
            var w = UnityEditor.AssetDatabase.LoadAssetAtPath<WaveDataSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (w != null) waves.Add(w);
        }
#else
        waves.AddRange(Resources.LoadAll<WaveDataSO>(""));
#endif
        waves = waves.OrderBy(w => w.day).ThenBy(w => w.requiredCoreTier).ToList();
        Debug.Log($"[WaveSpawnManager] {waves.Count}개의 웨이브 데이터를 로드했습니다.");
    }

    public void SetSpawningEnabled(bool enabled)
    {
        spawningEnabled = enabled;
        if (enabled) 
        {
            nextSpawnTime = Time.time;
            DetermineCurrentWave();
            // Spawn location and nest-strength tracking are separate concerns.
            // Explicit dungeon entrances still need the live nest count for wave weakening.
            FindActiveNests();

            if (quantityBasedMode)
            {
                nightWaveMonsters.Clear();
                targetSpawnAmount = Mathf.Max(1, BaseAmount);
                spawnedThisWave = 0;
                lastNotifiedDefeated = -1;
                lastNotifiedSpawned = -1;
                quantityWaveCompleted = false;
                quantityWaveActive = true;
                QuantityWaveStarted?.Invoke(targetSpawnAmount);
                NotifyQuantityProgress();
            }
        }
    }

    private void DetermineCurrentWave()
    {
        int day = TimeManager.Instance != null ? TimeManager.Instance.DayNumber : 1;
        // 티어의 정본은 GameManager (RecipeManager는 팀 결정으로 취소·삭제됨 — PR #54)
        int coreTier = GameManager.Instance != null ? GameManager.Instance.UnlockedTier : 0;

        // 해당 일차와 티어에 맞는 가장 적합한 웨이브 탐색 (조건을 만족하는 마지막 웨이브)
        currentWave = waves.LastOrDefault(w => w.day <= day && w.requiredCoreTier <= coreTier);
        if (currentWave == null)
        {
            currentWave = waves.FirstOrDefault(); // fallback
        }
        
        Debug.Log($"[WaveSpawnManager] Day {day}, CoreTier {coreTier} -> 선택된 웨이브: {(currentWave != null ? currentWave.displayName : "None")}");
    }

    private void FindActiveNests()
    {
        activeNests.Clear();
        var allNests = UnityEngine.Object.FindObjectsByType<MonsterNest>(FindObjectsSortMode.None);
        foreach (var nest in allNests)
        {
            if (!nest.IsDestroyed)
            {
                activeNests.Add(nest);
            }
        }
        
        Debug.Log($"[WaveSpawnManager] 활성화된 둥지 수: {activeNests.Count}");
    }

    public void Tick()
    {
        CleanupDead();

        if (!spawningEnabled) return;

        if (quantityBasedMode)
        {
            if (!quantityWaveActive) return;
            NotifyQuantityProgress();
            if (spawnedThisWave >= targetSpawnAmount)
            {
                if (NightWaveAliveCount == 0) CompleteQuantityWave();
                return;
            }
        }

        if (Time.time < nextSpawnTime) return;
        
        // 둥지 파괴 시 웨이브 약화 (절반으로 줄임, 최소 1)
        int effectiveMaxAlive = MaxAlive;
        if (activeNests.Count == 0)
        {
            effectiveMaxAlive = Mathf.Max(1, MaxAlive / 2);
        }

        int concurrencyCount = quantityBasedMode ? NightWaveAliveCount : AliveCount;
        if (concurrencyCount >= effectiveMaxAlive) return;

        if (TrySpawn(out Monster spawnedMonster))
        {
            if (quantityBasedMode)
            {
                nightWaveMonsters.Add(spawnedMonster);
                spawnedThisWave++;
                NotifyQuantityProgress();
            }
            nextSpawnTime = Time.time + SpawnInterval;
        }
    }

    public void DespawnAll()
    {
        foreach (var m in monsters)
            if (m != null) UnityEngine.Object.Destroy(m.gameObject);
        monsters.Clear();
        nightWaveMonsters.Clear();
    }

    /// <summary>
    /// 둥지 방어 몬스터 스폰. <paramref name="spawnPositions"/>는 둥지가 판정한
    /// "지금 스폰 가능한 포인트들"(MonsterNest.GetDaySpawnablePositions) — 거리·가림 규칙은
    /// 반경 값을 소유한 둥지 쪽에 있다. null이면 모든 활성 포인트를 쓴다(레거시 호출).
    /// </summary>
    public void SpawnNestDefenders(MonsterNest nest, Player target, int amount,
                                   List<Vector3> spawnPositions = null)
    {
        if (spawnPositions == null) spawnPositions = nest.GetAllActiveSpawnPositions();
        if (spawnPositions.Count == 0 || amount <= 0) return;

        for (int i = 0; i < amount; i++)
        {
            Vector3 position = spawnPositions[i % spawnPositions.Count];
            GameObject go = monsterPrefab != null
                ? UnityEngine.Object.Instantiate(monsterPrefab, position, Quaternion.identity, parent)
                : CreateFallbackMonster(position);

            go.SetActive(true);
            SnapToGround(go);

            int monsterLayer = LayerMask.NameToLayer("Monster");
            if (monsterLayer >= 0 && go.layer == 0)
                SetLayerRecursively(go.transform, monsterLayer);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var monster = go.GetComponent<Monster>();
            if (monster == null) monster = go.AddComponent<Monster>();
            monsters.Add(monster);
            
            // 스폰된 몬스터에게 방어자 플래그 부여 및 타겟 강제 지정
            var zone = nest.GetComponent<NestEngagementZone>();
            if (zone != null)
                monster.SetAsNestDefender(target, nest.transform.position, zone);
            else
                monster.SetAsNestDefender(target);
        }
        Debug.Log($"[WaveSpawnManager] 둥지 근처에 방어 몬스터 {amount}마리를 스폰했습니다.");
    }

    // ── 세이브 복원 표면 ─────────────────────────────────────────

    /// <summary>
    /// 다음 스폰까지 남은 시간(초). nextSpawnTime은 Time.time 기준 절대값이라
    /// 그대로 저장하면 다음 실행에서 의미가 없다 — 남은 시간으로 환산해 저장한다.
    /// </summary>
    public float NextSpawnDelay => Mathf.Max(0f, nextSpawnTime - Time.time);

    /// <summary>세이브 복원 전용 — 스폰 진행 상태를 되돌린다.</summary>
    public void RestoreState(bool enabled, float spawnDelay)
    {
        spawningEnabled = enabled;
        nextSpawnTime = Time.time + Mathf.Max(0f, spawnDelay);

        if (!enabled) return;
        DetermineCurrentWave();
        FindActiveNests();
    }

    /// <summary>
    /// 세이브 복원 전용 — 저장된 자리에 몬스터를 되살린다.
    /// 지형 스냅을 하지 않는 이유: 저장된 좌표가 이미 지형 위에 있던 값이고,
    /// 다시 스냅하면 경사면에서 조금씩 위치가 밀린다.
    /// </summary>
    public Monster RestoreMonster(Vector3 position, Quaternion rotation)
    {
        GameObject go = monsterPrefab != null
            ? UnityEngine.Object.Instantiate(monsterPrefab, position, rotation, parent)
            : CreateFallbackMonster(position);

        go.SetActive(true);
        go.transform.SetPositionAndRotation(position, rotation);

        int monsterLayer = LayerMask.NameToLayer("Monster");
        if (monsterLayer >= 0 && go.layer == 0)
            SetLayerRecursively(go.transform, monsterLayer);

        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var monster = go.GetComponent<Monster>();
        if (monster == null) monster = go.AddComponent<Monster>();
        monsters.Add(monster);
        return monster;
    }

    private void CleanupDead()
    {
        for (int i = monsters.Count - 1; i >= 0; i--)
        {
            var m = monsters[i];
            if (m == null)
            {
                monsters.RemoveAt(i);
                continue;
            }
            if (m.IsDead && !m.gameObject.activeInHierarchy)
            {
                UnityEngine.Object.Destroy(m.gameObject);
                monsters.RemoveAt(i);
            }
        }

        for (int i = nightWaveMonsters.Count - 1; i >= 0; i--)
        {
            var monster = nightWaveMonsters[i];
            if (monster == null || (monster.IsDead && !monster.gameObject.activeInHierarchy))
                nightWaveMonsters.RemoveAt(i);
        }
    }

    private bool TrySpawn(out Monster spawnedMonster)
    {
        spawnedMonster = null;
        Vector3 position = default;
        bool foundPos = false;

        if (useExplicitNightSpawnPoints)
        {
            if (nightSpawnPoints.Count == 0) return false;
            Transform point = nightSpawnPoints[UnityEngine.Random.Range(0, nightSpawnPoints.Count)];
            position = point.position;
            foundPos = true;
        }
        else if (activeNests.Count > 0)
        {
            var nest = activeNests[UnityEngine.Random.Range(0, activeNests.Count)];
            foundPos = nest.TryGetSpawnPosition(out position);
        }
        else
        {
            // 모든 둥지가 파괴되었거나 없을 경우 맵 가장자리에서 스폰
            foundPos = TryGetEdgeSpawnPosition(out position);
        }

        if (!foundPos) return false;

        GameObject go = monsterPrefab != null
            ? UnityEngine.Object.Instantiate(monsterPrefab, position, Quaternion.identity, parent)
            : CreateFallbackMonster(position);

        go.SetActive(true);
        SnapToGround(go);

        int monsterLayer = LayerMask.NameToLayer("Monster");
        if (monsterLayer >= 0 && go.layer == 0)
            SetLayerRecursively(go.transform, monsterLayer);

        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        spawnedMonster = go.GetComponent<Monster>();
        if (spawnedMonster == null) spawnedMonster = go.AddComponent<Monster>();
        monsters.Add(spawnedMonster);

        return true;
    }

    private void NotifyQuantityProgress()
    {
        if (!quantityBasedMode) return;
        int defeated = DefeatedThisWave;
        if (defeated == lastNotifiedDefeated && spawnedThisWave == lastNotifiedSpawned) return;
        lastNotifiedDefeated = defeated;
        lastNotifiedSpawned = spawnedThisWave;
        QuantityWaveProgressChanged?.Invoke(defeated, spawnedThisWave, targetSpawnAmount);
    }

    private void CompleteQuantityWave()
    {
        if (!quantityWaveActive || quantityWaveCompleted) return;
        quantityWaveActive = false;
        quantityWaveCompleted = true;
        spawningEnabled = false;
        NotifyQuantityProgress();
        Debug.Log($"[WaveSpawnManager] 물량 웨이브 완료: {targetSpawnAmount}마리 전멸");
        QuantityWaveCompleted?.Invoke(targetSpawnAmount);
    }

    private bool TryGetEdgeSpawnPosition(out Vector3 position)
    {
        position = default;
        if (grid == null) return false;

        Vector2Int size = grid.gridSize;
        if (size.x < 2 || size.y < 2) return false;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            int side = UnityEngine.Random.Range(0, 4);
            Vector2Int cell = side switch
            {
                0 => new Vector2Int(UnityEngine.Random.Range(0, size.x), 0),
                1 => new Vector2Int(UnityEngine.Random.Range(0, size.x), size.y - 1),
                2 => new Vector2Int(0, UnityEngine.Random.Range(0, size.y)),
                _ => new Vector2Int(size.x - 1, UnityEngine.Random.Range(0, size.y)),
            };
            if (!grid.IsWalkable(cell)) continue;

            position = grid.GetNode(cell).worldPosition;
            position.y = grid.SurfaceY;
            return true;
        }
        return false;
    }

    private GameObject CreateFallbackMonster(Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Monster(Spawned)";
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        return go;
    }

    private void SnapToGround(GameObject go)
    {
        float surfaceY = grid != null ? grid.SurfaceY : go.transform.position.y;
        var col = go.GetComponentInChildren<Collider>();
        if (col != null)
        {
            float bottom = col.bounds.min.y;
            go.transform.position += Vector3.up * (surfaceY - bottom + 0.02f + spawnHeight);
        }
        else
        {
            var pos = go.transform.position;
            pos.y = surfaceY + spawnHeight;
            go.transform.position = pos;
        }
    }

    private static void SetLayerRecursively(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t)
            SetLayerRecursively(child, layer);
    }
}
