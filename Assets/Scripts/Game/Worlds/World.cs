using UnityEngine;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.Worlds
{
    /// <summary>
    /// 월드 루트 — 격자 원점·칸 크기의 기준점. 씬은 맵 <b>id</b>만 들고,
    /// 맵 데이터(MapDef)는 팩 <c>maps/*.json</c>이 정본이다(5a-4d, MapDataSO 퇴역).
    /// 격자·건설·길찾기는 전부 이 하나에서 파생된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class World : MonoBehaviour
    {
        [Tooltip("이 월드의 맵 — 팩 id. packs/<pack>/maps/<이름>.json에서 읽는다(PackMaps).")]
        [SerializeField] private string mapId = "coredawn:map/test_map0";

        MapDef map;
        string loadedId;      // 마지막으로 읽으려 한 id
        int loadedVersion;    // PackMaps.Clear(맵 재내보내기)가 있었으면 다시 읽는다
        float failedUntil;    // 실패는 잠깐만 기억한다 — 미리보기·기즈모가 매 프레임 오류를 쏟지 않으면서도, 파일이 생기면 스스로 회복하게

        public string MapId => mapId;

        public MapDef Map
        {
            get
            {
                bool stale = loadedId != mapId || loadedVersion != PackMaps.Version
                          || (map == null && Time.realtimeSinceStartup >= failedUntil);
                if (stale)
                {
                    loadedId = mapId;
                    loadedVersion = PackMaps.Version;
                    map = PackMaps.Of(PackLoader.CurrentPack, mapId);
                    if (map == null) failedUntil = Time.realtimeSinceStartup + 5f;
                }
                return map;
            }
        }

        public float CellSize => Map != null && Map.cellSize > 0f ? Map.cellSize : 1f;

        public Vector3 Origin => transform.position;

        /// <summary>타일 좌표 → 월드 좌표(칸의 왼쪽 아래).</summary>
        public Vector3 CellToWorld(Vector2Int cell) =>
            Origin + new Vector3(cell.x * CellSize, 0f, cell.y * CellSize);

        /// <summary>타일 좌표 → 칸 중앙의 월드 좌표.</summary>
        public Vector3 CellToWorldCenter(Vector2Int cell) =>
            CellToWorld(cell) + new Vector3(CellSize * 0.5f, 0f, CellSize * 0.5f);


        void OnDrawGizmosSelected()
        {
            var m = Map;
            if (m == null) return;

            // 맵 경계를 그려 배치 감각을 잡는다 (선택했을 때만)
            Vector3 size = new(m.width * CellSize, 0f, m.height * CellSize);
            Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.9f);
            Gizmos.DrawWireCube(Origin + size * 0.5f, size);

            // 코어 3×3 자리
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(CellToWorld(m.core) + new Vector3(CellSize * 1.5f, 0f, CellSize * 1.5f),
                                new Vector3(CellSize * 3f, 0f, CellSize * 3f));
        }
    }
}
