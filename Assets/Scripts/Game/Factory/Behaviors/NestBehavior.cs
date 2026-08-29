using UnityEngine;
using CoreDawn.Placement;
using CoreDawn.Worlds;
using CoreDawn.Sim;
using CoreDawn.Factory;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 아무 일도 하지 않는 행동. 둥지는 아이템을 주고받지 않으므로 심의 틱에 걸릴 일이 없다
    /// (Dirty 큐에 들어가지 않으면 Tick 자체가 호출되지 않는다).
    /// </summary>
    public class NestBehavior : IBuildingBehavior
    {
        public void Tick(float dt) { }
        public void OnAfterPlaced() { }
    }
}
