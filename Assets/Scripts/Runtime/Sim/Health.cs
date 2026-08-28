using System;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 체력·사망 — 모든 엔티티(몬스터·플레이어·건물·둥지)의 HP는 여기 하나뿐이다.
    ///
    /// 구 HealthComponent(뷰 인라인)의 후신. 뷰에서 심으로 옮긴 이유: HP가 뷰에 있으면 심이 뷰를 알아야
    /// 파괴를 결정할 수 있고(역참조), 세이브·서버·헤드리스 테스트가 전부 그 지점에서 막힌다.
    /// 뷰는 이 모듈의 이벤트를 구독해 체력바·사망 연출을 그릴 뿐이다.
    ///
    /// 받는 피해의 단일 수렴점은 <see cref="Damage"/>다 — 보호막·무적·아군 공격 무시 같은 규칙은
    /// 소유 엔티티의 <see cref="IDamageInterceptor"/> 모듈이 여기서 한 번에 걸러낸다.
    /// </summary>
    public sealed class Health : EntityModule
    {
        float _max;
        float _current;

        public float MaxHealth => _max;
        public float CurrentHealth => _current;
        public bool IsDead { get; private set; }

        /// <summary>(현재, 최대) — 값이 바뀔 때마다. 복원(RestoreState) 때도 발화해 표시가 따라오게 한다.</summary>
        public event Action<float, float> OnHealthChanged;

        /// <summary>죽는 순간 1회. 복원으로 "죽어 있던 상태"가 될 때는 발화하지 않는다(연출·드롭이 로드마다 도는 것 방지).</summary>
        public event Action OnDeath;

        public Health(float max)
        {
            _max = Math.Max(1f, max);
            _current = _max;
        }

        /// <summary>
        /// 피해 적용 — 인터셉터(보호막·무적)를 거친 뒤 깎는다. 실제로 깎인 양을 돌려준다.
        /// 시전측 배율(공격 버프)은 이미 항목에 구워져 있어야 하고, 받는 쪽 배율(방어 디버프)은 호출자가 곱해 넘긴다.
        /// </summary>
        /// <param name="source">때린 엔티티. 모르면 null.</param>
        public float Damage(float amount, Entity source)
        {
            if (IsDead || amount <= 0f) return 0f;

            if (Owner != null) amount = Owner.InterceptDamage(amount, source);
            if (amount <= 0f) return 0f;

            float before = _current;
            _current = Clamp(_current - amount, 0f, _max);
            OnHealthChanged?.Invoke(_current, _max);

            if (_current <= 0f) Die();
            return before - _current;
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            _current = Clamp(_current + amount, 0f, _max);
            OnHealthChanged?.Invoke(_current, _max);
        }

        /// <summary>최대치 변경. refill이면 가득 채우고, 아니면 현재치만 상한에 맞춘다(코어 수리처럼 최대치만 올릴 때).</summary>
        public void SetMaxHealth(float max, bool refill = true)
        {
            _max = Math.Max(1f, max);
            _current = refill && !IsDead ? _max : Math.Min(_current, _max);
            OnHealthChanged?.Invoke(_current, _max);
        }

        /// <summary>부활·둥지 재생 — 가득 채우고 사망을 푼다. OnDeath의 반대 사건은 없다(부활 연출은 호출자가).</summary>
        public void ResetToFull()
        {
            _current = _max;
            IsDead = false;
            OnHealthChanged?.Invoke(_current, _max);
        }

        /// <summary>즉시 사망 — 치트·연출·강제 처치. 인터셉터를 거치지 않는다.</summary>
        public void Kill()
        {
            if (IsDead) return;
            _current = 0f;
            OnHealthChanged?.Invoke(_current, _max);
            Die();
        }

        /// <summary>
        /// 세이브 복원 전용 — 값과 사망 여부를 그대로 옮겨 앉는다. OnDeath는 쏘지 않는다:
        /// 복원은 사망이라는 사건이 아니라 죽어 있던 상태이고, 쏘면 연출·드롭·전멸 판정이 로드할 때마다 다시 돈다.
        /// </summary>
        public void RestoreState(float max, float current, bool isDead)
        {
            _max = Math.Max(1f, max);
            _current = Clamp(current, 0f, _max);
            IsDead = isDead;
            OnHealthChanged?.Invoke(_current, _max);
        }

        void Die()
        {
            IsDead = true;
            OnDeath?.Invoke();
            Owner?.NotifyDied();
        }

        static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
