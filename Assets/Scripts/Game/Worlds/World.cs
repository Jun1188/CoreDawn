using UnityEngine;
using CoreDawn.Managers;
using CoreDawn.Navigation;
using CoreDawn.Placement;
using CoreDawn.Data;

namespace CoreDawn.Worlds
{
    /// <summary>
    /// 월드 씬의 유일한 배선 — "이 씬이 어떤 맵인가"를 선언한다.
    ///
    /// 맵 데이터(MapDataSO)는 json에서 구운 정본이고, 격자·건설·길찾기는 전부 이 하나에서 파생된다.
    /// 시스템들(GridManager·PlacementSystem)은 별도 씬으로 오므로 인스펙터 참조가 씬 경계를 넘지
    /// 못한다 — <see cref="GameBootstrap"/>이 여기서 맵을 읽어 각 시스템에 주입한다.
    /// 월드가 데이터를 들고, 부트스트랩이 배포하고, 시스템은 받기만 한다.
    ///
    /// 이 오브젝트의 <b>위치가 곧 맵의 (0,0) 타일</b>이다 — 맵을 통째로 옮기려면 이것을 옮긴다.
    /// </summary>
    [DisallowMultipleComponent]
    public class World : MonoBehaviour
    {
        [Tooltip("이 월드의 맵 데이터 — MapData.json에서 MapImporter가 구운 에셋.")]
        [SerializeField] private MapDataSO map;

        [Header("맵대로 세울 것들")]
        [Tooltip("광맥 프리팹 — 아이템·크기·채굴 속도는 맵이 정하고, 이건 껍데기다.")]
        [SerializeField] private GameObject resourceNodePrefab;

        [Tooltip("둥지 프리팹 — 반경·방어 수치는 맵이 정한다.")]
        [SerializeField] private GameObject nestPrefab;


        public MapDataSO Map => map;
        /// <summary>칸 한 변(m) — 맵 데이터의 값. 씬에 사본을 두지 않는다(GameBootstrap이 공장·배치·길찾기에 이 값을 주입한다).</summary>
        public float CellSize => map != null && map.cellSize > 0f ? map.cellSize : 1f;
        public GameObject ResourceNodePrefab => resourceNodePrefab;
        public GameObject NestPrefab => nestPrefab;

        /// <summary>맵 (0,0) 타일이 놓이는 월드 좌표.</summary>
        public Vector3 Origin => transform.position;

        /// <summary>타일 좌표 → 월드 좌표(칸의 왼쪽 아래).</summary>
        public Vector3 CellToWorld(Vector2Int cell) =>
            Origin + new Vector3(cell.x * CellSize, 0f, cell.y * CellSize);

        /// <summary>타일 좌표 → 칸 중앙의 월드 좌표.</summary>
        public Vector3 CellToWorldCenter(Vector2Int cell) =>
            CellToWorld(cell) + new Vector3(CellSize * 0.5f, 0f, CellSize * 0.5f);


        void OnDrawGizmosSelected()
        {
            if (map == null) return;

            // 맵 경계를 그려 배치 감각을 잡는다 (선택했을 때만)
            Vector3 size = new(map.width * CellSize, 0f, map.height * CellSize);
            Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.9f);
            Gizmos.DrawWireCube(Origin + size * 0.5f, size);

            // 코어 3×3 자리
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(CellToWorld(map.core) + new Vector3(CellSize * 1.5f, 0f, CellSize * 1.5f),
                                new Vector3(CellSize * 3f, 0f, CellSize * 3f));
        }
    }
}
