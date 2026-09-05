using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// 계통(<see cref="ItemLine"/>) → USS 수식 클래스 매핑.
    ///
    /// 색은 데이터가 아니다. 아이템 에셋은 "어느 계통인가"만 들고 있고,
    /// 그것이 어떤 색으로 보이는지는 여기와 tokens.uss 두 곳에서만 정해진다.
    /// 아트 방향이 바뀌어도 에셋 23개를 건드릴 일이 없다.
    /// </summary>
    public static class UIItemPalette
    {
        /// <summary>수식 클래스 접미사. 계통 미지정이면 null — 중립으로 두고 클래스를 붙이지 않는다.</summary>
        public static string SuffixOf(ItemLine line) => line switch
        {
            ItemLine.Iron => "iron",
            ItemLine.Copper => "copper",
            ItemLine.Crystal => "crystal",
            ItemLine.Beast => "beast",
            _ => null,
        };

        public static string SuffixOf(ItemDef item) => item != null ? SuffixOf(item.Line) : null;

        /// <summary>"ui-chip--iron" 등. 계통 미지정이면 null.</summary>
        public static string ChipClass(ItemDef item) => Prefix("ui-chip--", SuffixOf(item));

        /// <summary>"ui-bar--iron" 등. 계통 미지정이면 null (기본 채움색 유지).</summary>
        public static string BarClass(ItemDef item) => Prefix("ui-bar--", SuffixOf(item));

        /// <summary>"ui-mat--iron" 등. 계통 미지정이면 null.</summary>
        public static string MatClass(ItemDef item) => Prefix("ui-mat--", SuffixOf(item));

        static string Prefix(string prefix, string suffix) => suffix == null ? null : prefix + suffix;
    }
}
