using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Sim;

namespace CoreDawn.Data
{

    /// <summary>
    /// 컨베이어 벨트. 연속된 같은 종류의 벨트는 BeltSegment로 묶여 처리된다.
    /// 직선/커브는 별도 SO가 아니라 배치 시 결정되는 모양(BeltShape) —
    /// 포트는 BuildPorts()로 계산해 Building.PortOverride에 주입한다.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBelt", menuName = "Factory/Buildings/Belt")]
    public class BeltDataSO : BuildingDataSO
    {
        [Header("운반")]
        [Tooltip("아이템 이동 속도 (타일/초).")]
        public float speedTilesPerSec = 2f;

        [Header("커브 프리팹 (기본 prefab = 직선)")]
        public GameObject curveLPrefab;
        public GameObject curveRPrefab;

        public GameObject PrefabFor(BeltShape shape) => shape switch
        {
            BeltShape.CurveL => curveLPrefab,
            BeltShape.CurveR => curveRPrefab,
            _                => prefab,
        };


        // 모양·회전 기하(InputDirFor·OutputDirFor·MeshYaw·BuildPorts)는 CoreDawn.Sim.BeltGeometry로 옮겨졌다 (5a-3b)
    }

}
