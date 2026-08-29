using UnityEngine;
using UnityEngine.UIElements;

namespace CoreDawn.UI
{
    /// <summary>
    /// 나침반 마커 — 아래를 가리키는 삼각형. CSS의 border 삼각형 트릭이 USS에 없어
    /// Painter2D로 그린다. 색은 USS/style의 color를 따른다 (붉음 = 위협, 초록 = 코어).
    /// </summary>
    public class TriangleGlyph : VisualElement
    {
        public TriangleGlyph()
        {
            pickingMode = PickingMode.Ignore;
            style.width = 15f;
            style.height = 10.5f;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;

            var p = ctx.painter2D;
            p.fillColor = resolvedStyle.color;
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, r.yMin));
            p.LineTo(new Vector2(r.xMax, r.yMin));
            p.LineTo(new Vector2(r.center.x, r.yMax));
            p.ClosePath();
            p.Fill();
        }
    }

    /// <summary>
    /// 괴수 두상 — 적 수 앞에 붙어 "무엇의 23인지"를 라벨 없이 말한다 (문서 SCR-02).
    /// 레퍼런스 SVG 경로를 Painter2D로 옮겼다 (16×16 뷰박스 기준).
    /// </summary>
    public class MonsterGlyph : VisualElement
    {
        public MonsterGlyph()
        {
            pickingMode = PickingMode.Ignore;
            style.width = 21f;
            style.height = 21f;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;

            float s = Mathf.Min(r.width, r.height) / 16f;
            Vector2 o = r.min;
            Vector2 P(float x, float y) => o + new Vector2(x, y) * s;

            var p = ctx.painter2D;
            p.fillColor = resolvedStyle.color;

            p.BeginPath();
            p.MoveTo(P(8f, 1.6f));
            p.BezierCurveTo(P(5.4f, 1.6f), P(3.4f, 3.7f), P(3.4f, 6.3f));
            p.LineTo(P(2f, 9.2f));
            p.LineTo(P(4f, 9.2f));
            p.LineTo(P(4.6f, 11f));
            p.LineTo(P(6.4f, 11f));
            p.LineTo(P(6.4f, 13.6f));
            p.LineTo(P(9.6f, 13.6f));
            p.LineTo(P(9.6f, 11f));
            p.LineTo(P(11.4f, 11f));
            p.LineTo(P(12f, 9.2f));
            p.LineTo(P(14f, 9.2f));
            p.LineTo(P(12.6f, 6.3f));
            p.BezierCurveTo(P(12.6f, 3.7f), P(10.6f, 1.6f), P(8f, 1.6f));
            p.ClosePath();
            p.Fill();

            // 눈 — 배경색으로 뚫는다
            p.fillColor = new Color(0.043f, 0.071f, 0.125f);   // --bg-void
            foreach (float cx in new[] { 6.1f, 9.9f })
            {
                p.BeginPath();
                p.Arc(P(cx, 6.4f), 1.15f * s, 0f, 360f);
                p.Fill();
            }
        }
    }
}
