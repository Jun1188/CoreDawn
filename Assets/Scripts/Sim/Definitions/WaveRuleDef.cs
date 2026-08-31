using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 밤 웨이브 규칙 — 팩 `wave` 섹션(하나). 일차별 표가 아니라 <b>점수(예산)식</b>이다:
    ///   score = (day × dayPoints + gate × gatePoints) × stimuli × (살아 있는 둥지 / 전체 둥지)
    /// 점수가 곧 포인트다 — 명단(roster)의 cost로 깎으며 스폰한다. 파괴된 둥지는 총량을 줄이고(비율) 남은 둥지를 자극한다(stimuli 배율 + 버프).
    /// 스폰은 밤에 고른 둥지들의 스폰 포인트에서 버스트(뭉텅이)로, 진입로(nightSpawnPoints)에서는 점수와 무관한 기본 몹 무리가 지루함을 막는다.
    /// </summary>
    public sealed class WaveRuleDef : Def
    {
        /// <summary>밤마다 깔리는 기본 점수 — 일차·게이트와 무관.</summary>
        [JsonProperty("basePoints")] public float BasePoints;
        /// <summary>일차 1당 점수.</summary>
        [JsonProperty("dayPoints")] public float DayPoints = 40f;
        /// <summary>납품한 게이트(코어 티어) 1당 점수 — 곱이 아니라 합.</summary>
        [JsonProperty("gatePoints")] public float GatePoints = 80f;
        /// <summary>
        /// 자극 강화분(사용자 식, 2026-08-31): r = 파괴 수 / 전체 둥지 수일 때 h(r) = amplitude·r^exponent + linear·r.
        /// 밤 총량 = 살아 있는 몫 (1 − r) + h(r) — 곱이 아니라 합. 첫 파괴엔 총량이 줄고(h가 작다), 파괴가 쌓일수록 r^p가 급히 커져
        /// 마지막 둥지는 둘 남았을 때보다 세다. 둥지 수가 달라도 같은 모양(비율 기준).
        /// </summary>
        [JsonProperty("stimulusAmplitude")] public float StimulusAmplitude = 2f;
        [JsonProperty("stimulusExponent")] public float StimulusExponent = 4f;
        [JsonProperty("stimulusLinear")] public float StimulusLinear = 0.1f;
        /// <summary>자극이 버스트 몬스터에 거는 영구 버프 — 값 = clamp(base + perStimulus × (stimuli − 1)).</summary>
        [JsonProperty("stimulusBuffs")] public List<StimulusBuff> StimulusBuffs = new List<StimulusBuff>();

        /// <summary>밤마다 고르는 둥지 수의 범위. max 0 = 살아 있는 전부까지.</summary>
        [JsonProperty("nestsPerNightMin")] public int NestsPerNightMin = 1;
        [JsonProperty("nestsPerNightMax")] public int NestsPerNightMax;
        /// <summary>목표 밤 길이(초) — 웨이브가 다 나오기까지의 시간. 규칙이 든다: 주야 시계의 밤은 달이 뜨고 지는 시간이지 밤 총 길이가 아니다(밤은 클리어로 끝난다).</summary>
        [JsonProperty("targetNightLength")] public float TargetNightLength = 60f;
        /// <summary>밤당 버스트 수. 간격 = 목표 밤 길이 ÷ 버스트 수.</summary>
        [JsonProperty("burstsPerNight")] public int BurstsPerNight = 4;
        /// <summary>버스트 간격(초) — 파생값.</summary>
        [JsonIgnore] public float BurstInterval => TargetNightLength / Math.Max(1, BurstsPerNight);
        /// <summary>버스트 몬스터가 스폰 포인트 둘레에 퍼지는 반경(m).</summary>
        [JsonProperty("burstSpread")] public float BurstSpread = 2f;

        [JsonProperty("roster")] public List<RosterEntry> Roster = new List<RosterEntry>();
        [JsonProperty("trickle")] public TrickleRule Trickle = new TrickleRule();

        public sealed class StimulusBuff
        {
            [JsonProperty("effect")] public string EffectId;
            [JsonProperty("base")] public float Base = 1f;
            [JsonProperty("perStimulus")] public float PerStimulus;
            [JsonProperty("min")] public float Min = 0.05f;
            [JsonProperty("max")] public float Max = 10f;
            [JsonIgnore] public EffectSpec Spec { get; internal set; }

            public float ValueAt(float stimuli) => System.Math.Clamp(Base + PerStimulus * (stimuli - 1f), Min, Max);
        }

        /// <summary>명단 항목 — cost는 점수에서 얼마를 깎나, weight는 지금 뽑힐 수 있는 항목들 사이의 확률 비율(합이 분모).</summary>
        public sealed class RosterEntry
        {
            [JsonProperty("monster")] public string MonsterId;
            [JsonProperty("cost")] public float Cost = 10f;
            [JsonProperty("weight")] public float Weight = 1f;
            [JsonProperty("minDay")] public int MinDay = 1;
            [JsonProperty("minGate")] public int MinGate;
            [JsonIgnore] public EntityDef Monster { get; internal set; }

            public bool Eligible(int day, int gate) => Monster != null && day >= MinDay && gate >= MinGate && Cost > 0f && Weight > 0f;
        }

        /// <summary>진입로의 지루함 방지 무리 — 점수·자극과 무관한 기본 몹. 점수 몹이 untilKilledFraction만큼 잡힐 때까지 주기마다.</summary>
        public sealed class TrickleRule
        {
            [JsonProperty("monster")] public string MonsterId;
            [JsonProperty("group")] public int Group = 3;
            [JsonProperty("interval")] public float Interval = 20f;
            [JsonProperty("untilKilledFraction")] public float UntilKilledFraction = 0.9f;
            [JsonIgnore] public EntityDef Monster { get; internal set; }
        }

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            foreach (var b in StimulusBuffs) b.Spec = db.ResolveEffect(b.EffectId, errors, Id);
            foreach (var r in Roster)
            {
                r.Monster = db.ResolveEntity(r.MonsterId, errors, Id);
                if (r.Monster != null && !r.Monster.Has<MonsterBrainModuleDef>()) errors.Add($"{Id}: roster '{r.MonsterId}'은(는) 몬스터(MonsterBrain)가 아닙니다");
            }
            if (Roster.Count == 0) errors.Add($"{Id}: roster가 비었습니다 — 무엇을 스폰할지 없다");
            if (!string.IsNullOrEmpty(Trickle.MonsterId))
            {
                Trickle.Monster = db.ResolveEntity(Trickle.MonsterId, errors, Id);
                if (Trickle.Group <= 0 || Trickle.Interval <= 0f) errors.Add($"{Id}: trickle의 group·interval은 0보다 커야 합니다");
            }
            if (TargetNightLength <= 0f) errors.Add($"{Id}: targetNightLength는 0보다 커야 합니다");
            if (BurstsPerNight <= 0) errors.Add($"{Id}: burstsPerNight는 1 이상이어야 합니다");
            if (BasePoints < 0f || DayPoints < 0f || GatePoints < 0f) errors.Add($"{Id}: basePoints·dayPoints·gatePoints는 0 이상이어야 합니다");
            if (StimulusAmplitude < 0f || StimulusLinear < 0f) errors.Add($"{Id}: stimulusAmplitude·stimulusLinear는 0 이상이어야 합니다");
            if (StimulusExponent < 1f) errors.Add($"{Id}: stimulusExponent는 1 이상이어야 합니다(1 미만이면 초반부터 가팔라 첫 파괴에 공세가 는다)");
        }

        /// <summary>자극 강화분 h(r), r = 파괴 수 / 전체.</summary>
        public float BonusFor(int destroyedNests, int totalNests)
        {
            if (totalNests <= 0) return 0f;
            float r = System.Math.Clamp(destroyedNests / (float)totalNests, 0f, 1f);
            return StimulusAmplitude * (float)System.Math.Pow(r, StimulusExponent) + StimulusLinear * r;
        }

        /// <summary>밤 총량 배율(전체 둥지 = 1) = 살아 있는 몫 + 강화분.</summary>
        public float TotalFactor(int livingNests, int totalNests)
            => totalNests <= 0 || livingNests <= 0 ? 0f : (float)livingNests / totalNests + BonusFor(totalNests - livingNests, totalNests);

        /// <summary>점수식. 둥지가 하나도 없으면 0 — 스폰원이 없다.</summary>
        public float ScoreFor(int day, int gate, int livingNests, int totalNests)
            => (BasePoints + day * DayPoints + gate * GatePoints) * TotalFactor(livingNests, totalNests);

        /// <summary>자극(버프 축) = 남은 둥지 하나의 강도 = 총량 ÷ 살아 있는 몫. 파괴 0이면 1. 버프는 (자극 − 1)에 비례.</summary>
        public float StimuliFor(int destroyedNests, int totalNests)
        {
            int living = totalNests - destroyedNests;
            if (totalNests <= 0 || living <= 0) return 1f;
            return TotalFactor(living, totalNests) / ((float)living / totalNests);
        }
    }
}
