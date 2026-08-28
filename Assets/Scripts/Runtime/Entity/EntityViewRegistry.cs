using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;
using SimEntity = CoreDawn.Sim.Entity;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 심 엔티티 → 씬 뷰. id↔뷰 매핑은 여기 한 곳뿐이다(리팩토링 불변식 ③).
    ///
    /// 심은 뷰를 모르므로, 심 질의(EntityWorld.QueryRadius 등)가 돌려준 엔티티를 화면·효과 시스템이 다뤄야 할 때
    /// 이 등록부로 뷰를 찾는다. 등록·해제는 EntityView.AttachEntity/DetachEntity가 한다 — 뷰가 사라지면 항목도 사라진다.
    /// 5단계에서 WorldRunner가 소유하는 인스턴스로 옮긴다(지금은 SimHost.World와 같은 과도기 정적).
    /// </summary>
    public static class EntityViewRegistry
    {
        static readonly Dictionary<EntityKey, EntityView> views = new Dictionary<EntityKey, EntityView>();

        // 도메인 리로드를 끈 환경에서 static이 플레이를 넘어 살아남는 것 방지
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => views.Clear();

        internal static void Register(EntityView view, SimEntity entity)
        {
            if (view == null || entity == null) return;
            views[entity.Id] = view;
        }

        internal static void Unregister(EntityView view, SimEntity entity)
        {
            if (entity == null) return;
            if (views.TryGetValue(entity.Id, out var current) && ReferenceEquals(current, view)) views.Remove(entity.Id);
        }

        /// <summary>엔티티의 뷰. 없으면(헤드리스·아직 안 붙음·파괴됨) null.</summary>
        public static EntityView ViewOf(SimEntity entity)
            => entity != null && views.TryGetValue(entity.Id, out var v) && v != null ? v : null;

        public static EntityView ViewOf(EntityKey id)
            => views.TryGetValue(id, out var v) && v != null ? v : null;

        /// <summary>특정 뷰 타입으로. 타입이 다르면 null.</summary>
        public static T ViewOf<T>(SimEntity entity) where T : EntityView => ViewOf(entity) as T;
    }
}
