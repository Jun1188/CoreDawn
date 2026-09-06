using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreDawn.UI
{
    /// <summary>
    /// 메뉴 버튼(UI) ↔ 3D 지점(벨트 위 아이템·우주선) 사이의 점선 + 레티클 — 레퍼런스 <c>wires.js</c> 이식.
    /// 화면 전체를 덮는 요소 하나가 선 3개를 Painter2D 로 그린다. 진행률 <see cref="Wire.Draw"/>(0..1)로 그려지고 지워진다.
    /// 3D → 패널 변환은 <see cref="RuntimePanelUtils.CameraTransformWorldToPanel"/>, 버튼 앵커는 worldBound 의 오른쪽 가운데.
    /// </summary>
    public sealed class TitleWires : VisualElement
    {
        public sealed class Wire
        {
            public float Draw, Target, RetA;
            public bool HasAnchor;
            public Vector2 Anchor;
            public VisualElement AnchorEl;
            public bool HasLastEnd;
            public Vector3 LastEnd;
            public Color Color;

            // 그릴 것(로컬 좌표)
            public bool Visible;
            public float Alpha, DashOffset, RetSize;
            public Vector2 RetPos;
            public readonly List<Vector2> Points = new();
        }

        static readonly Color[] LineColors =
        {
            new Color32(0x4f, 0xe8, 0xf0, 255), new Color32(0x9f, 0xf7, 0xff, 255), new Color32(0x2f, 0xb8, 0xc4, 255),
        };

        public readonly List<Wire> List = new();

        public TitleWires(int count)
        {
            name = "wires";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
            for (int i = 0; i < count; i++) List.Add(new Wire { Color = LineColors[i % LineColors.Length] });
            generateVisualContent += OnDraw;
        }

        public void Hide(Wire w) { if (w.Visible) { w.Visible = false; MarkDirtyRepaint(); } }

        /// <summary>앵커 요소를 바꾸면 처음부터 다시 그려진다.</summary>
        public void Rebind(Wire w, VisualElement el)
        {
            if (w.AnchorEl == el) return;
            w.Draw = 0f; w.HasAnchor = false; w.AnchorEl = el;
        }

        public void Reset() { foreach (var w in List) { w.Draw = 0f; w.Target = 0f; } }

        /// <summary>
        /// <paramref name="endPt"/> 월드 좌표, <paramref name="el"/> 시작 앵커. 글리치 중(<paramref name="animating"/>)엔 마지막 정상 앵커를 유지한다.
        /// </summary>
        public void DrawWire(Wire w, int i, Vector3 endPt, VisualElement el, bool animating, Camera cam, float dt, float time)
        {
            var r = el.worldBound;
            if (!animating && r.width > 40f && r.height > 20f)
            {
                w.Anchor = this.WorldToLocal(new Vector2(r.xMax, r.center.y));
                w.HasAnchor = true; w.AnchorEl = el;
            }
            if (!w.HasAnchor || panel == null || cam == null) { Hide(w); return; }

            // 카메라 뒤·아주 먼 점은 그리지 않는다 — 점선 조각이 수천 개로 늘어 Painter2D 정점 한도(65535)를 넘긴다(2026-09-07 실측)
            var vp = cam.WorldToViewportPoint(endPt);
            if (vp.z <= 0f || Mathf.Abs(vp.x) > 4f || Mathf.Abs(vp.y) > 4f || float.IsNaN(vp.x) || float.IsNaN(vp.y)) { Hide(w); return; }
            var end = this.WorldToLocal(RuntimePanelUtils.CameraTransformWorldToPanel(panel, endPt, cam));
            float bx = w.Anchor.x, by = w.Anchor.y;
            float kx = bx + Mathf.Max(60f, (end.x - bx) * 0.45f);

            // 레티클 먼저: 소멸은 레티클이 사라진 뒤 선이 줄고, 등장은 선이 도착한 뒤 레티클
            float tr = w.Target > 0.5f ? (w.Draw > 0.97f ? 1f : 0f) : 0f;
            w.RetA += (tr - w.RetA) * Mathf.Min(1f, dt * 10f);
            bool holdLine = w.Target < 0.5f && w.RetA > 0.05f;
            if (!holdLine) w.Draw += (w.Target - w.Draw) * Mathf.Min(1f, dt * 6f);
            float t = Mathf.Clamp01(w.Draw);

            float l1 = Mathf.Abs(kx - bx), l2 = Vector2.Distance(new Vector2(kx, by), end), L = l1 + l2;
            float ex, ey;
            w.Points.Clear();
            w.Points.Add(new Vector2(bx, by));
            if (t * L <= l1)
            {
                ex = bx + Mathf.Sign(kx - bx) * t * L; ey = by;
                w.Points.Add(new Vector2(ex, ey));
            }
            else
            {
                float u = l2 > 0f ? (t * L - l1) / l2 : 1f;
                ex = kx + (end.x - kx) * u; ey = by + (end.y - by) * u;
                w.Points.Add(new Vector2(kx, by));
                w.Points.Add(new Vector2(ex, ey));
            }
            w.Visible = t >= 0.01f;
            w.Alpha = Mathf.Min(1f, t * 3f);
            w.DashOffset = Mathf.Repeat(time * 30f, 10f);
            // 레티클: 등장 시 큰 사각에서 조여들며 나타나고 천천히 호흡
            w.RetSize = (13f + Mathf.Sin(time * 1.6f + i) * 1.2f) * (1f + (1f - w.RetA) * 1.6f);
            w.RetPos = new Vector2(ex, ey);
            MarkDirtyRepaint();
        }

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        void OnDraw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineCap = LineCap.Butt;
            p.lineJoin = LineJoin.Miter;
            for (int i = 0; i < List.Count; i++)
            {
                var w = List[i];
                if (!w.Visible || w.Points.Count < 2) continue;

                // 글로우(drop-shadow 근사) — 굵고 옅은 실선
                p.strokeColor = A(w.Color, 0.22f * w.Alpha); p.lineWidth = 5f;
                p.BeginPath(); p.MoveTo(w.Points[0]);
                for (int k = 1; k < w.Points.Count; k++) p.LineTo(w.Points[k]);
                p.Stroke();

                // 점선 6/4 — Painter2D 에 점선이 없어 구간을 번갈아 그린다
                p.strokeColor = A(w.Color, w.Alpha); p.lineWidth = 1.5f;
                p.BeginPath();
                float phase = w.DashOffset;   // 0..10, 패턴 기준 위치
                for (int k = 1; k < w.Points.Count; k++)
                {
                    var a = w.Points[k - 1]; var b = w.Points[k];
                    float len = Vector2.Distance(a, b);
                    if (len <= 0f) continue;
                    var dir = (b - a) / len;
                    float s = 0f;
                    while (s < len)
                    {
                        float inPat = Mathf.Repeat(phase + s, 10f);
                        if (inPat < 6f)
                        {
                            float e = Mathf.Min(len, s + (6f - inPat));
                            p.MoveTo(a + dir * s); p.LineTo(a + dir * e);
                            s = e;
                        }
                        else s += 10f - inPat;
                    }
                    phase = Mathf.Repeat(phase + len, 10f);
                }
                p.Stroke();

                // 레티클: 네 모서리 브래킷 + 중심 십자
                if (w.RetA <= 0.01f) continue;
                float sz = w.RetSize, ar = sz * 0.42f;
                var c = w.RetPos;
                p.strokeColor = A(w.Color, w.RetA); p.lineWidth = 1.5f; p.lineCap = LineCap.Butt;
                p.BeginPath();
                p.MoveTo(c + new Vector2(-sz, -sz + ar)); p.LineTo(c + new Vector2(-sz, -sz)); p.LineTo(c + new Vector2(-sz + ar, -sz));
                p.MoveTo(c + new Vector2(sz - ar, -sz)); p.LineTo(c + new Vector2(sz, -sz)); p.LineTo(c + new Vector2(sz, -sz + ar));
                p.MoveTo(c + new Vector2(sz, sz - ar)); p.LineTo(c + new Vector2(sz, sz)); p.LineTo(c + new Vector2(sz - ar, sz));
                p.MoveTo(c + new Vector2(-sz + ar, sz)); p.LineTo(c + new Vector2(-sz, sz)); p.LineTo(c + new Vector2(-sz, sz - ar));
                p.Stroke();
                p.strokeColor = A(w.Color, 0.8f * w.RetA); p.lineWidth = 1f; p.lineCap = LineCap.Butt;
                p.BeginPath();
                p.MoveTo(c + new Vector2(-3, 0)); p.LineTo(c + new Vector2(3, 0));
                p.MoveTo(c + new Vector2(0, -3)); p.LineTo(c + new Vector2(0, 3));
                p.Stroke();
            }
        }
    }
}
