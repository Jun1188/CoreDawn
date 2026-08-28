using UnityEngine;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 벨트의 모양 — 배치 시점에 결정되는 인스턴스 상태 (SO가 아님).
    ///
    /// <b>회전(RotationSteps)이 정하는 것은 입구다.</b> 모양은 그 입구로 들어온 아이템이
    /// 어디로 나가는지를 정한다 — 직선은 곧장, CurveL은 좌회전, CurveR은 우회전.
    ///
    /// 예전에는 반대였다(회전이 출구를 정하고 모양이 입구를 정함). 그러면 벨트를 깔다가
    /// T로 모양만 바꿨을 때 <b>입구가 다른 변으로 튀어</b> 방금 이어 붙인 상류 벨트와 끊긴다.
    /// 벨트는 상류에서 하류로 이어 나가며 까는 물건이라, 고정돼야 하는 쪽은 이미 연결된
    /// 입구이고 움직여야 하는 쪽이 아직 아무것도 없는 출구다.
    /// </summary>
    public enum BeltShape { Straight, CurveL, CurveR }

    /// <summary>
    /// 컨베이어 벨트. 연속된 같은 종류의 벨트는 BeltSegment로 묶여 처리된다.
    /// 직선/커브는 별도 SO가 아니라 배치 시 결정되는 모양(BeltShape) —
    /// 포트는 BuildPorts()로 계산해 Building.PortOverride에 주입한다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBelt", menuName = "Factory/Buildings/Belt")]
    public class BeltDataSO : BuildingDataSO
    {
        [Header("운반")]
        [Tooltip("아이템 이동 속도 (타일/초).")]
        public float speedTilesPerSec = 2f;

        [Header("커브 프리팹 (기본 prefab = 직선)")]
        public GameObject curveLPrefab;
        public GameObject curveRPrefab;

        public GameObject PrefabFor(BeltShape shape) => shape switch
        {
            BeltShape.CurveL => curveLPrefab,
            BeltShape.CurveR => curveRPrefab,
            _                => prefab,
        };

        public override IBuildingBehavior CreateBehavior(Building building)
            => new BeltBehavior(building);

        // ── 모양별 포트 계산 (모양 3 × 회전 4 = 12조합 캐시)

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
        ///
        /// 이걸 프리팹의 메시 회전으로 굽지 않고 코드에 두는 이유: 포트 규칙과 메시 방향은
        /// 반드시 같이 움직여야 하는 한 쌍인데, 프리팹에 구워 두면 규칙을 고칠 때
        /// 프리팹을 함께 고쳐야 한다는 사실이 코드 어디에도 남지 않는다.
        /// </summary>
        public static float MeshYaw(BeltShape shape, int rotSteps)
            => rotSteps * 90f + (shape == BeltShape.Straight ? 0f : 180f);

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

    // ─── 행동 ──────────────────────────────────────────────────────

    /// <summary>
    /// 컨베이어 벨트. 실제 아이템 이동은 BeltSegment가 담당하고,
    /// 이 행동은 입력 버퍼를 세그먼트에 올리는 것과 세그먼트 구동 대표 역할만 한다.
    /// </summary>
    public class BeltBehavior : IBuildingBehavior
    {
        readonly Building _b;
        public BeltBehavior(Building b) => _b = b;
        public void OnAfterPlaced() { }

        public void Tick(float dt)
        {
            var seg = _b.Factory.Belts.EnsureSegment(_b);  // 항상 세그먼트 존재

            // 입력 버퍼 아이템을 벨트 위로 (입구가 막혔으면 받아준 만큼만 소비).
            // TryAddItem은 세그먼트 입구(pos 0) 삽입 — 생산자로부터 입력을 받는 벨트는
            // 상류 벨트가 없는 벨트뿐이므로(1입력 포트) 항상 자기 세그먼트의 입구다.
            foreach (var (item, count) in _b.Input.Snapshot())
            {
                int moved = 0;
                while (moved < count && seg.TryAddItem(item)) moved++;
                if (moved > 0)
                {
                    _b.Input.TryConsume(item, moved);
                    _b.NotifyUpstream(); // 입력 버퍼에 자리 생김 → 막혀 있던 상류 깨움
                }
            }

            // 대표 벨트(입구 = 마지막 인덱스)가 세그먼트 전체를 1번만 구동
            if (seg.BeltCount > 0 && seg.Belts[^1] == _b)
                seg.Tick(dt);

            // 입구가 막혀 버퍼가 안 비면 다음 틱에 재시도
            if (_b.Input.HasAny)
                _b.Factory.MarkDirty(_b);
        }
    }
}
