namespace CoreDawn.Sim
{
    /// <summary>
    /// 시계를 받아 한 걸음 나아가는 모듈(포탑·오라·지뢰·제작기·채굴기·라우터…).
    /// 반환 = 다음에 깨워야 할 때까지 초. 0 이하 = 예약 없음(그릇 변화 같은 이벤트가 깨운다).
    /// 누가 부르느냐는 시스템의 몫이다 — 건물은 FactorySystem(10Hz 깨움 큐), 플레이어는 PlayerSystem(매 프레임).
    /// 모듈은 자기가 어느 시계 위에 있는지 모른다.
    /// </summary>
    public interface ISteppable
    {
        float Step(float now, float dt);
    }
}
