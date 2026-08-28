using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CoreDawn.DayTime;
using CoreDawn.Factory;
using CoreDawn.Placement;
using CoreDawn.ResourceNodes;

namespace CoreDawn.Tests
{
    /// <summary>
    /// ResourceNodeTest 씬 생성 + 플레이모드 통합 테스트 자동 실행.
    ///
    ///   Tools ▸ ResourceNodeTest 씬 만들기      — MainScene을 복사해 광맥/TimeManager/하네스를 심는다
    ///   Tools ▸ ResourceNodeTest 씬 실행        — 만든 뒤 바로 플레이
    ///   CLI  -executeMethod ResourceNodeSceneSetup.RunFromCLI
    ///        씬 생성 → 플레이모드 진입 → 하네스 결과 폴링 → 종료 코드(0/1)로 반환
    ///
    /// 씬은 MainScene의 완전한 복사본이라 기존 배선(FactorySystem·PlacementSystem·FactoryTest 등)을
    /// 그대로 물려받는다. 여기서 추가하는 것은 셋뿐이다: 광맥 2개, TimeManager(원본에 없음), 테스트 하네스.
    /// </summary>
    public static class ResourceNodeSceneSetup
    {
        const string SourceScene = "Assets/Scenes/Test/MainScene.unity";
        const string TargetScene = "Assets/Scenes/Test/ResourceNodeTest.unity";
        const string OrePath     = "Assets/Data/Item/IronOre.asset";

        // CLI 실행이 도메인 리로드(플레이모드 진입)를 건너 살아남게 하는 표식
        const string RunningKey  = "ResourceNodeSceneSetup.Running";
        const string DeadlineKey = "ResourceNodeSceneSetup.Deadline";

        // 광맥을 놓을 셀 — 씬의 기존 건물과 겹치지 않게 원점에서 충분히 떨어뜨린다
        static readonly Vector2Int NodeACell = new(10, 10);
        static readonly Vector2Int NodeBCell = new(14, 10);


        [MenuItem("Tools/ResourceNodeTest 씬 만들기")]
        public static void BuildSceneMenu()
        {
            BuildScene();
            Debug.Log($"[ResourceNodeTest] 씬 생성 완료: {TargetScene}\n플레이하면 낮/밤 시나리오가 자동으로 돕니다.");
        }

        [MenuItem("Tools/ResourceNodeTest 씬 실행")]
        public static void BuildAndPlay()
        {
            BuildScene();
            EditorApplication.EnterPlaymode();
        }

        /// <summary>MainScene을 복사해 테스트 씬을 만든다. 이미 있으면 덮어쓴다.</summary>
        public static void BuildScene()
        {
            if (!File.Exists(SourceScene))
                throw new FileNotFoundException($"원본 씬을 찾을 수 없습니다: {SourceScene}");

            // 원본을 열어 대상 경로로 "다른 이름으로 저장" — 삭제 후 복사하면 .meta가 새로 발급되어
            // 재생성할 때마다 씬 GUID가 바뀐다(빌드 설정·참조가 깨지는 경로). 경로의 .meta를 살려 둔다.
            var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, TargetScene))
                throw new IOException($"씬 복사 실패: {SourceScene} → {TargetScene}");

            var ore = AssetDatabase.LoadAssetAtPath<ItemDataSO>(OrePath);
            if (ore == null) throw new FileNotFoundException($"광석 아이템을 찾을 수 없습니다: {OrePath}");

            // 씬의 PlacementSystem 좌표계를 그대로 쓴다 (광맥과 배치가 어긋나지 않도록)
            var placement = Object.FindFirstObjectByType<PlacementSystem>();
            var grid = placement != null
                ? new GridSystem(placement.CellSize, placement.GridOrigin)
                : new GridSystem(1f, Vector3.zero);

            var nodeA = CreateNode("ResourceNode_IronOre_A", ore, NodeACell, Vector2Int.one,
                                   interval: 0.5f, amount: 1, max: 5, grid: grid);
            var nodeB = CreateNode("ResourceNode_IronOre_B", ore, NodeBCell, new Vector2Int(2, 2),
                                   interval: 2f, amount: 1, max: 10, grid: grid);

            EnsureTimeManager();

            // 테스트 하네스 — 광맥 참조를 직렬화로 꽂아 둔다
            var harnessGO = new GameObject("ResourceNodeSceneTest");
            var harness = harnessGO.AddComponent<ResourceNodeSceneTest>();
            var so = new SerializedObject(harness);
            so.FindProperty("nodeA").objectReferenceValue = nodeA;
            so.FindProperty("nodeB").objectReferenceValue = nodeB;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TargetScene);
            AssetDatabase.Refresh();
        }

        /// <summary>광맥 하나 — 저작은 ResourceNodeAuthoring이 담당한다 (PlayLoopTest와 공유).</summary>
        static ResourceNode CreateNode(string name, ItemDataSO ore, Vector2Int cell, Vector2Int size,
                                       float interval, int amount, int max, GridSystem grid)
            => ResourceNodeAuthoring.Create(name, ore, cell, size, interval, amount, max, grid);

        /// <summary>MainScene에는 TimeManager가 없다 — 낮/밤 케이스를 위해 추가한다.</summary>
        static void EnsureTimeManager()
        {
            if (Object.FindFirstObjectByType<TimeManager>() != null) return;
            new GameObject("TimeManager").AddComponent<TimeManager>();
        }

        // ─── CLI ───────────────────────────────────────────────────

        /// <summary>CLI 진입점 — 씬 생성 후 플레이모드에서 하네스를 돌리고 종료 코드로 결과를 알린다.</summary>
        public static void RunFromCLI()
        {
            try
            {
                BuildScene();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ResourceNodeTest] 씬 생성 실패:\n" + e);
                EditorApplication.Exit(1);
                return;
            }

            SessionState.SetBool(RunningKey, true);
            SessionState.SetFloat(DeadlineKey, (float)EditorApplication.timeSinceStartup + 180f);
            EditorApplication.EnterPlaymode();   // 도메인 리로드 → 아래 Hook이 폴링을 다시 건다
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (!SessionState.GetBool(RunningKey, false)) return;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (!SessionState.GetBool(RunningKey, false)) { EditorApplication.update -= Poll; return; }

            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(DeadlineKey, 0f))
            {
                Finish(false, "[ResourceNodeTest] 시간 초과 — 하네스가 끝나지 않았습니다.\n"
                            + ResourceNodeSceneTest.Report);
                return;
            }

            if (!ResourceNodeSceneTest.Finished) return;

            Finish(ResourceNodeSceneTest.Passed, ResourceNodeSceneTest.Report);
        }

        static void Finish(bool passed, string report)
        {
            EditorApplication.update -= Poll;
            SessionState.SetBool(RunningKey, false);

            Debug.Log(report);
            Debug.Log($"[ResourceNodeTest] 결과: {(passed ? "ALL PASS" : "FAILED")}");
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
