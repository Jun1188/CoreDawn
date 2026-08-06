using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 발사 한 번의 명세 — 발사체가 어떻게 날고(속도·수명·사거리), 명중 시 무엇을 하는가(Power·효과).
/// Power는 발사 시점에 확정한다: 총알이 날아가는 동안 버프가 끝나도 발사 때 배율이 유지된다.
/// </summary>
public readonly struct ProjectileShot
{
    public readonly float Speed;
    public readonly float Lifetime;
    public readonly float Range;
    public readonly float Power;      // 명중 시 EffectContext.Power로 전달될 기본 수치
    public readonly int TargetMask;   // 효과를 적용할 레이어 (0이면 적용 없음, 소멸만)
    public readonly EffectSO[] Effects;
    public readonly Entity Source;    // 발사자 — 효과 출처이자 자기 명중 무시 기준

    public ProjectileShot(float speed, float lifetime, float range, float power,
                          int targetMask, EffectSO[] effects, Entity source)
    {
        Speed = speed;
        Lifetime = lifetime;
        Range = range;
        Power = power;
        TargetMask = targetMask;
        Effects = effects;
        Source = source;
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

        ApplyHit(hit.collider, hit.point, shot);
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

    /// <summary>명중 처리 — 맞은 것이 대상 레이어의 Entity면 실린 효과를 적용한다.</summary>
    public static void ApplyHit(Collider hit, Vector3 point, in ProjectileShot shot)
    {
        if (shot.TargetMask == 0) return;
        if ((shot.TargetMask & (1 << hit.gameObject.layer)) == 0) return;

        Entity entity = hit.GetComponentInParent<Entity>();
        if (entity != null && !entity.IsDead)
            entity.ApplyEffects(shot.Effects, new EffectContext(shot.Source, shot.Power, point));
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
