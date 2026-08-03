using System.Collections.Generic;

/// <summary>
/// ⚠ 전부 더미다. BuildingDataSO에 건설 비용 필드가 아직 없어서
/// 툴팁 자리만 먼저 세워 둔 것이다.
///
/// 진짜로 붙일 때는 BuildingDataSO에 비용 배열(아이템 + 수량)을 추가하고
/// 이 파일을 지운 뒤 BuildMenuView의 호출부를 그 필드로 바꾸면 된다 —
/// 가짜 값이 여기 한 곳에만 있으므로 어디까지가 진짜인지 헷갈리지 않는다.
/// </summary>
public static class BuildCostDummyData
{
    public readonly struct Entry
    {
        public readonly string Name;
        public readonly int Amount;
        public readonly ItemLine Line;

        public Entry(string name, int amount, ItemLine line)
        {
            Name = name;
            Amount = amount;
            Line = line;
        }
    }

    /// <summary>건물 하나의 건설 비용. 크기·티어에 대충 비례시켜 그럴듯하게만 만든다.</summary>
    public static IEnumerable<Entry> For(BuildingDataSO so)
    {
        if (so == null) yield break;

        int scale = UnityEngine.Mathf.Max(1, so.size.x * so.size.y);

        yield return new Entry("철판", 4 * scale, ItemLine.Iron);

        if (so.requiredCoreTier >= 1 || so.inputSlots > 1)
            yield return new Entry("철 기어", 2 * scale, ItemLine.Iron);

        if (so.requiredCoreTier >= 1)
            yield return new Entry("구리 전선", 3 * scale, ItemLine.Copper);

        if (so.requiredCoreTier >= 2)
            yield return new Entry("회로 기판", 2 * scale, ItemLine.Copper);

        if (so.requiredCoreTier >= 3)
            yield return new Entry("에너지 셀", 1 * scale, ItemLine.Crystal);
    }
}
