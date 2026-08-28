using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Tutorial
{
    /// <summary>
    /// 안내 카드 한 장 = 에셋 한 개. 완료 조건은 <see cref="TutorialConditionSO"/> 모듈을 조합한다.
    ///
    /// <see cref="GameDataSO.Id"/>가 세이브에 기록되는 키다 — 관례대로 "Tutorial:이름" 형식으로
    /// 직접 지정할 것. <b>세이브가 존재하는 id는 바꾸면 안 된다</b>(그 스텝이 미완료로 되살아난다).
    ///
    /// 본문을 <see cref="GameDataSO.description"/>이 아니라 별도 <see cref="body"/>에 두는 이유:
    /// description은 툴팁 등 다른 표시 경로가 이미 쓰고 있어 성격이 다르다.
    ///
    /// 조건을 enum이 아니라 모듈로 두는 이유: 조건의 파라미터와 판정이 한 클래스에 있어
    /// 인스펙터에 쓰이는 필드만 보이고, 한 스텝에 조건을 여럿 걸 수 있으며, 새 조건은 파일 하나다.
    /// 모듈은 이 에셋의 서브에셋으로 저장되므로(ItemModuleSO 패턴) 에셋 파일 수는 늘지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "TutorialStep", menuName = "Tutorial/Step")]
    public class TutorialStepSO : GameDataSO
    {
        [Header("순서")]
        [Tooltip("작을수록 먼저. 동률이면 Id 사전순으로 갈린다.")]
        public int order;

        [Header("표시")]
        [Tooltip("카드 왼쪽 위 영문 배지. 대문자 짧은 낱말 (GUIDE / BUILD / NIGHT …).")]
        public string tag = "GUIDE";

        [Tooltip("카드 본문. 한 문장 두 줄 안쪽으로. 줄바꿈은 그대로 표시된다.")]
        [TextArea(2, 5)] public string body;

        [Tooltip("본문 아래 키캡으로 그릴 문자열들. 예: W A S D / E / B. 비우면 줄 자체가 사라진다.")]
        public string[] keyHints;

        [Header("완료 조건 — 전부 충족해야 끝난다")]
        [Tooltip("인스펙터의 '조건 추가' 메뉴로 붙인다. 비어 있으면 이 안내는 영영 끝나지 않는다(저작 중 상태).")]
        [SerializeField] List<TutorialConditionSO> conditions = new();

        [Header("진행 속도")]
        [Tooltip("이 안내가 현재 안내가 된 뒤 최소 이만큼(초)은 완료 판정을 미룬다.\n" +
                 "카드가 들어오는 연출 시간은 여기에 자동으로 더해지므로, 이 값은 순수한 '읽을 시간'이다.")]
        public float minSeconds = 2.5f;

        [Tooltip("켜면 앞질러 해도 건너뛰지 않는다 — 자기 차례가 와야 비로소 완료 판정을 시작한다.\n" +
                 "숫자키·T처럼 다른 안내를 따르다 얻어걸리기 쉬운 동작, 그리고 밤처럼 반드시 읽혀야 하는 경고에 쓴다.")]
        public bool requireInOrder;

        public IReadOnlyList<TutorialConditionSO> Conditions => conditions;

        /// <summary>
        /// 이 스텝을 끝냈는가 — 조건 전부가 충족해야 한다.
        /// <paramref name="baseline"/>은 조건별 기준점(<see cref="CounterOf"/>가 만든 배열). null이면 전부 0 —
        /// 아직 뜬 적 없는 스텝은 절대값으로 판정되어 앞질러 해버린 단계가 자동 완료된다.
        /// </summary>
        public bool Evaluate(TutorialObserver world, int[] baseline)
        {
            if (conditions == null || conditions.Count == 0) return false;   // 조건 없음 = 저작 중, 영영 안 끝난다

            for (int i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i];
                if (c == null) continue;   // 깨진 참조는 없는 조건으로 — 나머지로 판정한다

                int b = baseline != null && i < baseline.Length ? baseline[i] : 0;
                if (!c.Evaluate(world, b)) return false;
            }
            return true;
        }

        /// <summary>조건별 현재 카운터 — 이 스텝이 화면에 뜨는 순간 기준점으로 굳힌다.</summary>
        public int[] CounterOf(TutorialObserver world)
        {
            if (conditions == null || conditions.Count == 0) return System.Array.Empty<int>();

            var result = new int[conditions.Count];
            for (int i = 0; i < conditions.Count; i++)
                result[i] = conditions[i] != null ? conditions[i].CounterOf(world) : 0;
            return result;
        }

    #if UNITY_EDITOR
        /// <summary>커스텀 인스펙터 전용 — 서브에셋 추가·제거는 에디터가 한다. 런타임 코드는 Conditions만 쓸 것.</summary>
        public List<TutorialConditionSO> EditorConditions => conditions;
    #endif
    }
}
