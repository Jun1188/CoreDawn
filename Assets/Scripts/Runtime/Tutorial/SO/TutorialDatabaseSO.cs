using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Tutorial
{
    /// <summary>
    /// 튜토리얼 스텝 목록 — Recipe/ItemDatabaseSO와 같은 Resources 레지스트리 패턴.
    ///
    /// 여기만 씬·프리팹 바깥에 있으므로 튜토리얼은 어떤 씬에 있든 같은 목록을 본다.
    /// 스캐너가 자동 수집하는 다른 DB와 달리 <b>순서가 곧 콘텐츠</b>라 사람이 직접 채운다.
    /// </summary>
    [CreateAssetMenu(fileName = "TutorialDatabase", menuName = "Tutorial/Database")]
    public class TutorialDatabaseSO : ScriptableObject
    {
        [Tooltip("표시 순서는 이 배열이 아니라 각 스텝의 order가 정한다 — 여기 순서는 편의상일 뿐이다.")]
        public TutorialStepSO[] steps;

        public const string ResourcePath = "TutorialDatabase";

        /// <summary>Resources의 기본 데이터베이스. 없으면 null — 튜토리얼이 통째로 꺼진다.</summary>
        public static TutorialDatabaseSO LoadDefault()
            => Resources.Load<TutorialDatabaseSO>(ResourcePath);

        /// <summary>order → Id 순으로 정렬한 유효 스텝 목록. null·중복 Id는 걸러낸다.</summary>
        public List<TutorialStepSO> BuildOrdered()
        {
            var list = new List<TutorialStepSO>();
            var seen = new HashSet<string>();

            if (steps != null)
            {
                foreach (var s in steps)
                {
                    if (s == null) continue;
                    if (string.IsNullOrEmpty(s.Id))
                    {
                        Debug.LogWarning($"[Tutorial] id가 비어 있어 건너뜁니다: {s.name} — \"Tutorial:이름\" 형식으로 지정하세요", s);
                        continue;
                    }
                    if (!seen.Add(s.Id))
                    {
                        Debug.LogWarning($"[Tutorial] id가 중복되어 건너뜁니다: {s.Id} ({s.name})", s);
                        continue;
                    }
                    list.Add(s);
                }
            }

            list.Sort((a, b) => a.order != b.order
                ? a.order.CompareTo(b.order)
                : string.CompareOrdinal(a.Id, b.Id));
            return list;
        }
    }
}
