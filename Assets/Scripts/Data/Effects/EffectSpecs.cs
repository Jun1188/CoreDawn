using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>
    /// 에셋(EffectSO) → 심 정의(<see cref="EffectSpec"/>) 변환기 — 데이터와 심 사이의 다리.
    /// 에셋 하나에 정의 하나: 심이 "같은 효과"를 참조 동일성으로 판정하므로 이 캐시가 곧 계약이다.
    /// MonsterDataSO.ToSpec()과 같은 방식 — 심은 SO를 모르고, SO는 데이터만 갖는다.
    /// </summary>
    public static class EffectSpecs
    {
        static readonly Dictionary<EffectSO, EffectSpec> cache = new Dictionary<EffectSO, EffectSpec>();

        public static EffectSpec Of(EffectSO so)
        {
            if (so == null) return null;
            if (cache.TryGetValue(so, out var spec)) return spec;

            spec = so.BuildSpec();
            cache[so] = spec;   // affects를 잇기 전에 등록 — 버프끼리 서로 가리켜도(순환) 무한 재귀 없음

            if (so is AttackModifierEffectSO buff && buff.affects != null)
            {
                var targets = new List<EffectSpec>(buff.affects.Length);
                foreach (var target in buff.affects)
                {
                    var t = Of(target);
                    if (t != null) targets.Add(t);
                }
                spec.SetAffects(targets.ToArray());
            }
            return spec;
        }

        /// <summary>
        /// 데이터 항목(에셋 + 크기) 목록 → 심 효과 목록. 빈 에셋은 건너뛴다.
        /// 발사·공격·웨이브 결정 시점에 한 번 변환한다 — 명중마다 변환하지 않는다.
        /// </summary>
        public static Effect[] ToSim(IReadOnlyList<EffectEntry> entries)
        {
            if (entries == null || entries.Count == 0) return System.Array.Empty<Effect>();

            var result = new List<Effect>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                var spec = Of(entries[i].effect);
                if (spec != null) result.Add(new Effect(spec, entries[i].value));
            }
            return result.ToArray();
        }

        // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 플레이를 다시 들어가도 에셋 편집이 반영되게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => cache.Clear();
    }
}
