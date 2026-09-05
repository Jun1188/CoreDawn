using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 채굴기 — 덮고 있는 칸들의 광맥(<see cref="ResourceDepositModule"/>)에서 주기마다 1개를 꺼내 출력 그릇으로(구 MinerBehavior).
    ///
    /// 광맥은 한 칸짜리라 2×2 채굴기는 광맥 넷 위에 선다. 매장량이 없으므로 산출 속도는 광맥의 난이도(extractInterval)와
    /// 채굴기 배율만이 정한다. 캔 것은 덮는 광맥들에 돌아가며 기록한다(누적 채굴량 — 튜토리얼·상태 표시용).
    /// 배치 규칙(덮는 칸 전부가 같은 자원의 광맥, 부분 덮기 금지)은 FactorySystem.CanPlaceMiner가 지킨다.
    /// 광맥 없이 놓인 채굴기(테스트·규칙 미적용 배치)는 아무것도 캐지 않는다.
    /// 어느 광맥을 덮었는지는 배치가 끝난 뒤 소유자(공장)가 <see cref="SetDeposits"/>로 알려준다 — 모듈은 그리드를 모른다.
    /// </summary>
    public sealed class ExtractorModule : EntityModule, ISteppable, ISaveableModule
    {
        public ExtractorModuleDef Def { get; }
        public ExtractorModule(ExtractorModuleDef def) { Def = def ?? throw new ArgumentNullException(nameof(def)); }

        readonly List<ResourceDepositModule> _deposits = new();
        ItemDef _target;
        float   _readyAt = -1f;   // 채굴 완료 예정 시각 (-1 = 예약 없음 = 정지 상태)
        int     _next;            // 다음 채굴을 기록할 광맥 인덱스 — 세이브하지 않는다

        public ItemDef Target => _target;
        public IReadOnlyList<ResourceDepositModule> Deposits => _deposits;

        // 출력 그릇 — 같은 엔티티의 InventoryModule. 한 번 찾으면 굳힌다(Crafter와 같은 규칙).
        ItemContainer _output;
        ItemContainer Output => _output ??= Owner?.Get<InventoryModule>()?.Output;

        /// <summary>배치가 확정된 뒤 — 덮는 칸들의 광맥. 자원은 규칙상 전부 같으므로 첫 광맥의 것.</summary>
        public void SetDeposits(IEnumerable<ResourceDepositModule> deposits)
        {
            _deposits.Clear();
            if (deposits != null) _deposits.AddRange(deposits);
            _target = _deposits.Count > 0 ? _deposits[0].Resource : null;
        }

        // ── 공통 틱(ISteppable): 완료 시각이 됐으면 캐서 출력 그릇에 넣고(밀어내기는 공통 틱이 한다),
        // 그릇에 자리가 있으면 다음 채굴을 예약한다. 자리가 없으면 정지(stall) — 하류가 소비하면 깨어난다.
        float ISteppable.Step(float now, float dt)
        {
            if (_target == null || _deposits.Count == 0) return 0f;

            // 채굴 완료 판정 — 덮는 광맥들에 돌아가며 기록
            if (_readyAt >= 0f && now >= _readyAt)
            {
                _readyAt = -1f;
                int taken = _deposits[_next].Extract(1);
                _next = (_next + 1) % _deposits.Count;
                // 예약 시점에 그릇 여유를 확인했고 그릇을 채우는 것은 자신뿐이라 여기서 유실될 수 없다
                if (taken > 0) Output?.TryAdd(_target);
            }

            // 다음 채굴 예약 — 출력 그릇에 자리가 있을 때만
            if (_readyAt < 0f && Output != null && Output.HasRoomFor(_target))
            {
                // 광맥 난이도 ÷ 채굴기 배율 — 같은 채굴기라도 크리스탈 광맥에서는 느리다
                float interval = _deposits[0].ExtractInterval / Math.Max(0.01f, Def.SpeedMultiplier);
                _readyAt = now + interval;
                return interval;
            }
            return _readyAt >= 0f ? Math.Max(0f, _readyAt - now) : 0f;   // 이른 기상 — 완료 시각에 다시
        }

        // ── 세이브(ISaveableModule) — 키는 옛 MinerBehavior 저장과 같다 ──
        public sealed class SaveState
        {
            [JsonProperty("target")] public string TargetItemId;
            [JsonProperty("readyAt")] public float ReadyAt;
        }

        public object CaptureState() => new SaveState { TargetItemId = _target?.Id, ReadyAt = _readyAt };

        /// <summary>기상 예약은 여기서 하지 않는다 — 복원자가 MarkDirty를 걸면 공통 틱이 남은 시간으로 다시 예약한다.</summary>
        public void RestoreState(JToken state)
        {
            var s = state?.ToObject<SaveState>();
            if (s == null) return;
            // 배치 직후 SetDeposits가 광맥에서 이미 대상을 정했겠지만, 저장된 값이 정본이다
            if (!string.IsNullOrEmpty(s.TargetItemId)) _target = FindItem(s.TargetItemId);
            _readyAt = s.ReadyAt;
        }

        static ItemDef FindItem(string id)
        {
            var def = SimHost.Database?.Item(id);
            if (def == null) UnityEngine.Debug.LogWarning($"[Extractor] 세이브의 아이템 id \"{id}\"가 팩에 없습니다 — 그 항목은 건너뜁니다.");
            return def;
        }
    }
}
