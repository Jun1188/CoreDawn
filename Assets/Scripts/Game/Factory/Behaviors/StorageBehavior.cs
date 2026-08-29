using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.UI;
using CoreDawn.Factory;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 큰 버퍼를 가진 저장소.
    ///
    /// **보관은 입력 버퍼에서 한다.** 플레이어가 E로 여는 보관함이 그 버퍼이고,
    /// 벨트가 넣어 주는 곳도 같은 곳이다 — 넣은 물건과 받은 물건이 한자리에 모인다.
    ///
    /// 출력 버퍼는 쓰지 않는다. 예전에는 받은 즉시 출력 버퍼로 옮겼는데, 하류가 막히면
    /// 그 물건들이 **플레이어가 손댈 수 없는 곳에** 쌓여 있었다. 배출은 입력 버퍼에서
    /// 하류로 곧장 밀고, 못 미는 만큼은 보관함에 그대로 남는다.
    ///
    /// 하류가 받는 만큼은 계속 흘려보낸다 — 저장소는 라인을 막는 마개가 아니라 완충 장치다.
    /// </summary>
    public class StorageBehavior : IBuildingBehavior, IInteractiveBehavior
    {
        readonly BuildingModule _b;

        public StorageBehavior(BuildingModule b)
        {
            _b = b;

            // 플레이어가 보관함에 직접 넣는 경로는 벨트를 거치지 않아 아무도 깨워 주지 않는다.
            // 비어 있던 저장소는 stall 상태로 자고 있으므로, 그대로 두면 넣은 물건이
            // 하류로 나가지 않고 그 자리에 머문다 — 다음 벨트 입고 때까지.
            // (코어가 같은 이유로 이미 쓰고 있는 방식)
            _b.Output.Changed += Wake;
            _b.Input.Changed  += Wake;
        }

        void Wake() => _b.Factory.MarkDirty(_b);

        public void OnAfterPlaced() { }

        public string InteractPrompt => "보관함 열기";

        public void Interact(PlayerController player)
        {
            // 보관함 = 입력 버퍼. 벨트가 넣는 곳과 같아서 화면에 보이는 것이 곧 저장소의 전부다.
            GameScreens.OpenContainer(_b.Input);
        }

        public void Tick(float dt)
        {
            // 보관함에서 하류로 곧장 민다. 출력 버퍼를 거치지 않는 이유는,
            // 옮겨 놓고 나서 밀기에 실패하면 그 물건이 플레이어가 못 여는 버퍼에 갇히기 때문이다.
            foreach (var (item, count) in _b.Input.Snapshot())
            {
                int moved = 0;
                while (moved < count && _b.TryPushOutput(item)) moved++;

                if (moved > 0)
                {
                    _b.Input.TryConsume(item, moved);
                    _b.NotifyUpstream();   // 자리 생김 → 막혀 있던 상류 깨움
                }
            }
            // 전부 막혔으면 보관함에 그대로 쌓인다 — 하류가 소비하면 NotifyUpstream으로 깨어난다
        }
    }
}
