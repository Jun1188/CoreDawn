using UnityEngine;
using CoreDawn.Worlds;

namespace CoreDawn.Navigation
{
    /// <summary>
    /// 지면 높이를 묻는 단일 창구.
    ///
    /// 이 월드의 바닥은 하이트맵 지형이다(<c>WorldTerrainGenerator</c>가 굽는다). 그런데
    /// <see cref="GridManager.SurfaceY"/>는 <b>맵 전체에 하나뿐인 스칼라</b>라 기복을 표현하지 못한다.
    /// 실측하면 지형은 -0.85m ~ +0.14m로 약 1m를 오르내리는데 SurfaceY는 0이다 —
    /// 그 차이만큼 개체가 땅에 묻히거나 뜬다. 키 1m짜리 몬스터에게 0.14m는 다리가 통째로 사라지는 양이다.
    ///
    /// 물리 레이캐스트가 아니라 <see cref="Terrain.SampleHeight"/>(하이트맵 이중선형 보간)를 쓴다.
    /// 개체 수백 마리가 매 프레임 물어봐도 부담이 없어야 하기 때문이다.
    ///
    /// 지형이 없는 씬(테스트 씬 등)에서는 예전처럼 <see cref="GridManager.SurfaceY"/>로 물러난다 —
    /// 즉 이 클래스가 들어와도 기존 씬의 동작은 그대로다.
    /// </summary>
    public static class GroundSampler
    {
        private static Terrain cached;

        // 도메인 리로드를 끈 환경에서 static이 플레이를 넘어 살아남는 것 방지
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => cached = null;

        /// <summary>
        /// 활성 지형. 씬에 지형이 하나뿐인 것을 전제한다(현재 World.unity가 그렇다).
        /// 지형을 여러 장으로 쪼개게 되면 여기서 위치로 골라야 한다.
        /// </summary>
        private static Terrain Active
        {
            get
            {
                if (cached == null) cached = Terrain.activeTerrain;
                return cached;
            }
        }

        /// <summary>주어진 월드 좌표(XZ)의 지면 높이(Y).</summary>
        public static float HeightAt(Vector3 worldPosition)
        {
            Terrain terrain = Active;
            if (terrain != null)
            {
                // SampleHeight는 지형 트랜스폼 기준 높이를 준다 — 월드로 올리려면 지형 원점을 더한다
                return terrain.SampleHeight(worldPosition) + terrain.transform.position.y;
            }

            GridManager grid = GridManager.Instance;
            return grid != null ? grid.SurfaceY : worldPosition.y;
        }
    }
}
