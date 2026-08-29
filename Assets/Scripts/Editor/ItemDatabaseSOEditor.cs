using UnityEditor;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Data;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// ItemDatabaseSO 커스텀 인스펙터 — 타입별 그룹으로 보여주고, 편집은 막는다.
    /// 목록은 스캐너가 자동 관리 (BuildingDatabaseSOEditor와 같은 패턴).
    /// </summary>
    [CustomEditor(typeof(ItemDatabaseSO))]
    public class ItemDatabaseSOEditor : Editor
    {
        private static string TypeName(ItemType t) => t switch
        {
            ItemType.Ore        => "원광",
            ItemType.Ingot      => "소재",
            ItemType.Part       => "부품",
            ItemType.RepairPart => "수리 부품",
            ItemType.Ammo       => "탄약",
            ItemType.Weapon     => "무기",
            ItemType.Armor      => "방어구",
            ItemType.Placeable  => "설치물",
            ItemType.Salvage    => "회수물",
            _ => t.ToString(),
        };

        public override void OnInspectorGUI()
        {
            var db = (ItemDatabaseSO)target;

            EditorGUILayout.HelpBox(
                "자동 수집 목록 — 직접 편집할 수 없습니다.\n" +
                "아이템 SO를 만들거나 지우면 자동 반영됩니다. (수동: Tools/Factory/Rebuild Data Databases)",
                MessageType.Info);

            if (GUILayout.Button("지금 재수집"))
                BuildingDatabaseScanner.RebuildItems();

            EditorGUILayout.Space(6);

            int total = 0;
            using (new EditorGUI.DisabledScope(true))   // 전체 읽기 전용
            {
                foreach (var (type, items) in db.GroupedByType())
                {
                    EditorGUILayout.LabelField($"{TypeName(type)} ({items.Count})", EditorStyles.boldLabel);

                    using (new EditorGUI.IndentLevelScope())
                    {
                        foreach (var so in items)
                        {
                            EditorGUILayout.ObjectField(
                                string.IsNullOrEmpty(so.displayName) ? so.name : so.displayName,
                                so, typeof(ItemDataSO), false);
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
