using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Interaction;
using CoreDawn.Managers;
using CoreDawn.Navigation;
using CoreDawn.Placement;
using CoreDawn.UI;
using CoreDawn.Sim;

namespace CoreDawn.Entities
{
    // 건물 엔티티 — 심 건물의 "씬 위 껍데기(View)". 데이터 원본은 팩토리 심의 Building이다.
    //
    // 역할 분담 (합치지 않고 나눈다 — MonoBehaviour 제약 때문에 상속으로는 못 합침):
    //   Building (순수 C#, Runtime/Factory) = 데이터 원본. Data/Origin/회전/버퍼/연결/행동/IsRemoved.
    //   BuildingEntity (여기)               = 씬 표현 + 전투. HP·피격·사망, 코어 여부, 상호작용 창구,
    //                                         그리고 심 데이터는 아래 위임 프로퍼티로만 노출한다.
    // 그래서 소비자는 view.Sim.Data처럼 심 내부로 두 단계 들어가지 말고 view.Data를 쓰면 된다.
    //
    // 생명주기는 양방향으로 맞춘다 (한쪽만 사라진 유령 방지):
    //   심 제거 → FactorySim.Removed → FactoryBootstrap이 이 GameObject를 파괴
    //   뷰 파괴 → OnDestroy에서 심도 제거 (씬 언로드/종료 중에는 건너뜀)
    //
    // 이름 주의: 심의 plain C# Building과 헷갈리지 않게 Entity 접미사를 붙였다 (2026-07-31 이전 이름: Entities.Building).
    // 코어처럼 심 없이 씬에 직접 배치하는 건물은 Sim이 null이어도 된다.
    public class BuildingView : EntityView, IInteractable
    {
        [Header("Building Settings")]
        [Tooltip("맵 중앙의 코어인지 여부. 플로우필드에서 타워보다 우선하는 최종 목표가 된다.")]
        [SerializeField] private bool isCore;

        [Tooltip("전투로 파괴될 때 낼 소리. 비워두면 조용히 사라진다.\n" +
                 "타워는 TowerVisualController가 따로 내므로 여기 넣지 않아도 된다.")]
        [SerializeField] private AudioClip destroySfx;

        // 이 엔티티가 대변하는 팩토리 심 건물(plain C#). PlacementBridge가 배치 시 연결한다.
        private Building sim;

        /// <summary>
        /// 심이 붙는 순간이 <b>풋프린트가 확정되는 순간</b>이라 여기서 길찾기 비용을 다시 칠한다.
        ///
        /// OnEnable 때는 아직 Sim이 없어 풋프린트를 모른다 — 그래서 그때는 점 하나만 칠하고,
        /// 진짜 칠은 여기로 미룬다. 이 대입이 조용하면 건물이 차지한 칸이 길찾기에 반영되지
        /// 않아 몬스터가 그대로 통과한다. 씬에 굳혀 둔 배치물(나무·코어)은 심이 훨씬 나중에
        /// 붙으므로(WorldPopulator가 잇는다) 증상이 특히 뚜렷하다.
        /// </summary>
        public Building Sim
        {
            get => sim;
            set
            {
                if (sim == value) return;
                sim = value;
                RefreshPathingCosts();
                if (FlowFieldManager.Instance != null) FlowFieldManager.Instance.MarkDirty();
            }
        }

        public bool IsCore => isCore;
        public override bool IsDead => base.IsDead || (Sim != null && Sim.IsRemoved);

        // ── 심 데이터 위임 — 소비자가 Sim 내부로 두 단계 들어가지 않게 하는 창구.
        //    심이 없는 씬 직접 배치 건물(코어 등)에서도 안전하게 null/기본값을 돌려준다.

        /// <summary>이 건물의 설계도(SO). 심이 없으면 null.</summary>
        public BuildingDataSO Data => Sim?.Data;

        /// <summary>점유 풋프린트의 왼쪽 아래 셀. 심이 없으면 기본값.</summary>
        public Vector2Int Origin => Sim != null ? Sim.Origin : default;

        /// <summary>회전이 반영된 점유 크기(타일). 심이 없으면 1x1.</summary>
        public Vector2Int Size =>
            Sim != null ? Sim.Data.GetRotatedSize(Sim.RotationSteps) : Vector2Int.one;

        /// <summary>살아 있는 심에 연결돼 있는가 (철거·파괴된 심은 false).</summary>
        public bool HasSim => Sim != null && !Sim.IsRemoved;

        // 칸 크기·원점의 소유자 — 씬이 바뀌면 파괴돼 가짜 null이 되므로 그때 다시 찾는다
        static PlacementSystem placementCache;

        /// <summary>
        /// 점유 풋프린트의 월드 사각형(XZ 평면, y는 이 건물의 높이). 심이나 배치 시스템이 없으면 false.
        ///
        /// 모델(콜라이더)이 아니라 <b>차지한 칸</b>이 기준이다. 콜라이더는 메시마다 조각조각
        /// 흩어져 있어서 하나만 집으면 건물의 일부만 가리키고(코어의 안테나 한 짝),
        /// 전부 합쳐도 모델일 뿐 풋프린트는 아니다. 길을 막는 것도, 몬스터가 다가와 때리는
        /// 것도 풋프린트이므로 시각화·목표·거리의 기준은 전부 여기여야 한다.
        /// </summary>
        public bool TryGetFootprintRect(out Vector3 min, out Vector3 max)
        {
            min = max = default;
            if (!HasSim || Data == null) return false;

            if (placementCache == null) placementCache = FindFirstObjectByType<PlacementSystem>();
            if (placementCache == null) return false;

            float cell = placementCache.CellSize;
            Vector3 gridOrigin = placementCache.GridOrigin;
            Vector2Int size = Size;

            min = new Vector3(gridOrigin.x + Origin.x * cell,
                              transform.position.y,
                              gridOrigin.z + Origin.y * cell);
            max = min + new Vector3(size.x * cell, 0f, size.y * cell);
            return true;
        }

        // ── 플레이어 상호작용(E) — 행동이 IInteractiveBehavior를 구현한 건물만 반응 (opt-in)
        public string Prompt => Sim?.Behavior is IInteractiveBehavior i ? i.InteractPrompt : null;

        /// <summary>핑 이름은 설계도의 표시명 — 오브젝트 이름은 프리팹 이름(Clone)이라 사람이 읽을 것이 아니다.</summary>
        public override string PingLabel =>
            Data != null && !string.IsNullOrEmpty(Data.displayName) ? Data.displayName : name;

        public void Interact(PlayerController player)
        {
            if (Sim?.Behavior is IInteractiveBehavior i) i.Interact(player);
        }

        // 살아있는 건물 레지스트리 — 플로우필드 목표 수집과 몬스터의 사거리 검색용
        private static readonly List<BuildingView> all = new List<BuildingView>();
        public static IReadOnlyList<BuildingView> All => all;

        // 코어 파괴 = 게임오버 조건. BattleManager가 구독한다.
        public static event Action<BuildingView> CoreDestroyed;

        private void OnEnable()
        {
            all.Add(this);
            // 건물 배치/파괴는 몬스터 경로에 영향을 주므로 플로우필드 갱신 예약
            RefreshPathingCosts();
            if (FlowFieldManager.Instance != null) FlowFieldManager.Instance.MarkDirty();
        }

        private void OnDisable()
        {
            all.Remove(this);
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

            // <b>제거된 심도 풋프린트로 쓴다.</b> 철거·파괴 직후에는 IsRemoved 라 HasSim 이 false 인데,
            // 그때 점 하나만 칠하면 덮고 있던 칸의 가장자리가 막힌 채 남는다 — 칸 4m·세분 4면
            // 1×1 건물도 노드 4×4 를 덮는데 점 재칠은 3×3 밖에 닿지 않는다.
            // 자리를 비우는 순간이야말로 덮었던 자리를 전부 다시 칠해야 하는 순간이다.
            if (TryFootprintRectRaw(out Vector3 min, out Vector3 max)) grid.RefreshCostsIn(min, max);
            else grid.RefreshCostsIn(transform.position, transform.position);
        }

        /// <summary>
        /// <see cref="TryGetFootprintRect"/>와 같되 <b>심이 이미 제거되었어도</b> 사각형을 낸다.
        /// 목표 선정 같은 곳은 제거된 건물을 세면 안 되므로 그쪽은 HasSim 을 보는 원래 표면을 쓴다 —
        /// 이 완화는 "덮었던 자리를 되돌린다"는 용도에만 쓴다.
        /// </summary>
        private bool TryFootprintRectRaw(out Vector3 min, out Vector3 max)
        {
            min = max = default;
            if (Sim == null || Sim.Data == null) return false;

            if (placementCache == null) placementCache = FindFirstObjectByType<PlacementSystem>();
            if (placementCache == null) return false;

            float cell = placementCache.CellSize;
            Vector3 gridOrigin = placementCache.GridOrigin;
            Vector2Int size = Sim.Data.GetRotatedSize(Sim.RotationSteps);

            min = new Vector3(gridOrigin.x + Sim.Origin.x * cell,
                              transform.position.y,
                              gridOrigin.z + Sim.Origin.y * cell);
            max = min + new Vector3(size.x * cell, 0f, size.y * cell);
            return true;
        }

        // 게임 종료/씬 언로드 중에는 정리에 손대지 않는다 — 그 시점의 Sim.Remove는
        // 벨트 아이템 드롭 같은 새 오브젝트 생성을 유발해 에러가 난다.
        private static bool quitting;
        private void OnApplicationQuit() => quitting = true;

        // 뷰가 다른 경로로 파괴돼도(부모 파괴·직접 Destroy) 심이 그리드를 계속 점유하지 않게.
        // 정상 경로(철거·전투 파괴)는 이미 심이 먼저 지워져 있어 여기서는 아무 일도 하지 않는다.
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (quitting || !gameObject.scene.isLoaded) return;
            if (Sim == null || Sim.IsRemoved) return;

            var boot = FactoryBootstrap.Instance;
            if (boot != null && boot.Sim != null) boot.Sim.Remove(Sim);
        }

        // ── 머리 위 HP 바 ─────────────────────────────────────────
        // 몬스터와 같은 WorldHealthBar를 그대로 쓴다 — 건물도 Entity라 갱신 경로(HealthBarUI)가 똑같다.

        /// <summary>건물 체력바가 앵커 위로 올라갈 수 있는 최대 높이(m). 포탑(≈3m)은 그대로 꼭대기, 키 큰 나무만 잘린다.</summary>
        const float MaxHealthBarHeight = 4f;
        //
        // 다만 몬스터와 달리 처음부터 세우지 않는다. 벨트 한 칸까지 전부 건물이라 만피인 것들에도
        // 바를 얹으면 월드스페이스 Canvas가 수백 개 생기고 화면이 게이지로 덮인다.
        // 그래서 처음 피해를 입는 순간에만 붙이고, 그 뒤의 표시/숨김은 hideWhenFull이 맡는다
        // (수리로 만피가 되면 다시 숨는다).
        protected override void Start()
        {
            base.Start();

            // 코어는 제외 — CorePanelView·GameplayHUDView가 이미 체력을 훨씬 크게 보여준다.
            if (isCore) return;

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

        /// <summary>
        /// <b>아군의 공격</b>이 통하지 않는 건물은 명중 자체를 흘린다 (BuildingDataSO.isAttackable).
        /// 총·근접이 모두 여기로 수렴하므로 한 곳만 막으면 된다.
        ///
        /// 몬스터의 공격은 이 값과 무관하다 — 밤 웨이브가 무엇을 노리는지는 플로우필드의
        /// 목표 선정(threatSeedCost)이 정하고, 정한 목표는 실제로 부술 수 있어야 한다.
        /// </summary>
        public override void ApplyEffects(IReadOnlyList<EffectEntry> entries, EntityView source,
                                          Vector3 hitPoint, Vector3 hitDirection = default)
        {
            if (Data != null && !Data.isAttackable && !IsHostile(source)) return;
            base.ApplyEffects(entries, source, hitPoint, hitDirection);
        }

        /// <summary>
        /// 이 공격이 <b>적에게서</b> 왔는가 — 편(<see cref="Faction"/>)으로 본다.
        ///
        /// 예전에는 레이어("Monster")로 갈랐다. 레이어는 물리·렌더링의 도구라 게임 규칙을 얹으면 원래 기능을 벗어나고,
        /// 헤드리스 심에는 레이어가 없다. 편은 심 엔티티의 상태라 어디서 물어도 같은 답이 나온다.
        ///
        /// 출처를 모르는 피해(null)는 아군으로 본다 — 실제 공격자는 모두 자신을 넘기므로,
        /// 출처가 없다는 것은 곧 "누구의 공격도 아니다"이고 무적 건물이 그걸로 깎이면 안 된다.
        /// </summary>
        bool IsHostile(EntityView source)
        {
            if (source == null || source.Entity == null || Entity == null) return false;
            return source.Entity.Faction.IsHostileTo(Entity.Faction);
        }

        // 코어의 보호막이 내구도보다 먼저 맞는다 — 남은 몫만 HP로 내려간다.
        // 보호막의 원본은 심(CoreBehavior)이다: 자원 소각으로 차오르는 값이라
        // 자원과 같은 곳에 있어야 두 수치가 어긋나지 않는다.
        // 수렴점(ReceiveDamage)에서 막아야 몬스터 공격(ApplyEffects 경로)도 보호막을 거친다.
        public override void ReceiveDamage(float amount, EntityView source)
        {
            if (amount > 0f && Sim != null && Sim.Behavior is CoreBehavior core)
            {
                amount = core.AbsorbDamage(amount);
                if (amount <= 0f) return; // 전부 막았다 — OnHealthChanged도 뜨지 않는다
            }

            base.ReceiveDamage(amount, source);
        }

        // HP 0 → 몬스터의 사망 연출 지연 없이 즉시 소멸
        protected override void HandleDeath()
        {
            if (isCore) CoreDestroyed?.Invoke(this); // 소멸 전에 게임오버 통지

            // 파괴음 — 타워는 TowerVisualController가 종류별 클립과 폭발 연출까지 함께 내므로
            // 여기서는 그 외 건물만 맡는다. 둘 다 내면 타워가 두 번 터지는 소리가 난다.
            if (GetComponent<TowerVisualController>() == null && SoundManager.Instance != null)
                SoundManager.Instance.Play3DSFX(destroySfx, transform.position);

            if (Sim != null && !Sim.IsRemoved)
            {
                PlacementBridge.Remove(Sim); // 심 제거 + GridIndex 해제 + 뷰(GO) 파괴 일괄 처리
            }
            else
            {
                // 심 연결이 없는 건물(코어 등 씬 직접 배치)은 뷰만 정리
                Destroy(gameObject);
            }
        }

        // 심 건물(POCO) → 건물 엔티티 (구 BuildingDamageable.GetOrAttach).
        // PlacementBridge가 배치 시 모든 뷰 GO에 이 컴포넌트를 붙이고 매핑을 등록한다.
        public static BuildingView GetOrAttach(Building sim)
        {
            if (sim == null || sim.IsRemoved) return null;

            var boot = FactoryBootstrap.Instance;
            if (boot == null) return null;

            var entity = boot.GetView(sim);
            if (entity == null) return null; // 뷰 없는 심 전용 건물(테스트 등)은 공격 대상이 될 수 없음

            if (entity.Sim == null) entity.Sim = sim;
            return entity;
        }

        // 사거리 내 가장 가까운 살아있는 건물 — 몬스터(FlowFieldState)의 도착/공격 판정용.
        // 멀티타일 건물을 고려해 콜라이더 표면 거리(DistanceTo)를 사용한다.
        public static BuildingView FindClosestInRange(Vector3 from, float range)
        {
            BuildingView closest = null;
            float minDistance = float.MaxValue;
            foreach (var building in all)
            {
                if (!building.IsValidTarget()) continue;
                float dist = building.DistanceTo(from);
                if (dist <= range && dist < minDistance)
                {
                    minDistance = dist;
                    closest = building;
                }
            }
            return closest;
        }
    }
}
