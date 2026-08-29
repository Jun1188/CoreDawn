using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Interaction;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>
    /// 프로젝트의 모든 아이템 SO 레지스트리 — BuildingDatabaseSO와 같은 패턴.
    /// 에디터 스캐너(Editor/BuildingDatabaseScanner)가 ItemDataSO 에셋을 만들거나
    /// 지울 때마다 자동으로 이 목록을 갱신한다 (타입 → 표시명 순 정렬).
    ///
    /// 예정 소비자: 자원 배치(광석 목록), 레시피 UI(재료 표시), 세이브(id ↔ SO 해석).
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Factory/Item Database")]
    public class ItemDatabaseSO : ScriptableObject
    {
        [Tooltip("자동 수집됨 — 직접 편집하지 말 것 (Tools/Factory/Rebuild Data Databases로 재수집)")]
        public ItemDataSO[] items;

        [Header("공용 설정")]
        [Tooltip("모든 월드 드롭 아이템이 쓰는 공용 프리팹 (마크식 정형화 — 아이콘만 교체).\n비우면 코드 조립 폴백.")]
        public DroppedItem droppedItemPrefab;

        /// <summary>Resources의 기본 데이터베이스. 씬 연결 없이도 어디서든 접근 가능.</summary>
        public static ItemDatabaseSO LoadDefault()
            => Resources.Load<ItemDatabaseSO>("ItemDatabase");

        /// <summary>세이브/조회용 — id로 아이템 해석. 없으면 null.</summary>
        public ItemDataSO FindById(string id)
        {
            if (items == null || string.IsNullOrEmpty(id)) return null;
            foreach (var item in items)
                if (item != null && item.Id == id) return item;
            return null;
        }

        /// <summary>타입별 그룹 (enum 선언 순서). 빈 타입은 생략.</summary>
        public IEnumerable<(ItemType type, List<ItemDataSO> items)> GroupedByType()
        {
            if (items == null) yield break;

            var groups = new Dictionary<ItemType, List<ItemDataSO>>();
            foreach (var i in items)
            {
                if (i == null) continue;
                if (!groups.TryGetValue(i.type, out var list))
                    groups[i.type] = list = new List<ItemDataSO>();
                list.Add(i);
            }

            foreach (ItemType t in System.Enum.GetValues(typeof(ItemType)))
                if (groups.TryGetValue(t, out var list))
                    yield return (t, list);
        }
    }
}
