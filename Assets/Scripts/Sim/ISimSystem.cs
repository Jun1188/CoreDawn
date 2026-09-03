namespace CoreDawn.Sim
{
    /// <summary>
    /// 월드 스텝마다 한 번 불리는 시스템 — <see cref="SimWorld.Step"/>이 등록 순서(<see cref="SimOrder"/>)대로 돈다.
    /// dt는 언제나 <see cref="SimWorld.TickDt"/>(고정 20Hz). 자기 시계를 두지 말고 <see cref="SimWorld.Now"/>를 볼 것 —
    /// 웨이브처럼 "시작부터 흐른 시간"이 의미인 곳만 자기 누적값을 둔다.
    /// </summary>
    public interface ISimSystem
    {
        void Tick(float dt);
    }

    /// <summary>시스템 실행 순서 — 주야 시계, 효과(배율)가 먼저, 그 배율로 몬스터가 움직이고, 플레이어 무기, 웨이브 판정, 공장 순.</summary>
    public static class SimOrder
    {
        /// <summary>주야 시계(TimeManager) — 이 틱의 낮/밤을 먼저 정한 뒤 나머지가 읽는다.</summary>
        public const int DayCycle = -10;
        public const int Effects = 0;
        public const int Monsters = 10;
        public const int Players = 20;
        public const int Waves = 30;
        public const int Factory = 40;
    }
}
