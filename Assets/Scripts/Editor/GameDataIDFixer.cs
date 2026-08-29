#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Data;

namespace CoreDawn.EditorTools
{
    public class GameDataIDFixer
    {
        [MenuItem("Tools/Fix Missing GameDataSO IDs")]
        public static void FixMissingIds()
        {
            string[] guids = AssetDatabase.FindAssets("t:GameDataSO");
            int fixedCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameDataSO data = AssetDatabase.LoadAssetAtPath<GameDataSO>(path);

                if (data != null && string.IsNullOrEmpty(data.Id))
                {
                    // SerializedObject를 이용해 private 필드인 id 변경
                    SerializedObject serializedObj = new SerializedObject(data);
                    SerializedProperty idProperty = serializedObj.FindProperty("id");

                    if (idProperty != null)
                    {
                        // 예시: 분류 기본값 Item: + 에셋 이름
                        string defaultCategory = data is RecipeDataSO ? "Recipe" : "Item";
                        idProperty.stringValue = $"{defaultCategory}:{data.name}";

                        serializedObj.ApplyModifiedProperties();
                        EditorUtility.SetDirty(data);
                        fixedCount++;
                    }
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"<color=cyan>[GameDataIDFixer] 총 {fixedCount}개 에셋의 ID를 성공적으로 채웠습니다!</color>");
        }
    }
}
#endif
