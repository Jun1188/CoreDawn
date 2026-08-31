using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Inventories;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// 화면 열기의 단일 창구 — "무엇을 여는가"만 게임플레이 코드가 말하고,
    /// "어느 UI가 그것을 그리는가"는 여기서 정한다.
    ///
    /// UITK 패널(SCR-04·08·코어)이 정본이다. 구 uGUI 화면(InventoryManager)으로 떨어지던
    /// 폴백은 제거했다 — 폴백이 있으면 UITK가 없는 구성에서도 "그럭저럭" 열려서, UI가 실제로
    /// 탑재됐는지 아무도 확인하지 않게 된다. 지금은 열지 못하면 그 자리에서 알린다.
    /// (UITK 패널은 GameUI 부트스트랩 씬이 실어 오므로, 플레이어가 있는 씬이면 항상 있다)
    /// </summary>
    public static class GameScreens
    {
        /// <summary>플레이어 인벤토리 화면 (I키).</summary>
        public static void OpenInventory()
        {
            if (InventoryPanelView.TryOpen()) return;
            Missing("인벤토리");
        }

        /// <summary>컨테이너 화면 — 상자·보관소 등 아이템 그릇 공용.</summary>
        public static void OpenContainer(ItemContainer container)
        {
            if (container == null) return;
            if (StoragePanelView.TryOpen(container)) return;
            Missing("보관 화면");
        }

        /// <summary>코어 수리/납품 화면.</summary>
        public static void OpenCore(CoreModule core)
        {
            if (core == null) return;
            if (CorePanelView.TryOpen(core)) return;
            Missing("코어 화면");
        }

        /// <summary>월드 맵 오버레이 (M키) — 지형·배치물 위에 플레이어 위치를 실시간으로 찍는다.</summary>
        public static void OpenWorldMap()
        {
            if (WorldMapPanelView.TryOpen()) return;
            Missing("맵");
        }

        /// <summary>게임오버 — 코어가 파괴됐을 때. 닫을 수 없는 창이라 여는 쪽만 있다.</summary>
        public static void OpenGameOver()
        {
            if (GameOverPanelView.TryOpen()) return;
            Missing("게임오버 화면");
        }

        static void Missing(string what) =>
            Debug.LogWarning($"[GameScreens] {what}을(를) 열지 못했습니다 — UI(GameUI 씬)가 탑재되지 않았거나 " +
                             "플레이어 인벤토리가 준비되지 않았습니다.");
    }
}
