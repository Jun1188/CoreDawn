using System;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>칸 하나의 성질. 통행과 건설을 <b>따로</b> 막는다 — 강은 지나가되 못 짓고(방어선 못 세우는 길), 절벽은 통행을 막아 동선을 가른다.</summary>
    public enum MapTile : byte
    {
        Ground = 0,   // 지면 — 통행 O, 건설 O
        River  = 1,   // 강   — 통행 O, 건설 X
        Cliff  = 2,   // 절벽 — 통행 X, 건설 X
    }

    [Serializable] public struct StartItemSpec { public string itemId; public int amount; }

    /// <summary>광맥 한 칸 — 수치(재생·상한·난이도)는 팩의 광맥 정의(ResourceDeposit)가 갖는다. 넓은 광맥은 칸을 여럿 놓는다.</summary>
    [Serializable] public struct ResourceNodeSpec { public string itemId; public Vector2Int cell; }

    [Serializable]
    public struct SpawnPointSpec
    {
        public Vector2Int offset;   // 둥지 기준 상대 좌표 — 둥지를 옮겨도 배치가 유지된다
        public string boss;         // 이 자리에 서는 보스의 팩 id. 비면 보스 없음(자리는 웨이브 출구)
        public bool HasBoss => !string.IsNullOrEmpty(boss);
    }

    [Serializable]
    public struct NestSpec
    {
        public Vector2Int cell;
        public float warningRange;          // 플레이어가 이 안에 들면 경고
        public float triggerRange;          // 이 안이면 방어 몬스터 스폰(경고보다 작아야)
        public int defenseSpawnAmount;
        public float defenseSpawnCooldown;
        public SpawnPointSpec[] spawnPoints;
        public float engageMinRange;        // 0이면 교전 구역 없음
        public float engageMaxRange;
        public float chaseRange;
        public float leashRange;
        public bool engageDayOnly;
        public string defender;             // 낮 방어 몬스터 팩 id. 비면 스포너 기본
    }

    /// <summary>
    /// 고정 맵 하나 — "어디서 만드는가"의 정본. 팩 <c>maps/&lt;이름&gt;.json</c>에서 읽는다(PackMaps).
    /// 구 MapDataSO(씬이 참조하던 구운 에셋)의 후계 — 이제 씬은 맵 <b>id</b>만 들고, 데이터는 팩이 정본이다(5a-4d).
    /// 좌표계는 GridSystem과 같다: 원점 왼쪽 아래, x 오른쪽, y는 월드의 z.
    /// </summary>
    public sealed class MapDef
    {
        public string Id;               // 팩 id — coredawn:map/test_map0
        public string displayName;
        public string description;
        public int width, height;
        public float cellSize;          // 칸 한 변(m) — 공장·배치·길찾기가 이 값으로 격자를 잡는다
        public Vector2Int core;         // 코어(3×3)의 원점 칸
        public byte[] tiles;            // width*height, 행 우선. 값 = MapTile
        public ResourceNodeSpec[] nodes = Array.Empty<ResourceNodeSpec>();
        public NestSpec[] nests = Array.Empty<NestSpec>();
        public Vector2Int[] nightSpawnPoints = Array.Empty<Vector2Int>();   // 밤 웨이브 진입로(둥지 스폰 지점과 별개)
        public Vector2Int[] trees = Array.Empty<Vector2Int>();
        public StartItemSpec[] startItems = Array.Empty<StartItemSpec>();

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;
        public bool InBounds(Vector2Int cell) => InBounds(cell.x, cell.y);

        public MapTile TileAt(int x, int y)
        {
            if (!InBounds(x, y) || tiles == null) return MapTile.Cliff;   // 맵 밖 = 절벽 취급
            return (MapTile)tiles[y * width + x];
        }

        public MapTile TileAt(Vector2Int cell) => TileAt(cell.x, cell.y);

        /// <summary>발자국 전체가 지면(건설 가능)인가.</summary>
        public bool CanBuildFootprint(Vector2Int origin, Vector2Int size)
        {
            for (int y = origin.y; y < origin.y + size.y; y++)
                for (int x = origin.x; x < origin.x + size.x; x++)
                    if (TileAt(x, y) != MapTile.Ground) return false;
            return true;
        }
    }
}
