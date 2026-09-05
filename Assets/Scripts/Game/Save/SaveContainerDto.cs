using System.Collections.Generic;
using Newtonsoft.Json;
using CoreDawn.Sim;
using UnityEngine;

namespace CoreDawn.Save
{
    /// <summary>
    /// <see cref="ItemContainer"/> 한 개의 저장 형태 — 플레이어 가방, 핫바, 건물 입출력 버퍼,
    /// 상자까지 프로젝트의 모든 보관함이 같은 타입을 쓰므로 DTO도 하나면 된다.
    ///
    /// 비어 있는 슬롯은 기록하지 않는다 (36칸짜리 가방에 아이템 2개면 항목도 2개).
    /// 슬롯 인덱스를 함께 적으므로 배치가 그대로 복원된다.
    /// </summary>
    public class SaveContainerDto
    {
        [JsonProperty("slotCount")]
        public int SlotCount;

        [JsonProperty("slots")]
        public List<SaveStackDto> Slots = new();

        /// <summary>컨테이너의 현재 내용을 DTO로. null이면 빈 컨테이너.</summary>
        public static SaveContainerDto From(ItemContainer c)
        {
            if (c == null) return null;

            var dto = new SaveContainerDto { SlotCount = c.SlotCount };
            for (int i = 0; i < c.SlotCount; i++)
            {
                var s = c.PeekAt(i);
                if (s.IsEmpty) continue;
                dto.Slots.Add(new SaveStackDto
                {
                    Index = i,
                    ItemId = SaveRefs.IdOf(s.item),
                    Amount = s.amount,
                });
            }
            return dto;
        }

        /// <summary>
        /// 컨테이너를 이 DTO 내용으로 덮어쓴다.
        /// 규칙 검사(수용 필터·종류당 1스택)를 거치지 않는다 — 저장 당시엔 이미 유효했던 배치이고,
        /// 필터는 로드 직후 각 행동이 다시 설치하기 전이라 아직 비어 있을 수 있기 때문이다.
        /// </summary>
        public void ApplyTo(ItemContainer c)
        {
            if (c == null) return;

            var stacks = new ItemStack[c.SlotCount];
            foreach (var s in Slots)
            {
                if (s == null || s.Index < 0 || s.Index >= stacks.Length) continue;

                var item = SaveRefs.Item(s.ItemId);
                if (item == null || s.Amount <= 0) continue;

                stacks[s.Index] = new ItemStack(item, s.Amount);
            }

            c.RestoreSlotsRaw(stacks);
        }
    }

    /// <summary>슬롯 하나에 놓인 스택. 인덱스를 들고 있어 배치가 보존된다.</summary>
    /// <summary>
    /// 역할별 그릇 묶음 ↔ <see cref="InventoryModule"/>. 역할 이름표(input·output·main·hotbar…)는 InventoryModule.Roles/ByRole이 붙인다 —
    /// 여기엔 역할 목록이 없으므로 역할이 늘어도 세이브 코드는 그대로다. 저장된 역할이 지금 정의에 없으면 조용히 버리지 않고 경고한다.
    /// </summary>
    public static class SaveContainers
    {
        public static Dictionary<string, SaveContainerDto> Capture(InventoryModule inventory)
        {
            var saved = new Dictionary<string, SaveContainerDto>();
            if (inventory != null)
                foreach (var (role, container) in inventory.Roles) saved[role] = SaveContainerDto.From(container);
            return saved;
        }

        public static void Restore(Dictionary<string, SaveContainerDto> saved, InventoryModule inventory, string owner)
        {
            if (saved == null) return;
            foreach (var (role, dto) in saved)
            {
                var container = inventory?.ByRole(role);
                if (container == null)
                {
                    Debug.LogWarning($"[Save] '{owner}'에 그릇 '{role}'이 없어 저장된 내용물을 되돌리지 못했습니다 — 정의(Inventory)가 바뀐 대상입니다.");
                    continue;
                }
                dto?.ApplyTo(container);
            }
        }
    }

    public class SaveStackDto
    {
        [JsonProperty("i")]
        public int Index;

        [JsonProperty("item")]
        public string ItemId;

        [JsonProperty("n")]
        public int Amount;

        // (구 "max" — 스택마다의 상한. ItemDef.maxStack이 유일한 주인이 되면서 저장할 것이 없어졌다.
        //  옛 세이브에 남은 키는 역직렬화에서 조용히 무시된다.)

        public static SaveStackDto From(ItemStack s) =>
            s.IsEmpty
                ? null
                : new SaveStackDto { Index = -1, ItemId = SaveRefs.IdOf(s.item), Amount = s.amount };

        public ItemStack ToStack()
        {
            var item = SaveRefs.Item(ItemId);
            if (item == null || Amount <= 0) return ItemStack.Empty;
            return new ItemStack(item, Amount);
        }
    }
}
