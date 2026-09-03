using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Navigation
{
    /// <summary>
    /// A* 경로 요청 창구 — 계산은 워커 스레드에서, 결과 전달은 메인 스레드에서.
    ///
    /// 몬스터마다 제 경로를 즉석에서 구하면 그 비용이 전부 프레임에 얹힌다(측정: 12마리에 100ms).
    /// 그래서 요청을 받아 두고 워커가 하나씩 풀며, 끝난 것만 다음 Update에 돌려준다.
    /// 경로가 한 프레임 늦게 오는 것은 문제가 되지 않는다 — 추적은 어차피 0.5초마다 갱신한다.
    ///
    /// <b>왜 워커 하나인가</b>: A*는 작업 배열을 재사용해 할당을 없앴고, 그 배열은 인스턴스에 딸려
    /// 있다. 워커를 늘리려면 인스턴스도 늘려야 하는데, 지금은 요청이 초당 수십 건이라 하나로 충분하다.
    /// 늘릴 때가 오면 PathFinder를 워커 수만큼 두고 라운드로빈하면 된다.
    ///
    /// 비용 필드는 <see cref="GridManager.Costs"/>를 그대로 읽는다 — 건물이 바뀐 자리만 갱신되므로
    /// 워커가 읽는 도중 값이 조금 바뀔 수는 있다. 배열 크기는 그대로라 안전하고,
    /// 최악의 결과는 "방금 놓인 건물을 모르는 경로" 한 번인데 다음 갱신이 곧 바로잡는다.
    /// </summary>
    public class PathRequestQueue : MonoBehaviour
    {
        public static PathRequestQueue Instance { get; private set; }

        struct Job
        {
            public Vector2Int Start, Goal;
            public bool IgnoreBuildings;
            public Action<List<Vector2Int>> OnDone;
        }

        readonly Queue<Job> pending = new();
        readonly Queue<(Action<List<Vector2Int>> callback, List<Vector2Int> path)> finished = new();
        readonly PathFinder finder = new PathFinder();
        readonly CostField costSnapshot = new CostField();   // 워커용 스냅샷 — FlowFieldManager와 같은 이유

        System.Threading.Tasks.Task worker;

        [Tooltip("한 프레임에 돌려주는 결과 수 — 콜백이 몰려 프레임이 튀지 않게 나눠 낸다.")]
        [SerializeField, Range(1, 32)] int deliveriesPerFrame = 8;

        /// <summary>대기 중인 요청 수 — 진단용.</summary>
        public int PendingCount { get { lock (pending) return pending.Count; } }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 경로를 요청한다. 결과는 <paramref name="onDone"/>으로 <b>메인 스레드에서</b> 온다
        /// (못 찾으면 null, 이미 도착이면 빈 목록 — 동기 시절 반환값과 같은 규약).
        /// </summary>
        public void Request(Vector2Int start, Vector2Int goal, bool ignoreBuildings,
                            Action<List<Vector2Int>> onDone)
        {
            if (onDone == null) return;

            lock (pending)
            {
                pending.Enqueue(new Job
                {
                    Start = start,
                    Goal = goal,
                    IgnoreBuildings = ignoreBuildings,
                    OnDone = onDone,
                });
            }
        }

        void Update()
        {
            DeliverFinished();
            PumpWorker();
        }

        void DeliverFinished()
        {
            for (int i = 0; i < deliveriesPerFrame; i++)
            {
                Action<List<Vector2Int>> callback;
                List<Vector2Int> path;

                lock (finished)
                {
                    if (finished.Count == 0) return;
                    (callback, path) = finished.Dequeue();
                }

                try { callback(path); }
                catch (Exception e) { Debug.LogException(e); }   // 한 구독자의 사고가 큐를 세우지 않게
            }
        }

        void PumpWorker()
        {
            if (worker != null && !worker.IsCompleted) return;

            if (worker != null)
            {
                var failed = worker.Exception;
                worker = null;
                if (failed != null) Debug.LogException(failed);
            }

            var grid = GridManager.Instance;
            if (grid == null || !grid.Costs.IsReady) return;

            lock (pending) { if (pending.Count == 0) return; }

            // 워커가 없는 지금이 스냅샷을 갈아 끼울 유일한 때 — 위에서 worker 완료를 확인했다
            if (costSnapshot.Version != grid.Costs.Version) costSnapshot.CopyFrom(grid.Costs);
            var costs = costSnapshot;
            worker = System.Threading.Tasks.Task.Run(() => Drain(costs));
        }

        /// <summary>워커 본체 — 쌓인 요청을 한 번에 비운다. Unity API는 하나도 부르지 않는다.</summary>
        void Drain(CostField costs)
        {
            while (true)
            {
                Job req;
                lock (pending)
                {
                    if (pending.Count == 0) return;
                    req = pending.Dequeue();
                }

                List<Vector2Int> path = finder.FindPath(costs, req.Start, req.Goal, req.IgnoreBuildings);

                lock (finished) finished.Enqueue((req.OnDone, path));
            }
        }
    }
}
