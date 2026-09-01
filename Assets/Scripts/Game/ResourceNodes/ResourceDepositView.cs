using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Worlds;
using CoreDawn.Data;
using CoreDawn.Sound;
using CoreDawn.Sim;
using CoreDawn.Save;

namespace CoreDawn.ResourceNodes
{
    // ================================================================
    //  광맥 뷰 — 심의 광맥 엔티티(ResourceDepositModule)를 그리고 손 채굴 상호작용을 받는다.
    //
    //  구 ResourceNode(MonoBehaviour가 재고·생산·색인까지 들던 것)의 뷰 절반. 채굴기 배치 규칙은 심
    //  (ResourceDepositModule · FactorySystem.Deposits)에 있고, 여기엔 모형·기즈모·E 홀드 채굴만 남았다.
    //
    //  한 칸짜리다. 맵의 광맥은 에디터가 이 뷰를 씬에 굳히고(보이게) 런타임의 WorldPopulator.Connect가 마커의 칸으로
    //  심에 세워 붙인다. 씬에 직접 놓은 광맥(마커 없는 테스트 씬)은 Start에서 자기 자원의 정의를 찾아 스스로 선다.
    // ================================================================

    /// <summary>광맥 한 칸의 씬 표현. 오브젝트 위치가 칸 중앙이다.</summary>
    [DisallowMultipleComponent]
    public class ResourceDepositView : EntityView, IHoldInteractable
    {
        [Header("자원")]
        [Tooltip("이 칸에 묻힌 자원의 팩 id(coredawn:item/iron_ore) — 맵이 굳힌 광맥은 베이커가 채우고, 씬에 직접 놓는 광맥은 손으로 적는다.")]
        [SerializeField] private string resourceId;

        [Header("기즈모")]
        [SerializeField] private Color gizmoColor = new Color(1f, 0.75f, 0.1f, 0.35f);

        public ResourceDepositModule Deposit => Entity?.Get<ResourceDepositModule>();
        /// <summary>저작된 자원 id — 심에 서기 전(에디터·Connect 전)에 읽는다.</summary>
        public string AuthoredResourceId => resourceId;
        ItemDef AuthoredResource => string.IsNullOrEmpty(resourceId) ? null : SaveRefs.Item(resourceId);
        public ItemDef Resource => Deposit != null ? Deposit.Resource : AuthoredResource;
        public Vector2Int Cell => Deposit != null ? Deposit.Cell : CellOfTransform();
        public int TotalExtracted => Deposit?.TotalExtracted ?? 0;

        /// <summary>베이커·런타임 스폰이 자원을 적는다.</summary>
        public void Configure(ItemDef item) => resourceId = item?.Id;

        public override string PingLabel
        {
            get
            {
                var r = Resource;
                return r != null ? (string.IsNullOrEmpty(r.DisplayName) ? r.Id : r.DisplayName) : name;
            }
        }
        public override bool CanBePinged => isActiveAndEnabled;

        Vector2Int CellOfTransform()
        {
            var boot = FactoryBootstrap.Instance;
            return boot != null && boot.Factory != null ? boot.Factory.Geometry.CellOf(transform.position) : Vector2Int.zero;
        }

        protected override void Start()
        {
            base.Start();
            if (Entity != null) return;                       // 이미 심에 섰다(WorldPopulator)
            if (GetComponent<PlacedMapObject>() != null) return;   // 맵이 굳힌 광맥 — Connect가 마커의 칸으로 세운다
            // 씬에 직접 놓인 광맥(테스트 씬) — 자원으로 광맥 정의를 찾아 심에 세운다
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null)
            {
                Debug.LogWarning($"[ResourceDeposit] '{name}': 공장(FactoryBootstrap)이 없어 광맥을 심에 세우지 못했습니다.", this);
                return;
            }
            var cell = boot.Factory.Geometry.CellOf(transform.position);
            if (!TryAttachAt(cell)) return;
        }

        /// <summary>이 뷰의 자원으로 광맥 정의를 찾아 심의 칸에 세우고 붙인다. 실패는 소리 내어 알린다.</summary>
        public bool TryAttachAt(Vector2Int cell)
        {
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null) return false;
            var def = DepositDefs.For(AuthoredResource);
            if (def == null)
            {
                Debug.LogError($"[ResourceDeposit] '{name}': 자원 '{(string.IsNullOrEmpty(resourceId) ? "(없음)" : resourceId)}'을 캐는 광맥 정의가 팩에 없습니다.", this);
                return false;
            }
            if (boot.Factory.DepositAt(cell) != null)
            {
                Debug.LogError($"[ResourceDeposit] '{name}': 칸 {cell}에 이미 광맥이 있습니다 — 겹친 배치입니다.", this);
                return false;
            }
            AttachEntity(boot.Factory.PlaceDeposit(def, cell));
            return true;
        }

        // ── 손 채굴 (IHoldInteractable) ───────────────────────────────
        //  채굴기와 같은 광맥에서 캔다. 손은 배율 1 — 광맥의 채굴 시간(extractInterval) 그대로 1개. 채굴기는 배율만큼 빠르다.
        public bool ManualMiningEnabled => Deposit != null && Deposit.Resource != null;

        string IInteractable.Prompt
        {
            get
            {
                if (!ManualMiningEnabled) return null;
                var r = Deposit.Resource;
                string what = string.IsNullOrEmpty(r.DisplayName) ? r.Id : r.DisplayName;
                return $"{what} 손으로 캐기 (누르고 있기)";
            }
        }

        void IInteractable.Interact(PlayerController player) { }
        float IHoldInteractable.HoldSeconds => Deposit?.ExtractInterval ?? 3f;
        string IHoldInteractable.HoldLabel => "채굴";
        bool IHoldInteractable.CanHold => ManualMiningEnabled;

        void IHoldInteractable.OnHoldComplete(PlayerController player)
        {
            var deposit = Deposit;
            if (deposit == null) return;
            int taken = deposit.Extract(1);
            if (taken <= 0) return;
            // silent: 이 프레임에 바로 아래에서 Mine음을 낸다 — 획득음까지 겹치면 같은 AudioSource에서
            // 두 파형이 합산돼 찢어진 소리가 난다. 채굴 완료의 신호는 Mine음 하나로 충분하다.
            var holder = PlayerInventoryHolder.Instance;
            bool stored = holder != null && holder.AddItemToPlayer(deposit.Resource, taken, silent: true);
            if (!stored) DropAtHand(deposit.Resource, taken, player);
            SoundManager.Instance?.PlayCommon("mine");
        }

        void DropAtHand(ItemDef item, int amount, PlayerController player)
        {
            Vector3 top = transform.position + Vector3.up * 0.8f;
            Vector3 toPlayer = player != null ? player.transform.position - top : Vector3.zero;
            toPlayer.y = 0f;
            Vector3 dir = toPlayer.sqrMagnitude > 1e-4f ? toPlayer.normalized : Vector3.forward;
            var boot = FactoryBootstrap.Instance;
            float cell = boot != null && boot.Factory != null ? boot.Factory.Geometry.CellSize : 1f;
            float reach = 0.5f * cell + 0.4f;
            DroppedItem.Spawn(item, amount, top + dir * reach, dir * 0.3f + Vector3.up * 0.4f);
        }

        // ── 기즈모 ────────────────────────────────────────────────────
        void OnDrawGizmos()
        {
            var boot = FactoryBootstrap.Instance;
            float cell = boot != null && boot.Factory != null ? boot.Factory.Geometry.CellSize : 1f;
            Gizmos.color = Resource != null ? gizmoColor : new Color(1f, 0.2f, 0.2f, 0.35f);
            Gizmos.DrawCube(transform.position + Vector3.up * 0.01f, new Vector3(cell * 0.96f, 0.02f, cell * 0.96f));
        }
    }

    /// <summary>자원 → 광맥 정의. 팩의 엔티티 중 ResourceDeposit 모듈의 resource가 같은 것을 찾는다(자원당 하나).</summary>
    public static class DepositDefs
    {
        static Dictionary<ItemDef, EntityDef> byResource;
        static SimDatabase builtFrom;

        public static EntityDef For(ItemDef resource)
        {
            if (resource == null) return null;
            var db = SimHost.Database;
            if (db == null) return null;
            if (byResource == null || !ReferenceEquals(builtFrom, db))
            {
                byResource = new Dictionary<ItemDef, EntityDef>();
                foreach (var e in db.Entities.Values)
                {
                    var d = e.Get<ResourceDepositModuleDef>();
                    if (d?.Resource == null) continue;
                    if (byResource.ContainsKey(d.Resource))
                        Debug.LogWarning($"[ResourceDeposit] 자원 '{d.Resource.Id}'를 캐는 광맥 정의가 둘 이상입니다 — '{byResource[d.Resource].Id}'를 씁니다, '{e.Id}'는 무시.");
                    else byResource[d.Resource] = e;
                }
                builtFrom = db;
            }
            return byResource.TryGetValue(resource, out var def) ? def : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { byResource = null; builtFrom = null; }
    }
}
