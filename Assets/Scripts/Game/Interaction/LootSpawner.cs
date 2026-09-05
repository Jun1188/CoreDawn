using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Sim;

namespace CoreDawn.Interaction
{
    /// <summary>
    /// 사망 드롭의 게임 쪽 — 심의 <c>EntityWorld.Died</c>를 듣고, 죽은 엔티티의 <see cref="LootModule"/>(정의 드롭)과
    /// 그릇 내용물(<see cref="InventoryModule"/>, 정의의 dropInventory)을 그 자리에 뿌린다.
    /// 구 NestView.dropItem(인스펙터 SO)과 FactoryBootstrap의 제거 시 드롭을 대체한다 — 철거(죽음 아님)의 내용물 드롭은
    /// 여전히 FactoryBootstrap의 Removed 경로가 맡고, 죽어서 제거된 건물은 거기서 건너뛴다(이중 드롭 방지).
    /// </summary>
    public static class LootSpawner
    {
        static EntityWorld hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            var world = SimHost.World;
            if (ReferenceEquals(hooked, world)) return;
            if (hooked != null) hooked.Died -= OnDied;
            world.Died += OnDied;
            hooked = world;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => hooked = null;

        static void OnDied(Entity e)
        {
            var loot = e.Get<LootModule>();
            if (loot == null) return;
            var pos = e.Position + Vector3.up * 0.6f;

            foreach (var d in loot.Def.Drops)
            {
                if (d.Item == null || d.Amount <= 0) continue;
                var throwDir = (Vector3.up + Random.insideUnitSphere * 0.2f).normalized;   // 위로 약간 던지는 방향
                DroppedItem.Spawn(d.Item, d.Amount, pos, throwDir);
            }

            if (!loot.Def.DropInventory) return;
            var inventory = e.Get<InventoryModule>();
            if (inventory == null) return;
            foreach (var (_, container) in inventory.Roles)
                PlacementBridge.DropContainer(container, pos);   // 건물이 사라져도 아이템은 보존된다
        }
    }
}
