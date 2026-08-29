using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;
using CoreDawn.Data;

namespace CoreDawn.Factory
{
    // ================================================================
    //  FactorySystem.cs
    //  공장 시뮬레이션의 루트 — plain C#, Unity 씬/컴포넌트 의존 없음
    //
    //  포함:
    //    FactorySystem — 시계 + Dirty Queue + Wake 예약 + 배치/제거 진입점
    //    GridIndex     — 좌표 → BuildingModule O(1) 조회
    //
    //  Unity와의 접점은 FactoryBootstrap(드라이버)과 BuildingView(씬 표현)뿐.
    //  씬 없이 생성해 Advance()를 직접 호출하면 헤드리스로 돌릴 수 있다 (테스트용).
    // ================================================================

    /// <summary>좌표 → BuildingModule O(1) 조회. 배치 로직은 없다 — FactorySystem이 채운다.</summary>
    public class GridIndex
    {
        readonly Dictionary<Vector2Int, BuildingModule> _grid = new();

        public void Add(Vector2Int cell, BuildingModule b) => _grid[cell] = b;
        public void Remove(Vector2Int cell) => _grid.Remove(cell);
        public BuildingModule GetAt(Vector2Int cell) => _grid.TryGetValue(cell, out var b) ? b : null;
        public bool IsOccupied(Vector2Int cell) => _grid.ContainsKey(cell);
    }

    /// <summary>
    /// Dirty Queue + Wake 예약 기반 이벤트 주도 시뮬레이션.
    ///
    /// 핵심 아이디어:
    ///   건물 10,000개 중 100개만 현재 활성 → 100번만 Tick() 호출.
    ///   큐에 없는 건물은 완전 무시.
    ///
    /// 건물을 깨우는 두 가지 경로:
    ///   MarkDirty(b)        — "지금 변화가 생겼다" (아이템 수신, 상류/하류 상태 변화, 새 연결)
    ///   ScheduleWake(b, t)  — "t초 후에 깨워라"   (채굴/조합 타이머의 완료 시점 예약)
    ///
    /// 건물은 심 엔티티(<see cref="Entity"/>)에 붙는 모듈(<see cref="BuildingModule"/>)이다. 배치가 엔티티를 먼저 만들고
    /// 체력(Data.maxHp)과 건물 모듈을 붙인다 — HP·편·번호는 엔티티의 것이고, 칸·포트·버퍼·행동은 모듈의 것.
    /// </summary>
    public class FactorySystem
    {
        /// <summary>건물 엔티티가 사는 등록부. 여러 시스템이 같은 월드를 나눠 쓴다.</summary>
        public readonly EntityWorld World;

        /// <summary>
        /// 칸 ↔ 월드 좌표. 값의 출처는 맵이고 드라이버(FactoryBootstrap)가 배치 전에 넣는다.
        /// 심이 이걸 갖는 이유는 건물의 풋프린트 월드 사각형(몬스터 공격 거리·플로우필드 목표)을 뷰에 묻지 않기 위해서다.
        /// </summary>
        public GridGeometry Geometry { get; private set; }

        public readonly GridIndex          Grid;
        public readonly BuildingGraph      Graph;
        public readonly BeltSegmentManager Belts;

        /// <summary>
        /// 배치된 모든 건물 — 세이브·목표 수집이 순회하는 정본 목록.
        /// 그리드(GridIndex)를 훑으면 안 되는 이유: 멀티타일 건물은 여러 칸이 같은 Building을 가리켜 중복된다.
        /// 순회 중 배치·제거 금지.
        /// </summary>
        public IReadOnlyList<BuildingModule> Buildings => _buildings;
        readonly List<BuildingModule> _buildings = new();

        /// <summary>시뮬레이션 누적 시간(초). 틱마다 틱 간격씩 증가한다.</summary>
        public float Now { get; private set; }

        /// <summary>
        /// 세이브 복원 전용 — 심 시계를 저장 시점으로 되돌린다. 건물을 배치하기 전에 호출할 것.
        ///
        /// 이게 없으면 채굴/조합 타이머가 전부 어긋난다. 각 행동은 완료 시각을 이 시계 기준
        /// 절대값(_readyAt)으로 들고 있어서, 시계만 0으로 되돌아가면 예약이 아득한 미래가 되어
        /// 공장 전체가 멈춘 것처럼 보인다.
        /// </summary>
        public void RestoreClock(float now) => Now = now;

        /// <summary>마이너가 채굴 대상을 결정하는 서비스 포인트 (ResourceGrid 등에서 주입).</summary>
        public Func<Vector2Int, ItemDef> GetResourceAt;

        /// <summary>
        /// 마이너가 채굴 1회분을 실제로 꺼내가는 서비스 포인트 (셀, 요청 개수) → 실제로 꺼낸 개수.
        /// 0이면 광맥 재고가 비었다는 뜻 — 마이너는 생산하지 않고 다음 주기에 재시도한다.
        /// 주입하지 않으면(null) 재고 개념 없이 무한 채굴 — 기존 동작 그대로.
        /// </summary>
        public Func<Vector2Int, int, int> TryExtractResourceAt;

        /// <summary>
        /// 이 칸의 광맥에서 1개를 캐는 데 걸리는 기준 시간(초) — 배율 1인 채굴기 기준.
        /// 실제 시간 = 이 값 ÷ MinerDataSO.speedMultiplier.
        /// 주입하지 않으면(null) 1초 — 광맥마다 난이도가 없던 기존 동작.
        /// </summary>
        public Func<Vector2Int, float> GetExtractIntervalAt;

        readonly Queue<BuildingModule>   _queue = new();
        readonly HashSet<BuildingModule> _inQ   = new(); // 중복 등록 방지 O(1)

        // wake 예약 — (깨울 시각, 건물) 이진 min-heap. index 0 = 가장 이른 예약.
        readonly List<(float time, BuildingModule b)> _wake = new();

        readonly float _interval;
        readonly int   _maxCatchUpTicks;
        float _timer;

        public FactorySystem(EntityWorld world, GridGeometry geometry, float tps = 10f, int maxCatchUpTicks = 5)
        {
            World            = world ?? throw new ArgumentNullException(nameof(world));
            Geometry         = geometry;
            _interval        = 1f / Mathf.Max(0.1f, tps);
            _maxCatchUpTicks = Mathf.Max(1, maxCatchUpTicks);
            Grid  = new GridIndex();
            Graph = new BuildingGraph(this);
            Belts = new BeltSegmentManager(this);

            World.Died += OnEntityDied;
        }

        /// <summary>
        /// 월드 구독 해제 — 등록부는 씬을 넘어 살지만 이 시스템은 씬과 함께 죽는다. 드라이버(FactoryBootstrap)가 OnDestroy에서 부른다.
        /// 안 부르면 죽은 시스템이 다음 씬의 사망 통지를 받아 남의 건물을 지우려 든다.
        /// </summary>
        public void Dispose() => World.Died -= OnEntityDied;

        /// <summary>
        /// 건물 엔티티의 사망 = 건물 제거. 파괴를 결정하는 것은 심이고 뷰는 Removed를 받아 따라온다 —
        /// 구 BuildingEntity.HandleDeath(뷰가 심을 지우던 경로)를 대체한다.
        /// 남의 엔티티에 얹힌 건물(둥지)은 주인이 죽어도 칸을 계속 차지한다 — 둥지는 며칠 뒤 되살아난다.
        /// </summary>
        void OnEntityDied(Entity e)
        {
            var b = e.Get<BuildingModule>();
            if (b != null && b.OwnsEntity && !b.IsRemoved) Remove(b);
        }

        /// <summary>
        /// 격자 기하 교체 — 맵이 정하는 값이라 드라이버가 씬 조립 때 넣는다. 배치 전에만 의미가 있다:
        /// 이미 선 건물의 위치·풋프린트는 옛 기하로 계산돼 있다.
        /// </summary>
        public void SetGeometry(GridGeometry geometry)
        {
            if (_buildings.Count > 0)
                Debug.LogWarning($"[FactorySystem] 건물 {_buildings.Count}개가 선 뒤에 격자 기하를 바꿨습니다 — 기존 건물의 위치가 어긋납니다.");
            Geometry = geometry;
        }

        // ── 배치/제거 (외부 진입점 — 뷰 생성은 PlacementBridge가 별도로)

        /// <summary>
        /// 건물 배치 — 심 엔티티를 먼저 만들고(편·위치·체력) 건물 모듈을 붙인다.
        /// </summary>
        /// <param name="host">
        /// 이미 있는 엔티티에 건물을 붙일 때(둥지처럼 스스로 엔티티를 가진 개체가 칸만 차지하는 경우).
        /// null이면 새 엔티티를 만들고, 그 엔티티의 생사는 이 시스템이 책임진다(OwnsEntity).
        /// </param>
        public BuildingModule Place(EntityDef def, Vector2Int origin, int rotSteps = 0,
            PortDefinition[] portOverride = null, BeltShape shape = BeltShape.Straight, Entity host = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            var size = BuildingPorts.RotatedSize(def, rotSteps);
            bool ownsEntity = host == null;
            var entity = host ?? World.Create(def.Faction, Geometry.CenterOf(origin, size));
            if (ownsEntity)
            {
                def.Assemble(entity);   // Health·Effects — 정의가 만든다. HP 정본은 정의(maxHp)지 프리팹 값이 아니다
                // 타워의 사거리·연사는 정의 — 심 공격 모듈. 효과는 탄(전달 계층)이 정하므로 비워 둔다 (TowerBrain 런타임 모듈은 5a-2e)
                if (def.Get<TowerBrainModuleDef>() is { } brain)
                    entity.Add(new AttackModule(brain.Range * Geometry.CellSize, brain.FireRate > 0f ? 1f / brain.FireRate : 1f));
            }
            var b = new BuildingModule(this, def, origin, rotSteps, portOverride, shape, ownsEntity);
            entity.Add(b);
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                    Grid.Add(origin + new Vector2Int(x, y), b);
            _buildings.Add(b);
            Graph.OnPlaced(b);
            MarkDirty(b);
            return b;
        }

        /// <summary>
        /// 건물이 심에서 제거된 직후 1회. 씬 표현(뷰)은 이 통지를 받아 스스로 정리한다 —
        /// 심이 원본이고 뷰가 따라오는 방향을 코드로 고정하는 지점.
        /// 벨트 폐기 통지(Belts.ItemDiscarded)보다 항상 뒤에 오고(그때는 뷰가 아직 살아 있어야 하므로),
        /// 엔티티가 월드에서 빠지기 전이라 수신자는 b.Owner를 아직 읽을 수 있다.
        /// </summary>
        public event Action<BuildingModule> Removed;

        public void Remove(BuildingModule b)
        {
            if (b == null || b.IsRemoved) return;
            b.IsRemoved = true;            // 큐/힙에 남은 참조는 틱에서 걸러진다

            Graph.OnRemoved(b);

            var size = b.Size;
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                    Grid.Remove(b.Origin + new Vector2Int(x, y));

            _inQ.Remove(b);
            _buildings.Remove(b);

            Removed?.Invoke(b);

            // 이 시스템이 만든 엔티티는 여기서 지운다. 남의 엔티티(둥지)는 건물 모듈만 떨어지고 주인이 남는다.
            if (b.OwnsEntity && b.Owner != null) World.Remove(b.Owner);
        }

        // ── 깨우기

        /// <summary>건물을 다음 틱 처리 대상에 추가. O(1). 중복 호출 안전.</summary>
        public void MarkDirty(BuildingModule b)
        {
            if (b == null || b.IsRemoved) return;
            if (_inQ.Add(b)) _queue.Enqueue(b);
        }

        /// <summary>
        /// delay초 후 건물을 깨운다(= MarkDirty). 타이머 완료 시점 예약용.
        /// 같은 건물을 중복 예약해도 안전하다 — 이른 기상은 각 행동이 Now로 걸러낸다.
        /// </summary>
        public void ScheduleWake(BuildingModule b, float delay)
        {
            if (b == null || b.IsRemoved) return;
            _wake.Add((Now + delay, b));
            for (int i = _wake.Count - 1; i > 0; )
            {
                int p = (i - 1) / 2;
                if (_wake[p].time <= _wake[i].time) break;
                (_wake[p], _wake[i]) = (_wake[i], _wake[p]);
                i = p;
            }
        }

        void PopWake()
        {
            _wake[0] = _wake[^1];
            _wake.RemoveAt(_wake.Count - 1);
            for (int i = 0; ; )
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < _wake.Count && _wake[l].time < _wake[m].time) m = l;
                if (r < _wake.Count && _wake[r].time < _wake[m].time) m = r;
                if (m == i) break;
                (_wake[m], _wake[i]) = (_wake[i], _wake[m]);
                i = m;
            }
        }

        // ── 구동

        /// <summary>
        /// 마지막 틱 이후 흐른 시간(초) — 뷰가 틱 사이를 외삽해 부드럽게 그릴 때 사용.
        /// 틱 지연(캐치업 한도 초과) 시에도 한 틱 분량을 넘지 않게 클램프.
        /// </summary>
        public float TickLeftover => Mathf.Min(_timer, _interval);

        /// <summary>
        /// 실시간 dt만큼 시뮬레이션을 진행한다 (고정 틱 + 따라잡기 상한).
        /// 드라이버(FactoryBootstrap)가 매 프레임 호출하거나, 테스트가 직접 호출한다.
        /// </summary>
        public void Advance(float dt)
        {
            _timer += dt;

            // 밀린 틱을 따라잡되, 프레임당 한도를 둬서 저사양에서
            // "틱 몰아치기 → 프레임 더 느려짐 → 더 밀림" 나선을 방지한다.
            int ticks = 0;
            while (_timer >= _interval && ticks < _maxCatchUpTicks)
            {
                _timer -= _interval;
                RunTick();
                ticks++;
            }

            // 한도를 넘긴 빚은 버린다 (다음 프레임에 처리할 1틱분만 유지).
            if (_timer > _interval) _timer = _interval;
        }

        void RunTick()
        {
            Now += _interval;

            // 예약 시각이 된 건물을 큐로 이동
            while (_wake.Count > 0 && _wake[0].time <= Now)
            {
                MarkDirty(_wake[0].b);
                PopWake();
            }

            // 틱 시작 시점의 큐 크기만큼만 처리.
            // 이번 틱에서 새로 MarkDirty된 건물은 다음 틱에 처리된다.
            int count = _queue.Count;
            for (int i = 0; i < count; i++)
            {
                var b = _queue.Dequeue();
                _inQ.Remove(b);
                if (b == null || b.IsRemoved) continue;
                b.Tick(_interval);
            }
        }
    }
}
