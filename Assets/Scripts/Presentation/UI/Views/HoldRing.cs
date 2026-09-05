using UnityEngine;
using UnityEngine.UIElements;

namespace CoreDawn.UI
{
    /// <summary>
    /// 홀드 진행 링 (SCR-06) — 누르고 있는 동안 테두리가 차오른다.
    ///
    /// USS로는 호(arc)를 그릴 수 없어 <see cref="Painter2D"/>로 직접 그린다.
    /// 레이더(RadarScope)와 같은 이유·같은 방식이다 — 값에 따라 매번 달라지는 도형은
    /// 임포트 시점에 구워지는 SVG로 대체할 수 없다.
    ///
    /// UXML에 쓰지 않고 코드로 붙인다. 카드 내용 자체가 어차피 동적이라
    /// UxmlFactory를 등록해 봐야 얻는 게 없다.
    /// </summary>
    public class HoldRing : VisualElement
    {
        const float Size   = 69f;
        const float Radius = 28.5f;
        const float Stroke = 6f;

        static readonly Color TrackColor = new(0.133f, 0.200f, 0.314f);   // #223350
        static readonly Color FillColor  = new(1f, 0.365f, 0.451f);       // #FF5D73 = --danger

        readonly Label _label;
        float _progress;
        Color _accent = FillColor;

        /// <summary>
        /// 차오르는 호와 가운데 글자의 색. 기본값은 철거의 붉은색(--danger)이다.
        ///
        /// 철거만 쓰던 때는 상수로 충분했지만, 손 채굴이 같은 링을 쓰면서 붉은색이 거짓말이 됐다 —
        /// 색은 "무엇이 일어나는가"를 먼저 말하고, 캐는 일은 파괴가 아니다.
        /// </summary>
        public Color Accent
        {
            set
            {
                if (_accent == value) return;
                _accent = value;
                _label.style.color = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// 가운데 글자. 링이 직접 들고 있어야 중앙에 놓인다.
        ///
        /// 남은 시간(0.4s)이 아니라 "HOLD"를 적는다 — 숫자는 읽고 해석해야 하지만
        /// 무엇을 해야 하는지는 링이 차오르는 것만으로 이미 보인다.
        /// </summary>
        public string Text { set { if (_label.text != value) _label.text = value; } }

        /// <summary>0~1. 값이 바뀔 때만 다시 그린다.</summary>
        public float Progress
        {
            get => _progress;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, _progress)) return;
                _progress = v;
                MarkDirtyRepaint();
            }
        }

        public HoldRing()
        {
            AddToClassList("ui-holdring");
            style.width  = Size;
            style.height = Size;
            pickingMode  = PickingMode.Ignore;   // 월드 클릭을 가로막지 않는다
            generateVisualContent += Draw;

            // 라벨을 링의 자식으로 둔다 — 형제로 두면 .ui-holdring의 가운데 정렬이 닿지 않아
            // 절대 배치 기본값인 좌상단에 붙는다
            // 절대 배치 + inset 0 대신 그냥 가운데 정렬된 자식으로 둔다 —
            // 절대 배치는 텍스트 상자의 높이가 폰트 메트릭을 따라가서 세로 중앙이 미세하게 어긋난다
            _label = new Label("HOLD") { pickingMode = PickingMode.Ignore };
            _label.AddToClassList("ui-holdring__time");
            Add(_label);
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var center = new Vector2(Size * 0.5f, Size * 0.5f);

            p.lineWidth = Stroke;

            // 바탕 링 — 아직 채워지지 않은 만큼이 남아 보이게 항상 전체를 깐다
            p.strokeColor = TrackColor;
            p.BeginPath();
            p.Arc(center, Radius, 0f, 360f);
            p.Stroke();

            if (_progress <= 0f) return;

            // 12시에서 시작해 시계 방향 — 시계와 같은 방향이라 설명이 필요 없다
            p.strokeColor = _accent;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(center, Radius, -90f, -90f + 360f * _progress);
            p.Stroke();
        }
    }
}
