using UnityEditor;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Data;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// BuildingDatabaseSO 커스텀 인스펙터 — 카테고리별 그룹으로 보여주고, 편집은 막는다.
    /// 목록은 스캐너가 자동 관리하므로 사람 손이 닿으면 안 된다 (재수집 버튼만 제공).
    /// </summary>
    [CustomEditor(typeof(BuildingDatabaseSO))]
    public class BuildingDatabaseSOEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var db = (BuildingDatabaseSO)target;

            EditorGUILayout.HelpBox(
                "자동 수집 목록 — 직접 편집할 수 없습니다.\n" +
                "건물 SO를 만들거나 지우면 자동 반영됩니다. (수동: Tools/Factory/Rebuild Building Database)",
                MessageType.Info);

            if (GUILayout.Button("지금 재수집"))
                BuildingDatabaseScanner.RebuildAll();

            EditorGUILayout.Space(6);

            int total = 0;
            using (new EditorGUI.DisabledScope(true))   // 전체 읽기 전용
            {
                foreach (var (category, items) in db.GroupedByCategory())
                {
                    EditorGUILayout.LabelField(
                        $"{BuildingCategoryNames.Korean(category)} ({items.Count})", EditorStyles.boldLabel);

                    using (new EditorGUI.IndentLevelScope())
                    {
                        foreach (var so in items)
                        {
                            EditorGUILayout.ObjectField(
                                string.IsNullOrEmpty(so.displayName) ? so.name : so.displayName,
                                so, typeof(BuildingDataSO), false);
                            total++;
                        }
                    }
                    EditorGUILayout.Space(4);
                }
            }

            EditorGUILayout.LabelField($"총 {total}종", EditorStyles.miniLabel);
        }
    }
}
