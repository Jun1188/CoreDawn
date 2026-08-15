using System;
using UnityEngine;

/// <summary>
/// 칸 하나의 성질. 통행과 건설을 <b>따로</b> 막는 것이 설계의 핵심이다 —
/// 강은 지나갈 수 있지만 지을 수 없어서 "방어선을 세울 수 없는 길"을 만들고,
/// 절벽은 통행 자체를 막아 몬스터의 동선을 가른다.
/// </summary>
public enum MapTile : byte
{
    Ground = 0,   // 지면 — 통행 O, 건설 O
    River  = 1,   // 강   — 통행 O, 건설 X
    Cliff  = 2,   // 절벽 — 통행 X, 건설 X
}

/// <summary>
/// 고정 맵 하나. "무엇을 만들 수 있는가"(GameData)와 달리 <b>"어디서 만드는가"</b>를 정의한다.
///
/// 정본은 <c>Assets/Data/Import/MapData.json</c>이고 <see cref="MapImporter"/>가 이 에셋으로 굽는다.
/// 맵은 여러 개라 GameData.json과 파일을 나눈다 — 맵 하나가 121×121이면 타일만 약 15KB다.
///
/// 좌표계는 GridSystem과 같다: 원점이 왼쪽 아래, x는 오른쪽, y는 월드의 <b>z</b> 축.
/// </summary>
[CreateAssetMenu(fileName = "NewMap", menuName = "Factory/Map")]
public class MapDataSO : GameDataSO
{
    [Header("크기 (타일)")]
    public int width;
    public int height;

    [Tooltip("코어 3×3의 왼쪽 아래 칸. 중심 칸이 정확히 하나여야 해서 width/height는 홀수를 쓴다.")]
    public Vector2Int core;

    [Tooltip("타일 격자 — 행 우선(y*width + x). 문자열이 아니라 바이트로 굽는다: 런타임에 매 칸을 조회하므로.")]
    [SerializeField] private byte[] tiles;

    public ResourceNodeSpec[] nodes;
    public NestSpec[] nests;

    // ── 타일 조회 ───────────────────────────────────────────────

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;
    public bool InBounds(Vector2Int cell) => InBounds(cell.x, cell.y);

    /// <summary>맵 밖은 절벽으로 취급한다 — 경계를 넘는 통행·건설을 한 번에 막는다.</summary>
    public MapTile TileAt(int x, int y)
    {
        if (!InBounds(x, y) || tiles == null || tiles.Length < width * height) return MapTile.Cliff;
        return (MapTile)tiles[y * width + x];
    }
    public MapTile TileAt(Vector2Int cell) => TileAt(cell.x, cell.y);

    // 통행·비용·속도는 타일에서 파생된다 — 규칙은 TileRules가 갖는다.
    // 맵은 "어느 칸이 무슨 타일인가"만 알면 된다.

    /// <summary>사각 풋프린트 전체가 건설 가능한가 — 건물은 한 칸이라도 걸치면 못 짓는다.</summary>
    public bool CanBuildFootprint(Vector2Int origin, Vector2Int size)
    {
        for (int dy = 0; dy < size.y; dy++)
            for (int dx = 0; dx < size.x; dx++)
                if (!TileRules.CanBuild(TileAt(origin.x + dx, origin.y + dy))) return false;
        return true;
    }

    // ── 임포터 전용 ─────────────────────────────────────────────
#if UNITY_EDITOR
    /// <summary>MapImporter가 json의 문자열 타일을 바이트 격자로 굽는다.</summary>
    public void EditorSetTiles(byte[] baked) => tiles = baked;
    public byte[] EditorTiles => tiles;
#endif
}

/// <summary>맵에 놓인 자원 광맥 하나. 채굴 난이도(extractInterval)는 광맥의 성질이라 여기 있다.</summary>
[Serializable]
public struct ResourceNodeSpec
{
    [Tooltip("캘 수 있는 아이템 — GameData.json의 광석 3종 중 하나.")]
    public ItemDataSO item;

    [Tooltip("풋프린트의 왼쪽 아래 칸.")]
    public Vector2Int cell;

    [Tooltip("정사각 풋프린트 한 변(1~3). 크기가 곧 등급이다.")]
    public int size;

    [Tooltip("배율 1 기준 1개당 초. 실제 채굴 시간 = 이 값 ÷ 채굴기 speedMultiplier.")]
    public float extractInterval;

    [Tooltip("이 광맥이 쌓아둘 수 있는 최대 재고.")]
    public int maxStock;
}

/// <summary>몬스터 둥지 하나 — 낮의 습격 조건과 밤 웨이브의 출구를 함께 정의한다.</summary>
[Serializable]
public struct NestSpec
{
    public Vector2Int cell;

    [Tooltip("플레이어가 이 안에 들면 경고가 뜬다.")]
    public float warningRange;

    [Tooltip("이 안으로 들어오면 방어 몬스터가 나온다. 경고 없이 튀어나오지 않도록 warningRange보다 작아야 한다.")]
    public float triggerRange;

    public int defenseSpawnAmount;
    public float defenseSpawnCooldown;

    [Tooltip("밤 웨이브가 나오는 자리들 — 둥지 기준 상대 좌표라 둥지를 옮겨도 배치가 유지된다.")]
    public SpawnPointSpec[] spawnPoints;
}

[Serializable]
public struct SpawnPointSpec
{
    public Vector2Int offset;
    public bool hasBoss;
}
