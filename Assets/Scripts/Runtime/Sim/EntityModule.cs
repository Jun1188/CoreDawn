namespace CoreDawn.Sim
{
    /// <summary>
    /// 엔티티에 붙는 상태 덩어리 — 체력·건물·이동·두뇌가 전부 이 모양이다.
    ///
    /// 상속 대신 조합인 이유: 둥지는 건물이면서 스포너이고, 코어는 건물이면서 목표이며, 나무는 건물이면서 자원이다.
    /// "건물이면서 X"가 이미 셋이라 상속으로 가르면 곧 다이아몬드가 된다. 모듈이면 엔티티는 가벼운 컨테이너로 남고
    /// 아키타입(몬스터·플레이어·건물 종류)은 데이터(SO)가 어떤 모듈을 붙일지로 정해진다 — 모딩이 붙는 자리.
    ///
    /// 순수 ECS와 다른 점: 모듈은 데이터만이 아니라 자기 로컬 로직(<c>Health.Damage</c>)을 가진다.
    /// 엔티티를 가로지르는 로직과 틱 순서는 시스템(FactorySystem 등)의 몫이다.
    /// </summary>
    public abstract class EntityModule
    {
        /// <summary>붙어 있는 엔티티. <see cref="Entity.Add{T}"/>가 채운다 — 스스로 바꾸지 않는다.</summary>
        public Entity Owner { get; internal set; }

        /// <summary>엔티티에 붙은 직후 1회 — Owner가 유효하다.</summary>
        protected internal virtual void OnAttach() { }

        /// <summary>엔티티가 월드에서 제거될 때 1회 — 이벤트 구독 해제 등. Owner는 아직 유효하다.</summary>
        protected internal virtual void OnDetach() { }
    }

    /// <summary>
    /// 받는 피해를 가로채는 모듈 — 체력이 깎이기 전에 값을 줄이거나(보호막) 없앤다(아군 공격 무시).
    /// <see cref="Health.Damage"/>가 소유 엔티티의 모듈을 붙은 순서대로 거친다. 0 이하를 돌려주면 그 자리에서 끝난다.
    ///
    /// 예전에는 이 규칙들이 뷰(BuildingEntity)의 ReceiveDamage/ApplyEffects override에 흩어져 있었다 —
    /// 규칙이 뷰에 있으면 헤드리스 심에서는 사라지고, 서버 권위 멀티에서는 클라이언트가 규칙을 쥐게 된다.
    /// </summary>
    public interface IDamageInterceptor
    {
        /// <param name="amount">지금까지 남은 피해량(양수).</param>
        /// <param name="source">때린 엔티티. 모르면 null — 출처 없는 피해는 "누구의 공격도 아니다".</param>
        /// <returns>이 모듈을 거친 뒤의 피해량. 0 이하면 흡수.</returns>
        float Intercept(float amount, Entity source);
    }
}
