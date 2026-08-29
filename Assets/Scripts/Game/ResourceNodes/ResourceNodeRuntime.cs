using System.Collections.Generic;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Pings;
using CoreDawn.Placement;
using CoreDawn.Data;

namespace CoreDawn.ResourceNodes
{
    /// <summary>광맥 생산 구동 + 철거 대기열 처리 전용 러너. 레지스트리가 직접 만든다(씬 배선 없음).</summary>
    [AddComponentMenu("")]
    internal class ResourceNodeRuntime : MonoBehaviour
    {
        void Update()     => ResourceNodeRegistry.TickProduction();
        void LateUpdate() => ResourceNodeRegistry.ProcessRejections();
    }
}
