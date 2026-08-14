/// <summary>
/// 화면 열기의 단일 창구 — "어느 UI 체계가 정본인가"라는 정책을 이 파일 하나에만 둔다.
///
/// 정책: UITK 패널(SCR-04·08·코어)이 정본이고, uGUI 화면(InventoryManager)은
/// 아직 uGUI Canvas를 든 씬들을 위한 잔존 폴백이다. 게임플레이 코드(플레이어·상자·
/// 보관소·코어)는 InventoryManager를 직접 알면 안 된다 — 그러면 uGUI 폐기 때
/// 호출처를 전부 뒤져야 한다. 여기만 거치면 폐기 = 이 파일의 폴백 줄 삭제다.
///
/// (CoreDataSO가 먼저 쓰던 "UITK 먼저, uGUI 폴백 — 이관 중 공존" 패턴의 일반화)
/// </summary>
public static class GameScreens
{
    /// <summary>플레이어 인벤토리 화면 (I키).</summary>
    public static void OpenInventory()
    {
        if (InventoryPanelView.TryOpen()) return;           // UITK(SCR-04) 정본
        InventoryManager.Instance?.OpenPlayerScreen();      // uGUI 잔존 씬 폴백 — uGUI 폐기 때 삭제
    }

    /// <summary>컨테이너 화면 — 상자·보관소 등 아이템 그릇 공용.</summary>
    public static void OpenContainer(ItemContainer container)
    {
        if (container == null) return;
        if (StoragePanelView.TryOpen(container)) return;    // UITK(SCR-08) 정본
        InventoryManager.Instance?.OpenContainerScreen(container);
    }

    /// <summary>코어 수리/납품 화면.</summary>
    public static void OpenCore(CoreBehavior core)
    {
        if (core == null) return;
        if (CorePanelView.TryOpen(core)) return;            // UITK 정본
        InventoryManager.Instance?.OpenCoreScreen(core);
    }
}
