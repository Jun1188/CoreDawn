using UnityEngine;

namespace CoreDawn.Placement
{
    /// <summary>
    /// 월드 좌표 ↔ 그리드 좌표 변환만 담당하는 순수 로직 클래스.
    /// 점유 데이터는 들고 있지 않는다 (그건 PlacementSystem의 책임).
    /// 바닥은 XZ 평면, 높이는 Y로 가정한다.
    /// </summary>
    public class GridSystem
    {
        public float CellSize { get; }
        private readonly Vector3 origin;

        public GridSystem(float cellSize, Vector3 origin)
        {
            this.CellSize = cellSize;
            this.origin = origin;
        }

        /// <summary>월드 좌표가 속한 셀을 구한다. (반드시 Floor — Round 쓰면 경계에서 어긋남)</summary>
        public Vector2Int WorldToGrid(Vector3 world)
        {
            int x = Mathf.FloorToInt((world.x - origin.x) / CellSize);
            int z = Mathf.FloorToInt((world.z - origin.z) / CellSize);
            return new Vector2Int(x, z);
        }

        /// <summary>셀의 코너(왼쪽 아래) 월드 좌표.</summary>
        public Vector3 GridToWorld(Vector2Int cell)
            => new Vector3(cell.x, 0, cell.y) * CellSize + origin;

        /// <summary>단일 셀의 중앙 월드 좌표.</summary>
        public Vector3 GridToWorldCenter(Vector2Int cell)
            => GridToWorld(cell) + new Vector3(0.5f, 0, 0.5f) * CellSize;

        /// <summary>
        /// origin 셀을 왼쪽 아래로 하는 size 풋프린트의 중앙 월드 좌표.
        /// 프리팹 피벗이 가운데라는 가정 하에 프리뷰/설치 위치로 사용.
        /// </summary>
        public Vector3 GetFootprintCenter(Vector2Int originCell, Vector2Int size)
            => GridToWorld(originCell) + new Vector3(size.x, 0, size.y) * 0.5f * CellSize;
    }
}
