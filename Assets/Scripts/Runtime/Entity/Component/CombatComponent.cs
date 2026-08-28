using System;
using UnityEngine;
using CoreDawn.Combat;

namespace CoreDawn.Entities
{
    // 공격 — 순수 C# 클래스. 쿨다운 관리와 효과 전달만 담당한다.
    // 공격의 내용은 전부 attackEffects 목록이 정의한다: 피해도 항목의 하나 ({Damage, 10}).
    [Serializable]
    public class CombatComponent
    {
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private float attackCooldown = 2f;

        [Tooltip("명중 시 무슨 일이 일어나는가 — 이 공격의 정의 전부. 피해도 항목의 하나다: {Damage, 10}.")]
        [SerializeField] private EffectEntry[] attackEffects;

        private EntityView owner; // 효과의 출처(Source)로 전달 — Initialize로 주입
        private float lastAttackTime = float.MinValue;

        public float AttackRange => attackRange;
        public float AttackCooldown => attackCooldown;

        /// <summary>공격 정의 — 발사체로 쏘는 쪽(BattleTower 폴백 등)이 스펙에 싣는다.</summary>
        public EffectEntry[] AttackEffects => attackEffects;

        public event Action OnAttackAction;

        // 소유 엔티티 주입 — MovementComponent.Initialize와 같은 패턴. Awake에서 호출한다.
        public void Initialize(EntityView owner) => this.owner = owner;

        // 런타임 부착 엔티티(EnsurePlayerEntity 등) 전용 — 인스펙터를 못 쓰는 경우 공격 정의 주입
        public void SetAttackEffects(EffectEntry[] entries) => attackEffects = entries;

        /// <summary>
        /// 데이터(SO)에서 전투 수치를 주입한다 — 인스펙터 값을 덮어쓴다.
        /// 타워처럼 BuildingDataSO가 사거리·연사를 정의하는 경우에 쓴다.
        /// </summary>
        public void Configure(float range, float cooldown)
        {
            attackRange = Mathf.Max(0f, range);
            attackCooldown = Mathf.Max(0.01f, cooldown);
        }

        public bool CanAttack() => Time.time >= lastAttackTime + attackCooldown;

        // 효과를 직접 적용하지 않는 공격(투사체 발사 등)이 쿨다운만 소비할 때 사용.
        // 효과는 발사체(Bullet)가 명중 시 전달한다.
        public void MarkAttackPerformed()
        {
            lastAttackTime = Time.time;
            OnAttackAction?.Invoke();
        }

        public void TryAttack(EntityView target)
        {
            if (!target.IsValidTarget()) return;

            if (CanAttack())
            {
                lastAttackTime = Time.time;

                // 공격 정의를 통째로 적용 — 공격 버프는 항목별로 구워서(affects 목록 대상만) 나간다
                var effects = owner != null ? owner.Effects.BakeOutgoing(attackEffects) : attackEffects;
                // 근접은 명중점이 곧 대상 위치라 그것만으로는 방향이 나오지 않는다 —
                // 때린 사람에게서 대상 쪽으로가 곧 "날아온 방향"이다.
                Vector3 dir = owner != null ? target.GetPosition() - owner.GetPosition() : Vector3.zero;
                target.ApplyEffects(effects, owner, target.GetPosition(), dir);

                // 이벤트 발생 (애니메이션, 사운드 등에서 구독)
                OnAttackAction?.Invoke();
            }
        }
    }
}
