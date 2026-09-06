using UnityEngine;
using UnityEngine.UIElements;

namespace CoreDawn.UI
{
    /// <summary>
    /// 홀로그램 패널 배경 — 레퍼런스 <c>.holo</c>(잘린 모서리 clip-path · 가로 그라디언트 · 글로우 · 왼쪽 강조선).
    /// USS 에는 clip-path·box-shadow·그라디언트가 없어 Painter2D 로 그린다. 부모(버튼·패널)의 첫 자식으로 끼워 전체를 덮고,
    /// 부모는 자기 배경·테두리를 비운다(<c>.holo</c> 스타일). 호버·위험(종료) 색은 <see cref="Hot"/>·<see cref="Danger"/>로 바꾼다.
    /// </summary>
    public sealed class HoloBox : VisualElement
    {
        static readonly Color Cyan = new Color32(79, 232, 240, 255);
        static readonly Color Red = new Color32(255, 90, 90, 255);
        static readonly Color Ink = new Color32(7, 13, 26, 255);   // 화면 바탕색 — 레퍼런스 backdrop-filter blur 대신 불투명한 바탕을 깐다

        /// <summary>잘린 모서리 크기(px) — 오른쪽 위·왼쪽 아래.</summary>
        public float Cut = 12f;

        bool hot, danger;

        public bool Hot { get => hot; set { if (hot == value) return; hot = value; MarkDirtyRepaint(); } }
        public bool Danger { get => danger; set { if (danger == value) return; danger = value; MarkDirtyRepaint(); } }

        public HoloBox()
        {
            name = "holo-box";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
            generateVisualContent += Draw;
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        void Outline(Painter2D p, float w, float h)
        {
            float c = Cut;
            p.BeginPath();
            p.MoveTo(new Vector2(0, 0));
            p.LineTo(new Vector2(w - c, 0));
            p.LineTo(new Vector2(w, c));
            p.LineTo(new Vector2(w, h));
            p.LineTo(new Vector2(c, h));
            p.LineTo(new Vector2(0, h - c));
            p.ClosePath();
        }

        // 잘린 모서리 모양 ∩ (x0 ≤ x ≤ x1) — 오른쪽 위·왼쪽 아래 컷의 꺾임점이 띠 안에 들면 꼭짓점으로 넣는다
        void Strip(Painter2D p, float w, float h, float x0, float x1)
        {
            float c = Cut;
            float Top(float x) => x <= w - c ? 0f : x - (w - c);
            float Bot(float x) => x >= c ? h : h - (c - x);
            p.BeginPath();
            p.MoveTo(new Vector2(x0, Top(x0)));
            if (x0 < w - c && x1 > w - c) p.LineTo(new Vector2(w - c, 0f));
            p.LineTo(new Vector2(x1, Top(x1)));
            p.LineTo(new Vector2(x1, Bot(x1)));
            if (x0 < c && x1 > c) p.LineTo(new Vector2(c, h));
            p.LineTo(new Vector2(x0, Bot(x0)));
            p.ClosePath();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w <= 0f || h <= 0f) return;
            var p = ctx.painter2D;
            bool red = danger && hot;
            var c = red ? Red : Cyan;

            // 글로우 — 바깥 세 겹의 굵고 옅은 선(box-shadow 0 0 12px/.25, 핫 24px/.55 근사)
            p.lineJoin = LineJoin.Miter;
            Outline(p, w, h); p.strokeColor = A(c, hot ? 0.08f : 0.04f); p.lineWidth = 22f; p.Stroke();
            Outline(p, w, h); p.strokeColor = A(c, hot ? 0.18f : 0.09f); p.lineWidth = 12f; p.Stroke();
            Outline(p, w, h); p.strokeColor = A(c, hot ? 0.34f : 0.18f); p.lineWidth = 5f; p.Stroke();

            // 바탕 — 뒤(3D·스캔라인)가 비치지 않게 완전 불투명한 잉크색. 레퍼런스는 backdrop blur 로 같은 효과를 낸다.
            // α .86 으로는 부족했다: 선형 색공간이라 어두운 바탕 위 청록 줄무늬가 밝게 섞여 잔여 14% 가 그대로 보였다(2026-09-07 실측)
            Outline(p, w, h); p.fillColor = Ink; p.Fill();

            // 채움 — 가로 그라디언트 근사(왼쪽이 밝다). 띠는 잘린 모서리 모양에 맞춰 전체 높이를 채운다
            // (예전엔 위아래 Cut 만큼 비워 가운데만 밝은 띠처럼 보였다 — 사용자 지적 2026-09-07)
            float a0 = hot ? 0.40f : 0.22f, a1 = hot ? 0.12f : 0.05f;
            const int strips = 32;
            for (int i = 0; i < strips; i++)
            {
                float x0 = w * i / strips, x1 = w * (i + 1) / strips;
                p.fillColor = A(c, Mathf.Lerp(a0, a1, (i + 0.5f) / strips));
                Strip(p, w, h, x0, x1); p.Fill();
            }

            // 테두리
            Outline(p, w, h); p.strokeColor = red ? A(Red, 0.8f) : A(Cyan, 0.55f); p.lineWidth = 1f; p.Stroke();

            // 왼쪽 강조선(3px) — 아래쪽은 잘린 모서리를 따라간다
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(0, 0)); p.LineTo(new Vector2(3, 0));
            p.LineTo(new Vector2(3, h - Cut + 3)); p.LineTo(new Vector2(0, h - Cut));
            p.ClosePath(); p.Fill();
            p.strokeColor = A(c, 0.35f); p.lineWidth = 4f;
            p.BeginPath(); p.MoveTo(new Vector2(1.5f, 2f)); p.LineTo(new Vector2(1.5f, h - Cut - 2f)); p.Stroke();
        }
    }
}
