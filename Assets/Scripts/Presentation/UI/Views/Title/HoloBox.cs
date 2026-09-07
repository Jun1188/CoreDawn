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

        // 레퍼런스가 브라우저(sRGB)에서 "잉크 위에 색 α" 로 섞은 결과를 그대로 목표색으로 — 이 프로젝트는 Linear 라
        // 같은 알파를 여기서 얹으면 훨씬 진한 청록이 된다(2026-09-07 사용자 "색감이 많이 다르다"). UITK 색은 sRGB 값이라 불투명으로 칠하면 정확히 나온다.
        static Color Over(Color c, float a) => new Color(Ink.r + (c.r - Ink.r) * a, Ink.g + (c.g - Ink.g) * a, Ink.b + (c.b - Ink.b) * a, 1f);

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

        // 잘린 모서리 모양을 정점 6개·삼각형 4개로, 색은 x 에 따라 왼쪽→오른쪽 선형(삼각형 안 보간이 정확히 같은 직선이라 이음새가 없다)
        void GradientFill(MeshGenerationContext ctx, float w, float h, Color left, Color right)
        {
            float c = Cut;
            var pts = new[]
            {
                new Vector2(0, 0), new Vector2(w - c, 0), new Vector2(w, c),
                new Vector2(w, h), new Vector2(c, h), new Vector2(0, h - c),
            };
            var mesh = ctx.Allocate(pts.Length, (pts.Length - 2) * 3);
            for (int i = 0; i < pts.Length; i++)
            {
                var col = Color.Lerp(left, right, pts[i].x / w);
                mesh.SetNextVertex(new Vertex { position = new Vector3(pts[i].x, pts[i].y, Vertex.nearZ), tint = col });
            }
            for (int i = 1; i + 1 < pts.Length; i++)
            {
                mesh.SetNextIndex(0); mesh.SetNextIndex((ushort)i); mesh.SetNextIndex((ushort)(i + 1));
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w <= 0f || h <= 0f) return;
            var p = ctx.painter2D;
            bool red = danger && hot;
            var c = red ? Red : Cyan;

            // 글로우 — 바깥 세 겹의 굵고 옅은 선(box-shadow 0 0 12px/.25, 핫 24px/.55 근사). 알파는 Linear 에서 세게 보여 레퍼런스의 절반쯤
            p.lineJoin = LineJoin.Miter;
            Outline(p, w, h); p.strokeColor = A(c, hot ? 0.05f : 0.02f); p.lineWidth = 22f; p.Stroke();
            Outline(p, w, h); p.strokeColor = A(c, hot ? 0.11f : 0.05f); p.lineWidth = 12f; p.Stroke();
            Outline(p, w, h); p.strokeColor = A(c, hot ? 0.22f : 0.10f); p.lineWidth = 5f; p.Stroke();

            // 채움 — 레퍼런스 그라디언트 rgba(색 .16 → .04), 핫 .34 → .1 을 sRGB 로 잉크 위에 섞은 불투명 색. 왼쪽이 밝다.
            // 정점 색을 보간하는 메시 하나로 — 세로 띠 여러 장으로 근사하면 폴리곤 경계마다 이음새가 줄무늬로 보인다(2026-09-07 사용자 지적).
            // 불투명이라 뒤(3D·스캔라인)가 비치지 않는다 — 레퍼런스는 backdrop blur 로 같은 효과를 낸다
            GradientFill(ctx, w, h, Over(c, hot ? 0.34f : 0.16f), Over(c, hot ? 0.10f : 0.04f));

            // 테두리 — 1px, rgba(색 .55 / 위험 .8) 를 sRGB 로 섞은 불투명 색
            Outline(p, w, h); p.strokeColor = red ? Over(Red, 0.8f) : Over(Cyan, 0.55f); p.lineWidth = 1f; p.Stroke();

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
