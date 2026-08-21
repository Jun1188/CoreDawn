using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PlayLoopTest 씬 생성 — "낮에 짓고 밤에 싸운다"는 한 판을 통째로 돌려보는 통합 플레이 씬.
///
///   Tools ▸ PlayLoopTest 씬 만들기 / 실행
///
/// 바탕은 TestCombat1.0이다. 세 씬을 비교해 보면 계보가 이렇게 갈린다:
///   MainScene       = 입력 + 공장(FactorySystem/PlacementSystem) + 플레이어/인벤/UI (+ EnemyPathfinding 샌드박스)
///   TestCombat1.0   = MainScene 계열 + CombatSystem(FlowField·BattleManager·Core·타워4·TimeManager)
///   ResourceNodeTest= MainScene 복사본 + 광맥 + TimeManager + 자동 테스트 하네스
/// 즉 전투가 이미 붙어 있는 TestCombat1.0에 광맥만 얹으면 셋이 합쳐진다. 자동 테스트 하네스는 넣지 않는다
/// — 여기서는 플레이어가 직접 짓고 직접 싸운다.
///
/// 얹는 것:
///   · 광맥 2개 (지형지물 — 장애물 콜라이더 O, HP·피격 X)
///   · 채굴기는 상자가 아니라 B키 빌드 메뉴로 짓는다 (광맥은 "채굴기만 지을 수 있는 자리")
///   · ResourceNodeStatusLog (채굴 진행을 콘솔로만 보고)
///   · Day HUD 아이콘 비율 보정 (원본 스프라이트가 64x64·29x26으로 제각각이라 늘어나 깨져 보였다)
/// </summary>
public static class PlayLoopTestSetup
{
    const string SourceScene = "Assets/Scenes/Test/TestCombat/TestCombat1.0.unity";
    const string TargetScene = "Assets/Scenes/Test/PlayLoopTest.unity";
    const string OrePath     = "Assets/Data/Item/IronOre.asset";
    const string VolumeProfile = "Assets/Scenes/Test/MainScene/Global Volume Profile.asset";

    // 광맥 자리 — 코어/타워와 겹치지 않게 원점에서 떨어뜨린다
    static readonly Vector2Int NodeACell = new(10, 10);
    static readonly Vector2Int NodeBCell = new(14, 10);

    [MenuItem("Tools/PlayLoopTest 씬 만들기")]
    public static void BuildSceneMenu()
    {
        BuildScene();
        Debug.Log($"[PlayLoopTest] 씬 생성 완료: {TargetScene}\n" +
                  "플레이 → B키 빌드 메뉴로 광맥 위에 채굴기 배치 → 밤이 오면 전투.");
    }

    [MenuItem("Tools/PlayLoopTest 씬 실행")]
    public static void BuildAndPlay()
    {
        BuildScene();
        EditorApplication.EnterPlaymode();
    }

    public static void BuildScene()
    {
        if (!File.Exists(SourceScene))
            throw new FileNotFoundException($"원본 씬을 찾을 수 없습니다: {SourceScene}");

        // 원본을 열어 대상 경로로 저장 — 삭제 후 복사하면 .meta가 새로 발급되어 씬 GUID가 바뀐다
        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(scene, TargetScene))
            throw new IOException($"씬 복사 실패: {SourceScene} → {TargetScene}");

        var ore = AssetDatabase.LoadAssetAtPath<ItemDataSO>(OrePath);
        if (ore == null) throw new FileNotFoundException($"광석 아이템을 찾을 수 없습니다: {OrePath}");

        // 광맥은 배치 좌표계(PlacementSystem)를 그대로 따른다
        var placement = Object.FindFirstObjectByType<PlacementSystem>();
        var grid = placement != null
            ? new GridSystem(placement.CellSize, placement.GridOrigin)
            : new GridSystem(1f, Vector3.zero);

        ResourceNodeAuthoring.Create("ResourceNode_IronOre_A", ore, NodeACell, Vector2Int.one,
                                     interval: 0.5f, amount: 1, max: 5, grid: grid);
        ResourceNodeAuthoring.Create("ResourceNode_IronOre_B", ore, NodeBCell, new Vector2Int(2, 2),
                                     interval: 2f, amount: 1, max: 10, grid: grid);

        new GameObject("ResourceNodeStatusLog").AddComponent<ResourceNodeStatusLog>();

        EnsureGlobalVolume();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, TargetScene);
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// 포스트프로세싱 — MainScene에는 Global Volume이 있는데 TestCombat1.0에는 없다.
    /// 총기·전투 연출이 MainScene과 같은 그림으로 보이도록 같은 프로필을 얹는다.
    /// </summary>
    static void EnsureGlobalVolume()
    {
        if (Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include) != null) return;

        var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(VolumeProfile);
        if (profile == null) { Debug.LogWarning($"[PlayLoopTest] 볼륨 프로필을 찾지 못했습니다: {VolumeProfile}"); return; }

        var volume = new GameObject("Global Volume").AddComponent<UnityEngine.Rendering.Volume>();
        volume.isGlobal      = true;
        volume.sharedProfile = profile;
    }

}
