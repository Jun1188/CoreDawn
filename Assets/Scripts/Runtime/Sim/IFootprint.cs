using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 부피가 있는 개체(건물)의 점유 사각형 — 거리를 중심점이 아니라 경계까지 재야 하는 쪽(몬스터 공격 사거리)이 쓴다.
    /// 심 핵심이 공장(Building)을 모르게 하는 접점: Building이 구현하고 두뇌는 이 인터페이스만 본다.
    /// </summary>
    public interface IFootprint
    {
        /// <summary>점유 풋프린트의 월드 사각형(XZ 평면). y는 호출자가 넘긴 값 그대로.</summary>
        void WorldRect(float y, out Vector3 min, out Vector3 max);
    }
}
