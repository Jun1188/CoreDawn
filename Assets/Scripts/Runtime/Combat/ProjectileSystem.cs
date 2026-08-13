using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 전달 방식 — 효과를 대상에게 어떻게 전달하는가. 클래스 상속이 아니라 데이터가 정하며,
/// 총(GunData)과 타워(TowerDataSO)가 공용으로 쓴다.
/// </summary>
public enum FireMode
{
    Projectile, // 발사체가 날아가 명중한 하나에게 — 탄속·탄도 있음
    Hitscan,    // 즉시 판정으로 명중한 하나에게 — 속도 무한의 발사 (레이저·저격)
    Aura,       // 원점 반경의 전원에게 (펄스) — 감속 필드 등
    None,       // 전달하지 않음 — 비전투 구조물 (인펜스 같은 순수 장애물)
}

/// <summary>
/// 발사 한 번의 명세 — 발사체가 어떻게 날고(속도·수명·사거리), 명중 시 무엇을 하는가(효과 목록).
/// 배율(공격 버프·발사기 배율)은 발사 시점에 목록에 구워져 확정된다:
/// 총알이 날아가는 동안 버프가 끝나도 발사 때 배율이 유지된다.
/// </summary>
public readonly struct ProjectileShot
{
    public readonly float Speed;
    public readonly float Lifetime;
    public readonly float Range;
    public readonly int TargetMask;        // 효과를 적용할 레이어 (0이면 적용 없음, 소멸만)
    public readonly EffectEntry[] Effects; // 명중 시 무슨 일이 일어나는가 — 배율이 이미 구워진 최종 목록
    public readonly Entity Source;         // 발사자 — 효과 출처이자 자기 명중 무시 기준
    public readonly float Gravity;         // 낙하 가속 — 0이면 직선탄, >0이면 포물선 (탄약의 성질)
    public readonly float ExplosionRadius; // 착탄 폭발 반경 — 0이면 단일 명중, >0이면 착탄점 Pulse

    public ProjectileShot(float speed, float lifetime, float range,
                          EffectEntry[] effects, int targetMask, Entity source,
                          float gravity = 0f, float explosionRadius = 0f)
    {
        Speed = speed;
        Lifetime = lifetime;
        Range = range;
        Effects = effects;
        TargetMask = targetMask;
        Source = source;
        Gravity = gravity;
        ExplosionRadius = explosionRadius;
    }
}

/// <summary>
/// 발사 공용 시스템 — 총(Gun)과 공격 타워(BattleTower)가 같은 코드로 쏜다.
/// 투사체(Fire)와 히트스캔(Hitscan)은 같은 스펙(ProjectileShot)·같은 명중 처리(ApplyHit)를
/// 공유한다 — 히트스캔은 속도 무한의 발사일 뿐이다.
///
/// 풀은 프리팹당 하나를 전역 공유한다. 총기마다 전용 풀을 들면 같은 총알 프리팹인데도
/// 인스턴스가 이중으로 쌓이고, 타워처럼 풀 없이 Instantiate/Destroy 하던 곳은
/// 발사마다 GC 쓰레기를 만든다. 풀 소속 인스턴스는 전용 루트(DontDestroyOnLoad) 아래 두어
/// 씬 전환이 풀 안의 오브젝트를 파괴해 죽은 참조를 남기는 일을 막는다.
/// </summary>
public static class ProjectileSystem
{
    // ── 명중 판정 공용부 (투사체 스윕·히트스캔이 함께 쓴다) ──────────

    // GC 방지용 재사용 버퍼 (메인 스레드 전용, SensorComponent와 같은 패턴)
    private static readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    /// <summary>
    /// 히트스캔 발사 — 투사체 없이 즉시 판정. 명중했으면 true.
    /// 트리거(총알 등)는 무시하고, 발사자 자신도 무시한다.
    /// </summary>
    public static bool Hitscan(Vector3 origin, Vector3 direction, in ProjectileShot shot)
    {
        if (direction.sqrMagnitude < 0.0001f) return false;

        Transform ignore = shot.Source != null ? shot.Source.transform.root : null;
        if (!TryClosestHit(origin, direction.normalized, shot.Range, 0f, ignore, out RaycastHit hit))
            return false;

        Impact(hit.collider, hit.point, shot);
        return true;
    }

    /// <summary>
    /// 발사자(ignoreRoot 소속)를 제외한 가장 가까운 히트를 찾는다.
    /// radius > 0이면 스피어캐스트(투사체 굵기), 0이면 순수 레이(히트스캔).
    /// 트리거 콜라이더는 무시 — 총알끼리 서로 맞고 소멸하는 일이 없다.
    /// </summary>
    public static bool TryClosestHit(Vector3 origin, Vector3 direction, float distance, float radius,
                                     Transform ignoreRoot, out RaycastHit closest)
    {
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, direction, hitBuffer, distance,
                                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, hitBuffer, distance,
                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        closest = default;
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var h = hitBuffer[i];
            if (ignoreRoot != null && h.transform.IsChildOf(ignoreRoot)) continue;
            if (h.distance < best)
            {
                best = h.distance;
                closest = h;
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// 착탄 처리 — 폭발탄(ExplosionRadius > 0)은 착탄점 반경 전원에게(Pulse), 아니면
    /// 명중한 하나에게(ApplyHit). 투사체 스윕·히트스캔이 같은 분기를 쓴다.
    /// 폭발은 오라와 같은 코드다 — "터진다"는 착탄점에서 한 번 펄스하는 것.
    /// </summary>
    public static void Impact(Collider hit, Vector3 point, in ProjectileShot shot)
    {
        if (shot.ExplosionRadius > 0f) Pulse(point, shot.ExplosionRadius, shot);
        else ApplyHit(hit, point, shot);
    }

    /// <summary>명중 처리 — 맞은 것이 대상 레이어의 Entity면 실린 효과를 적용한다.</summary>
    public static void ApplyHit(Collider hit, Vector3 point, in ProjectileShot shot)
    {
        if (shot.TargetMask == 0) return;
        if ((shot.TargetMask & (1 << hit.gameObject.layer)) == 0) return;

        Entity entity = hit.GetComponentInParent<Entity>();
        if (entity != null && !entity.IsDead)
            entity.ApplyEffects(shot.Effects, shot.Source, point);
    }

    // ── 오라(펄스) — 세 번째 전달 방식 ──────────────────────────

    private static readonly Collider[] pulseBuffer = new Collider[64];
    private static readonly HashSet<Entity> pulseSeen = new HashSet<Entity>();

    /// <summary>
    /// 원점 반경의 대상 전원에게 효과를 적용한다 (펄스형 오라 — 감속 필드 등).
    /// 히트스캔이 "속도 무한의 발사"이듯 오라는 "반경 전체 명중의 발사"다 —
    /// 발사기(타워)는 언제 펄스할지·연료를 태울지만 정하고, 적용은 여기가 한다.
    /// 적용된 대상 수를 반환한다.
    /// </summary>
    public static int Pulse(Vector3 origin, float radius, in ProjectileShot shot)
    {
        if (shot.TargetMask == 0) return 0;

        int count = Physics.OverlapSphereNonAlloc(origin, radius, pulseBuffer, shot.TargetMask);
        pulseSeen.Clear();
        int applied = 0;
        for (int i = 0; i < count; i++)
        {
            Entity entity = pulseBuffer[i].GetComponentInParent<Entity>();
            if (entity == null || entity.IsDead) continue;
            if (!pulseSeen.Add(entity)) continue;   // 콜라이더 여러 개인 대상 중복 방지

            entity.ApplyEffects(shot.Effects, shot.Source, origin);
            applied++;
        }
        return applied;
    }

    /// <summary>반경 안의 유효 대상 수 — 빈 필드에 연료를 태우지 않으려는 발사기가 펄스 전에 확인한다.</summary>
    public static int CountTargets(Vector3 origin, float radius, int targetMask)
    {
        if (targetMask == 0) return 0;

        int count = Physics.OverlapSphereNonAlloc(origin, radius, pulseBuffer, targetMask);
        pulseSeen.Clear();
        for (int i = 0; i < count; i++)
        {
            Entity entity = pulseBuffer[i].GetComponentInParent<Entity>();
            if (entity != null && !entity.IsDead) pulseSeen.Add(entity);
        }
        return pulseSeen.Count;
    }

    // ── 배율 ────────────────────────────────────────────────────

    /// <summary>
    /// 발사기 배율(damageMultiplier)을 피해형(Damage·DoT) 항목에만 곱한다 —
    /// 배율 1.5 발사기가 감속탄을 쏜다고 감속(비율형 value)이 뭉개지면 안 된다.
    /// 총·타워 공용 (탄약이 효과의 주인, 발사기는 배율).
    /// </summary>
    public static EffectEntry[] ScaleDamage(EffectEntry[] effects, float multiplier)
    {
        if (effects == null || Mathf.Approximately(multiplier, 1f)) return effects;

        var scaled = new EffectEntry[effects.Length];
        for (int i = 0; i < effects.Length; i++)
        {
            var entry = effects[i];
            bool damageLike = entry.effect is DamageEffectSO || entry.effect is DamageOverTimeEffectSO;
            scaled[i] = damageLike ? new EffectEntry(entry.effect, entry.value * multiplier) : entry;
        }
        return scaled;
    }

    // ── 곡사 조준 ───────────────────────────────────────────────

    /// <summary>
    /// 고정 초속·중력으로 표적을 맞추는 발사 방향(탄도해). 발사기는 각도만 정한다는
    /// 문법의 "각도 계산기" — 탄속·중력은 탄약의 성질이라 인자로 받는다.
    /// 같은 사거리에 해가 둘(저각·고각) 있으면 highArc로 고른다 — 박격포는 고각으로
    /// 장애물을 넘기고, 직사 발사기는 저각으로 빨리 닿는다.
    /// 초속이 모자라 닿지 않으면 최대 사거리인 45°로 최선을 다한다.
    /// </summary>
    public static Vector3 BallisticAim(Vector3 origin, Vector3 target, float speed, float gravity, bool highArc = false)
    {
        Vector3 delta = target - origin;
        var flat = new Vector3(delta.x, 0f, delta.z);
        float d = flat.magnitude;
        if (d < 0.001f || gravity <= 0f || speed <= 0f)
            return delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.up;

        // 포물선 공식: tanθ = (v² ± √(v⁴ − g(gd² + 2yv²))) / (gd)  (y = 높이차)
        float v2 = speed * speed;
        float disc = v2 * v2 - gravity * (gravity * d * d + 2f * delta.y * v2);
        float tan = disc <= 0f
            ? 1f // 도달 불가 — 45°
            : (v2 + (highArc ? Mathf.Sqrt(disc) : -Mathf.Sqrt(disc))) / (gravity * d);

        return (flat / d + Vector3.up * tan).normalized;
    }

    // ── 투사체 발사·풀 ──────────────────────────────────────────
    private static readonly Dictionary<GameObject, ObjectPool<GameObject>> pools
        = new Dictionary<GameObject, ObjectPool<GameObject>>();
    private static Transform poolRoot;

    // 도메인 리로드를 끈 환경에서 static이 플레이를 넘어 살아남는 것 방지
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pools.Clear();
        poolRoot = null;
    }

    /// <summary>발사체 하나를 발사한다. direction은 정규화돼 있지 않아도 된다.</summary>
    public static Bullet Fire(GameObject prefab, Vector3 position, Vector3 direction, in ProjectileShot shot)
    {
        if (prefab == null || direction.sqrMagnitude < 0.0001f) return null;

        GameObject go = GetPool(prefab).Get();
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction.normalized));

        var bullet = go.GetComponent<Bullet>();
        if (bullet == null)
        {
            Debug.LogWarning($"[ProjectileSystem] 발사체 프리팹에 Bullet 컴포넌트가 없습니다: {prefab.name}");
            go.SetActive(false);
            return null;
        }
        bullet.Launch(shot);
        return bullet;
    }

    private static ObjectPool<GameObject> GetPool(GameObject prefab)
    {
        if (pools.TryGetValue(prefab, out var pool)) return pool;

        pool = new ObjectPool<GameObject>(
            createFunc: () =>
            {
                var go = Object.Instantiate(prefab, PoolRoot());
                var b = go.GetComponent<Bullet>();
                if (b != null) b.SetPool(pools[prefab]);
                return go;
            },
            actionOnGet: go => go.SetActive(true),
            actionOnRelease: go => go.SetActive(false),
            actionOnDestroy: Object.Destroy,
            collectionCheck: true,
            defaultCapacity: 20,
            maxSize: 100);

        pools.Add(prefab, pool);
        return pool;
    }

    private static Transform PoolRoot()
    {
        if (poolRoot == null)
        {
            var go = new GameObject("ProjectilePool (Runtime)");
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            poolRoot = go.transform;
        }
        return poolRoot;
    }
}
