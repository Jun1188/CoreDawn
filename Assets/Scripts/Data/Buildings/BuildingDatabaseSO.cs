using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Placement;

namespace CoreDawn.Data
{
    /// <summary>
    /// 프로젝트의 모든 건물 SO 레지스트리 — 수동 연결 금지.
    /// 에디터 스캐너(Editor/BuildingDatabaseScanner)가 BuildingDataSO 에셋을 만들거나
    /// 지울 때마다 자동으로 이 목록을 갱신한다 (카테고리 → 표시명 순 — 에셋 diff를 안정시키는 저장 순서).
    ///
    /// 화면에 늘어놓는 순서는 저장 순서와 다르다 — GroupedByCategory를 볼 것.
    ///
    /// 소비자는 이 에셋 하나만 참조하면 된다:
    ///   - PlacementSystem: 배치 후보 목록 (미연결 시 Resources 폴백)
    ///   - BuildMenuPopup: 카테고리별 버튼 자동 생성
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingDatabase", menuName = "Factory/Building Database")]
    public class BuildingDatabaseSO : ScriptableObject
    {
        [Tooltip("자동 수집됨 — 직접 편집하지 말 것 (Tools/Factory/Rebuild Building Database로 재수집)")]
        public BuildingDataSO[] buildings;

        /// <summary>Resources의 기본 데이터베이스. 씬 연결 없이도 어디서든 접근 가능.</summary>
        public static BuildingDatabaseSO LoadDefault()
            => Resources.Load<BuildingDatabaseSO>("BuildingDatabase");

        /// <summary>
        /// 카테고리별 그룹 (enum 선언 순서 = 메뉴 표시 순서). 빈 카테고리는 생략.
        ///
        /// 그룹 안은 티어 → menuOrder → 표시명 순. 저장 순서(이름순)를 그대로 내보내면
        /// 해금 순서가 뒤섞여 보이는데, 무엇을 지을지 고를 때 쓰는 기준은 "언제 열리는가"다.
        ///
        /// 티어만으로는 부족하다 — 채굴기·제련로·제작기가 전부 게이트 1이라 이름순으로 갈리면
        /// 제련로·제작기·채굴기가 되어 생산 흐름과 거꾸로 읽힌다. 공정 단계는 데이터로
        /// 역산할 수 없어(제련로와 제작기 둘 다 레시피 tier가 1~3에 걸쳐 있고 채굴기는
        /// 레시피가 없다) BuildingDataSO.menuOrder에 명시한다.
        /// </summary>
        public IEnumerable<(BuildingCategory category, List<BuildingDataSO> items)> GroupedByCategory()
        {
            if (buildings == null) yield break;

            var groups = new Dictionary<BuildingCategory, List<BuildingDataSO>>();
            foreach (var b in buildings)
            {
                if (b == null) continue;
                if (!groups.TryGetValue(b.category, out var list))
                    groups[b.category] = list = new List<BuildingDataSO>();
                list.Add(b);
            }

            foreach (BuildingCategory cat in System.Enum.GetValues(typeof(BuildingCategory)))
                if (groups.TryGetValue(cat, out var list))
                {
                    list.Sort(ByTierThenOrder);
                    yield return (cat, list);
                }
        }

        /// <summary>티어 → menuOrder → 표시명. 표시명이 비면 에셋 이름 (UI들의 DisplayNameOf와 같은 규칙).</summary>
        static int ByTierThenOrder(BuildingDataSO a, BuildingDataSO b)
        {
            int t = a.requiredCoreTier.CompareTo(b.requiredCoreTier);
            if (t != 0) return t;
            int o = a.menuOrder.CompareTo(b.menuOrder);
            return o != 0 ? o : string.Compare(NameOf(a), NameOf(b), System.StringComparison.Ordinal);
        }

        static string NameOf(BuildingDataSO so) =>
            string.IsNullOrEmpty(so.displayName) ? so.name : so.displayName;
    }
}
