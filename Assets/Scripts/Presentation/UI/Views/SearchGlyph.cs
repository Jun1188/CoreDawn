using UnityEngine;
using UnityEngine.UIElements;

namespace CoreDawn.UI
{
    /// <summary>
    /// 검색창의 돋보기 아이콘 — 레퍼런스의 13×13 SVG(원 + 손잡이)를 Painter2D로 그린다.
    /// USS에는 SVG·의사요소가 없어 실제 요소로 만든다 (레이더·홀드 링과 같은 이유).
    /// 색은 USS의 color를 따른다 — 뷰마다 색을 다시 정하지 않는다.
    /// </summary>
    public class SearchGlyph : VisualElement
    {
        public SearchGlyph()
        {
            AddToClassList("ui-search__icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;

            // 레퍼런스 SVG는 16×16 뷰박스 — 실제 크기에 맞춰 비율만 옮긴다
            float s = Mathf.Min(r.width, r.height) / 16f;
            Vector2 o = r.min;

            var p = ctx.painter2D;
            p.strokeColor = resolvedStyle.color;
            p.lineWidth = 1.5f * s;
            p.lineCap = LineCap.Round;

            p.BeginPath();
            p.Arc(o + new Vector2(7f, 7f) * s, 4.6f * s, 0f, 360f);
            p.Stroke();

            p.BeginPath();
            p.MoveTo(o + new Vector2(10.4f, 10.4f) * s);
            p.LineTo(o + new Vector2(14f, 14f) * s);
            p.Stroke();
        }
    }
}
