using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Placement;
using CoreDawn.Save;
using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// FactorySystem의 Unity 드라이버 — 심과 씬의 유일한 접점.
    /// 씬에 이 컴포넌트 하나만 있으면 공장 시뮬레이션이 돌아간다.
    ///
    /// 역할:
    ///   1. 심 생성·매 프레임 Advance() 호출
    ///   2. 심 Building ↔ BuildingView(GameObject) 매핑 관리
    /// 시뮬레이션 로직은 전부 FactorySystem(plain C#)에 있다.
    ///
    /// 씬을 넘어 살아남지 않는다 — 심과 건물 뷰는 한 씬 안에서만 의미가 있고,
    /// 씬이 바뀌면 새 심으로 시작한다. 엔티티 등록부(SimHost.World)는 공유하되, 이 씬의 건물 엔티티는
    /// OnDestroy에서 전부 빼서 다음 씬에 유령을 남기지 않는다.
    /// </summary>
    /// <remarks>
    /// 실행 순서를 뒤로 민 이유: 코어 자동 설치가 씬에 미리 놓인
    /// <see cref="CoreBootstrap"/>보다 나중에 판정돼야 코어가 둘로 늘지 않는다.
    /// Awake는 실행 순서와 무관하게 모든 Start보다 먼저 끝나므로
    /// CoreBootstrap.Start가 쓰는 <see cref="Instance"/>는 여전히 준비돼 있다.
    /// </remarks>
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(BeltItemView))]
    public class FactoryBootstrap : MonoBehaviour
    {
        public static FactoryBootstrap Instance { get; private set; }

        [Tooltip("초당 틱 수. 10이면 0.1초마다 처리.")]
        [SerializeField] float _tps = 10f;

        [Tooltip("프레임 드랍 후 한 프레임에 몰아서 따라잡을 수 있는 최대 틱 수.")]
        [SerializeField] int _maxCatchUpTicks = 5;

        [Header("격자 (맵이 있으면 GameBootstrap이 덮는다)")]
        [Tooltip("칸 한 변의 길이(m). 맵 없는 테스트 씬용 기본값 — PlacementSystem 기본값과 같아야 한다.")]
        [SerializeField] float _cellSize = 1f;
        [SerializeField] Vector3 _gridOrigin = Vector3.zero;

        [Header("코어 자동 설치")]
        [Tooltip("게임 시작 시 코어가 하나도 없으면 자동으로 세운다. " +
                 "씬에 CoreBootstrap으로 미리 배치해 뒀다면 그쪽이 우선이고 여기서는 아무것도 하지 않는다.")]
        [SerializeField] bool _autoPlaceCore = true;

        [Tooltip("세울 코어 데이터. 비워두면 BuildingDatabase에서 CoreDataSO를 찾아 쓴다.")]
        [SerializeField] CoreDataSO _coreData;

        [Tooltip("코어를 세울 그리드 좌표. 월드(맵)가 있으면 맵이 정한 자리로 덮인다 — Inject 참조.")]
        [SerializeField] Vector2Int _coreOrigin = Vector2Int.zero;

        [SerializeField] int _coreRotationSteps = 0;

        /// <summary>
        /// 코어 자리 주입 — 코어가 어디 서는지는 맵이 정한다(MapDataSO.core).
        /// 공장 심은 별도 씬으로 오므로 GameBootstrap이 월드에서 읽어 넘긴다.
        /// 자동 설치는 Start에서 일어나고 주입은 그 전(씬 로드 직후)이라 제때 반영된다.
        /// </summary>
        public void Inject(Vector2Int coreOrigin)
        {
            _coreOrigin = coreOrigin;
        }

        /// <summary>
        /// 격자 기하 주입 — 칸 크기·원점은 맵이 정하고 PlacementSystem·GridManager와 같은 출처에서 온다.
        /// 배치(Start의 코어 자동 설치·WorldPopulator)보다 먼저, 씬 조립 때 들어온다.
        /// </summary>
        public void Inject(GridGeometry geometry)
        {
            _cellSize = geometry.CellSize;
            _gridOrigin = geometry.Origin;
            Factory?.SetGeometry(geometry);
        }

        public FactorySystem Factory { get; private set; }

        readonly Dictionary<Building, BuildingView> _views = new();

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            Factory = new FactorySystem(SimHost.World, new GridGeometry(_cellSize, _gridOrigin), _tps, _maxCatchUpTicks);

            // 벨트 철거로 세그먼트에서 밀려난 아이템 → 월드 드롭 (통지 시점엔 벨트 뷰가 아직 살아있음)
            Factory.Belts.ItemDiscarded += (belt, item) =>
            {
                var view = GetView(belt);
                if (view != null) PlacementBridge.DropAt(item, 1, view.transform.position);
            };

            // 심에서 건물이 사라지면 그 씬 표현도 함께 정리 — 매핑 소유자가 한 곳에서 책임진다.
            // (전투 파괴·철거·테스트의 Factory.Remove 직접 호출까지 전부 이 경로로 모인다)
            // 버퍼 내용물은 뷰가 사라지기 전에 월드로 — 건물이 사라져도 아이템은 보존된다.
            // 심은 아이템을 어디에 떨굴지 모르므로(월드 좌표는 뷰의 것) 드롭은 여기 브리지의 몫이다.
            Factory.Removed += b =>
            {
                var view = GetView(b);
                _views.Remove(b);
                if (view == null) return;

                PlacementBridge.DropContainer(b.Input, view.transform.position);
                PlacementBridge.DropContainer(b.Output, view.transform.position);
                Destroy(view.gameObject);
            };

            // 벨트 위 아이템 시각화 뷰 — 씬 배선 없이 드라이버가 직접 부착
            if (GetComponent<BeltItemView>() == null) Debug.LogWarning("No Belt Item Renderer");
        }

        void Start()
        {
            // 복원 중에는 세이브에 적힌 코어를 그대로 세우므로 자동 설치가 끼어들면 안 된다
            // (씬에 미리 놓인 코어를 잇는 CoreBootstrap은 그대로 둔다 — 복원이 그 뷰를 재사용한다)
            if (_autoPlaceCore && !SaveLoadContext.IsRestoring) AutoPlaceCore();
        }

        void Update() => Factory.Advance(Time.deltaTime);

        // 씬이 내려갈 때 이 씬의 건물 엔티티를 등록부에서 뺀다 — 등록부는 씬을 넘어 살기 때문이다.
        // 종료 중에는 손대지 않는다(드롭 등 새 오브젝트 생성이 에러를 낸다).
        static bool quitting;
        void OnApplicationQuit() => quitting = true;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Factory?.Dispose();
            if (quitting || Factory == null) return;

            var buildings = new List<Building>(Factory.Buildings);
            foreach (var b in buildings)
                if (b.OwnsEntity && b.Owner != null && !b.Owner.IsRemoved) Factory.World.Remove(b.Owner);
        }

        // ── 코어 자동 설치 ───────────────────────────────────────────

        /// <summary>
        /// 씬에 코어가 없으면 하나 세운다. 이미 있으면(=CoreBootstrap이 심에 연결해 둔 코어,
        /// 혹은 씬 전환 전에 세워 둔 코어) 아무것도 하지 않는다 — 코어는 맵에 하나뿐이다.
        /// </summary>
        void AutoPlaceCore()
        {
            if (HasCore()) return;

            var data = _coreData != null ? _coreData : FindCoreData();
            if (data == null)
            {
                Debug.LogWarning("[FactoryBootstrap] 코어를 세울 CoreDataSO를 찾지 못했습니다 — " +
                                 "인스펙터에 지정하거나 BuildingDatabase에 넣으세요.", this);
                return;
            }

            var placement = FindFirstObjectByType<PlacementSystem>();
            if (placement == null)
            {
                Debug.LogWarning("[FactoryBootstrap] 씬에 PlacementSystem이 없어 코어를 세우지 못했습니다 " +
                                 "(그리드·지형 판정이 거기 있습니다).", this);
                return;
            }

            if (placement.TryPlaceAt(data, _coreOrigin, _coreRotationSteps, out _, out string reason))
                Debug.Log($"[FactoryBootstrap] 코어 자동 설치 — {data.name} @ {_coreOrigin}");
            else
                Debug.LogWarning($"[FactoryBootstrap] 코어 자동 설치 실패 @ {_coreOrigin}: {reason}", this);
        }

        bool HasCore()
        {
            foreach (var b in Factory.Buildings)
                if (b.IsCore) return true;
            return false;
        }

        static CoreDataSO FindCoreData()
        {
            var db = BuildingDatabaseSO.LoadDefault();
            if (db == null || db.buildings == null) return null;

            foreach (var b in db.buildings)
                if (b is CoreDataSO core) return core;
            return null;
        }

        // ── Building ↔ View 매핑 (PlacementBridge가 등록/해제)

        /// <summary>배치된 모든 건물 — 심의 정본 목록(FactorySystem.Buildings)을 그대로 낸다.</summary>
        public IEnumerable<Building> Buildings => Factory.Buildings;

        public void RegisterView(Building b, BuildingView v) => _views[b] = v;
        public void UnregisterView(Building b) => _views.Remove(b);
        public BuildingView GetView(Building b) =>
            b != null && _views.TryGetValue(b, out var v) ? v : null;
    }
}
