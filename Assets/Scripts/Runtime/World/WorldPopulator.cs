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
        var provider = PlaceNightSpawns(world, root, battleRoot);

        Debug.Log($"[WorldPopulator] '{world.Map.Id}' 배치 — 광맥 {nodes} · 둥지 {nests} · " +
                  $"밤 진입로 {(provider != null ? provider.SpawnPoints.Count : 0)}", world);
        return provider;
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

            var go = Object.Instantiate(world.ResourceNodePrefab, root);
            go.name = $"Node_{spec.item.name}_{spec.cell.x}_{spec.cell.y}";

            // 오브젝트 위치가 풋프린트 <b>중앙</b>이다(ResourceNode의 규약) — 왼쪽 아래 칸에서
            // 크기의 절반만큼 밀어야 칸에 맞는다. 1칸짜리는 그대로 칸 중앙이다.
            int size = Mathf.Max(1, spec.size);
            Vector3 corner = world.CellToWorld(spec.cell);
            go.transform.position = corner + new Vector3(size, 0f, size) * (0.5f * world.CellSize);

            var node = go.GetComponent<ResourceNode>();
            if (node != null) node.Configure(spec.item, size, spec.extractInterval, spec.maxStock);
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
    /// 그것을 옮겨 쓰고, 모자라면 새로 만든다 — 프리팹이 들고 있는 보스 배선을 잃지 않기 위해서다.
    /// </summary>
    static void ApplySpawnPoints(World world, MonsterNest nest, in NestSpec spec)
    {
        if (spec.spawnPoints == null || spec.spawnPoints.Length == 0) return;
        if (nest.spawnPoints == null) nest.spawnPoints = new List<MonsterNest.NestSpawnPoint>();

        for (int i = 0; i < spec.spawnPoints.Length; i++)
        {
            var point = spec.spawnPoints[i];
            Vector3 pos = world.CellToWorldCenter(spec.cell + point.offset);

            if (i < nest.spawnPoints.Count && nest.spawnPoints[i] != null && nest.spawnPoints[i].point != null)
            {
                nest.spawnPoints[i].point.position = pos;
                continue;
            }

            var t = new GameObject($"SpawnPoint_{i + 1}").transform;
            t.SetParent(nest.transform, false);
            t.position = pos;

            if (i < nest.spawnPoints.Count && nest.spawnPoints[i] != null) nest.spawnPoints[i].point = t;
            else nest.spawnPoints.Add(new MonsterNest.NestSpawnPoint { point = t });
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
