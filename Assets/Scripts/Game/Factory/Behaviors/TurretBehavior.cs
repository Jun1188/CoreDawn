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
    /// 포탑의 공장 어댑터 — 심 모듈 <see cref="TurretModule"/>과 공장 틱 사이. 조립기의 AssemblerBehavior와 같은 자리다.
    /// 하는 일: 틱마다 Step, 깨우기 예약(표적이 있으면 매 틱, 없으면 탐색 주기, 탄이 없으면 안 깨움 —
    /// 탄이 들어오면 그릇의 Changed가 깨운다), 한 발 소비 뒤 상류 깨우기, 세이브(쿨다운·방위), 탄약함 열기.
    /// 판단은 전부 모듈에 있다 — 여기엔 공장 배관만.
    /// </summary>
    public class TurretBehavior : IBuildingBehavior, ISaveableBehavior, IInteractiveBehavior
    {
        readonly BuildingModule _b;
        readonly TurretModule _turret;

        public TurretBehavior(BuildingModule b, TurretModule turret)
        {
            _b = b;
            _turret = turret;

            var ammo = b.Owner.Get<AmmoConsumerModule>();
            if (ammo != null) ammo.Consumed += _ => _b.NotifyUpstream();   // 자리 생김 → 막혀 있던 벨트 재개
            // 벨트 보급이든 손 장전이든 탄이 들어오면 굶던 포탑이 깬다 (push 경로는 MarkDirty를 이미 하지만 UI 장전은 이 길뿐)
            _b.Input.Changed += () => _b.Factory.MarkDirty(_b);
        }

        public TurretModule Turret => _turret;

        public string InteractPrompt => _b.Input.SlotCount > 0 ? "탄약함 열기" : null;

        public void Interact(PlayerController player)
        {
            // 보관함 = 입력 버퍼. 벨트가 넣는 곳과 같아서 화면에 보이는 것이 곧 저장소의 전부다.
            GameScreens.OpenContainer(_b.Input);
        }

        public void OnAfterPlaced() { }

        public void Tick(float dt)
        {
            var sim = _b.Factory;
            _turret.Step(sim.Now, dt);
            if (_turret.Phase == TurretPhase.Starved) return;   // 탄이 오면 Input.Changed가 깨운다
            sim.ScheduleWake(_b, _turret.Target != null ? dt : TurretModule.ScanInterval);
        }

        // ── 세이브 ────────────────────────────────────────────────
        public class SaveState
        {
            [JsonProperty("readyAt")] public float ReadyAt;
            [JsonProperty("yaw")] public float Yaw;
        }

        public object CaptureState() => new SaveState { ReadyAt = _turret.ReadyAt, Yaw = _turret.Yaw };

        public void RestoreState(JToken state)
        {
            var s = SaveJson.FromToken<SaveState>(state);
            if (s == null) return;
            _turret.RestoreState(s.ReadyAt, s.Yaw);
            _b.Factory.MarkDirty(_b);
        }
    }
}
