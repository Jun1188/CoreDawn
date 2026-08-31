using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 벨트 모양·회전 기하(구 BeltDataSO의 정적들) — 포트 규칙과 메시 방향은 반드시 같이 움직여야
    /// 하는 한 쌍이라 코드 한 곳에 둔다. 프리팹에 구워 두면 규칙을 고칠 때 프리팹을 함께
    /// 고쳐야 한다는 사실이 코드 어디에도 남지 않는다.
    /// </summary>
    public static class BeltGeometry
    {
        /// <summary>
        /// 회전이 정하는 입력 방향 — 모양과 무관하게 <b>직선 벨트와 같다</b>.
        /// rotSteps=0이면 West(서쪽 이웃에게서 받아 동쪽으로 흐른다).
        /// </summary>
        public static Direction InputDirFor(int rotSteps) => Dir.RotateCW(Direction.West, rotSteps);

        /// <summary>
        /// 모양이 정하는 출력 방향 — 입구로 들어온 아이템의 진행 방향 기준으로
        /// 직선은 그대로, CurveL은 좌회전, CurveR은 우회전.
        /// rotSteps=0이면 각각 East / North / South.
        /// </summary>
        public static Direction OutputDirFor(BeltShape shape, int rotSteps) => shape switch
        {
            BeltShape.CurveL => Dir.RotateCW(Direction.East, rotSteps + 3),
            BeltShape.CurveR => Dir.RotateCW(Direction.East, rotSteps + 1),
            _                => Dir.RotateCW(Direction.East, rotSteps),
        };

        /// <summary>
        /// 모양별 메시 요(yaw). 커브 프리팹의 <b>뚫린 두 변</b>이 포트와 맞도록 돌린다.
        ///
        /// 프리팹 원본(회전 0)에서 실측한 열린 변:
        ///   Belt(직선) = 서·동,  BeltLCurve = 남·동,  BeltRCurve = 북·동
        /// 그런데 회전 0에서 포트가 요구하는 열린 변은
        ///   직선 = 서·동,  CurveL = 서·북,  CurveR = 서·남
        /// 이라 두 커브 모두 180°가 모자란다. 회전 4방향 전부에서 같은 보정이므로 상수다.
        /// </summary>
        public static float MeshYaw(BeltShape shape, int rotSteps)
            => rotSteps * 90f + (shape == BeltShape.Straight ? 0f : 180f);

        // 모양 3 × 회전 4 = 12조합 캐시
        static readonly PortDefinition[][] _portCache = new PortDefinition[12][];

        /// <summary>모양+회전에 맞는 포트 쌍. rotSteps=0일 때 입력은 언제나 West.</summary>
        public static PortDefinition[] BuildPorts(BeltShape shape, int rotSteps)
        {
            int steps = (rotSteps % 4 + 4) % 4;
            int key   = (int)shape * 4 + steps;
            if (_portCache[key] != null) return _portCache[key];

            return _portCache[key] = new[]
            {
                new PortDefinition { IsInput = true,  Direction = InputDirFor(steps),         LocalOffset = Vector2Int.zero },
                new PortDefinition { IsInput = false, Direction = OutputDirFor(shape, steps), LocalOffset = Vector2Int.zero },
            };
        }
    }
}
