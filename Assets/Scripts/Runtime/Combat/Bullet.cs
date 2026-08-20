using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 발사체 — ProjectileSystem.Fire로만 발사한다.
///
/// 이동은 매 프레임 "직전 위치 → 다음 위치" 스피어캐스트 스윕이다. 트리거/충돌 이벤트에
/// 기대지 않는 이유: 빠른 탄은 한 프레임에 콜라이더보다 멀리 점프해 얇은 대상을
/// 관통(터널링)한다 — 속도 50이면 60fps에서 프레임당 0.83m. 스윕은 경로 전체를 검사하므로
/// 어떤 속도에서도 명중이 보장되고, 착탄 처리(Impact)는 히트스캔과 같은 코드를 쓴다.
/// 궤적은 속도 벡터의 적분이다 — 중력 있는 탄(유탄)은 스윕 방향이 매 프레임 함께 굽는다.
///
/// 발사자 자신(Source 소속 콜라이더)은 스윕에서 걸러진다 — 타워 총구가 자기 콜라이더
/// 안쪽에서 시작해도 자기 몸에 맞지 않고, 구 방식의 발사마다 IgnoreCollision 순회도 필요 없다.
/// </summary>
public class Bullet : MonoBehaviour
{
    private IObjectPool<GameObject> managedPool;
    private ProjectileShot shot;
    private Transform shooterRoot; // 발사자 무시 판정 기준 (Source의 루트)
    private Vector3 start;
    private Vector3 velocity;      // 현재 속도 — 중력탄은 매 프레임 아래로 굽는다
    private float age;
    private float sweepRadius;

    // 조준선 합류(FPS 뷰모델) — 총구에서 태어난 탄이 조준선 위 합류점까지 날아간 뒤
    // 조준선 방향으로 직진한다. 합류 전에는 중력을 미루고(구간이 1m 남짓), 합류 후 정상 비행.
    private bool joining;
    private Vector3 joinPoint;
    private Vector3 joinDirection;

    private ParticleSystem[] particles;
    private TrailRenderer[] trails;
    private MeshRenderer[] meshes;

    // 시체 모드 — 착탄한 탄을 즉시 반환하지 않고, 궤적(트레일)과 파티클 여운이
    // 눈에서 사라질 때까지 그 자리에 세워 둔다. 즉시 반환(SetActive(false))하면
    // 총구 코앞에 맞은 탄의 꼬리가 태어나자마자 뚝 끊겨 없던 것처럼 보인다.
    private bool dying;
    private float dieAt;

    private void Awake()
    {
        // 탄의 단면 반경 — 콜라이더 가로/세로 절반 중 큰 쪽. 콜라이더가 없으면 가는 탄 취급.
        // (콜라이더는 이제 판정에 쓰이지 않지만, 크기 정보원으로는 유효하다)
        var col = GetComponentInChildren<Collider>();
        sweepRadius = col != null
            ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.y)
            : 0.05f;

        // 파티클 기반 탄 프리팹(아트 에셋)을 풀에서 재사용하기 위한 캐시 — Launch가 되살린다
        particles = GetComponentsInChildren<ParticleSystem>(true);
        trails = GetComponentsInChildren<TrailRenderer>(true);
        meshes = GetComponentsInChildren<MeshRenderer>(true);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 탄이 자기 자신·다른 탄에 맞아 태어난 자리(총구)에서 즉시 터지지 않도록 두 겹으로 막는다.
    /// 아트 프리팹을 새로 만들어 꽂아도 여기서 바로잡히므로 아트 설정에 의존하지 않는다.
    ///   레이어(Bullet)  — ProjectileSystem의 스윕 대상에서 제외된다 (판정 쪽 방어선)
    ///   콜라이더 트리거 — 물리 충돌 자체가 일어나지 않는다 (물리 쪽 방어선).
    ///                     콜라이더는 판정에 쓰이지 않고 탄 굵기(스윕 반경)의 정보원일 뿐이다.
    /// </summary>
    private void OnValidate()
    {
        int layer = LayerMask.NameToLayer("Bullet");
        if (layer >= 0)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.layer == layer) continue;
                t.gameObject.layer = layer;
                UnityEditor.EditorUtility.SetDirty(t.gameObject);
            }
        }

        foreach (var c in GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger) continue;
            c.isTrigger = true;
            UnityEditor.EditorUtility.SetDirty(c);
        }
    }
#endif

    public void SetPool(IObjectPool<GameObject> pool) => managedPool = pool;

    /// <summary>ProjectileSystem.Fire가 호출한다. 위치·회전은 이미 잡힌 상태.</summary>
    public void Launch(in ProjectileShot shot)
    {
        this.shot = shot;
        shooterRoot = shot.Source != null ? shot.Source.transform.root : null;
        start = transform.position;
        velocity = transform.forward * shot.Speed;
        age = 0f;
        joining = false; // 풀 재사용 — 이전 발사의 합류 상태 초기화
        dying = false;   // 시체로 반환됐던 탄 되살리기
        for (int i = 0; i < meshes.Length; i++) meshes[i].enabled = true;

        // 파티클·트레일은 SetActive(true)만으로 되살아나지 않는다 (playOnAwake는 최초 1회뿐).
        // 되살리지 않으면 풀의 두 번째 발사부터 탄이 날아가도 아무것도 보이지 않는다.
        // 트레일도 지워야 한다 — 이전 발사의 궤적이 남아 탄이 순간이동한 것처럼 보인다.
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Clear(true);
            particles[i].Play(true);
        }
        for (int i = 0; i < trails.Length; i++) trails[i].Clear();
    }

    /// <summary>Launch 직후 호출 — 합류점까지 날아간 뒤 조준선 방향으로 꺾는다.</summary>
    public void SetJoin(Vector3 point, Vector3 finalDirection)
    {
        joining = true;
        joinPoint = point;
        joinDirection = finalDirection;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // 시체 모드 — 움직임·판정 없이 여운(트레일 페이드, 남은 파티클)만 재생하다 반환
        if (dying)
        {
            age += dt;
            if (age >= dieAt) ReleaseToPool();
            return;
        }

        // 조준선 합류 구간 — 합류점을 향해 날고, 이번 프레임에 닿으면 조준선 방향으로 꺾는다
        if (joining)
        {
            Vector3 toJoin = joinPoint - transform.position;
            if (toJoin.magnitude <= shot.Speed * dt + 0.001f)
            {
                transform.position = joinPoint;
                velocity = joinDirection * shot.Speed;
                transform.rotation = Quaternion.LookRotation(joinDirection);
                joining = false;
            }
            else
            {
                velocity = toJoin.normalized * shot.Speed;
            }
        }

        // 중력 적분 — 탄약이 gravity를 가지면(유탄) 속도가 매 프레임 아래로 굽어 포물선이 된다.
        // 합류 전(1m 남짓)에는 미룬다 — 합류점 도달이 정확해야 조준선이 어긋나지 않는다.
        if (!joining && shot.Gravity > 0f) velocity += Vector3.down * (shot.Gravity * dt);

        Vector3 pos = transform.position;
        Vector3 step = velocity * dt;
        float dist = step.magnitude;

        // TargetMask 0 = 연출탄(히트스캔 트레이서) — 판정은 이미 끝났으니 스윕도 하지 않는다.
        // 관통 빔의 트레이서가 뚫린 대상 몸에 걸려 멈추면 안 된다; Range가 곧 판정된 빔 길이다.
        if (shot.TargetMask != 0 && dist > 0f &&
            ProjectileSystem.TryClosestHit(pos, step / dist, dist, sweepRadius, shooterRoot, out RaycastHit hit))
        {
            ProjectileSystem.Impact(hit.collider, hit.point, shot); // 폭발탄은 착탄점 Pulse
            transform.position = hit.point;   // 여운은 착탄점에 남는다
            BeginDeath();
            return;
        }

        transform.position = pos + step;
        if (shot.Gravity > 0f && velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(velocity); // 탄두가 궤적을 따라 기운다

        // 수명·사거리 초과 시 소멸. 곡사탄은 수평 거리로 재야 한다 — 고각 궤적의 정점이
        // 직선 거리 기준을 넘겨 공중에서 사라지면 안 되니까. 폭발탄은 만료 순간에도 터진다.
        age += Time.deltaTime;
        Vector3 travelled = transform.position - start;
        if (shot.Gravity > 0f) travelled.y = 0f;
        if (age >= shot.Lifetime || travelled.sqrMagnitude >= shot.Range * shot.Range)
        {
            if (shot.ExplosionRadius > 0f)
            {
                ProjectileSystem.PlayEffect(shot.HitEffect, transform.position, Quaternion.identity);
                ProjectileSystem.Pulse(transform.position, shot.ExplosionRadius, shot);
            }
            BeginDeath();
        }
    }

    /// <summary>
    /// 탄을 죽이되 바로 반환하지 않는다 — 새 파티클 방출만 멈추고(이미 나온 것은 살게),
    /// 탄두 메시만 숨긴 채 트레일·파티클 여운이 제일 오래 남는 시간만큼 기다린다.
    /// 풀은 반환 전까지 이 인스턴스를 다시 내주지 않으므로 재사용과 충돌하지 않는다.
    /// </summary>
    private void BeginDeath()
    {
        if (dying) return;
        dying = true;
        velocity = Vector3.zero;

        float linger = 0f;
        for (int i = 0; i < meshes.Length; i++) meshes[i].enabled = false;
        for (int i = 0; i < trails.Length; i++) linger = Mathf.Max(linger, trails[i].time);
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            linger = Mathf.Max(linger, particles[i].main.startLifetime.constantMax);
        }

        age = 0f;
        dieAt = Mathf.Min(linger, 2f);   // 안전 상한 — 여운이 아무리 길어도 풀을 2초 넘게 붙잡지 않는다
        if (dieAt <= 0f) ReleaseToPool();
    }

    private void ReleaseToPool()
    {
        if (!gameObject.activeSelf) return;

        if (managedPool != null) managedPool.Release(gameObject);
        else Destroy(gameObject); // 풀 밖에서 태어난 총알(직접 Instantiate)은 직접 소멸
    }
}
