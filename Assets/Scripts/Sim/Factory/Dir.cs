using UnityEngine;

namespace CoreDawn.Sim
{
    public static class Dir
    {
        static readonly Vector2Int[] _v = { new(0,1), new(1,0), new(0,-1), new(-1,0) };

        public static Vector2Int ToVec(Direction d) => _v[(int)d];
        public static Direction   Opposite(Direction d) => (Direction)(((int)d + 2) % 4);

        // 시계 방향으로 steps만큼 회전 (건물 회전 지원용)
        public static Direction RotateCW(Direction d, int steps = 1) =>
            (Direction)(((int)d + steps % 4 + 4) % 4);

        /// <summary>
        /// 풋프린트 내 셀 좌표의 시계 방향 90° 회전.
        /// 원점 기준 수학 회전 (x,y)→(y,−x)가 아니라, 회전 후에도 origin이
        /// 왼쪽 아래를 유지하도록 재앵커링한다: (x,y) → (y, w−1−x).
        /// w = 회전 전 풋프린트의 가로 크기.
        /// </summary>
        public static Vector2Int RotateCellCW(Vector2Int v, int footprintWidth)
            => new(v.y, footprintWidth - 1 - v.x);
    }
}
