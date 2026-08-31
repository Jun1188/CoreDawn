using Newtonsoft.Json.Linq;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 세이브에 실을 고유 상태가 있는 모듈(쿨다운·방위·필터·진행도…). 그릇(Inventory)·HP는 따로 저장되므로 여기 넣지 않는다.
    /// 세이브 모듈이 엔티티의 모듈을 훑어 타입 이름(…Module 접미사 제외, 예: "Turret")을 키로 싣는다 — 건물 종류를 몰라도 된다.
    /// 옛 <c>ISaveableBehavior</c>의 후계: 행동이 사라지면서 저장 주체가 모듈이 됐다.
    /// </summary>
    public interface ISaveableModule
    {
        object CaptureState();
        void RestoreState(JToken state);
    }
}
