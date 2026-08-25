using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class UpdateMonsterNest
{
    [MenuItem("Tools/Update Monster Nest")]
    public static void Run()
    {
        Debug.Log("[UpdateMonsterNest] Starting update...");

        // 1. Create Boss Prefab if not exists
        string monsterPrefabPath = "Assets/Prefabs/Monster/Monster.prefab";
        string bossPrefabPath = "Assets/Prefabs/Monster/BossMonster.prefab";
        
        GameObject bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(bossPrefabPath);
        if (bossPrefab == null)
        {
            if (AssetDatabase.CopyAsset(monsterPrefabPath, bossPrefabPath))
            {
                bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(bossPrefabPath);
                // Make it bigger to look like a boss
                bossPrefab.transform.localScale = new Vector3(2f, 2f, 2f);
                EditorUtility.SetDirty(bossPrefab);
                PrefabUtility.SavePrefabAsset(bossPrefab);
                Debug.Log("[UpdateMonsterNest] BossMonster prefab created.");
            }
            else
            {
                Debug.LogError("[UpdateMonsterNest] Failed to copy Monster.prefab to BossMonster.prefab");
                return;
            }
        }

        // 2. Update Prefab MonsterNest
        string[] guids = AssetDatabase.FindAssets("t:Prefab MonsterNest");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var nest = prefab.GetComponent<MonsterNest>();
            if (nest != null)
            {
                if (nest.spawnPoints == null || nest.spawnPoints.Count == 0)
                {
                    nest.spawnPoints = new List<MonsterNest.NestSpawnPoint>();
                    nest.spawnPoints.Add(new MonsterNest.NestSpawnPoint { point = nest.transform, bossPrefab = bossPrefab });
                }
                else
                {
                    foreach (var sp in nest.spawnPoints)
                    {
                        sp.bossPrefab = bossPrefab;
                    }
                }
                EditorUtility.SetDirty(prefab);
            }
            PrefabUtility.SavePrefabAsset(prefab);
            Debug.Log("[UpdateMonsterNest] Prefab updated with BossPrefab.");
        }

        // 3. Update Scene MonsterNest
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Test/MapPrototype.unity");
        var nests = Object.FindObjectsByType<MonsterNest>(FindObjectsSortMode.None);
        foreach (var nest in nests)
        {
            if (nest.spawnPoints == null || nest.spawnPoints.Count == 0)
            {
                nest.spawnPoints = new List<MonsterNest.NestSpawnPoint>();
                // try to find old spawn points children
                foreach (Transform child in nest.transform)
                {
                    if (child.name.Contains("Spawn"))
                    {
                        nest.spawnPoints.Add(new MonsterNest.NestSpawnPoint { point = child, bossPrefab = bossPrefab });
                    }
                }
                if (nest.spawnPoints.Count == 0)
                {
                    nest.spawnPoints.Add(new MonsterNest.NestSpawnPoint { point = nest.transform, bossPrefab = bossPrefab });
                }
                EditorUtility.SetDirty(nest);
            }

            // Assign boss prefab and instantiate boss in scene
            foreach (var sp in nest.spawnPoints)
            {
                sp.bossPrefab = bossPrefab;
                if (sp.linkedBoss == null)
                {
                    if (sp.point != null)
                    {
                        GameObject bossObj = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab);
                        bossObj.transform.position = sp.point.position;
                        bossObj.transform.rotation = sp.point.rotation;
                        bossObj.name = "BossMonster_Linked";
                        
                        // Set it in the same scene, parent it to something if you want, here root is fine
                        SceneManager.MoveGameObjectToScene(bossObj, scene);
                        
                        Monster m = bossObj.GetComponent<Monster>();
                        sp.linkedBoss = m;
                    }
                }
            }
            EditorUtility.SetDirty(nest);
        }
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[UpdateMonsterNest] Scene updated successfully with Bosses instantiated.");
    }
}
