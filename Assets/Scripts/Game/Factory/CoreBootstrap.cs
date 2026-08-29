using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Data;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 씬에 미리 배치된 코어(디펜스 코어와 합체된 싱글턴)를 FactorySim에 연결한다.
    /// PlacementBridge.Place처럼 새 프리팹을 만들지 않고, 이 컴포넌트가 붙은 기존
    /// BuildingEntity 뷰를 그대로 심에 매핑한다(PlacementBridge.PlaceExisting).
    ///
    /// FactoryBootstrap.Instance는 Awake에서 생성되므로 Start 시점엔 항상 준비돼 있다
    /// (유니티의 Awake→Start 실행 순서 보장).
    /// </summary>
    [RequireComponent(typeof(BuildingView))]
    public class CoreBootstrap : MonoBehaviour
    {
        [Tooltip("이 코어의 건물 데이터(CoreDataSO) — 티어별 요구량을 정의한다.")]
        [SerializeField] private CoreDataSO coreData;

        [Tooltip("이 코어가 차지하는 그리드 원점 좌표.")]
        [SerializeField] private Vector2Int gridOrigin;

        [SerializeField] private int rotationSteps = 0;

        /// <summary>씬에 굳히는 쪽(에디터)이 채운다 — 인스펙터로 손대지 않아도 되게.</summary>
        public void Configure(CoreDataSO data, Vector2Int origin, int rotation = 0)
        { coreData = data; gridOrigin = origin; rotationSteps = rotation; }

        void Start()
        {
            if (coreData == null)
            {
                Debug.LogError("[CoreBootstrap] coreData(CoreDataSO)가 지정되지 않았습니다.", this);
                return;
            }

            var view = GetComponent<BuildingView>();
            PlacementBridge.PlaceExisting(coreData, gridOrigin, rotationSteps, view);
        }
    }
}
