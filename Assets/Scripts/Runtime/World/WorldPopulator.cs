using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵 데이터를 씬의 실물로 세운다 — 광맥·둥지·밤 진입로.
///
/// <b>왜 씬에 미리 놓지 않는가.</b> 이것들의 위치와 성질은 전부 맵이 정한다
/// (<see cref="MapDataSO"/>의 nodes·nests·nightSpawnPoints). 씬에 박아두면 맵을 갈아끼울 때
/// 배치가 따라오지 않아 "새 맵인데 옛 둥지가 서 있는" 상태가 된다. 맵이 곧 배치이므로,
/// 배치도 맵에서 만들어져야 한다.
///
/// 생성물은 월드 아래 <c>Spawned</c> 노드에 모은다 — 지형(Terrain (Generated))과 같은 원칙으로,
/// 다시 만들 때 통째로 지우고 새로 세운다.
/// </summary>
public static class WorldPopulator
{
    const string RootName = "Spawned";

    /// <summary>
    /// 맵대로 세우고, 밤 웨이브의 진입로를 담은 제공자를 돌려준다.
    /// 이미 세워져 있으면 지우고 다시 만든다(맵이 바뀌었을 수 있다).
    /// </summary>
    public static NightSpawnPointProvider Populate(World world, Transform battleRoot)
    {
        if (world == null || world.Map == null) return null;

        var old = world.transform.Find(RootName);
        if (old != null) Object.Destroy(old.gameObject);

        var root = new GameObject(RootName).transform;
        root.SetParent(world.transform, false);

        int nodes = PlaceNodes(world, root);
        int nests = PlaceNests(world, root);
        int startingDrops = PlaceStartingDrops(world, root);
        var provider = PlaceNightSpawns(world, root, battleRoot);

        Debug.Log($"[WorldPopulator] '{world.Map.Id}' 배치 — 광맥 {nodes} · 둥지 {nests} · " +
                  $"시작 아이템 {startingDrops} · 밤 진입로 {(provider != null ? provider.SpawnPoints.Count : 0)}", world);
        return provider;
    }

    // ── 시작 드롭 아이템 ───────────────────────────────────────

    // 시작 잔해가 흩어지는 범위 — 코어 중심에서 몇 칸까지인가. 2칸이면 3×3 코어 바로 바깥이다.
    const float StartDropMinRing = 2f;
    const float StartDropMaxRing = 5f;

    /// <summary>같은 아이템은 이 거리 안에서 스택으로 합쳐진다(트리거 센서) — 그보다 넉넉히 띄운다.</summary>
    const float StartDropMinGap = 3.5f;

    /// <summary>
    /// 시작 잔해를 코어 주변에 흩는다.
    ///
    /// 씬의 <c>StartItem_*</c> DroppedItem은 이제 <b>배치가 아니라 사양</b>이다 — "무엇을 몇 개"만
    /// 읽고 지운다. 씬에 실물로 두면 세이브를 불러올 때 이 함수가 다시 돌면서 저장된 드롭 위에
    /// 시작 잔해가 겹쳐 생겨 아이템이 불어난다. 복원 중이면 아예 만들지 않는다 —
    /// 저장된 바닥 아이템을 곧 되살릴 참이고, 그것이 이 잔해의 현재 상태다.
    ///
    /// 흩는 방식: 낱개로 쪼개 원주를 균등 분할하고 각도·반지름에 지터를 준다. 무작위지만
    /// 뭉치지 않는다 — 순수 난수는 반드시 몇 개가 붙고, 붙으면 스택으로 합쳐져 하나가 된다.
    /// </summary>
    static int PlaceStartingDrops(World world, Transform root)
    {
        // 1) 사양 수집 — 씬 마커는 읽고 지운다(복원 중이라도 지운다: 실물로 남으면 안 된다)
        ItemDataSO item = null;
        int total = 0;

        foreach (var marker in Object.FindObjectsByType<DroppedItem>(FindObjectsInactive.Include,
                                                                    FindObjectsSortMode.None))
        {
            if (marker == null || marker.gameObject.scene != world.gameObject.scene ||
                !marker.name.StartsWith("StartItem_") || marker.item == null || marker.amount <= 0)
                continue;

            item ??= marker.item;
            if (marker.item == item) total += marker.amount;
            Object.Destroy(marker.gameObject);
        }

        if (item == null || total <= 0) return 0;
        if (SaveLoadContext.IsRestoring) return 0;

        // 2) 코어 중심 — 코어는 3×3이라 원점 칸에서 1.5칸이 한가운데다(World의 기즈모와 같은 식)
        Vector3 center = world.CellToWorld(world.Map.core)
                       + new Vector3(1.5f, 0f, 1.5f) * world.CellSize;

        var placedPoints = new List<Vector3>(total);
        float step = 360f / total;
        float baseAngle = Random.Range(0f, 360f);

        for (int i = 0; i < total; i++)
        {
            // 자기 몫의 각도 구간 안에서만 흔든다(±40%) — 이웃과 겹칠 여지를 남기지 않는다
            float angle = baseAngle + step * i + Random.Range(-step * 0.4f, step * 0.4f);

            // 지형(강·절벽)이나 이웃과의 간격 때문에 실패하면 조금씩 비틀어 다시 시도
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float radius = Random.Range(StartDropMinRing, StartDropMaxRing) * world.CellSize;
                float rad = (angle + attempt * 6f) * Mathf.Deg2Rad;
                Vector3 pos = center + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;

                if (!IsOpenGround(world, pos)) continue;
                if (TooClose(placedPoints, pos)) continue;

                // 살짝 띄워 떨어뜨린다 — 지형이 칸마다 조금씩 높낮이가 있어 파묻히지 않게
                var drop = DroppedItem.Spawn(item, 1, pos + Vector3.up * 0.5f, Vector3.zero);
                if (drop == null) break;

                drop.transform.SetParent(root, true);
                placedPoints.Add(pos);
                break;
            }
        }

        return placedPoints.Count;
    }

    /// <summary>맵 위의 지면 칸인가 — 강·절벽·맵 밖에는 떨어뜨리지 않는다.</summary>
    static bool IsOpenGround(World world, Vector3 position)
    {
        var map = world.Map;
        if (map == null) return true;   // 맵 없는 구성(테스트 씬)에서는 제한하지 않는다

        Vector3 local = position - world.Origin;
        var cell = new Vector2Int(Mathf.FloorToInt(local.x / world.CellSize),
                                  Mathf.FloorToInt(local.z / world.CellSize));

        if (cell.x < 0 || cell.y < 0 || cell.x >= map.width || cell.y >= map.height) return false;
        return map.TileAt(cell) == MapTile.Ground;
    }

    static bool TooClose(List<Vector3> points, Vector3 candidate)
    {
        foreach (var p in points)
            if ((p - candidate).sqrMagnitude < StartDropMinGap * StartDropMinGap) return true;
        return false;
    }

    // ── 광맥 ────────────────────────────────────────────────────

    static int PlaceNodes(World world, Transform root)
    {
        var map = world.Map;
        if (map.nodes == null || map.nodes.Length == 0) return 0;

        if (world.ResourceNodePrefab == null)
        {
            Debug.LogWarning("[WorldPopulator] 광맥 프리팹이 World에 배선되지 않아 광맥을 세우지 못했습니다.", world);
            return 0;
        }

        int placed = 0;
        foreach (var spec in map.nodes)
        {
            if (spec.item == null) continue;

            // 오브젝트 위치가 풋프린트 <b>중앙</b>이다(ResourceNode의 규약) — 왼쪽 아래 칸에서
            // 크기의 절반만큼 밀어야 칸에 맞는다. 1칸짜리는 그대로 칸 중앙이다.
            // 위치는 Instantiate에 함께 넘긴다 — 생성 후에 옮기면 OnEnable의 레지스트리 등록이
            // 프리팹 기본 위치의 셀에서 일어나, 모든 광맥이 같은 셀에 겹쳐 서로를 덮어쓴다.
            int size = Mathf.Max(1, spec.size);
            Vector3 corner = world.CellToWorld(spec.cell);
            Vector3 center = corner + new Vector3(size, 0f, size) * (0.5f * world.CellSize);

            var go = Object.Instantiate(world.ResourceNodePrefab, center, Quaternion.identity, root);
            go.name = $"Node_{spec.item.name}_{spec.cell.x}_{spec.cell.y}";

            var node = go.GetComponent<ResourceNode>();
            if (node != null)
            {
                node.Configure(spec.item, size, spec.extractInterval, spec.maxStock);
                node.Refresh();   // 크기가 바뀌었을 수 있다 — 점유 셀을 새 풋프린트로 재등록
            }
            placed++;
        }
        return placed;
    }

    // ── 둥지 ────────────────────────────────────────────────────

    static int PlaceNests(World world, Transform root)
    {
        var map = world.Map;
        if (map.nests == null || map.nests.Length == 0) return 0;

        if (world.NestPrefab == null)
        {
            Debug.LogWarning("[WorldPopulator] 둥지 프리팹이 World에 배선되지 않아 둥지를 세우지 못했습니다.", world);
            return 0;
        }

        int placed = 0;
        foreach (var spec in map.nests)
        {
            var go = Object.Instantiate(world.NestPrefab, root);
            go.name = $"Nest_{spec.cell.x}_{spec.cell.y}";
            go.transform.position = world.CellToWorldCenter(spec.cell);

            var nest = go.GetComponent<MonsterNest>();
            if (nest != null)
            {
                nest.Configure(spec.warningRange, spec.triggerRange,
                               spec.defenseSpawnAmount, spec.defenseSpawnCooldown,
                               spec.bossRecoveryDays, spec.nestRecoveryDays);
                ApplySpawnPoints(world, nest, spec);
            }

            // 교전 구역은 값이 있을 때만 붙인다 — 프리팹에 없으면 둥지는 기본 동작을 그대로 쓴다
            if (spec.engageMaxRange > 0f)
            {
                var zone = go.GetComponent<NestEngagementZone>();
                if (zone == null) zone = go.AddComponent<NestEngagementZone>();
                zone.Configure(spec.engageMinRange, spec.engageMaxRange,
                               spec.chaseRange, spec.leashRange, spec.engageDayOnly);
            }

            placed++;
        }
        return placed;
    }

    /// <summary>
    /// 둥지의 스폰 자리를 맵대로 다시 놓는다. 프리팹에 이미 자식(SpawnPoint_1…)이 있으면
    /// 그것을 옮겨 쓰고, 모자라면 새로 만든다.
    ///
    /// 보스 유무는 맵(hasBoss)이 정한다 — 프리팹 배선은 "무슨 보스인가"의 출처일 뿐이다.
    /// 프리팹 배선을 그대로 두면 맵이 점을 늘리거나 보스를 빼도 반영되지 않아,
    /// json과 실제 스폰 수가 어긋난다.
    /// </summary>
    static void ApplySpawnPoints(World world, MonsterNest nest, in NestSpec spec)
    {
        if (spec.spawnPoints == null || spec.spawnPoints.Length == 0) return;
        if (nest.spawnPoints == null) nest.spawnPoints = new List<MonsterNest.NestSpawnPoint>();

        GameObject bossTemplate = null;
        foreach (var existing in nest.spawnPoints)
            if (existing != null && existing.bossPrefab != null) { bossTemplate = existing.bossPrefab; break; }

        for (int i = 0; i < spec.spawnPoints.Length; i++)
        {
            var point = spec.spawnPoints[i];
            Vector3 pos = world.CellToWorldCenter(spec.cell + point.offset);

            MonsterNest.NestSpawnPoint slot;
            if (i < nest.spawnPoints.Count && nest.spawnPoints[i] != null && nest.spawnPoints[i].point != null)
            {
                slot = nest.spawnPoints[i];
                slot.point.position = pos;
            }
            else
            {
                var t = new GameObject($"SpawnPoint_{i + 1}").transform;
                t.SetParent(nest.transform, false);
                t.position = pos;

                if (i < nest.spawnPoints.Count && nest.spawnPoints[i] != null) { slot = nest.spawnPoints[i]; slot.point = t; }
                else { slot = new MonsterNest.NestSpawnPoint { point = t }; nest.spawnPoints.Add(slot); }
            }

            slot.bossPrefab = point.hasBoss ? bossTemplate : null;
            if (point.hasBoss && bossTemplate == null)
                Debug.LogWarning($"[WorldPopulator] 둥지({spec.cell.x},{spec.cell.y}) 스폰 포인트 {i + 1}: " +
                                 "hasBoss인데 프리팹에 보스 배선이 없어 보스를 세울 수 없습니다.", nest);
        }

        // 맵이 정한 것보다 많으면 남는 자리는 버린다 — 옛 배치가 유령처럼 남지 않게
        if (nest.spawnPoints.Count > spec.spawnPoints.Length)
            nest.spawnPoints.RemoveRange(spec.spawnPoints.Length,
                                         nest.spawnPoints.Count - spec.spawnPoints.Length);
    }

    // ── 밤 진입로 ───────────────────────────────────────────────

    /// <summary>
    /// 밤 웨이브가 들어오는 자리. 빈 Transform이면 충분하므로 프리팹이 필요 없다.
    /// 제공자는 <b>BattleManager 아래</b>에 둔다 — 그쪽이 자기 자식에서 찾기 때문이다.
    /// </summary>
    static NightSpawnPointProvider PlaceNightSpawns(World world, Transform root, Transform battleRoot)
    {
        var map = world.Map;
        if (map.nightSpawnPoints == null || map.nightSpawnPoints.Length == 0) return null;

        var host = battleRoot != null ? battleRoot : root;
        var provider = host.GetComponentInChildren<NightSpawnPointProvider>(true);
        if (provider == null)
        {
            var go = new GameObject("NightSpawnPoints");
            go.transform.SetParent(host, false);
            provider = go.AddComponent<NightSpawnPointProvider>();
        }

        var points = new List<Transform>(map.nightSpawnPoints.Length);
        foreach (var cell in map.nightSpawnPoints)
        {
            var t = new GameObject($"NightSpawn_{cell.x}_{cell.y}").transform;
            t.SetParent(provider.transform, false);
            t.position = world.CellToWorldCenter(cell);
            points.Add(t);
        }

        provider.SetSpawnPoints(points);
        return provider;
    }
}
