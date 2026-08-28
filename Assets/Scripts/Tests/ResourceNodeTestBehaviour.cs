using UnityEngine;
using CoreDawn.ResourceNodes;

namespace CoreDawn.Tests
{
    /// <summary>
    /// 플레이모드에서 광맥 테스트를 돌리고 싶을 때 쓰는 껍데기.
    /// 빈 GameObject에 붙이고 플레이하면 Start에서 <see cref="ResourceNodeTests"/> 스위트를 1회 실행한다.
    /// (에디터/CLI에서는 Tools ▸ ResourceNode 테스트 실행 쪽이 더 빠르다)
    /// </summary>
    public class ResourceNodeTestBehaviour : MonoBehaviour
    {
        void Start()
        {
            bool ok = ResourceNodeTests.RunAll(out string report);
            if (ok) Debug.Log(report);
            else    Debug.LogError(report);
        }
    }
}
