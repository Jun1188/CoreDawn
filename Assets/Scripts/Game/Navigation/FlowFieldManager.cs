using System.Collections.Generic;
using UnityEngine;
using CoreDawn.DayTime;
using CoreDawn.Entities;
using CoreDawn.Factory;
using CoreDawn.Sim;
using CoreDawn.Data;

namespace CoreDawn.Navigation
{
    // 플로우필드 구동 매니저 — 갱신 스케줄만 담당하고 계산은 FlowField(순수 C#)에 위임한다.
    // 갱신 조건 (기획):
    //   1. 밤 시작 시 최초 1회 (TimeManager.Cycle.NightStarted)
    //   2. 이후 rebuildInterval(1~3초)마다 1회
    //   3. 건물 배치/파괴 시 즉시 (BuildingEntity의 OnEnable/OnDisable → MarkDirty)
    // 낮에는 몬스터가 없으므로 주기 갱신을 쉰다. TimeManager가 없는 테스트 씬은 항상 갱신.
    public class FlowFieldManager : MonoBehaviour
    {
        public static FlowFieldManager Instance { get; private set; }

        [Tooltip("주기 갱신 간격(초). 1~3초 권장.")]
        [SerializeField, Range(1f, 3f)] private float rebuildInterval = 2f;

        [Tooltip("건물 데이터에 위협도가 없을 때 쓰는 시드 비용(구 씬 호환). 10 = 한 칸 거리.")]
        [SerializeField] private int fallbackGoalCost = 80;

        // 더블 버퍼 — 워커가 back을 채우는 동안 몬스터는 front를 계속 읽는다.
        // 계산이 끝나야 교체하므로 "반쯤 계산된 필드"를 보는 순간이 없다.
        private FlowField front = new FlowField();
        private FlowField back = new FlowField();

        private readonly List<FlowField.Goal> goalBuffer = new List<FlowField.Goal>();
        private readonly List<FlowField.Goal> workerGoals = new List<FlowField.Goal>();

        private System.Threading.Tasks.Task rebuildTask;
        private bool dirty;
        private float nextRebuildTime;

        public bool HasField => front.HasField;

        /// <summary>지금 읽고 있는 필드 — 시각화가 워커에서 훑을 때 이 참조를 잡아 둔다.</summary>
        public FlowField Field => front;

        /// <summary>필드를 다시 계산했다 — 시각화처럼 필드를 그대로 베껴 두는 쪽이 구독한다.</summary>
        public event System.Action FieldRebuilt;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // FlowFieldManager가 타워·둥지와 같은 통합 루트에 붙을 수 있으므로
                // 중복 정리 시 GameObject 전체를 파괴하면 안 된다 — 컴포넌트만 뗀다.
                Destroy(this);
                return;
            }
            else
            {
                Instance = this;
            }
        }

        private void Start()
        {
            // 밤 시작 시 최초 1회 갱신 예약
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.Cycle.NightStarted += _ => RebuildNow();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // 워커가 끝났으면 이번 프레임에 교체한다 — 교체와 통지는 메인 스레드에서만 일어난다
            if (rebuildTask != null && rebuildTask.IsCompleted)
            {
                var failed = rebuildTask.Exception;
                rebuildTask = null;

                if (failed != null) Debug.LogException(failed);
                else
                {
                    (front, back) = (back, front);
                    FieldRebuilt?.Invoke();
                }
            }

            // 낮에는 주기 갱신을 쉰다 (건물 변화는 MarkDirty로 쌓였다가 밤 시작 갱신에 반영)
            if (TimeManager.Instance != null && TimeManager.Instance.Phase == DayPhase.Day) return;

            if (dirty || Time.time >= nextRebuildTime)
            {
                RebuildNow();
            }
        }

        // 건물 배치/파괴 등 경로 지형이 바뀌었을 때 호출 — 다음 프레임에 재계산
        public void MarkDirty() => dirty = true;

        /// <summary>
        /// 다시 계산을 <b>건다</b> — 다익스트라는 워커 스레드에서 돌고, 결과 교체는 Update가 한다.
        ///
        /// 메인 스레드가 하는 일은 목표 수집과 비용 스냅샷뿐이다(둘 다 Unity 데이터를 만져야 한다).
        /// 맵 전체를 훑는 탐색은 프레임을 100ms 단위로 멎게 하므로 메인에 둘 수 없다.
        /// 이미 계산 중이면 건너뛴다 — 밀린 요청을 쌓아 봐야 같은 답을 두 번 구할 뿐이다.
        /// </summary>
        private void RebuildNow()
        {
            if (rebuildTask != null) return;

            dirty = false;
            nextRebuildTime = Time.time + rebuildInterval;

            var grid = GridManager.Instance;
            if (grid == null)
            {
                front.Clear();
                FieldRebuilt?.Invoke();
                return;
            }

            CollectGoals();

            // 목표 목록은 복사해서 넘긴다 — 워커가 읽는 동안 메인이 다음 수집으로 덮어쓰면 안 된다
            workerGoals.Clear();
            workerGoals.AddRange(goalBuffer);

            // 비용 필드는 그리드가 소유하고 항상 최신이다(건물이 바뀐 자리만 갱신됨)
            var costs = grid.Costs;
            var target = back;
            rebuildTask = System.Threading.Tasks.Task.Run(() => target.Rebuild(costs, workerGoals));
        }

        // 살아있는 건물이 차지한 셀을 목표로 수집. 단 벨트는 제외 —
        // 운송로(벨트)와 그 위의 아이템은 몬스터의 "진격 목표"가 아니다
        // (아이템/DroppedItem은 애초에 BuildingEntity이 아니라 목표 대상이 될 수 없다).
        // 경로를 막는 벨트는 기존대로 FlowFieldState의 사거리 판정으로 부술 수 있다.
        // 멀티타일 건물은 콜라이더 바운즈가 걸친 모든 셀을 시드로 넣는다.
        private void CollectGoals()
        {
            goalBuffer.Clear();
            var grid = GridManager.Instance;

            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null) return;

            foreach (var building in boot.Factory.Buildings)
            {
                if (!IsAlive(building)) continue;

                // 웨이브의 목표는 플레이어의 것이다 — 둥지(Monster)는 자기 편이고, 나무(Neutral)는 누구의 것도 아니다.
                // 나무를 목표에 넣으면 몬스터가 그쪽으로 돌아가고, 나무는 다시 자라지 않아 그 손실이 영구적이다.
                // 벨트도 뺀다 — 운송로는 진격 목표가 아니다. 경로를 막는 벨트는 FlowFieldState의 사거리 판정이 부순다.
                //
                // <b>사거리 판정(FlowFieldState)에서는 빼지 않는다.</b> 건물이 선 칸은
                // Walkable=false 라 "뚫고는 가도 걸어서는 못 지나간다" — 목표에서도 빼고 공격
                // 대상에서도 빼면 나무에 막힌 몬스터가 부수지도 돌아가지도 못하고 갇힌다.
                if (building.Owner.Faction != Faction.Player) continue;
                if (building.IsConveyor) continue;

                // 무엇부터 노릴지는 건물이 정한다 — 코어 0(최종 목표), 공격 타워는 낮게(먼저 부순다),
                // 일반 건물은 높게(굳이 돌아가지 않는다). 시드가 작을수록 그 목표가 가깝게 계산된다.
                //
                // 시드는 "월드 칸 = 10" 단위로 적혀 있는데 길찾기 격자는 월드 칸을 subdiv 등분한다.
                // 그대로 쓰면 이동 비용만 subdiv배로 불어나 시드의 우회 허용 거리가 그만큼 줄어든다 —
                // 80(8칸)이 4등분 격자에서는 2칸이 되어, 코어로 가던 몬스터가 두 칸 옆 건물로 새는 원인이었다.
                int seedCost = building.IsCore ? 0 : building.Building.ThreatSeedCost;
                seedCost *= Mathf.Max(1, grid.Subdiv);

                // 점유 풋프린트가 기준이다 — 심이 낸다(뷰·콜라이더에 묻지 않는다). 콜라이더는 메시마다 조각나 있어서
                // 하나만 집으면 건물의 일부(코어의 안테나 한 짝)만 목표가 되고, 전부 합쳐도 모델일 뿐이다.
                // 풋프린트의 바깥 모서리는 이미 다음 칸이라 살짝 안쪽을 찍는다.
                building.WorldRect(0f, out Vector3 lo, out Vector3 hi);
                hi -= new Vector3(0.01f, 0f, 0.01f);

                Node min = grid.NodeFromWorldPoint(lo);
                Node max = grid.NodeFromWorldPoint(hi);
                if (min != null && max != null)
                {
                    for (int x = min.gridCoord.x; x <= max.gridCoord.x; x++)
                        for (int y = min.gridCoord.y; y <= max.gridCoord.y; y++)
                            goalBuffer.Add(new FlowField.Goal(new Vector2Int(x, y), seedCost));
                    continue;
                }

                // 그리드 밖에 걸친 경우 중심 셀 하나만 시드로
                Node node = grid.NodeFromWorldPoint(building.Owner.Position);
                if (node != null) goalBuffer.Add(new FlowField.Goal(node.gridCoord, seedCost));
            }
        }

        // ── 공격 대상 ────────────────────────────────────────────────

        /// <summary>
        /// 이 자리에서 <b>진격 경로 위에 서 있는</b> 건물 — 지금 부숴야 앞으로 갈 수 있는 것.
        ///
        /// 사거리 안이라도 경로 밖 건물은 돌려주지 않는다. "닿는 건물은 다 친다"로 두면 몬스터가
        /// 코어로 가다 말고 길가의 나무·설비마다 멈춰 서고, 그게 곧 "코어를 우선하지 않는다"였다.
        ///
        /// 필드는 건물을 "비싼 길"로 계산하므로 next 사슬이 건물 칸을 지난다는 것은 곧
        /// "돌아가는 것보다 뚫는 게 싸다"는 판정이다 — 뚫을 건물만 여기서 잡힌다.
        /// 목표 건물(시드)은 사슬의 끝 칸이 그 건물 자리라 같은 방식으로 잡힌다.
        /// </summary>
        public BuildingModule FindBreachTarget(Vector3 from, float range)
        {
            var grid = GridManager.Instance;
            if (grid == null || !front.HasField) return null;

            Node node = grid.NodeFromWorldPoint(from);
            if (node == null) return null;

            // 사거리만큼만 사슬을 따라간다 — 대각이 섞여 칸 수가 들쭉날쭉하니 두 칸 여유를 둔다
            int maxSteps = Mathf.CeilToInt(range / Mathf.Max(0.01f, grid.cellSize)) + 2;
            Vector2Int cell = node.gridCoord;

            for (int i = 0; i <= maxSteps; i++)
            {
                var building = grid.BuildingAt(cell);
                if (IsAlive(building) && building.DistanceTo(from) <= range)
                    return building;

                // 사슬의 끝 = 목표 칸에 닿았거나 길이 없다. 목표가 심 밖 건물(씬에 직접 놓인 코어 등)이면
                // 위 조회가 null이라 여기서 사거리로 한 번 더 본다 — 도착했는데 손을 놓고 서 있지 않게
                if (!front.TryGetNext(cell, out var next))
                    return FindClosestInRange(from, range);
                cell = next;
            }
            return null;
        }

        /// <summary>살아 있는 심 건물인가 — 제거되지 않았고 엔티티가 죽지 않았다.</summary>
        static bool IsAlive(BuildingModule b)
        {
            if (b == null || b.IsRemoved) return false;
            var e = b.Owner;
            return e != null && !e.IsRemoved && !(e.Health != null && e.Health.IsDead);
        }

        /// <summary>
        /// 사거리 내 가장 가까운 살아있는 건물 — 사슬의 끝에서 손을 놓지 않기 위한 폴백(구 BuildingView.FindClosestInRange).
        /// 몬스터 편(둥지)은 뺀다 — 자기 집을 부수지 않는다. 나무(Neutral)는 길을 막으면 부순다.
        /// 풋프린트 경계까지의 거리(Building.DistanceTo)로 잰다 — 멀티타일 건물 고려.
        /// </summary>
        public BuildingModule FindClosestInRange(Vector3 from, float range)
        {
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null) return null;

            BuildingModule closest = null;
            float minDistance = float.MaxValue;
            foreach (var b in boot.Factory.Buildings)
            {
                if (!IsAlive(b) || b.Owner.Faction == Faction.Monster) continue;
                float dist = b.DistanceTo(from);
                if (dist <= range && dist < minDistance)
                {
                    minDistance = dist;
                    closest = b;
                }
            }
            return closest;
        }

        // ── 조회 (시각화·검증용) ────────────────────────────────────

        /// <summary>이 칸에서 목표까지의 누적 비용. 도달 불가면 false.</summary>
        public bool TryGetCost(Vector2Int cell, out int cost) => front.TryGetCost(cell, out cost);

        /// <summary>이 칸의 다음 칸. 목표 칸 자체이거나 도달 불가면 false.</summary>
        public bool TryGetNextCell(Vector2Int cell, out Vector2Int next) => front.TryGetNext(cell, out next);

        // 현재 위치 셀에서 목표로 가는 방향 벡터. 필드 없음/목표 도달/맵 밖이면 zero.
        public Vector3 GetDirection(Vector3 worldPosition)
        {
            var grid = GridManager.Instance;
            if (grid == null || !front.HasField) return Vector3.zero;

            Node node = grid.NodeFromWorldPoint(worldPosition);
            if (node == null) return Vector3.zero;
            if (!front.TryGetNext(node.gridCoord, out Vector2Int nextCell)) return Vector3.zero;

            Node nextNode = grid.GetNode(nextCell);
            if (nextNode == null) return Vector3.zero;

            Vector3 dir = nextNode.worldPosition - worldPosition;
            dir.y = 0f;
            return dir.normalized;
        }
    }
}
