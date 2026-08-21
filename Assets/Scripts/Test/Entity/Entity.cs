using UnityEngine;
using System;

// Entity 확장 유틸 — 타겟 유효성/거리 판정 (구 IInteractable 확장의 Entity 버전)
public static class EntityExtensions
{
    // 순수 null 체크만으로는 Destroy된 MonoBehaviour(가짜 null)를 걸러내지 못하므로
    // UnityEngine.Object의 == 오버로드를 활용. SetActive(false)로 내려간 엔티티도 무효 처리.
    public static bool IsValidTarget(this Entity target)
    {
        if (target == null) return false;
        if (!target.gameObject.activeInHierarchy) return false;
        return !target.IsDead;
    }

    // 멀티타일 건물처럼 부피가 있는 대상은 중심점 대신 콜라이더 표면까지의 거리를 사용
    public static float DistanceTo(this Entity target, Vector3 from)
    {
        var col = target.GetComponentInChildren<Collider>();
        if (col != null) return Vector3.Distance(from, col.ClosestPoint(from));
        return Vector3.Distance(from, target.GetPosition());
    }
}

// 모든 게임 개체(몬스터/플레이어/건물)의 공통 베이스.
// HP/피격/사망은 전 엔티티 공통이고, 이동·전투·감지 컴포넌트(순수 C#)는
// 하위 클래스가 보유한 것만 virtual 프로퍼티로 노출한다.
public class Entity : MonoBehaviour
{
    [Header("Entity Settings (Compatibility)")]
    [Tooltip("PlayerController 등 기존 코드 호환용. 몬스터 이동 속도는 MovementComponent 쪽 값을 사용한다.")]
    public float moveSpeed = 5f;

    [Header("Death Settings")]
    [Tooltip("사망 연출(애니메이션 등)에 쓸 지연 시간(초). 이후 오브젝트가 소멸/비활성화된다.")]
    [SerializeField] private float deathDelay = 2f;
    [Tooltip("true면 사망 연출 후 Destroy로 완전 소멸, false면 SetActive(false)로 비활성화만 한다.")]
    [SerializeField] private bool destroyOnDeath = true;
    [SerializeField] private HealthComponent health = new HealthComponent();

    public HealthComponent Health => health;

    // 활성 지속 효과(감속·DoT 등) 관리 — Awake 전에 맞아도 안전하게 지연 생성
    private EffectController effects;
    public EffectController Effects => effects ??= new EffectController(this);

    // 하위 클래스가 보유한 컴포넌트만 노출 (없으면 null)
    public virtual MovementComponent Movement => null;
    public virtual CombatComponent Combat => null;
    public virtual SensorComponent Sensor => null;

    public virtual bool IsDead => health.IsDead;

    public event Action<float, float> OnHealthChanged
    {
        add => health.OnHealthChanged += value;
        remove => health.OnHealthChanged -= value;
    }

    public event Action OnDeath
    {
        add => health.OnDeath += value;
        remove => health.OnDeath -= value;
    }

    public event Action OnAttackAction
    {
        add { if (Combat != null) Combat.OnAttackAction += value; }
        remove { if (Combat != null) Combat.OnAttackAction -= value; }
    }

    protected virtual void Awake()
    {
        health.Initialize();
        health.OnDeath += Effects.Clear; // 사망 즉시 지속 효과 종료 (HandleDeath보다 먼저)
        health.OnDeath += HandleDeath;
    }

    protected virtual void Start() { }

    protected virtual void Update()
    {
        Effects.Tick(Time.deltaTime);
        if (Movement != null) Movement.SpeedMultiplier = Effects.MoveSpeedMultiplier;

        // (팀) 체력 디버그 로그는 비활성화됨
        /*
        if (!health.IsDead && (int)(health.CurrentHealth)%7 == 0
            && (this.GetType() == typeof(BattleTower)
            || this.GetType() == typeof(Player)
            || this.GetType() == typeof(Monster))) Debug.Log(this + "의 체력은 " + health.CurrentHealth);*/
    }

    /// <summary>
    /// 공격 명중의 단일 진입점 — 효과 항목 목록을 이 엔티티에 적용한다.
    /// 시전측 배율(공격 버프·포탑 배율)은 시전자가 보낼 때 이미 항목에 구워져(bake) 있다.
    /// </summary>
    public void ApplyEffects(System.Collections.Generic.IReadOnlyList<EffectEntry> entries,
                             Entity source, Vector3 hitPoint)
        => Effects.ApplyAll(entries, source, hitPoint);

    /// <summary>
    /// 받는 피해의 단일 수렴점 — 방어 배율(IncomingDamageMultiplier)을 적용해 체력을 깎는다.
    /// 피해를 주는 효과 구현(DamageEffectSO·DoT)이 Health.TakeDamage 대신 이걸 호출한다.
    /// 무적·보호막 같은 받는 쪽 규칙은 여기를 override해야 한다 — TakeDamage만 override하면
    /// 효과 경로(총알·몬스터 공격 = ApplyEffects → ReceiveDamage)가 그 규칙을 그냥 지나친다.
    /// </summary>
    public virtual void ReceiveDamage(float amount)
    {
        float final = amount * Effects.IncomingDamageMultiplier;

        if (final > 0f)
        {
            health.TakeDamage(final);
        }
        else
        {
            Debug.Log(
                $"[Entity] 데미지 무시됨 / final={final}"
            );
        }
    }

    // 구 호환 — 출처·효과 없는 순수 피해. 새 코드는 ApplyEffects를 쓸 것.
    public virtual void TakeDamage(float damageAmount) => ReceiveDamage(damageAmount);

    // 즉시 사망 — HP를 0으로 만들고 사망 흐름(OnDeath → HandleDeath)을 태운다
    public void Die() => health.Kill();
    public virtual void Revive(Vector3 respawnPosition)
    {
        print("부활");
        // 1. 위치 이동
        transform.position = respawnPosition;
        
        // 2. 꺼져있던 게임 오브젝트 다시 활성화
        gameObject.SetActive(true);
        
        // 3. 체력 및 IsDead 상태 초기화 (HealthComponent.Initialize 활용)
        health.Initialize();
    }
    // 런타임 부착 시 사망 방식 변경용 (예: FPS 플레이어는 Destroy 대신 비활성화)
    public void SetDeathBehavior(bool destroy, float delay)
    {
        destroyOnDeath = destroy;
        deathDelay = Mathf.Max(0f, delay);
    }

    public Vector3 GetPosition() => transform.position;

    // 기본 사망 처리: 연출(deathDelay) 후 소멸/비활성화. 즉시 소멸이 필요한 엔티티(건물)는 override.
    protected virtual void HandleDeath()
    {
        if (deathDelay <= 0f) FinishDeath();
        else Invoke(nameof(FinishDeath), deathDelay);
    }

    private void FinishDeath()
    {
        if (destroyOnDeath) Destroy(gameObject);
        else gameObject.SetActive(false);
    }
}
