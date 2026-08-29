using System.Collections.Generic;
using CoreDawn.Factory;
using CoreDawn.Managers;
using CoreDawn.Data;

namespace CoreDawn.UI
{
    /// <summary>
    /// ⚠ 전부 더미다. 뒷단 시스템이 아직 없어서 화면만 먼저 세워 둔 것이다.
    ///
    /// 실제 시스템이 붙으면 이 파일을 통째로 지우고 CorePanelView의 호출부를
    /// 진짜 소스로 바꾸면 된다 — 가짜 데이터가 여기 한 곳에만 있으므로
    /// "어디까지가 진짜인가"를 헷갈릴 일이 없다.
    ///
    /// 대체 대상:
    ///   Wave*  → 웨이브/스폰 시스템 (미구현)
    ///   Blips  → 맵의 광맥·둥지 레지스트리 + 이번 웨이브 진입 지점
    ///            (광맥은 ResourceNodeRegistry가 이미 있으니 거기서 뽑을 수 있다)
    /// </summary>
    public static class CoreInfoDummyData
    {
        public const string WaveNextIn = "02:15";
        public const string WaveNumber = "7";
        public const string WaveIncoming = "24";
        public const string WaveNestsCleared = "2";
        public const string WaveNestsTotal = "6";

        /// <summary>레퍼런스 문서 SCR-01c 목업과 같은 배치.</summary>
        public static IEnumerable<RadarBlip> Blips()
        {
            // 광맥 — 계통색 마름모
            yield return new RadarBlip { Kind = RadarBlipKind.Vein, Bearing = 318f, Distance = 0.31f, Line = ItemLine.Iron };
            yield return new RadarBlip { Kind = RadarBlipKind.Vein, Bearing = 122f, Distance = 0.34f, Line = ItemLine.Iron };
            yield return new RadarBlip { Kind = RadarBlipKind.Vein, Bearing = 243f, Distance = 0.55f, Line = ItemLine.Copper };
            yield return new RadarBlip { Kind = RadarBlipKind.Vein, Bearing = 133f, Distance = 0.74f, Line = ItemLine.Copper };
            yield return new RadarBlip { Kind = RadarBlipKind.Vein, Bearing = 345f, Distance = 0.70f, Line = ItemLine.Crystal };
            yield return new RadarBlip { Kind = RadarBlipKind.Vein, Bearing = 208f, Distance = 0.83f, Line = ItemLine.Crystal };

            // 둥지 — 붉은 삼각(장기 목표)
            yield return new RadarBlip { Kind = RadarBlipKind.Nest, Bearing = 214f, Distance = 0.65f };
            yield return new RadarBlip { Kind = RadarBlipKind.Nest, Bearing = 56f, Distance = 0.55f };
            yield return new RadarBlip { Kind = RadarBlipKind.Nest, Bearing = 147f, Distance = 0.75f };

            // 파괴한 둥지 — 회색 X
            yield return new RadarBlip { Kind = RadarBlipKind.NestDestroyed, Bearing = 318f, Distance = 0.62f };
            yield return new RadarBlip { Kind = RadarBlipKind.NestDestroyed, Bearing = 260f, Distance = 0.50f };

            // 이번 웨이브 진입 지점 — 주황 원 + 지시선(당장의 위협).
            // 맵 밖에서 들어오는 것이므로 바깥 링 위(Distance = 1)에 찍힌다.
            yield return new RadarBlip { Kind = RadarBlipKind.Entry, Bearing = 68f, Distance = 1f, Count = 14 };
            yield return new RadarBlip { Kind = RadarBlipKind.Entry, Bearing = 295f, Distance = 1f, Count = 10 };
        }
    }
}
