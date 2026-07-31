using System;
using System.Collections.Generic;
using UnityEngine;

// 몬스터 군집 생명주기 관리 — 순수 C# 클래스. BattleManager가 소유하고 Tick으로 구동한다.
// 스폰(생존 상한 유지) → 사망 개체 정리(연출 종료 후 파괴) → 아침 일괄 소멸(DespawnAll)까지
// 군집 전체를 책임진다. 개별 몬스터의 행동은 Monster(상태머신)가 알아서 한다.
[Serializable]
public class MonsterSpawnManager
{
    [Tooltip("스폰할 몬스터 프리팹. 비워두면 테스트용 캡슐 몬스터를 생성한다.")]
    [SerializeField] private GameObject monsterPrefab;

    [Tooltip("동시 생존 몬스터 상한(기본). 이 수를 넘지 않게만 자동 스폰한다.")]
    [SerializeField] private int baseMaxAlive = 4;

    [Tooltip("일차(DayNumber)당 상한 증가량. TimeManager가 없으면 무시된다.")]
    [SerializeField] private int maxAlivePerDay = 2;

    [Tooltip("스폰 시도 간격(초). 상한에 걸리면 스폰하지 않는다.")]
    [SerializeField] private float spawnInterval = 2f;

    [Tooltip("스폰 높이 보정 — 지면 스냅 후 추가로 띄우는 값. 콜라이더 스냅이 되면 0이어도 바닥에 정확히 선다.")]
    [SerializeField] private float spawnHeight = 0f;

    private GridManager grid;
    private Transform parent;
    private bool spawningEnabled;
    private float nextSpawnTime;
    private readonly List<Monster> monsters = new List<Monster>();

    public IReadOnlyList<Monster> Monsters => monsters;
    public bool SpawningEnabled => spawningEnabled;

    // 살아있는(사망 연출 중 제외) 개체 수
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

    // 현재 일차 기준 생존 상한 — "적절한 양"의 기준. 밤이 거듭될수록 늘어난다.
    public int MaxAlive =>
        baseMaxAlive + (TimeManager.Instance != null
            ? Mathf.Max(0, TimeManager.Instance.DayNumber - 1) * maxAlivePerDay
            : 0);

    public void Initialize(GridManager grid, Transform parent)
    {
        this.grid = grid;
        this.parent = parent;
        if (grid == null)
            Debug.LogWarning("[MonsterSpawnManager] GridManager가 없습니다. 스폰 위치를 잡을 수 없어 스폰이 비활성화됩니다.");
    }

    public void SetSpawningEnabled(bool enabled)
    {
        spawningEnabled = enabled;
        if (enabled) nextSpawnTime = Time.time; // 밤 시작 즉시 첫 스폰
    }

    public void Tick()
    {
        CleanupDead();

        if (!spawningEnabled || grid == null) return;
        if (Time.time < nextSpawnTime) return;
        if (AliveCount >= MaxAlive) return;

        if (TrySpawn())
            nextSpawnTime = Time.time + spawnInterval;
    }

    // 군집 일괄 소멸 — 아침이 오면 남은 몬스터를 모두 정리한다
    public void DespawnAll()
    {
        foreach (var m in monsters)
            if (m != null) UnityEngine.Object.Destroy(m.gameObject);
        monsters.Clear();
    }

    // 사망 연출(Entity의 2초 후 비활성화)까지 끝난 개체는 파괴하고 목록에서 제거
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
    }

    private bool TrySpawn()
    {
        if (!TryGetEdgeSpawnPosition(out Vector3 position)) return false;

        GameObject go = monsterPrefab != null
            ? UnityEngine.Object.Instantiate(monsterPrefab, position, Quaternion.identity, parent)
            : CreateFallbackMonster(position);

        SnapToGround(go);

        // 프리팹 레이어 설정 실수 방지 — Default면 Monster 레이어로 교정 (플레이어/타워 센서 감지용)
        int monsterLayer = LayerMask.NameToLayer("Monster");
        if (monsterLayer >= 0 && go.layer == 0)
            SetLayerRecursively(go.transform, monsterLayer);

        // 이동은 transform 구동이므로 물리 반응은 불필요 — kinematic RB는 움직이는 콜라이더의
        // 총알 충돌/트리거 이벤트를 안정적으로 받기 위한 관례적 부착
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var monster = go.GetComponent<Monster>();
        if (monster == null) monster = go.AddComponent<Monster>();
        monsters.Add(monster);
        return true;
    }

    // 그리드 가장자리의 걷기 가능한 셀을 무작위로 고른다 (몬스터는 맵 바깥에서 몰려온다는 연출)
    private bool TryGetEdgeSpawnPosition(out Vector3 position)
    {
        position = default;
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

            // y는 지면 윗면 기준 — 실제 착지는 SnapToGround가 콜라이더 바닥을 맞춰준다
            position = grid.GetNode(cell).worldPosition;
            position.y = grid.SurfaceY;
            return true;
        }
        return false;
    }

    // 프리팹이 없을 때 쓰는 테스트용 몬스터 (캡슐 + Monster 컴포넌트)
    private GameObject CreateFallbackMonster(Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Monster(Spawned)";
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        return go;
    }

    // 콜라이더 바닥이 지면 윗면(SurfaceY)에 닿도록 y를 보정 — 바닥 밑 스폰 방지.
    // 몬스터 이동(MovementComponent)은 transform y를 유지하므로 스폰 시 한 번만 맞추면 된다.
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
