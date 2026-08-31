using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 지뢰(접촉 기폭)의 공장 어댑터 — 심 모듈 <see cref="TriggerModule"/>과 공장 틱 사이.
    /// 깨우기 예약뿐이다. once 지뢰는 터지면 스스로 죽어 공장이 치우므로 세이브할 상태가 없다.
    /// </summary>
    public class TriggerBehavior : IBuildingBehavior
    {
        readonly BuildingModule _b;
        readonly TriggerModule _trigger;

        public TriggerBehavior(BuildingModule b, TriggerModule trigger)
        {
            _b = b;
            _trigger = trigger;
        }

        public TriggerModule Trigger => _trigger;

        public void OnAfterPlaced() { }

        public void Tick(float dt)
        {
            var sim = _b.Factory;
            _trigger.Step(sim.Now);
            if (!_trigger.Armed || _b.IsRemoved) return;
            float wait = _trigger.ReadyAt > sim.Now ? _trigger.ReadyAt - sim.Now : TriggerModule.ScanInterval;
            sim.ScheduleWake(_b, Mathf.Max(dt, wait));
        }
    }
}
