using System;
using CoreDawn.Save;
using CoreDawn.Sim;

namespace CoreDawn.Inventories
{
    /// <summary>씬·프리팹에 적는 아이템 스택 하나(시작 소지품·상자 초기 내용물). 정의는 직렬화되지 않으므로 팩 id로 적는다.</summary>
    [Serializable]
    public class ItemStackAuthoring
    {
        public string itemId;
        public int amount = 1;

        public ItemStack ToStack()
        {
            var def = SaveRefs.Item(itemId);
            return def != null && amount > 0 ? new ItemStack(def, amount) : ItemStack.Empty;
        }
    }
}
