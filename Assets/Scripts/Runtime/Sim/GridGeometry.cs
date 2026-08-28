using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 배치 격자의 기하 — 칸 크기와 원점. 칸(Vector2Int) ↔ 월드 좌표(Vector3) 변환의 정본.
    ///
    /// 심이 이걸 갖는 이유: 건물은 칸으로 놓이지만 몬스터의 공격 거리·플로우필드 목표는 월드 좌표로 잰다.
    /// 예전에는 이 변환이 PlacementSystem(MonoBehaviour)에만 있어서 심 건물이 자기 풋프린트의 월드 사각형을
    /// 내려면 뷰에 물어봐야 했다 — 심이 뷰에 기대는 역참조의 한 원인이었다.
    ///
    /// 값의 출처는 맵(MapDataSO) 하나다. PlacementSystem·GridManager도 같은 값을 따로 들고 있는데,
    /// 이는 5단계(심 루트로 통합)까지의 과도기다 — 출처가 하나라 어긋나지는 않는다.
    /// XZ 평면만 다룬다(지상전). y는 호출자가 안다.
    /// </summary>
    public readonly struct GridGeometry
    {
        public readonly float CellSize;
        public readonly Vector3 Origin;

        /// <summary>칸 1m·원점 0 — 맵이 없는 테스트 씬의 기본값(PlacementSystem 기본값과 같다).</summary>
        public static readonly GridGeometry Unit = new GridGeometry(1f, Vector3.zero);

        public GridGeometry(float cellSize, Vector3 origin)
        {
            CellSize = cellSize > 0f ? cellSize : 1f;
            Origin = origin;
        }

        /// <summary>칸의 왼쪽 아래(min x·z) 모서리.</summary>
        public Vector3 CornerOf(Vector2Int cell, float y = 0f)
            => new Vector3(Origin.x + cell.x * CellSize, y, Origin.z + cell.y * CellSize);

        /// <summary>원점 칸에서 size칸을 덮는 풋프린트의 중심.</summary>
        public Vector3 CenterOf(Vector2Int origin, Vector2Int size, float y = 0f)
            => CornerOf(origin, y) + new Vector3(size.x, 0f, size.y) * (CellSize * 0.5f);

        /// <summary>풋프린트의 월드 사각형(min·max). y는 둘 다 인자 그대로.</summary>
        public void RectOf(Vector2Int origin, Vector2Int size, float y, out Vector3 min, out Vector3 max)
        {
            min = CornerOf(origin, y);
            max = min + new Vector3(size.x * CellSize, 0f, size.y * CellSize);
        }

        /// <summary>월드 좌표가 놓인 칸 (내림).</summary>
        public Vector2Int CellOf(Vector3 world)
            => new Vector2Int(Mathf.FloorToInt((world.x - Origin.x) / CellSize),
                              Mathf.FloorToInt((world.z - Origin.z) / CellSize));

        /// <summary>점에서 풋프린트 사각형 경계까지의 XZ 거리. 안에 있으면 0.</summary>
        public static float DistanceToRect(Vector3 from, Vector3 min, Vector3 max)
        {
            float dx = Mathf.Max(min.x - from.x, 0f, from.x - max.x);
            float dz = Mathf.Max(min.z - from.z, 0f, from.z - max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public override string ToString() => $"GridGeometry(cell={CellSize}, origin={Origin})";
    }
}
