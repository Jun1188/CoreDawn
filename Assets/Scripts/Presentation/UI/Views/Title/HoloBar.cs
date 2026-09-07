using UnityEngine;
using UnityEngine.UIElements;

namespace CoreDawn.UI
{
    /// <summary>
    /// 로딩 진행 바 — 레퍼런스 <c>.load-bar</c>(양끝이 4px 기운 평행사변형 트랙 + 청록 채움 + 글로우).
    /// clip-path 가 USS 에 없어 Painter2D 로 그린다. 부모(.load-bar)가 높이를 정하고, 이 요소가 가득 채운다.
    /// </summary>
    public sealed class HoloBar : VisualElement
    {
        static readonly Color Cyan = new Color32(79, 232, 240, 255);
        const float Slant = 4f;

        float progress;
        bool indeterminate;
        float sweep;

        /// <summary>진행률을 모르는 단계(월드 생성) — 청록 조각이 좌우로 흐른다. 매 프레임 <see cref="Tick"/>을 불러야 움직인다.</summary>
        public bool Indeterminate
        {
            get => indeterminate;
            set { if (indeterminate == value) return; indeterminate = value; MarkDirtyRepaint(); }
        }

        /// <summary>미정 모드의 위상 — 시간(초)을 넘기면 알아서 왕복한다.</summary>
        public void Tick(float time)
        {
            if (!indeterminate) return;
            sweep = Mathf.PingPong(time * 0.9f, 1f);
            MarkDirtyRepaint();
        }

        /// <summary>0..1. 바뀔 때만 다시 그린다.</summary>
        public float Progress
        {
            get => progress;
            set { value = Mathf.Clamp01(value); if (Mathf.Approximately(progress, value)) return; progress = value; MarkDirtyRepaint(); }
        }

        public HoloBar()
        {
            name = "holo-bar";
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1;
            generateVisualContent += Draw;
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // 트랙: (0,0) → (w-s,0) → (w,h) → (s,h)
        static void Track(Painter2D p, float w, float h)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(0, 0)); p.LineTo(new Vector2(w - Slant, 0));
            p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(Slant, h));
            p.ClosePath();
        }

        // 채움: 트랙 ∩ (x ≤ X). 양끝 기울기 안에서는 세로 변의 위·아래가 기운 변을 따라간다
        static void Fill(Painter2D p, float w, float h, float X)
        {
            float yTop = X <= w - Slant ? 0f : (X - (w - Slant)) / Slant * h;
            float yBot = X >= Slant ? h : X / Slant * h;
            p.BeginPath();
            p.MoveTo(new Vector2(0, 0));
            p.LineTo(new Vector2(Mathf.Min(X, w - Slant), 0));
            p.LineTo(new Vector2(X, yTop));
            p.LineTo(new Vector2(X, yBot));
            if (X >= Slant) p.LineTo(new Vector2(Slant, h));
            p.ClosePath();
        }

        static float YTop(float w, float h, float x) => x <= w - Slant ? 0f : (x - (w - Slant)) / Slant * h;
        static float YBot(float h, float x) => x >= Slant ? h : x / Slant * h;

        // 조각: 트랙 ∩ (x0 ≤ x ≤ x1)
        static void Segment(Painter2D p, float w, float h, float x0, float x1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, YTop(w, h, x0)));
            if (x0 < w - Slant) p.LineTo(new Vector2(Mathf.Min(x1, w - Slant), 0f));
            p.LineTo(new Vector2(x1, YTop(w, h, x1)));
            p.LineTo(new Vector2(x1, YBot(h, x1)));
            if (x1 > Slant) p.LineTo(new Vector2(Mathf.Max(x0, Slant), h));
            p.LineTo(new Vector2(x0, YBot(h, x0)));
            p.ClosePath();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w <= 0f || h <= 0f) return;
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Miter;

            Track(p, w, h); p.fillColor = A(Cyan, 0.08f); p.Fill();
            Track(p, w, h); p.strokeColor = A(Cyan, 0.5f); p.lineWidth = 1f; p.Stroke();

            if (indeterminate)
            {
                // 폭 28% 조각이 트랙 안을 왕복
                float seg = w * 0.28f, x0 = Mathf.Lerp(0f, w - seg, sweep), x1 = x0 + seg;
                Segment(p, w, h, x0, x1); p.strokeColor = A(Cyan, 0.35f); p.lineWidth = 8f; p.Stroke();
                Segment(p, w, h, x0, x1); p.fillColor = Cyan; p.Fill();
                return;
            }
            float X = w * progress;
            if (X <= 0.5f) return;
            Fill(p, w, h, X); p.strokeColor = A(Cyan, 0.35f); p.lineWidth = 8f; p.Stroke();   // box-shadow 0 0 10px 근사
            Fill(p, w, h, X); p.fillColor = Cyan; p.Fill();
        }
    }
}
