using UnityEditor;
using UnityEngine;
namespace CoreDawn.Tests
{
    /// <summary>
    /// 광맥 테스트를 에디터/CLI에서 실행하는 진입점.
    ///
    ///   에디터: Tools ▸ ResourceNode 테스트 실행
    ///   CLI   : Unity.exe -batchmode -quit -nographics -projectPath &lt;프로젝트&gt; \
    ///           -executeMethod ResourceNodeTestRunner.RunFromCLI -logFile &lt;로그&gt;
    ///           (전부 통과 = 종료 코드 0, 하나라도 실패 = 1)
    ///
    /// 플레이모드가 필요 없다 — 심은 plain C#이고 광맥은 에디트 모드에서도 생성되므로.
    /// </summary>
    public static class ResourceNodeTestRunner
    {
        [MenuItem("Tools/ResourceNode 테스트 실행")]
        public static void RunFromMenu()
        {
            bool ok = ResourceNodeTests.RunAll(out string report);
            if (ok) Debug.Log(report);
            else    Debug.LogError(report);
        }

        /// <summary>CLI 전용 — 결과를 로그에 찍고 종료 코드로 통과 여부를 알린다.</summary>
        public static void RunFromCLI()
        {
            bool ok = false;
            string report;

            try
            {
                ok = ResourceNodeTests.RunAll(out report);
            }
            catch (System.Exception e)
            {
                report = "[ResourceNodeTests] 스위트 자체가 예외로 중단됨:\n" + e;
            }

            Debug.Log(report);
            Debug.Log($"[ResourceNodeTests] 결과: {(ok ? "ALL PASS" : "FAILED")}");

            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
