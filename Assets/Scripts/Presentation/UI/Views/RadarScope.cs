using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Factory;
using CoreDawn.Managers;
using CoreDawn.Data;

namespace CoreDawn.UI
{
    /// <summary>레이더에 찍히는 기호. 형태로 구분한다 — 삼각=둥지, 원=진입 지점, 마름모=광맥.</summary>
    public enum RadarBlipKind
    {
        /// <summary>광맥 — 계통색 마름모. 낮에 어느 쪽으로 채굴 나갈지 정하는 정보.</summary>
        Vein,
        /// <summary>둥지 — 붉은 삼각. 늘 거기 있는 스폰원(장기 목표).</summary>
        Nest,
        /// <summary>파괴한 둥지 — 회색 X.</summary>
        NestDestroyed,
        /// <summary>이번 웨이브 진입 지점 — 주황 원 + 지시선(당장의 위협).</summary>
        Entry,
    }

    /// <summary>레이더 표시 하나.</summary>
    public struct RadarBlip
    {
        public RadarBlipKind Kind;
        /// <summary>방위각(도). 0 = 북, 시계 방향.</summary>
        public float Bearing;
        /// <summary>중심(코어)으로부터의 거리. 0~1로 정규화하며 1이 Ring 3 가장자리다.</summary>
        public float Distance;
        /// <summary>Vein일 때 계통색을 정한다.</summary>
        public ItemLine Line;
        /// <summary>Entry일 때 규모(기).</summary>
        public int Count;
    }

    /// <summary>
    /// SCR-01c 레이더 스코프 — 코어가 중심, 동심원이 맵의 Ring 1~3이라 미니맵 역할을 겸한다.
    ///
    /// 목록이 아니라 스코프다: 진입 방위와 규모를 표에 적지 않고 스코프 위에 지시선으로 붙인다.
    /// 개별 적도, 플레이어 자신도 표시하지 않는다 — 코어에 설치된 장비를 들여다보는 화면이므로
    /// 중심은 언제나 코어다.
    ///
    /// USS에는 SVG가 없으므로 <see cref="Painter2D"/>로 직접 그린다. 글자는 벡터로 못 그리므로
    /// 자식 Label을 절대 배치로 얹는다.
    ///
    /// <see cref="Locked"/>가 true면 격자는 남기고 데이터만 비운다 (SCR-01d) —
    /// 잠긴 기능을 비활성 버튼 하나로 처리하지 않고 무엇이 채워질 자리인지 보여준다.
    /// </summary>
    public class RadarScope : VisualElement
    {
        // 팔레트 — tokens.uss와 같은 값. USS 변수는 Painter2D에서 못 읽으므로 여기 둔다.
        static readonly Color ColLine = new(0.133f, 0.200f, 0.314f);        // --line   #223350
        static readonly Color ColLineStrong = new(0.180f, 0.259f, 0.400f);  // --line-strong #2E4266
        static readonly Color ColScopeBg = new(0.039f, 0.067f, 0.125f);     // #0A1120
        static readonly Color ColOk = new(0.365f, 0.827f, 0.620f);          // --ok
        static readonly Color ColBeast = new(1f, 0.365f, 0.451f);           // --beast
        static readonly Color ColWarn = new(0.910f, 0.647f, 0.294f);        // --warn/--iron
        static readonly Color ColFaint = new(0.361f, 0.431f, 0.549f);       // --text-faint

        // 해금 전 전용 — 격자를 한 단계 더 죽이고 코어도 흐린 초록으로 (SCR-01d)
        static readonly Color ColLockedGrid = new(0.102f, 0.153f, 0.251f);  // #1A2740
        static readonly Color ColLockedText = new(0.231f, 0.294f, 0.400f);  // #3B4B66
        static readonly Color ColLockedCore = new(0.247f, 0.420f, 0.345f);  // #3F6B58
        static readonly Color ColNoise = new(0.180f, 0.259f, 0.400f, 0.55f);// #2E4266 @55%

        /// <summary>설계 SVG의 기준 반지름(viewBox 440x300 안의 r=112). 길이 비율을 여기 맞춘다.</summary>
        const float DesignRadius = 112f;

        /// <summary>해금 전 스코프에 흩뿌리는 잡음 — 설계 목업과 같은 자리(중심 기준 비율, 반지름 px).</summary>
        static readonly (float x, float y, float r)[] NoiseDots =
        {
            (-0.393f, -0.286f, 1.6f), (0.375f, 0.411f, 1.2f), (-0.143f, 0.554f, 1.4f),
            (0.589f, -0.161f, 1.2f), (-0.625f, 0.232f, 1.3f), (0.196f, -0.554f, 1.5f),
        };

        const float SweepArcDeg = 42f;
        const float SweepPeriodSec = 4f;

        readonly List<RadarBlip> blips = new();
        readonly List<Label> textPool = new();

        float sweepDeg;
        IVisualElementScheduledItem sweepTask;
        bool locked;

        /// <summary>해금 전 상태. 격자만 남고 표시는 전부 감춰진다.</summary>
        public bool Locked
        {
            get => locked;
            set
            {
                if (locked == value) return;
                locked = value;
                Relayout();
                MarkDirtyRepaint();
            }
        }

        public RadarScope()
        {
            style.flexGrow = 1f;
            generateVisualContent += OnGenerateVisualContent;

            RegisterCallback<GeometryChangedEvent>(_ => Relayout());
            RegisterCallback<AttachToPanelEvent>(_ => StartSweep());
            RegisterCallback<DetachFromPanelEvent>(_ => StopSweep());
        }

        public void SetBlips(IEnumerable<RadarBlip> source)
        {
            blips.Clear();
            if (source != null) blips.AddRange(source);
            Relayout();
            MarkDirtyRepaint();
        }

        // ───────────────────────── 스윕 ─────────────────────────

        void StartSweep()
        {
            StopSweep();
            // 잠긴 상태에서는 돌 것이 없다 — 꺼진 장비가 스캔하고 있으면 거짓말이 된다
            if (locked) return;

            sweepTask = schedule.Execute(() =>
            {
                sweepDeg = (sweepDeg + 360f * (1f / SweepPeriodSec) * 0.033f) % 360f;
                MarkDirtyRepaint();
            }).Every(33);
        }

        void StopSweep()
        {
            sweepTask?.Pause();
            sweepTask = null;
        }

        // ───────────────────────── 좌표 ─────────────────────────

        Vector2 Center => new(contentRect.width * 0.5f, contentRect.height * 0.5f);

        /// <summary>
        /// 바깥 링 반지름. 좌우에는 지시선 라벨 자리를 반드시 남겨야 한다(설계 SCR-01c) —
        /// 진입 지점 라벨은 점에서 약 39px 뻗어나가므로 그만큼은 확보된 채로 키운다.
        /// </summary>
        float Radius => Mathf.Max(10f, Mathf.Min(contentRect.width * 0.29f, contentRect.height * 0.44f));

        /// <summary>설계 SVG 길이를 현재 반지름으로 옮기는 배율.</summary>
        float Unit => Radius / DesignRadius;

        /// <summary>
        /// 진입 지점 지시선의 꺾임점. 점에서 바깥으로 대각선 한 번, 수평 한 번 꺾어 라벨까지 간다.
        /// 그리기와 라벨 배치가 반드시 같은 값을 써야 하므로 여기 한 곳에서만 계산한다.
        /// </summary>
        static void EntryLeader(Vector2 c, Vector2 at, float u, out Vector2 elbow, out Vector2 tail, out bool right)
        {
            right = at.x >= c.x;
            float s = right ? 1f : -1f;
            elbow = new Vector2(at.x + s * 22f * u, at.y - 18f * u);
            tail = new Vector2(elbow.x + s * 12f * u, elbow.y);
        }

        /// <summary>방위각(0=북, 시계방향) + 거리 → 화면 좌표.</summary>
        static Vector2 Polar(Vector2 c, float bearingDeg, float dist)
        {
            float r = bearingDeg * Mathf.Deg2Rad;
            return new Vector2(c.x + dist * Mathf.Sin(r), c.y - dist * Mathf.Cos(r));
        }

        // ───────────────────────── 그리기 ─────────────────────────

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var c = Center;
            float R = Radius;
            if (R < 12f) return;

            // 설계 SVG의 길이(2px 대시 등)를 현재 반지름에 비례해 옮긴다
            float u = R / DesignRadius;

            // 바탕
            p.fillColor = ColScopeBg;
            p.BeginPath();
            p.Arc(c, R, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Fill();

            p.lineWidth = 1f;

            if (locked)
            {
                // 바깥 링까지 점선 — 격자는 남기고 데이터만 비운다
                p.strokeColor = ColLine;
                DashedCircle(p, c, R, 3f * u, 5f * u);

                p.strokeColor = ColLockedGrid;
                DashedCircle(p, c, R * 0.34f, 2f * u, 5f * u);
                DashedCircle(p, c, R * 0.67f, 2f * u, 5f * u);

                p.BeginPath();
                p.MoveTo(new Vector2(c.x, c.y - R)); p.LineTo(new Vector2(c.x, c.y + R));
                p.MoveTo(new Vector2(c.x - R, c.y)); p.LineTo(new Vector2(c.x + R, c.y));
                p.Stroke();

                // 잡음 — 꺼져 있어도 화면이 죽어 보이지 않게
                p.fillColor = ColNoise;
                foreach (var (nx, ny, nr) in NoiseDots)
                {
                    var at = new Vector2(c.x + nx * R, c.y + ny * R);
                    p.BeginPath();
                    p.Arc(at, nr, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
                    p.Fill();
                }

                // 코어만 흐리게 — 레이더 없이도 아는 정보
                DrawCore(p, c, ColLockedCore);
                return;
            }

            p.strokeColor = ColLineStrong;
            p.BeginPath();
            p.Arc(c, R, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Stroke();

            // Ring 1 / Ring 2 점선 — 대시 길이는 각도가 아니라 호 길이로 잡는다.
            // 각도로 잡으면 안쪽 링의 점이 바깥 링보다 짧아져 크기가 어긋난다.
            p.strokeColor = ColLine;
            DashedCircle(p, c, R * 0.34f, 2f * u, 4f * u);
            DashedCircle(p, c, R * 0.67f, 2f * u, 4f * u);

            // 십자선
            p.BeginPath();
            p.MoveTo(new Vector2(c.x, c.y - R)); p.LineTo(new Vector2(c.x, c.y + R));
            p.MoveTo(new Vector2(c.x - R, c.y)); p.LineTo(new Vector2(c.x + R, c.y));
            p.Stroke();

            // 눈금
            p.strokeColor = ColLineStrong;
            p.BeginPath();
            for (int i = 0; i < 8; i++)
            {
                float b = i * 45f;
                var outer = Polar(c, b, R);
                var inner = Polar(c, b, R - (i % 2 == 0 ? 10f : 6f));
                p.MoveTo(outer); p.LineTo(inner);
            }
            p.Stroke();

            DrawSweep(p, c, R);
            DrawBlips(p, c, R);
            DrawCore(p, c, ColOk);
        }

        static void DrawCore(Painter2D p, Vector2 c, Color color)
        {
            p.strokeColor = color;
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.Arc(c, 6f, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Stroke();

            p.fillColor = color;
            p.BeginPath();
            p.Arc(c, 2.4f, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Fill();
        }

        /// <summary>대시/간격을 픽셀(호 길이)로 받는다 — 반지름이 달라도 점 크기가 같아진다.</summary>
        static void DashedCircle(Painter2D p, Vector2 c, float r, float dashPx, float gapPx)
        {
            if (r <= 0.5f) return;

            float circumference = 2f * Mathf.PI * r;
            float dashDeg = Mathf.Max(0.4f, dashPx / circumference * 360f);
            float gapDeg = Mathf.Max(0.4f, gapPx / circumference * 360f);

            for (float a = 0f; a < 360f; a += dashDeg + gapDeg)
            {
                p.BeginPath();
                p.Arc(c, r, new Angle(a, AngleUnit.Degree), new Angle(Mathf.Min(a + dashDeg, 360f), AngleUnit.Degree));
                p.Stroke();
            }
        }

        /// <summary>회전 스윕. Painter2D에 그라디언트가 없어 부채꼴을 겹쳐 밝기를 만든다.</summary>
        void DrawSweep(Painter2D p, Vector2 c, float R)
        {
            const int slices = 5;
            for (int i = 0; i < slices; i++)
            {
                float t = (i + 1f) / slices;                 // 선단으로 갈수록 진하게
                float from = sweepDeg - SweepArcDeg * (1f - i / (float)slices);
                float to = sweepDeg - SweepArcDeg * (1f - (i + 1f) / slices);

                p.fillColor = new Color(ColOk.r, ColOk.g, ColOk.b, 0.09f * t);
                p.BeginPath();
                p.MoveTo(c);
                // Painter2D의 0도는 +X축이고 y가 아래로 증가한다 — 방위각을 화면각으로 옮긴다
                p.Arc(c, R, new Angle(from - 90f, AngleUnit.Degree), new Angle(to - 90f, AngleUnit.Degree));
                p.ClosePath();
                p.Fill();
            }

            p.strokeColor = new Color(ColOk.r, ColOk.g, ColOk.b, 0.75f);
            p.lineWidth = 1.2f;
            p.BeginPath();
            p.MoveTo(c);
            p.LineTo(Polar(c, sweepDeg, R));
            p.Stroke();
        }

        void DrawBlips(Painter2D p, Vector2 c, float R)
        {
            foreach (var b in blips)
            {
                var at = Polar(c, b.Bearing, Mathf.Clamp01(b.Distance) * R);

                switch (b.Kind)
                {
                    case RadarBlipKind.Vein:
                        p.fillColor = VeinColor(b.Line);
                        Diamond(p, at, 5f);
                        break;

                    case RadarBlipKind.Nest:
                        p.fillColor = new Color(ColBeast.r, ColBeast.g, ColBeast.b, 0.85f);
                        Triangle(p, at, 7f);
                        break;

                    case RadarBlipKind.NestDestroyed:
                        p.strokeColor = ColFaint;
                        p.lineWidth = 1.4f;
                        p.BeginPath();
                        p.MoveTo(new Vector2(at.x - 4f, at.y - 4f)); p.LineTo(new Vector2(at.x + 4f, at.y + 4f));
                        p.MoveTo(new Vector2(at.x + 4f, at.y - 4f)); p.LineTo(new Vector2(at.x - 4f, at.y + 4f));
                        p.Stroke();
                        break;

                    case RadarBlipKind.Entry:
                        DrawEntry(p, c, at);
                        break;
                }
            }
        }

        /// <summary>
        /// 진입 지점 — 선이 두 개다 (설계 SCR-01c):
        ///   ① 코어를 향하는 점선 + 화살촉 = 몰려오는 방향
        ///   ② 바깥 라벨로 꺾여 뻗는 지시선 = 이 점이 무슨 값인지 가리키는 선
        /// 라벨 글자는 Relayout이 같은 꺾임점을 써서 얹는다.
        /// </summary>
        void DrawEntry(Painter2D p, Vector2 c, Vector2 at)
        {
            float u = Unit;
            var toward = Vector2.Lerp(at, c, 0.30f);

            // ① 진행 방향
            p.strokeColor = new Color(ColWarn.r, ColWarn.g, ColWarn.b, 0.8f);
            p.lineWidth = 1.2f;
            DashedLine(p, at, toward, 3f * u, 3f * u);

            Vector2 dir = (c - at).normalized;
            Vector2 side = new(-dir.y, dir.x);
            p.fillColor = ColWarn;
            p.BeginPath();
            p.MoveTo(toward + dir * 7f * u);
            p.LineTo(toward - dir * 2f * u + side * 5f * u);
            p.LineTo(toward - dir * 2f * u - side * 5f * u);
            p.ClosePath();
            p.Fill();

            // ② 라벨로 뻗는 지시선 — 점선보다 흐리게 둬서 진행 방향과 구분된다
            EntryLeader(c, at, u, out var elbow, out var tail, out _);
            p.strokeColor = new Color(ColWarn.r, ColWarn.g, ColWarn.b, 0.55f);
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(at);
            p.LineTo(elbow);
            p.LineTo(tail);
            p.Stroke();

            // 진입 지점 자체
            p.fillColor = ColWarn;
            p.BeginPath();
            p.Arc(at, 3.5f, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Fill();
        }

        static void DashedLine(Painter2D p, Vector2 a, Vector2 b, float dash, float gap)
        {
            float total = Vector2.Distance(a, b);
            if (total <= 0.01f) return;
            Vector2 dir = (b - a) / total;

            p.BeginPath();
            for (float t = 0f; t < total; t += dash + gap)
            {
                p.MoveTo(a + dir * t);
                p.LineTo(a + dir * Mathf.Min(t + dash, total));
            }
            p.Stroke();
        }

        static void Diamond(Painter2D p, Vector2 at, float s)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(at.x, at.y - s));
            p.LineTo(new Vector2(at.x + s * 0.72f, at.y));
            p.LineTo(new Vector2(at.x, at.y + s));
            p.LineTo(new Vector2(at.x - s * 0.72f, at.y));
            p.ClosePath();
            p.Fill();
        }

        static void Triangle(Painter2D p, Vector2 at, float s)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(at.x, at.y - s * 0.62f));
            p.LineTo(new Vector2(at.x + s * 0.5f, at.y + s * 0.38f));
            p.LineTo(new Vector2(at.x - s * 0.5f, at.y + s * 0.38f));
            p.ClosePath();
            p.Fill();
        }

        static Color VeinColor(ItemLine line) => line switch
        {
            ItemLine.Iron => new Color(0.910f, 0.647f, 0.294f),
            ItemLine.Copper => new Color(0.310f, 0.847f, 0.878f),
            ItemLine.Crystal => new Color(0.706f, 0.549f, 1f),
            ItemLine.Beast => new Color(1f, 0.365f, 0.451f),
            _ => new Color(0.361f, 0.431f, 0.549f),
        };

        // ───────────────────── 글자 (벡터로 못 그린다) ─────────────────────

        void Relayout()
        {
            int used = 0;
            var c = Center;
            float R = Radius;
            if (R < 12f) { HideFrom(0); return; }

            // 방위 — 해금 전에는 한 단계 더 죽인다
            var cardinal = locked ? ColLockedText : ColFaint;
            used = PlaceText(used, "N", Polar(c, 0f, R + 12f), TextAnchor.MiddleCenter, 11f, cardinal, true);
            used = PlaceText(used, "E", Polar(c, 90f, R + 14f), TextAnchor.MiddleCenter, 11f, cardinal, true);
            used = PlaceText(used, "S", Polar(c, 180f, R + 12f), TextAnchor.MiddleCenter, 11f, cardinal, true);
            used = PlaceText(used, "W", Polar(c, 270f, R + 14f), TextAnchor.MiddleCenter, 11f, cardinal, true);

            if (locked)
            {
                // 설계 SVG 기준: NO SIGNAL은 중심보다 위, 설명은 아래. 파손이 아니라
                // "아직 켜지지 않았음"을 뜻하므로 두 줄이면 충분하다.
                //
                // 오프셋은 설계 비율(-0.13R/+0.20R)을 그대로 쓰지 않는다 — 글자 크기는 고정인데
                // 스코프 반지름은 패널에 따라 줄어들어, 비율만 따르면 코어 표시를 덮는다.
                // 코어 링(r=6)을 반드시 비껴가도록 글자 높이 기준으로 띄운다.
                const float CoreClearance = 6f + 3f;   // 코어 링 반지름 + 여백
                float noSignalUp = Mathf.Max(R * 0.13f, CoreClearance + 12.5f * 0.5f);
                float offlineDown = Mathf.Max(R * 0.20f, CoreClearance + 10.5f * 0.5f);

                used = PlaceText(used, "NO SIGNAL", new Vector2(c.x, c.y - noSignalUp),
                    TextAnchor.MiddleCenter, 12.5f, ColFaint, false, letterSpacing: 2f);
                used = PlaceText(used, "항법 계통 오프라인", new Vector2(c.x, c.y + offlineDown),
                    TextAnchor.MiddleCenter, 10.5f, ColLockedText, false);
                HideFrom(used);
                return;
            }

            // 링 이름 — 위쪽 축을 따라 (해금 후에만 의미가 있다)
            used = PlaceText(used, "RING 1", new Vector2(c.x + 6f, c.y - R * 0.34f), TextAnchor.MiddleLeft, 8f, ColFaint, false);
            used = PlaceText(used, "RING 2", new Vector2(c.x + 6f, c.y - R * 0.67f), TextAnchor.MiddleLeft, 8f, ColFaint, false);
            used = PlaceText(used, "RING 3", new Vector2(c.x + 6f, c.y - R), TextAnchor.MiddleLeft, 8f, ColFaint, false);

            // 진입 지점 라벨 — 지시선이 끝나는 자리에 붙는다.
            // 지점이 오른쪽에 있으면 라벨도 오른쪽(좌측 정렬), 왼쪽이면 왼쪽(우측 정렬) —
            // 방위가 어디든 같은 규칙으로 그려진다 (설계 SCR-01c).
            float u = Unit;
            foreach (var b in blips)
            {
                if (b.Kind != RadarBlipKind.Entry) continue;

                var at = Polar(c, b.Bearing, Mathf.Clamp01(b.Distance) * R);
                EntryLeader(c, at, u, out _, out var tail, out bool right);

                float s = right ? 1f : -1f;
                var anchor = new Vector2(tail.x + s * 5f * u, tail.y);
                var align = right ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

                used = PlaceText(used, $"{Mathf.RoundToInt(b.Bearing):000}°",
                    new Vector2(anchor.x, anchor.y - 7f), align, 11.5f,
                    new Color(0.851f, 0.894f, 0.961f), false);
                used = PlaceText(used, $"{b.Count}기",
                    new Vector2(anchor.x, anchor.y + 6f), align, 10f, ColFaint, false);
            }

            HideFrom(used);
        }

        int PlaceText(int index, string text, Vector2 at, TextAnchor anchor, float size, Color color, bool bold,
            float letterSpacing = 0f)
        {
            while (textPool.Count <= index)
            {
                var created = new Label { pickingMode = PickingMode.Ignore };
                created.style.position = Position.Absolute;
                Add(created);
                textPool.Add(created);
            }

            var l = textPool[index];
            l.style.display = DisplayStyle.Flex;
            l.text = text;
            l.style.fontSize = size;
            l.style.color = color;
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.unityTextAlign = anchor;
            l.style.letterSpacing = letterSpacing;

            // 폭을 모르므로 넉넉히 잡고 정렬로 맞춘다
            const float w = 80f, h = 14f;
            float left = anchor switch
            {
                TextAnchor.MiddleLeft => at.x,
                TextAnchor.MiddleRight => at.x - w,
                _ => at.x - w * 0.5f,
            };
            l.style.left = left;
            l.style.top = at.y - h * 0.5f;
            l.style.width = w;
            l.style.height = h;

            return index + 1;
        }

        void HideFrom(int index)
        {
            for (int i = index; i < textPool.Count; i++)
                textPool[i].style.display = DisplayStyle.None;
        }
    }
}
