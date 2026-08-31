using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Save;
using CoreDawn.Sim;
using CoreDawn.UI;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 오라 건물의 공장 어댑터 — 심 모듈 <see cref="AuraEmitterModule"/>과 공장 틱 사이(포탑의 TurretBehavior와 같은 자리).
    /// 틱마다 Step, 깨우기 예약(다음 펄스 시각 또는 탐색 주기; 연료가 없으면 그릇의 Changed가 깨운다),
    /// 연료 소비 뒤 상류 깨우기, 세이브(쿨다운), 연료함 열기.
    /// </summary>
    public class AuraBehavior : IBuildingBehavior, ISaveableBehavior, IInteractiveBehavior
    {
        readonly BuildingModule _b;
        readonly AuraEmitterModule _aura;

        public AuraBehavior(BuildingModule b, AuraEmitterModule aura)
        {
            _b = b;
            _aura = aura;

            var ammo = b.Owner.Get<AmmoConsumerModule>();
            if (ammo != null) ammo.Consumed += _ => _b.NotifyUpstream();
            _b.Input.Changed += () => _b.Factory.MarkDirty(_b);
        }

        public AuraEmitterModule Aura => _aura;

        public string InteractPrompt => _b.Input.SlotCount > 0 ? "연료함 열기" : null;

        public void Interact(PlayerController player) => GameScreens.OpenContainer(_b.Input);

        public void OnAfterPlaced() { }

        public void Tick(float dt)
        {
            var sim = _b.Factory;
            _aura.Step(sim.Now);
            if (_aura.Starved) return;   // 연료가 오면 Input.Changed가 깨운다
            float wait = _aura.ReadyAt > sim.Now ? _aura.ReadyAt - sim.Now : AuraEmitterModule.ScanInterval;
            sim.ScheduleWake(_b, Mathf.Max(dt, wait));
        }

        public class SaveState
        {
            [JsonProperty("readyAt")] public float ReadyAt;
        }

        public object CaptureState() => new SaveState { ReadyAt = _aura.ReadyAt };

        public void RestoreState(JToken state)
        {
            var s = SaveJson.FromToken<SaveState>(state);
            if (s == null) return;
            _aura.RestoreState(s.ReadyAt);
            _b.Factory.MarkDirty(_b);
        }
    }
}
