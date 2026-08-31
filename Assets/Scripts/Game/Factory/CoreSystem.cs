using CoreDawn.Managers;
using CoreDawn.Sim;
using CoreDawn.UI;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 코어 진행 — 티어 승급·해금 같은 횡단 규칙(게임)과 심 모듈(<see cref="CoreModule"/>) 사이의 배선.
    /// 모듈은 상태(보호막·준비)와 로컬 규칙(소각·흡수)만 갖고, 진행도의 원본(GameManager)과
    /// 확인창(UI)의 존재는 게임의 사정이라 여기서 대리자로 꽂는다.
    /// </summary>
    public static class CoreSystem
    {
        public static void Wire(BuildingModule b, CoreModule core)
        {
            b.Input.SingleStackPerType = true; // 한 아이템이 슬롯 전부를 독점 못하게 (Crafter와 동일 이유)

            core.TierIndexProvider = () => GameManager.Instance != null ? GameManager.Instance.UnlockedTier : 0;
            core.TierAdvancer = next =>
            {
                var gm = GameManager.Instance;
                if (gm == null) return false;   // 진행을 기록할 곳이 없으면 시작하지 않는다(헤드리스)
                gm.AdvanceTier(next);
                return true;
            };
            // 확인창을 띄울 수 있는 씬에서는 사람의 결정을 기다린다 (SCR-01b)
            core.AutoRepairAllowed = () => !CorePanelView.ExistsInScene();
            core.InputsFreed += b.NotifyUpstream;
            core.RefreshAcceptFilter();
        }
    }
}
