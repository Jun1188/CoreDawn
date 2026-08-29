using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Interaction;
using CoreDawn.Managers;
using CoreDawn.Navigation;
using CoreDawn.Sim;
using CoreDawn.Data;
using CoreDawn.UI;
using CoreDawn.Sound;

namespace CoreDawn.Entities
{
    // 건물 뷰 — 심 건물의 "씬 위 껍데기". 데이터 원본은 팩토리 심의 Building(모듈)이고, HP·편은 그 엔티티의 것이다.
    //
    // 역할 분담 (합치지 않고 나눈다 — MonoBehaviour 제약 때문에 상속으로는 못 합침):
    //   Building (순수 C#, Runtime/Factory) = 데이터 원본. Data/Origin/회전/버퍼/연결/행동/IsRemoved, 피해 규칙.
    //   BuildingView (여기)                = 씬 표현. 상호작용 창구, 체력바·파괴음 연출, 길찾기 비용 재칠,
    //                                         그리고 심 데이터는 아래 위임 프로퍼티로만 노출한다.
    // 그래서 소비자는 view.Building.Data처럼 심 내부로 두 단계 들어가지 말고 view.Data를 쓰면 된다.
    //
    // 생명주기는 심이 앞선다: 배치(FactorySystem.Place)가 엔티티·건물을 먼저 만들고 브리지가 이 뷰에 잇는다.
    //   심 제거 → FactorySystem.Removed → FactoryBootstrap이 이 GameObject를 파괴
    //   뷰 파괴 → OnDestroy에서 심도 제거 (씬 언로드/종료 중에는 건너뜀) — 다른 경로로 파괴된 유령 방지
    //
    // 코어처럼 씬에 미리 놓인 건물은 CoreBootstrap이 Start에서 잇는다 — 그 전에는 Building·Entity가 null이다.
    public class BuildingView : EntityView, IInteractable
    {
        [Tooltip("전투로 파괴될 때 낼 소리. 비워두면 조용히 사라진다.\n" +
                 "타워는 TowerVisualController가 따로 내므로 여기 넣지 않아도 된다.")]
        [SerializeField] private AudioClip destroySfx;

        // 이 뷰가 대변하는 팩토리 심 건물(모듈). PlacementBridge가 배치 시 연결한다.
        private BuildingModule building;

        /// <summary>
        /// 심 건물 연결 — 엔티티(HP·편)도 이때 함께 붙는다.
        /// 심이 붙는 순간이 <b>풋프린트가 확정되는 순간</b>이라 여기서 길찾기 비용을 다시 칠한다.
        ///
        /// OnEnable 때는 아직 심이 없어 풋프린트를 모른다 — 그래서 그때는 점 하나만 칠하고,
        /// 진짜 칠은 여기로 미룬다. 이 대입이 조용하면 건물이 차지한 칸이 길찾기에 반영되지
        /// 않아 몬스터가 그대로 통과한다. 씬에 굳혀 둔 배치물(나무·코어)은 심이 훨씬 나중에
        /// 붙으므로(WorldPopulator가 잇는다) 증상이 특히 뚜렷하다.
        /// </summary>
        public BuildingModule Building
        {
            get => building;
            set
            {
                if (building == value) return;
                building = value;
                if (value != null && value.Owner != null) AttachEntity(value.Owner);
                RefreshPathingCosts();
                if (FlowFieldManager.Instance != null) FlowFieldManager.Instance.MarkDirty();
            }
        }


        /// <summary>맵의 코어인가 — 설계도(CoreDataSO)가 정한다. 심이 없으면 false.</summary>
        public bool IsCore => building != null && building.IsCore;

        public override bool IsDead => base.IsDead || (building != null && building.IsRemoved);

        // ── 심 데이터 위임 — 소비자가 Building 내부로 두 단계 들어가지 않게 하는 창구.
        //    심이 아직 없는 씬 직접 배치 건물(코어 등)에서도 안전하게 null/기본값을 돌려준다.

        /// <summary>이 건물의 정의(팩 json). 심이 없으면 null.</summary>
        public EntityDef Def => building?.Def;
        /// <summary>이 건물의 표현 에셋(프리팹·아이콘). 정의나 SO가 없으면 null — 5a-3에서 뷰 카탈로그로.</summary>
        public BuildingDataSO Data => BuildingAssets.Of(building?.Def);

        /// <summary>점유 풋프린트의 왼쪽 아래 셀. 심이 없으면 기본값.</summary>
        public Vector2Int Origin => building != null ? building.Origin : default;

        /// <summary>회전이 반영된 점유 크기(타일). 심이 없으면 1x1.</summary>
        public Vector2Int Size => building != null ? building.Size : Vector2Int.one;

        /// <summary>살아 있는 심에 연결돼 있는가 (철거·파괴된 심은 false).</summary>
        public bool HasBuilding => building != null && !building.IsRemoved;

        /// <summary>
        /// 점유 풋프린트의 월드 사각형(XZ 평면, y는 이 건물의 높이). 심이 없으면 false.
        /// 기준은 모델이 아니라 차지한 칸이다 — 자세한 이유는 <see cref="BuildingModule.WorldRect"/>.
        /// </summary>
        public bool TryGetFootprintRect(out Vector3 min, out Vector3 max)
        {
            min = max = default;
            if (!HasBuilding) return false;
            building.WorldRect(transform.position.y, out min, out max);
            return true;
        }

        // ── 플레이어 상호작용(E) — 행동이 IInteractiveBehavior를 구현한 건물만 반응 (opt-in)
        public string Prompt => building?.Behavior is IInteractiveBehavior i ? i.InteractPrompt : null;

        /// <summary>핑 이름은 설계도의 표시명 — 오브젝트 이름은 프리팹 이름(Clone)이라 사람이 읽을 것이 아니다.</summary>
        public override string PingLabel =>
            building != null ? building.DisplayName : name;

        public void Interact(PlayerController player)
        {
            if (building?.Behavior is IInteractiveBehavior i) i.Interact(player);
        }

        // (구 뷰 레지스트리 BuildingView.All은 퇴역 — 정본 목록은 FactorySystem.Buildings)

        private void OnEnable()
        {
            // 건물 배치/파괴는 몬스터 경로에 영향을 주므로 플로우필드 갱신 예약
            RefreshPathingCosts();
            if (FlowFieldManager.Instance != null) FlowFieldManager.Instance.MarkDirty();
        }

        private void OnDisable()
        {
            RefreshPathingCosts();
            if (FlowFieldManager.Instance != null) FlowFieldManager.Instance.MarkDirty();
        }

        /// <summary>
        /// 이 건물이 덮는 칸의 길찾기 비용만 다시 칠한다 — 배치·철거 순간.
        /// 맵 전체(20만 칸)를 다시 훑는 대신 바뀐 자리만 고치므로 벨트를 연달아 깔아도 부담이 없다.
        /// 심이 아직 없으면(뷰 먼저 생성) 건너뛴다 — 심이 붙는 쪽에서 어차피 다시 부른다.
        /// </summary>
        private void RefreshPathingCosts()
        {
            var grid = GridManager.Instance;
            if (grid == null) return;

            // <b>제거된 심도 풋프린트로 쓴다.</b> 철거·파괴 직후에는 IsRemoved 라 HasBuilding 이 false 인데,
            // 그때 점 하나만 칠하면 덮고 있던 칸의 가장자리가 막힌 채 남는다 — 칸 4m·세분 4면
            // 1×1 건물도 노드 4×4 를 덮는데 점 재칠은 3×3 밖에 닿지 않는다.
            // 자리를 비우는 순간이야말로 덮었던 자리를 전부 다시 칠해야 하는 순간이다.
            if (building != null)
            {
                building.WorldRect(transform.position.y, out Vector3 min, out Vector3 max);
                grid.RefreshCostsIn(min, max);
            }
            else grid.RefreshCostsIn(transform.position, transform.position);
        }

        // 게임 종료/씬 언로드 중에는 정리에 손대지 않는다 — 그 시점의 Remove는
        // 벨트 아이템 드롭 같은 새 오브젝트 생성을 유발해 에러가 난다.
        private static bool quitting;
        private void OnApplicationQuit() => quitting = true;

        // 뷰가 다른 경로로 파괴돼도(부모 파괴·직접 Destroy) 심이 그리드를 계속 점유하지 않게.
        // 정상 경로(철거·전투 파괴)는 이미 심이 먼저 지워져 있어 여기서는 아무 일도 하지 않는다.
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (quitting || !gameObject.scene.isLoaded) return;
            if (building == null || building.IsRemoved) return;

            var boot = FactoryBootstrap.Instance;
            if (boot != null && boot.Factory != null) boot.Factory.Remove(building);
        }

        // ── 머리 위 HP 바 ─────────────────────────────────────────
        // 몬스터와 같은 WorldHealthBar를 그대로 쓴다 — 건물도 EntityView라 갱신 경로(HealthBarUI)가 똑같다.

        /// <summary>건물 체력바가 앵커 위로 올라갈 수 있는 최대 높이(m). 포탑(≈3m)은 그대로 꼭대기, 키 큰 나무만 잘린다.</summary>
        const float MaxHealthBarHeight = 4f;
        bool healthBarWatching;

        // 다만 몬스터와 달리 처음부터 세우지 않는다. 벨트 한 칸까지 전부 건물이라 만피인 것들에도
        // 바를 얹으면 월드스페이스 Canvas가 수백 개 생기고 화면이 게이지로 덮인다.
        // 그래서 처음 피해를 입는 순간에만 붙이고, 그 뒤의 표시/숨김은 hideWhenFull이 맡는다
        // (수리로 만피가 되면 다시 숨는다).
        // Start가 아니라 심이 붙는 순간에 거는 이유: 씬에 미리 놓인 코어·나무는 Start 뒤에야 심이 붙는다.
        protected override void OnEntityAttached()
        {
            base.OnEntityAttached();

            // 코어는 제외 — CorePanelView·GameplayHUDView가 이미 체력을 훨씬 크게 보여준다.
            if (IsCore || healthBarWatching || Health == null) return;

            healthBarWatching = true;
            OnHealthChanged += TryShowHealthBar;
            TryShowHealthBar(Health.CurrentHealth, Health.MaxHealth); // 세이브 복원처럼 이미 깎인 채 시작하는 경우
        }

        private void TryShowHealthBar(float current, float max)
        {
            if (max <= 0f || current >= max) return;

            OnHealthChanged -= TryShowHealthBar; // 한 번 붙으면 감시는 끝 — 이후는 바가 알아서 한다

            // 높이 상한 — 바는 콜라이더 꼭대기에 서는데 키 큰 나무(BroadleafTree 01·04)는 줄기 캡슐이
            // 나무 전체 높이(≈10m)라 바가 하늘에 떠서 안 보였다. 다른 나무들의 바가 서는 높이(3.5~4.3m)에
            // 맞춰 자른다 — 줄기를 때리는 플레이어의 시야 안이다.
            WorldHealthBar.Attach(this, hideWhenFull: true, maxHeight: MaxHealthBarHeight);
        }

        // 받는 피해 규칙(아군 공격 무시·코어 보호막)은 심의 인터셉터(Building.Intercept)로 갔다 — 여기엔 연출만 남는다.

        // HP 0 → 심(FactorySystem)이 건물을 제거하고 Removed로 이 뷰를 파괴한다(월드 통지가 이 릴레이보다 먼저다).
        // 여기엔 연출만 남는다. 코어 파괴 = 게임오버 판정도 BattleManager가 월드 Died에서 직접 본다.
        protected override void HandleDeath()
        {
            // 파괴음 — 타워는 TowerVisualController가 종류별 클립과 폭발 연출까지 함께 내므로
            // 여기서는 그 외 건물만 맡는다. 둘 다 내면 타워가 두 번 터지는 소리가 난다.
            if (GetComponent<TowerVisualController>() == null && SoundManager.Instance != null)
                SoundManager.Instance.Play3DSFX(destroySfx, transform.position);

            // 심에 붙지 않은 뷰(테스트 씬의 껍데기)만 스스로 정리한다 — 나머지는 Removed가 파괴한다
            if (building == null) Destroy(gameObject);
        }

        // 심 건물(모듈) → 건물 뷰 (구 BuildingDamageable.GetOrAttach).
        // PlacementBridge가 배치 시 모든 뷰 GO에 이 컴포넌트를 붙이고 매핑을 등록한다.
        public static BuildingView GetOrAttach(BuildingModule sim)
        {
            if (sim == null || sim.IsRemoved) return null;

            var boot = FactoryBootstrap.Instance;
            if (boot == null) return null;

            var view = boot.GetView(sim);
            if (view == null) return null; // 뷰 없는 심 전용 건물(테스트 등)은 공격 대상이 될 수 없음

            if (view.Building == null) view.Building = sim;
            return view;
        }
    }
}
