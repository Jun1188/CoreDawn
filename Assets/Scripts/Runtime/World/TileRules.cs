/// <summary>
/// 타일이 게임 규칙에 어떻게 작용하는가 — 비용·속도·건설을 한 곳에서 정한다.
///
/// 타일 종류(MapTile)는 맵 데이터가 갖고, "그래서 어떻게 되는가"는 여기가 갖는다.
/// 판정마다 흩어져 있으면(길찾기는 A에서, 배치는 B에서, 이동은 C에서) 강 하나를 조정할 때
/// 세 곳을 고쳐야 하고 서로 어긋난다.
///
/// 비용의 단위: <b>직교 한 칸 = 10</b> (대각은 14). 그래서 강 30은 "3칸 우회할 값어치"다.
/// </summary>
public static class TileRules
{
    /// <summary>직교 한 칸의 기준 비용. 대각은 ×1.4로 계산된다.</summary>
    public const int BaseCost = 10;

    /// <summary>통행 불가를 뜻하는 비용 — 더할 때 넘치지 않도록 int.MaxValue보다 훨씬 작게 잡는다.</summary>
    public const int Blocked = 1_000_000;

    /// <summary>
    /// 이 칸에 발을 들이는 비용. 낮을수록 선호한다.
    ///   지면 10 — 기준
    ///   강   30 — 건널 수는 있지만 3칸 우회할 값어치. 다리·길목 설계가 의미를 갖는다
    ///   절벽 ∞  — 유일한 진짜 차단
    /// </summary>
    public static int EnterCost(MapTile tile) => tile switch
    {
        MapTile.Ground => BaseCost,
        MapTile.River => BaseCost * 3,
        _ => Blocked,          // Cliff · 맵 밖
    };

    /// <summary>이 칸을 지날 때의 이동 속도 배율 — 강은 허리까지 물이라 느리다.</summary>
    public static float SpeedMultiplier(MapTile tile) => tile switch
    {
        MapTile.River => 0.5f,
        _ => 1f,
    };

    /// <summary>건물을 세울 수 있는가 — 지면에만. 강은 지날 수는 있어도 짓지 못한다.</summary>
    public static bool CanBuild(MapTile tile) => tile == MapTile.Ground;
}
