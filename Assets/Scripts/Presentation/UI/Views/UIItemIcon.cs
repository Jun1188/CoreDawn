using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// 아이템 칸(ui-slot__icon 류)에 아이콘을 채우는 한 곳. 아이콘은 뷰 카탈로그(팩 view 블록)에서 온다.
    ///
    /// 아이콘 스프라이트가 있으면 그림으로, 없으면 예전 방식대로 계통색 사각형으로 칠한다 —
    /// 아이콘이 아직 없는 아이템이 생겨도 UI가 빈 칸이 되지 않는다. 요소를 재사용하는
    /// 뷰(carryIcon, yieldIcon)가 있어 두 경로 모두 반대쪽 스타일을 지워야 한다.
    /// </summary>
    public static class UIItemIcon
    {
        public static void Apply(VisualElement ve, ItemDef item)
        {
            var icon = item != null ? ViewCatalogSO.IconOf(item) : null;
            if (icon != null)
            {
                ve.style.backgroundImage = new StyleBackground(icon);
                ve.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                ve.style.backgroundColor = Color.clear;
            }
            else
            {
                ve.style.backgroundImage = StyleKeyword.Null;
                ve.style.backgroundColor = item != null ? UIFlowColors.Of(item.Line) : UIFlowColors.Muted;
            }
        }

        /// <summary>켬/끔이 있는 칸(분배기 필터 등) — 끄면 아이콘을 틴트로 죽인다.</summary>
        public static void ApplyToggle(VisualElement ve, ItemDef item, bool on)
        {
            Apply(ve, item);
            if (item != null && ViewCatalogSO.IconOf(item) != null)
                ve.style.unityBackgroundImageTintColor = on ? Color.white : UIFlowColors.Muted;
            else if (!on)
                ve.style.backgroundColor = UIFlowColors.Muted;
        }
    }
}
