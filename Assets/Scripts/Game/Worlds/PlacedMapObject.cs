using UnityEngine;
using CoreDawn.Save;
using CoreDawn.Sim;

namespace CoreDawn.Worlds
{
    /// <summary>
    /// 맵에서 나와 씬에 굳어 있는 배치물 하나의 표식 — <b>어느 칸의 무엇인가</b>를 들고 있다.
    ///
    /// 광맥·둥지·나무·코어는 에디터에서(맵을 임포트할 때) 씬에 미리 세워 둔다. 그래야 플레이하지
    /// 않고도 맵이 실제로 어떻게 생겼는지 보이고, 아트가 배치를 눈으로 확인하며 고칠 수 있다.
    /// 대신 런타임은 그것들을 <b>만들지 않고 잇는다</b> — 팩토리 심의 칸을 잡거나(둥지·나무),
    /// 뷰를 심에 연결하거나(나무·코어), 레지스트리에 등록한다(광맥).
    ///
    /// 좌표를 트랜스폼에서 역산하지 않고 심을 때의 칸을 적어 두는 이유: 모형은 칸 중앙에서
    /// 흔들어 놓기(지터) 때문에 위치를 되돌리면 옆 칸으로 새는 그루가 생긴다. 심은 칸이 정본이다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlacedMapObject : MonoBehaviour
    {
        [Tooltip("이 배치물이 차지하는 칸(맵 타일 좌표). 모형의 지터와 무관하게 심을 때 확정된다.")]
        [SerializeField] private Vector2Int cell;

        [Tooltip("차지하는 칸의 주인이 될 엔티티 정의의 팩 id(coredawn:entity/tree). 비어 있으면 칸을 잡지 않는다(광맥은 자체 " +
                 "레지스트리가 따로 관리한다).")]
        [SerializeField] private string dataId;
        [SerializeField] [System.Obsolete("5a-3e 이관용 — SoRefMigrator가 id로 옮긴 뒤 삭제된다")] private CoreDawn.Data.BuildingDataSO data;

        public Vector2Int Cell => cell;
        public string DataId => dataId;
        /// <summary>정의 — 팩에 없으면 null(경고는 SaveRefs가 한 번 남긴다).</summary>
        public EntityDef Def => string.IsNullOrEmpty(dataId) ? null : SaveRefs.Entity(dataId);

        /// <summary>씬에 굳히는 쪽(에디터)이 채운다.</summary>
        public void Configure(Vector2Int c, EntityDef d) { cell = c; dataId = d?.Id; }
    }
}
