using System;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Factory;
using CoreDawn.Worlds;

namespace CoreDawn.Data
{
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
    public class MapDataSO : ScriptableObject
    {
        [Header("식별")]
        [Tooltip("맵 id — MapData.json의 id(관례 \"Map:이름\"). 세이브·씬(World.map)이 이 에셋을 가리킨다.")]
        [SerializeField] string id;
        public string Id => id;

        [Header("표시")]
        public string displayName;
        [TextArea] public string description;

        [Header("크기 (타일)")]
        public int width;
        public int height;

        [Tooltip("코어 3×3의 왼쪽 아래 칸. 중심 칸이 정확히 하나여야 해서 width/height는 홀수를 쓴다.")]
        public Vector2Int core;

        [Tooltip("타일 격자 — 행 우선(y*width + x). 문자열이 아니라 바이트로 굽는다: 런타임에 매 칸을 조회하므로.")]
        [SerializeField] private byte[] tiles;

        public ResourceNodeSpec[] nodes;
        public NestSpec[] nests;

        /// <summary>
        /// 밤 웨이브가 맵으로 들어오는 자리들.
        ///
        /// <b>둥지의 스폰 지점과 일부러 나눠 둔다.</b> 둥지 것은 낮에 플레이어가 다가왔을 때
        /// 방어 몬스터가 튀어나오는 자리이고, 이쪽은 밤에 코어를 향해 밀려드는 진입로다.
        /// 하나로 합치면 낮의 보스 자리가 밤의 대문이 되어 버린다.
        /// </summary>
        public Vector2Int[] nightSpawnPoints;

        /// <summary>
        /// 나무가 선 칸들.
        ///
        /// 지형이 아니라 <b>맵이 정하는 배치물</b>이다 — 나무는 칸을 영구히 막으므로 어디에 서느냐가
        /// 곧 "어디에 지을 수 없느냐"이고, 그건 광맥·둥지와 같은 층위의 레벨 디자인이다.
        /// 그래서 지형 생성기가 흩뿌리는 장식이 아니라 맵 에디터에서 찍고 고치는 데이터로 둔다.
        ///
        /// 모형(어느 프리팹·크기·각도)은 여기 없다 — 런타임이 칸 좌표에서 결정론적으로 뽑는다.
        /// 그루마다 저장하면 맵 하나에 수백 줄이 늘어나는데, 그 값들은 사람이 고칠 것이 아니다.
        /// </summary>
        public Vector2Int[] trees;

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
        [Tooltip("이 칸에 묻힌 자원의 팩 id(coredawn:item/iron_ore). 광맥은 한 칸짜리다 — 넓은 광맥은 칸을 여럿 놓는다. 재생·상한·난이도 수치는 팩의 광맥 정의(ResourceDeposit)가 갖는다.")]
        public string itemId;
        [Tooltip("광맥 칸.")]
        public Vector2Int cell;
    }

    /// <summary>몬스터 둥지 하나 — 낮의 습격 조건과 방어 몬스터가 나오는 자리를 정의한다.</summary>
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

        [Tooltip("방어 몬스터가 나오는 자리들 — 둥지 기준 상대 좌표라 둥지를 옮겨도 배치가 유지된다.")]
        public SpawnPointSpec[] spawnPoints;

        // ── 교전 규칙 (NestEngagementZone) ──────────────────────────
        // 둥지가 "언제 얼마나 달려드는가"를 맵이 정한다. 같은 프리팹을 쓰면서도
        // 초반 둥지는 얌전하고 안쪽 둥지는 사납게 만들 수 있다.

        [Tooltip("이보다 가까우면 추가 스폰을 멈춘다 — 코앞에서 무한히 쏟아지지 않게. 0이면 교전 구역을 두지 않는다.")]
        public float engageMinRange;

        [Tooltip("이 밖이면 아예 반응하지 않는다.")]
        public float engageMaxRange;

        [Tooltip("이미 교전한 몬스터가 쫓아오는 한계. 최대 반경보다 넓어야 한다.")]
        public float chaseRange;

        [Tooltip("이보다 멀어지면 둥지로 돌아간다.")]
        public float leashRange;

        [Tooltip("낮에만 이 규칙을 적용할지. 밤에는 웨이브가 주도하므로 보통 true.")]
        public bool engageDayOnly;

        [Tooltip("낮 방어 몬스터·보스전 지원군의 팩 id. 비면 스포너의 기본 종류.")]
        public string defender;



    }

    [Serializable]
    public struct SpawnPointSpec
    {
        public Vector2Int offset;
        [Tooltip("이 자리에 서는 보스의 팩 id(coredawn:entity/boss). 비면 보스 없음 — 자리는 웨이브 출구로만 쓴다.")]
        public string boss;
        public bool HasBoss => !string.IsNullOrEmpty(boss);
    }
}
