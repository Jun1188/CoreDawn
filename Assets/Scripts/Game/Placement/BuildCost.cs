using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Inventories;
using CoreDawn.Data;
using CoreDawn.Sim;
using System.Collections.Generic;

namespace CoreDawn.Placement
{
    /// <summary>
    /// 건설 비용 판정·차감·환급을 한 곳에 모은다.
    /// 배치 판정(프리뷰 색)·실제 차감·철거 환급·건설 메뉴 UI가 전부 같은 규칙을 써야 하므로,
    /// "얼마가 드는가"를 계산하는 코드가 여러 벌로 갈라지지 않게 여기만 본다.
    ///
    /// 플레이어 소지품은 핫바와 가방 두 컨테이너에 나뉘어 있으므로 항상 둘을 합쳐 센다.
    /// </summary>
    public static class BuildCost
    {
        static ItemContainer Hotbar =>
            PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.HotbarContainer : null;

        static ItemContainer Bag =>
            PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.MainContainer : null;

        /// <summary>비용이 정의돼 있는가. 비어 있으면 공짜 건물(코어 등).</summary>
        static List<ItemAmount> CostOf(EntityDef def) => def?.Get<BuildingModuleDef>()?.Cost;

        public static bool HasCost(EntityDef def) { var cost = CostOf(def); return cost != null && cost.Count > 0; }

        public static int PlayerCountOf(ItemDef item)
        {
            if (item == null) return 0;
            int n = 0;
            if (Hotbar != null) n += Hotbar.CountOf(item);
            if (Bag != null) n += Bag.CountOf(item);
            return n;
        }

        /// <summary>
        /// 지금 지을 수 있는가. 인벤토리가 아직 없으면(테스트·부트스트랩 전) 통과시킨다 —
        /// 비용 때문에 시뮬레이션 테스트가 막히면 안 된다.
        /// </summary>
        public static bool CanAfford(EntityDef def)
        {
            if (!HasCost(def)) return true;
            if (PlayerInventoryHolder.Instance == null) return true;

            foreach (var c in CostOf(def))
            {
                if (c.Item == null) continue;
                if (PlayerCountOf(c.Item) < c.Amount) return false;
            }
            return true;
        }


        /// <summary>부족한 첫 재료 — 건설 메뉴가 "무엇이 모자란지" 보여줄 때 쓴다.</summary>
        public static bool TryGetMissing(EntityDef def, out ItemDef item, out int shortBy)
        {
            item = null;
            shortBy = 0;
            if (!HasCost(def) || PlayerInventoryHolder.Instance == null) return false;

            foreach (var c in CostOf(def))
            {
                if (c.Item == null) continue;
                int have = PlayerCountOf(c.Item);
                if (have >= c.Amount) continue;

                item = c.Item;
                shortBy = c.Amount - have;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 비용을 차감한다. 전량 가능할 때만 차감하고, 아니면 아무것도 건드리지 않는다 —
        /// 반쯤 깎인 채로 배치가 실패하면 아이템만 사라진다.
        /// </summary>
        public static bool TryCharge(EntityDef def)
        {
            if (!HasCost(def)) return true;
            if (PlayerInventoryHolder.Instance == null) return true;
            if (!CanAfford(def)) return false;

            foreach (var c in CostOf(def))
            {
                if (c.Item == null || c.Amount <= 0) continue;
                Consume(c.Item, c.Amount);
            }
            return true;
        }


        /// <summary>
        /// 철거 시 전액 환급. 부분 환급은 배치 실험을 망설이게 만들어 공장 게임의 재미를 깎는다.
        /// 인벤토리에 자리가 없으면 바닥에 떨군다 — 환급이 조용히 증발하면 안 된다.
        /// </summary>
        public static void Refund(EntityDef def, Vector3 dropPosition)
        {
            if (!HasCost(def)) return;

            var holder = PlayerInventoryHolder.Instance;
            foreach (var c in CostOf(def))
            {
                if (c.Item == null || c.Amount <= 0) continue;

                if (holder != null && holder.AddItemToPlayer(c.Item, c.Amount)) continue;
                PlacementBridge.DropAt(c.Item, c.Amount, dropPosition);
            }
        }

        /// <summary>가방부터 소진하고 모자라면 핫바에서 뺀다 — 핫바를 최대한 유지한다.</summary>
        static void Consume(ItemDef item, int n)
        {
            int fromBag = Bag != null ? Mathf.Min(n, Bag.CountOf(item)) : 0;
            if (fromBag > 0) Bag.TryConsume(item, fromBag);

            int rest = n - fromBag;
            if (rest > 0 && Hotbar != null) Hotbar.TryConsume(item, rest);
        }
    }
}
