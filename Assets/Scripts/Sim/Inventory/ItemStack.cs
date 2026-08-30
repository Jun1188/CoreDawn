namespace CoreDawn.Sim
{
    /// <summary>
    /// 슬롯 하나의 내용 — 아이템 정의 + 개수. <b>값</b>이다: 그릇에서 꺼내 보는 것(PeekAt)은 복사본이라
    /// 바깥에서 amount를 고쳐도 그릇은 모르고, 두 그릇이 같은 스택 객체를 나눠 갖는 일(앨리어싱)이 타입 수준에서 불가능하다.
    /// 슬롯을 바꾸려면 반드시 ItemContainer.SetAt/TryPutAt 등을 거친다 — 그래야 변경 통지(Touch)를 빠뜨릴 수 없다.
    ///
    /// 인스펙터에 직접 저작하지 않는다(정의는 Unity가 직렬화하지 않는다) — 씬·프리팹의 시작 아이템은 <c>ItemStackAuthoring</c>(SO 참조).
    /// </summary>
    public readonly struct ItemStack
    {
        /// <summary>아이템 데이터를 모를 때만 쓰는 폴백 — 정상 경로에서는 항상 정의(ItemDef.MaxStack)가 이긴다.</summary>
        public const int DefaultMaxStack = 64;

        public readonly ItemDef item;
        public readonly int amount;

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

        /// <summary>빈 슬롯. default(ItemStack)와 같다.</summary>
        public static readonly ItemStack Empty = default;

        /// <summary>아이템이 없거나 0개 이하 — "없음"의 유일한 판정(예전의 null·item null·amount 0 세 갈래를 대신한다).</summary>
        public bool IsEmpty => item == null || amount <= 0;

        /// <summary>같은 아이템, 다른 개수. 0 이하면 빈 슬롯.</summary>
        public ItemStack With(int newAmount) => newAmount > 0 ? new ItemStack(item, newAmount) : Empty;

        public override string ToString() => IsEmpty ? "(empty)" : $"{item.Id} x{amount}";
    }
}
