using System;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>
    /// 인스펙터에 적는 시작 아이템 — SO 참조 + 개수. 심의 <see cref="ItemStack"/>은 정의(ItemDef)를 들어 직렬화되지 않으므로
    /// 씬·프리팹 저작은 이 타입으로 하고 런타임에 <see cref="ToStack"/>으로 바꾼다.
    /// 필드 이름(item·amount)은 옛 ItemStack과 같아 기존 씬·프리팹 데이터가 그대로 읽힌다.
    /// </summary>
    [Serializable]
    public class ItemStackAuthoring
    {
        public ItemDataSO item;
        public int amount = 1;

        /// <summary>정의로 풀린 스택. 아이템이 비었거나 팩에 없으면 빈 스택.</summary>
        public ItemStack ToStack()
        {
            var def = item != null ? item.Def : null;
            return def != null && amount > 0 ? new ItemStack(def, amount) : ItemStack.Empty;
        }
    }
}
