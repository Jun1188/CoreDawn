using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.UI
{
    /// <summary>
    /// 흐름 표시에 쓰는 색과 램프 텍스처.
    ///
    /// 색값은 tokens.uss와 같아야 한다 — USS 변수를 C#에서 읽을 방법이 없어 여기 한 벌 더 있다.
    /// 늘리지 말 것. 새 색이 필요하면 클래스로 표현할 수 있는지 먼저 보고, 정말 코드에서
    /// 정해야 하는 것(런타임 생성 텍스처의 틴트 등)만 여기 둔다.
    ///
    /// 램프 텍스처가 필요한 이유: USS에 그라디언트가 없다. 흐름선의 "면에서 멀어질수록 옅어짐"을
    /// 표현할 다른 방법이 없어 1픽셀 폭 텍스처를 만들어 깔고 색은 틴트로 입힌다.
    /// </summary>
    public static class UIFlowColors
    {
        public static readonly Color In      = new(0.31f, 0.847f, 0.878f);   // --copper 들어온다
        public static readonly Color Out     = new(1f, 0.62f, 0.29f);        // --out    나간다
        public static readonly Color Blocked = new(1f, 0.365f, 0.451f);      // --danger 막힘
        public static readonly Color Muted   = new(0.36f, 0.43f, 0.55f);     // 꺼진 아이콘

        static readonly Color Iron    = new(0.910f, 0.647f, 0.294f);
        static readonly Color Copper  = new(0.310f, 0.847f, 0.878f);
        static readonly Color Crystal = new(0.706f, 0.549f, 1f);
        static readonly Color Beast   = new(1f, 0.365f, 0.451f);

        /// <summary>계통색. 미지정은 중립 회색 — 없는 계통을 억지로 칠하지 않는다.</summary>
        public static Color Of(ItemLine line) => line switch
        {
            ItemLine.Iron    => Iron,
            ItemLine.Copper  => Copper,
            ItemLine.Crystal => Crystal,
            ItemLine.Beast   => Beast,
            _                => Muted,
        };

        // 방향별 램프 — 같은 방향은 한 장을 돌려쓴다
        static readonly Dictionary<(Direction, bool), Texture2D> _ramps = new();

        const int Steps = 32;

        /// <summary>
        /// 흐름 램프. 진한 쪽이 흐름이 나오는 쪽이다.
        /// </summary>
        /// <param name="towardCenter">true면 바깥(출구 칸)에서 안쪽(본체)으로 옅어진다.</param>
        public static Texture2D Ramp(Direction dir, bool towardCenter)
        {
            var key = (dir, towardCenter);
            if (_ramps.TryGetValue(key, out var cached) && cached != null) return cached;

            bool vertical = dir == Direction.North || dir == Direction.South;

            // 진한 끝이 어디인가 — 세로선은 위/아래, 가로선은 좌/우
            bool solidAtStart = dir switch
            {
                Direction.North => towardCenter,     // 위(출구)에서 아래(본체)로
                Direction.South => !towardCenter,
                Direction.East  => !towardCenter,
                _               => towardCenter,     // West
            };

            // 정사각으로 만든다. 1픽셀 폭 램프를 깔면 background-size 기본값(auto)이
            // 원본 크기를 쓰려 해서 늘어나지 않고 한 줄만 찍혀 단색처럼 보인다.
            var tex = new Texture2D(Steps, Steps, TextureFormat.RGBA32, false)
            {
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideAndDontSave,
            };

            var px = new Color32[Steps * Steps];
            for (int y = 0; y < Steps; y++)
            {
                for (int x = 0; x < Steps; x++)
                {
                    // Texture2D의 y는 아래에서 위로 — 화면 좌표(위에서 아래)와 반대다
                    float t = vertical ? 1f - (y + 0.5f) / Steps
                                       : (x + 0.5f) / Steps;
                    float solid = solidAtStart ? 1f - t : t;
                    px[y * Steps + x] =
                        new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(solid) * 255f * 0.55f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);

            _ramps[key] = tex;
            return tex;
        }
    }
}
