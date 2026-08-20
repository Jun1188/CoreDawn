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
    public readonly int TargetMask;        // 효과를 적용할 레이어. 0 = 판정 없는 연출탄 — 스윕도 생략(트레이서)
    public readonly EffectEntry[] Effects; // 명중 시 무슨 일이 일어나는가 — 배율이 이미 구워진 최종 목록
    public readonly Entity Source;         // 발사자 — 효과 출처이자 자기 명중 무시 기준
    public readonly float Gravity;         // 낙하 가속 — 0이면 직선탄, >0이면 포물선 (탄약의 성질)
    public readonly float ExplosionRadius; // 착탄 폭발 반경 — 0이면 단일 명중, >0이면 착탄점 Pulse
    public readonly FireMode Mode;         // 전달 방식 — 발사기(GunData·TowerDataSO)가 정한다
    public readonly GameObject Prefab;     // 탄 외형(탄약의 bulletPrefab) — Projectile은 판정하는 몸, Hitscan은 트레이서 연출
    public readonly int Pierce;            // 추가 관통 대상 수 — 0이면 첫 대상에서 멈춤 (탄약의 성질)
    public readonly Vector3 Muzzle;        // 연출 출발점(총구) — FromMuzzle일 때만 유효
    public readonly bool FromMuzzle;       // true면 탄이 총구에서 출발해 조준선 1m 지점에 합류 후 직진 (FPS 뷰모델용)
    public readonly GameObject HitEffect;  // 착탄/폭발 지점에서 재생할 파티클 (탄약의 hitEffectPrefab)

    public ProjectileShot(float speed, float lifetime, float range,
                          EffectEntry[] effects, int targetMask, Entity source,
                          float gravity = 0f, float explosionRadius = 0f,
                          FireMode mode = FireMode.Projectile, GameObject prefab = null,
                          int pierce = 0, Vector3? muzzle = null, GameObject hitEffect = null)
    {
        Speed = speed;
        Lifetime = lifetime;
        Range = range;
        Effects = effects;
        TargetMask = targetMask;
        Source = source;
        Gravity = gravity;
        ExplosionRadius = explosionRadius;
        Mode = mode;
        Prefab = prefab;
        Pierce = pierce;
        FromMuzzle = muzzle.HasValue;
        Muzzle = muzzle ?? default;
        HitEffect = hitEffect;
    }
}

/// <summary>
/// 발사 공용 시스템 — 총(Gun)과 공격 타워(BattleTower)가 같은 코드로 쏜다.
/// 진입점은 Fire(origin, direction, shot) 하나 — 전달 방식(shot.Mode) 분기는 여기서 끝나고,
/// 네 모드 전부 같은 스펙(ProjectileShot)·같은 착탄 처리(Impact)를 공유한다.
/// 히트스캔은 속도 무한의 발사, 오라는 반경 전체 명중의 발사일 뿐이다.
///
/// 풀은 프리팹당 하나를 전역 공유한다. 총기마다 전용 풀을 들면 같은 총알 프리팹인데도
/// 인스턴스가 이중으로 쌓이고, 타워처럼 풀 없이 Instantiate/Destroy 하던 곳은
/// 발사마다 GC 쓰레기를 만든다. 풀 소속 인스턴스는 전용 루트(DontDestroyOnLoad) 아래 두어
/// 씬 전환이 풀 안의 오브젝트를 파괴해 죽은 참조를 남기는 일을 막는다.
/// </summary>
public static class ProjectileSystem
{
    // ── 발사 진입점 ─────────────────────────────────────────────

    /// <summary>
    /// 발사 한 번 — 발사기(총·타워)의 유일한 진입점. 발사기는 스펙(shot)만 만들고
    /// 전달 방식은 모른다 — 분기는 여기서 끝난다.
    ///
    /// 판정과 연출의 분리: <b>판정은 모드의 물리대로</b>(히트스캔·오라 = 즉시, 투사체 =
    /// 비행 스윕 — 판정을 인스턴스 Update에 태우면 프레임 지연·풀 수명 결합이 생긴다),
    /// <b>인스턴스는 연출</b> — 히트스캔도 판정 직후 같은 탄 프리팹을 트레이서(판정 없는 탄)로
    /// 날려 눈에 보인다. 프리팹이 없으면 연출만 생략되고 판정은 그대로다(헤드리스 안전).
    /// </summary>
    /// <summary>총구 출발 탄이 조준선에 합류하는 지점 — 총구의 조준선상 투영점 기준 전방 거리.</summary>
    private const float MuzzleJoinDistance = 2f;

    public static void Fire(Vector3 origin, Vector3 direction, in ProjectileShot shot)
    {
        switch (shot.Mode)
        {
            case FireMode.None:
                return;

            case FireMode.Hitscan:
                Hitscan(origin, direction, shot, out Vector3 beamEnd);
                SpawnTracer(origin, direction, shot, beamEnd);
                return;

            case FireMode.Aura:
                Pulse(origin, shot.Range, shot);
                return;

            default: // Projectile — 인스턴스가 곧 판정하는 몸
                SpawnAligned(origin, direction, shot, shot);
                return;
        }
    }

    /// <summary>
    /// 발사체 소환 — FromMuzzle이면 총구에서 태어나 조준선 1m 지점(합류점)으로 날아가
    /// 합류한 뒤 조준선 방향으로 직진한다: 카메라에서 태어나 화면을 가리지도, 총구 방향
    /// 그대로 날아가 조준선과 어긋나지도 않는다. 아니면(타워 등) origin에서 곧장.
    /// </summary>
    private static void SpawnAligned(Vector3 origin, Vector3 direction, in ProjectileShot spec, in ProjectileShot body)
    {
        direction = direction.normalized;
        if (spec.FromMuzzle)
        {
            // 합류점은 총구보다 항상 앞이어야 한다 — 총구가 조준선 원점(카메라)보다 앞에
            // 저작돼 있으면 "원점 기준 1m"는 총구 뒤가 되어 탄이 역주행한다.
            // 총구를 조준선에 투영한 지점에서 1m 앞을 합류점으로.
            float muzzleAlongRay = Mathf.Max(0f, Vector3.Dot(spec.Muzzle - origin, direction));
            float joinDistance = muzzleAlongRay + MuzzleJoinDistance;
            Vector3 join = origin + direction * joinDistance;

            // 합류 구간의 표적은 조준선에서 미리 판정한다.
            // 탄의 몸은 총구에서 태어나므로 <b>총구보다 가까운 것은 이미 지나쳐 있다</b> —
            // 벽에 붙어 쏘거나 몬스터가 코앞에 붙으면 탄이 그것을 통과해 날아가던 원인이다.
            // 판정 축이 조준선이라는 원칙 그대로, 근접 표적은 여기서 즉시 착탄시킨다.
            if (body.TargetMask != 0)
            {
                Transform ignore = spec.Source != null ? spec.Source.transform.root : null;
                if (TryClosestHit(origin, direction, joinDistance, 0f, ignore, out RaycastHit near))
                {
                    Impact(near.collider, near.point, spec);
                    return;
                }
            }

            // 탄두는 스폰부터 최종 진행 방향(조준선)을 본다 — 합류점 쪽을 보면 탄이
            // 비스듬히 나는 것처럼 보인다. 합류 구간의 실제 이동은 Bullet이 velocity로 처리.
            var b = Spawn(body.Prefab, spec.Muzzle, direction, body);
            if (b != null) b.SetJoin(join, direction);
        }
        else
        {
            Spawn(body.Prefab, origin, direction, body);
        }
    }

    /// <summary>히트스캔 트레이서 속도 — 판정은 즉시라 순수 연출값. 탄약의 speed는 투사체용이다.</summary>
    private const float TracerSpeed = 300f;

    /// <summary>
    /// 히트스캔 연출용 트레이서 — 효과도 판정도 없는 탄(TargetMask 0 = 스윕 생략)이
    /// 판정이 계산해 준 빔 길이만큼 날고 그 자리에서 소멸한다. 관통 빔이면 뚫린 대상들을
    /// 그대로 지나쳐 차폐물·종점에서 죽는다 — 총구 출발이면 조준선 합류 후 같은 자리에서 죽는다.
    /// 수명은 비행 시간에 비례(+여유) — 소멸은 Range가 하고 수명은 안전망이다.
    /// </summary>
    private static void SpawnTracer(Vector3 origin, Vector3 direction, in ProjectileShot shot, Vector3 beamEnd)
    {
        if (shot.Prefab == null) return;

        // 사거리는 탄이 태어난 자리에서 재므로(Bullet.start), 총구 출발이면 총구→종점으로 잰다.
        // 카메라 기준 거리를 쓰면 총구가 카메라보다 앞에 있는 만큼 더 날아가, 판정은 벽·땅에서
        // 끝났는데 연출만 그것을 뚫고 지나간 것처럼 보인다 (히트스캔이 정상 발사처럼 보이던 원인).
        Vector3 spawn = shot.FromMuzzle ? shot.Muzzle : origin;
        float travel = Vector3.Distance(spawn, beamEnd);
        if (travel <= 0.01f) return;

        var cosmetic = new ProjectileShot(TracerSpeed, travel / TracerSpeed + 0.5f, travel,
                                          null, 0, shot.Source, shot.Gravity, 0f,
                                          FireMode.Projectile, shot.Prefab);
        SpawnAligned(origin, direction, shot, cosmetic);
    }

    // ── 명중 판정 공용부 (투사체 스윕·히트스캔이 함께 쓴다) ──────────

    // GC 방지용 재사용 버퍼 (메인 스레드 전용, SensorComponent와 같은 패턴)
    private static readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    private static int sweepLayers;

    /// <summary>
    /// 스윕·히트스캔이 검사할 레이어 — 기본 레이캐스트 레이어에서 <b>탄(Bullet 레이어)을 뺀다</b>.
    /// 탄은 자기 자신·다른 탄에 맞아서는 안 된다: 스피어캐스트는 시작점에서 겹친 콜라이더를
    /// 거리 0으로 보고하므로, 탄이 검사 대상이면 태어난 자리(총구)에서 즉시 착탄한다.
    /// 레이어로 거르면 아트 프리팹의 콜라이더 설정(트리거 여부)에 의존하지 않는다.
    /// </summary>
    private static int SweepLayers
    {
        get
        {
            if (sweepLayers == 0)
            {
                int bullet = LayerMask.NameToLayer("Bullet");
                sweepLayers = bullet >= 0
                    ? Physics.DefaultRaycastLayers & ~(1 << bullet)
                    : Physics.DefaultRaycastLayers;
            }
            return sweepLayers;
        }
    }

    /// <summary>
    /// 히트스캔 발사 — 투사체 없이 즉시 판정. 명중했으면 true.
    /// 트리거(총알 등)는 무시하고, 발사자 자신도 무시한다.
    /// Pierce > 0이면 경로의 대상을 (1+Pierce)명까지 차례로 뚫는다 — 단 벽·건물 같은
    /// 비대상 콜라이더에서는 빔이 멈춘다 (에너지탄이 관통해도 차폐는 차폐).
    /// </summary>
    public static bool Hitscan(Vector3 origin, Vector3 direction, in ProjectileShot shot)
        => Hitscan(origin, direction, shot, out _);

    /// <summary>end로 빔의 실제 종점을 보고한다 — 트레이서가 판정과 같은 자리에서 죽도록.</summary>
    public static bool Hitscan(Vector3 origin, Vector3 direction, in ProjectileShot shot, out Vector3 end)
    {
        if (direction.sqrMagnitude < 0.0001f) { end = origin; return false; }
        direction.Normalize();
        end = origin + direction * shot.Range; // 아무것도 안 맞으면 사거리 끝까지

        Transform ignore = shot.Source != null ? shot.Source.transform.root : null;

        if (shot.Pierce <= 0)
        {
            if (!TryClosestHit(origin, direction, shot.Range, 0f, ignore, out RaycastHit hit))
                return false;

            end = hit.point;
            Impact(hit.collider, hit.point, shot);
            return true;
        }

        // 관통 — 경로의 모든 히트를 거리순으로 밟는다
        int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, shot.Range,
                                            SweepLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hitBuffer, 0, count, hitByDistance);

        pulseSeen.Clear(); // 콜라이더 여러 개인 대상 중복 방지 (버퍼 재사용)
        int applied = 0;
        for (int i = 0; i < count; i++)
        {
            var h = hitBuffer[i];
            if (ignore != null && h.transform.IsChildOf(ignore)) continue;

            if ((shot.TargetMask & (1 << h.collider.gameObject.layer)) == 0)
            {
                end = h.point; // 차폐물 — 빔 종료
                break;
            }

            Entity entity = h.collider.GetComponentInParent<Entity>();
            if (entity == null || entity.IsDead || !pulseSeen.Add(entity)) continue;

            entity.ApplyEffects(shot.Effects, shot.Source, h.point);
            PlayEffect(shot.HitEffect, h.point, Quaternion.identity); // 뚫린 대상마다 착탄 연출
            applied++;
            if (applied > shot.Pierce)
            {
                end = h.point; // 관통력 소진 — 마지막 대상에서 멈춘다
                break;
            }
        }
        return applied > 0;
    }

    private class HitDistanceComparer : IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }
    private static readonly HitDistanceComparer hitByDistance = new HitDistanceComparer();

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
                                         SweepLayers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, hitBuffer, distance,
                                      SweepLayers, QueryTriggerInteraction.Ignore);

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
    /// 착탄 이펙트(탄약의 hitEffectPrefab)는 벽에 맞아도 재생된다 — 명중 여부와 무관한 연출.
    /// </summary>
    public static void Impact(Collider hit, Vector3 point, in ProjectileShot shot)
    {
        PlayEffect(shot.HitEffect, point, Quaternion.identity);

        if (shot.ExplosionRadius > 0f) Pulse(point, shot.ExplosionRadius, shot);
        else ApplyHit(hit, point, shot);
    }

    // ── 연출 (파티클) ───────────────────────────────────────────

    /// <summary>
    /// 파티클 프리팹 1회 재생 — 탄과 같은 풀에서 꺼내 쓰고 파티클이 끝나면 자동 반환한다
    /// (연사 중 초당 수십 번 재생돼도 GC 쓰레기가 없다).
    /// parent를 주면 붙어서 따라간다 (총구 화염이 총과 함께 움직이도록).
    /// </summary>
    public static void PlayEffect(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null) return;

        var pool = GetPool(prefab);
        var go = pool.Get();

        // 풀에 든 인스턴스가 총구에 붙은 채 무기·씬과 함께 파괴됐을 수 있다(반환 시점에
        // 부모를 되돌릴 수 없기 때문이다 — 아래 GetPool 참고). 죽은 참조는 버리고 다시 꺼낸다.
        if (go == null) go = pool.Get();
        if (go == null) return;

        // 부모는 꺼낼 때마다 다시 잡는다 — 반환 쪽에서는 잡을 수 없다.
        go.transform.SetParent(parent != null ? parent : PoolRoot(), false);
        go.transform.SetPositionAndRotation(position, rotation);

        // 반환기는 시스템이 붙인다 — 아트 프리팹은 파티클만 갖고 있으면 된다.
        // 풀 배선은 매번 해야 한다: 프리팹에 PooledEffect가 미리 달려 있으면 여기를 건너뛰어
        // 풀을 모르는 채 스스로 Destroy해, 풀에 죽은 참조가 남는다.
        var effect = go.GetComponent<PooledEffect>();
        if (effect == null) effect = go.AddComponent<PooledEffect>();
        effect.SetPool(pool);
        effect.Play();
    }

    /// <summary>명중 처리 — 맞은 것이 대상 레이어의 Entity면 실린 효과를 적용한다.</summary>
    public static void ApplyHit(Collider hit, Vector3 point, in ProjectileShot shot)
    {
        if (shot.TargetMask == 0) return;
        if ((shot.TargetMask & (1 << hit.gameObject.layer)) == 0) return;

        Entity entity = hit.GetComponentInParent<Entity>();
        if (entity != null && !entity.IsDead)
        {
            entity.ApplyEffects(shot.Effects, shot.Source, point);
            if (shot.Source != null && shot.Source.GetComponent<PlayerController>() != null)
            {
                CombatEvents.OnPlayerHitEnemy?.Invoke();
            }
        }
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

        bool playerHitTriggered = false;

        for (int i = 0; i < count; i++)
        {
            Entity entity = pulseBuffer[i].GetComponentInParent<Entity>();
            if (entity == null || entity.IsDead) continue;
            if (!pulseSeen.Add(entity)) continue;   // 콜라이더 여러 개인 대상 중복 방지

            entity.ApplyEffects(shot.Effects, shot.Source, origin);
            applied++;

            if (!playerHitTriggered && shot.Source != null && shot.Source.GetComponent<PlayerController>() != null)
            {
                CombatEvents.OnPlayerHitEnemy?.Invoke();
                playerHitTriggered = true;
            }

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

    /// <summary>발사체 인스턴스 하나를 풀에서 꺼내 날린다. direction은 정규화돼 있지 않아도 된다.</summary>
    private static Bullet Spawn(GameObject prefab, Vector3 position, Vector3 direction, in ProjectileShot shot)
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
            // 여기서 부모를 되돌리지 않는다. 총구에 붙은 이펙트는 무기를 집어넣을 때
            // 반환되는데, 그 순간은 Unity가 계층을 비활성화하는 중이라 SetParent가
            // 거부당하며 에러가 난다("Cannot set the parent ... while activating or
            // deactivating"). 부모는 PlayEffect가 꺼낼 때마다 다시 잡는다.
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
