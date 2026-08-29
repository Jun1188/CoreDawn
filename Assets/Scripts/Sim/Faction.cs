namespace CoreDawn.Sim
{
    /// <summary>
    /// 편 — "누가 누구를 때릴 수 있는가"의 기준. 심의 상태이지 레이어가 아니다.
    ///
    /// 예전에는 뷰의 물리 레이어("Monster")로 적을 가렸다. 레이어는 물리·렌더링의 도구인데 게임 규칙까지
    /// 짊어지자 "레이어가 원래 기능을 벗어났다"가 됐고, 헤드리스 심(테스트·서버)에서는 레이어 자체가 없다.
    /// 편이 심에 있으면 무적 건물(isAttackable=false)이 아군 공격만 흘리는 규칙도 심 안에서 끝난다.
    /// </summary>
    public enum Faction
    {
        /// <summary>어느 편도 아님 — 나무·광맥처럼 공격 대상이지만 편을 가르지 않는 것.</summary>
        Neutral = 0,

        /// <summary>플레이어와 그 건물·타워.</summary>
        Player = 1,

        /// <summary>밤의 몬스터와 둥지.</summary>
        Monster = 2,
    }

    public static class FactionExtensions
    {
        /// <summary>서로 적인가. 중립은 누구의 적도 아니고, 같은 편끼리는 적이 아니다.</summary>
        public static bool IsHostileTo(this Faction a, Faction b)
            => a != Faction.Neutral && b != Faction.Neutral && a != b;
    }
}
