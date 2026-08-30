using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Save;
using CoreDawn.UI;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>
    /// 분배기. 입력 1개를 여러 출력 연결에 라운드로빈으로 고르게 나눈다.
    /// 막힌 출구는 건너뛴다 (Factorio 스타일) — 한쪽이 막혀도 나머지로 계속 흐름.
    /// 벨트가 아니므로 세그먼트는 분배기 앞뒤에서 끊긴다 (팀 합의: 분기/합류 = 전용 건물).
    ///
    /// 필터: 출구 방향별로 아이템 1종을 지정할 수 있다 —
    ///   지정 아이템은 그 출구로만 나가고, 필터 출구는 다른 아이템을 받지 않는다.
    ///   나머지 아이템은 무필터 출구들에 라운드로빈. 판정은 아이템당 O(1) (딕셔너리 조회).
    /// </summary>
    [CreateAssetMenu(fileName = "NewSplitter", menuName = "Factory/Buildings/Splitter")]
    public class SplitterDataSO : BuildingDataSO
    {
    }

    // ─── 행동 ──────────────────────────────────────────────────────

}
