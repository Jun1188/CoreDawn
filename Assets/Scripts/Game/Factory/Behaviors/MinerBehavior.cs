using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Save;
using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 채굴기 — 덮고 있는 칸들의 광맥(<see cref="ResourceDepositModule"/>)에서 주기마다 1개를 꺼내 출력 버퍼로.
    ///
    /// 광맥은 한 칸짜리라 2×2 채굴기는 광맥 넷 위에 선다. 매장량이 없으므로 산출 속도는 광맥의 난이도(extractInterval)와
    /// 채굴기 배율만이 정한다. 캔 것은 덮는 광맥들에 돌아가며 기록한다(누적 채굴량 — 튜토리얼·상태 표시용).
    /// 배치 규칙(덮는 칸 전부가 같은 자원의 광맥, 부분 덮기 금지)은 FactorySystem.CanPlaceMiner가 지킨다.
    /// 광맥 없이 놓인 채굴기(테스트·규칙 미적용 배치)는 아무것도 캐지 않는다.
    /// </summary>
    public class MinerBehavior : IBuildingBehavior, ISaveableBehavior
    {
        readonly BuildingModule     _b;
        readonly ExtractorModuleDef _data;
        readonly List<ResourceDepositModule> _deposits = new();

        ItemDef _target;
        float   _readyAt = -1f;   // 채굴 완료 예정 시각 (-1 = 예약 없음 = 정지 상태)
        int     _next;            // 다음 채굴을 기록할 광맥 인덱스 — 세이브하지 않는다

        public MinerBehavior(BuildingModule b, ExtractorModuleDef data) { _b = b; _data = data; }

        public ItemDef Target => _target;
        public IReadOnlyList<ResourceDepositModule> Deposits => _deposits;

        /// <summary>배치가 확정된 뒤 — 덮는 칸들의 광맥을 잡는다. 자원은 규칙상 전부 같으므로 첫 광맥의 것.</summary>
        public void OnAfterPlaced()
        {
            _deposits.Clear();
            _deposits.AddRange(_b.Factory.DepositsUnder(_b.Origin, _b.Size));
            _target = _deposits.Count > 0 ? _deposits[0].Resource : null;
        }

        public void Tick(float dt)
        {
            if (_target == null || _deposits.Count == 0) return;
            var sim = _b.Factory;

            // 1. 밀려 있던 출력 버퍼부터 배출 (하류가 받는 만큼 전부)
            _b.FlushOutputs();

            // 2. 채굴 완료 판정 — 덮는 광맥들에 돌아가며 기록
            if (_readyAt >= 0f && sim.Now >= _readyAt)
            {
                _readyAt = -1f;
                int taken = _deposits[_next].Extract(1);
                _next = (_next + 1) % _deposits.Count;
                // 예약 시점에 버퍼 여유를 확인했으므로 여기서 유실될 수 없다
                if (taken > 0 && !_b.TryPushOutput(_target))
                    _b.Output.TryAdd(_target);
            }

            // 3. 다음 채굴 예약 — 출력 버퍼에 자리가 있을 때만. 자리가 없으면 정지(stall); 하류의 NotifyUpstream이 다시 깨운다.
            if (_readyAt < 0f && _b.Output.HasRoomFor(_target))
            {
                // 광맥 난이도 ÷ 채굴기 배율 — 같은 채굴기라도 크리스탈 광맥에서는 느리다
                float interval = _deposits[0].ExtractInterval / Mathf.Max(0.01f, _data.SpeedMultiplier);
                _readyAt = sim.Now + interval;
                sim.ScheduleWake(_b, interval);
            }
        }

        // ── 세이브 ────────────────────────────────────────────────────
        public class SaveState
        {
            [JsonProperty("target")] public string TargetItemId;
            [JsonProperty("readyAt")] public float ReadyAt;
        }

        public object CaptureState() => new SaveState
        {
            TargetItemId = SaveRefs.IdOf(_target),
            ReadyAt = _readyAt,
        };

        public void RestoreState(JToken state)
        {
            var s = SaveJson.FromToken<SaveState>(state);
            if (s == null) return;
            // 배치 직후 OnAfterPlaced가 광맥에서 이미 대상을 정했겠지만, 저장된 값이 정본이다
            if (!string.IsNullOrEmpty(s.TargetItemId)) _target = SaveRefs.Item(s.TargetItemId);
            _readyAt = s.ReadyAt;
            // 기상 예약은 심의 힙에 있던 것이라 저장 대상이 아니다 — 완료 시각으로부터 다시 건다
            if (_readyAt >= 0f) _b.Factory.ScheduleWake(_b, Mathf.Max(0f, _readyAt - _b.Factory.Now));
        }
    }
}
