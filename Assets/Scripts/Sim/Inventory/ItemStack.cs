namespace CoreDawn.Sim
{
    /// <summary>
    /// 슬롯 하나의 내용 — 아이템 정의 + 개수. 심의 값이라 인스펙터에 직접 저작하지 않는다
    /// (씬·프리팹의 시작 아이템은 <c>ItemStackAuthoring</c>(SO 참조)으로 적고 런타임에 이것으로 바꾼다).
    /// </summary>
    public class ItemStack
    {
        /// <summary>아이템 데이터를 모를 때만 쓰는 폴백 — 정상 경로에서는 항상 정의(ItemDef.MaxStack)가 이긴다.</summary>
        public const int DefaultMaxStack = 64;

        public ItemDef item;
        public int amount;

        // 상한은 스택이 들고 있지 않다. 예전의 maxStackSize 필드는 같은 아이템인데도 어디서
        // 생겼느냐로 값이 갈렸고(세이브가 그 값을 굳혀 데이터 조정이 기존 세이브에 닿지 못했다),
        // 인스펙터에 저작한 값은 정작 아무도 읽지 않았다.
        //
        // "이 스택은 몇 개까지 쌓이나"는 스택만 봐서는 답이 없는 질문이다 — 같은 탄약도
        // 가방에서는 100개, 포탑 탄약함에서는 20개다. 담는 그릇을 알아야 하므로
        // ItemContainer.CapFor(item) / RoomAt(slot, item)에 물을 것.
        public ItemStack(ItemDef item, int amount)
        {
            this.item = item;
            this.amount = amount;
        }
    }
}
