using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    /// <summary>
    /// 안내 카드 한 장의 런타임 — 팩 정의(<see cref="TutorialStepDef"/>) + 그 조건들의 판정 인스턴스.
    /// 정의는 값뿐이고(심), 판정은 관측기(게임)를 보므로 둘을 여기서 잇는다.
    /// </summary>
    public sealed class TutorialStep
    {
        public TutorialStepDef Def { get; }
        public string Id => Def.Id;
        public IReadOnlyList<TutorialCondition> Conditions => conditions;
        readonly List<TutorialCondition> conditions = new List<TutorialCondition>();

        public TutorialStep(TutorialStepDef def)
        {
            Def = def;
            foreach (var cd in def.Conditions)
            {
                var c = TutorialConditions.Create(cd);
                if (c == null)
                {
                    Debug.LogError($"[Tutorial] '{def.Id}': 모르는 조건 type '{cd?.Type}' — 이 조건은 빠진 채로 판정합니다 (허용: {string.Join(", ", TutorialConditions.Kinds)})");
                    continue;
                }
                conditions.Add(c);
            }
        }

        /// <summary>
        /// 이 스텝을 끝냈는가 — 조건 전부가 충족해야 한다.
        /// <paramref name="baseline"/>은 조건별 기준점(<see cref="CounterOf"/>가 만든 배열). null이면 전부 0 —
        /// 아직 뜬 적 없는 스텝은 절대값으로 판정되어 앞질러 해버린 단계가 자동 완료된다.
        /// </summary>
        public bool Evaluate(TutorialObserver world, int[] baseline)
        {
            if (conditions.Count == 0) return false;   // 조건 없음 = 저작 중, 영영 안 끝난다
            for (int i = 0; i < conditions.Count; i++)
            {
                int b = baseline != null && i < baseline.Length ? baseline[i] : 0;
                if (!conditions[i].Evaluate(world, b)) return false;
            }
            return true;
        }

        /// <summary>조건별 현재 카운터 — 이 스텝이 화면에 뜨는 순간 기준점으로 굳힌다.</summary>
        public int[] CounterOf(TutorialObserver world)
        {
            if (conditions.Count == 0) return System.Array.Empty<int>();
            var result = new int[conditions.Count];
            for (int i = 0; i < conditions.Count; i++) result[i] = conditions[i].CounterOf(world);
            return result;
        }
    }
}
