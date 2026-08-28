using UnityEngine;
using System;
using Object = UnityEngine.Object;

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
        // 심 건물은 점유 풋프린트(칸) 기준으로 잰다 — 모델(콜라이더)이 풋프린트보다 한참
        // 작을 수 있는데(3×3칸 코어의 우주선), 몬스터의 길을 막는 것은 풋프린트라서
        // 풋프린트 경계까지 붙은 몬스터의 공격이 닿으려면 거리의 정본도 풋프린트여야 한다.
        if (target is BuildingEntity be && be.TryGetFootprintRect(out Vector3 min, out Vector3 max))
        {
            float dx = Mathf.Max(min.x - from.x, 0f, from.x - max.x);
            float dz = Mathf.Max(min.z - from.z, 0f, from.z - max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);   // 지상전이라 높이는 무시한다
        }

        var col = target.GetComponentInChildren<Collider>();
        if (col == null) return Vector3.Distance(from, target.GetPosition());

        // 논컨벡스 MeshCollider는 ClosestPoint를 지원하지 않는다 — 입력점을 그대로 돌려줘
        // 거리가 항상 0이 되고, 몬스터가 맵 반대편에서 "닿았다"며 공격 상태로 굳는다.
        // 건물(전부 논컨벡스 메시)은 전 콜라이더 AABB 합의 최근접점으로 근사한다 —
        // 구 루트 박스 콜라이더 시절과 같은 감각의 거리라 전투 밸런스도 그대로다.
        if (col is MeshCollider mesh && !mesh.convex)
        {
            var cols = target.GetComponentsInChildren<Collider>();
            var b = cols[0].bounds;
            for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
            return Vector3.Distance(from, b.ClosestPoint(from));
        }

        return Vector3.Distance(from, col.ClosestPoint(from));
    }

}

// 모든 게임 개체(몬스터/플레이어/건물)의 공통 베이스.
// HP/피격/사망은 전 엔티티 공통이고, 이동·전투·감지 컴포넌트(순수 C#)는
// 하위 클래스가 보유한 것만 virtual 프로퍼티로 노출한다.
public class Entity : MonoBehaviour, IPingable
{
    // ── 핑 대상 (IPingable) — 몬스터·건물·둥지·플레이어 공통. 표시 이름은 하위가 덮어쓴다.
    //    자기 자신(로컬 플레이어)은 여기서 거르지 않는다 — 다른 플레이어는 찍혀야 하므로 조준이 계층으로 뺀다.
    public virtual string PingLabel => name;
    public GameObject PingRoot => gameObject;
    public virtual bool CanBePinged => isActiveAndEnabled && !IsDead;

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
    ///
    /// virtual인 이유: "누가 때렸는가"로 갈리는 규칙은 여기서만 판단할 수 있다.
    /// ReceiveDamage는 시전자를 모르므로(수치만 받는다) 플레이어 공격만 막는 것 같은
    /// 규칙은 이쪽을 override해야 한다.
    /// </summary>
    public virtual void ApplyEffects(System.Collections.Generic.IReadOnlyList<EffectEntry> entries,
                                     Entity source, Vector3 hitPoint, Vector3 hitDirection = default)
        => Effects.ApplyAll(entries, source, hitPoint, hitDirection);

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
