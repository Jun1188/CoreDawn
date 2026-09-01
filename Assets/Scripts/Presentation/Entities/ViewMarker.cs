using UnityEngine;
using CoreDawn.Save;
using CoreDawn.Sim;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 굳힌 씬의 뷰 자리 — "이 오브젝트의 모양은 런타임이 정의(view.model[variant])에서 입힌다."
    /// 씬은 배치(위치·정의·변형)만 적는다: 팩 모델(glb)·재질을 씬에 굳히면 런타임 생성 객체라 씬 파일에 메시가 통째로 박힌다.
    /// 런타임(WorldPopulator.DressWhenReady)이 팩 자원 preload 뒤 <c>BuildingAssembler.Dress</c>로 모델·콜라이더를 붙인다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ViewMarker : MonoBehaviour
    {
        [Tooltip("모양의 출처가 되는 정의의 팩 id(coredawn:entity/tree)")]
        [SerializeField] private string dataId;

        [Tooltip("view.model 배열의 변형 번호 — [0]이 기본")]
        [SerializeField] private int variant;

        public string DataId => dataId;
        public int Variant => variant;
        public EntityDef Def => string.IsNullOrEmpty(dataId) ? null : SaveRefs.Entity(dataId);

        public void Configure(EntityDef def, int variantIndex) { dataId = def?.Id; variant = variantIndex; }
    }
}
