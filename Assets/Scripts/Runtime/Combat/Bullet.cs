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

    private void Awake()
    {
        // 탄의 단면 반경 — 콜라이더 가로/세로 절반 중 큰 쪽. 콜라이더가 없으면 가는 탄 취급.
        // (콜라이더는 이제 판정에 쓰이지 않지만, 크기 정보원으로는 유효하다)
        var col = GetComponentInChildren<Collider>();
        sweepRadius = col != null
            ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.y)
            : 0.05f;
    }

    public void SetPool(IObjectPool<GameObject> pool) => managedPool = pool;

    /// <summary>ProjectileSystem.Fire가 호출한다. 위치·회전은 이미 잡힌 상태.</summary>
    public void Launch(in ProjectileShot shot)
    {
        this.shot = shot;
        shooterRoot = shot.Source != null ? shot.Source.transform.root : null;
        start = transform.position;
        velocity = transform.forward * shot.Speed;
        age = 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // 중력 적분 — 탄약이 gravity를 가지면(유탄) 속도가 매 프레임 아래로 굽어 포물선이 된다
        if (shot.Gravity > 0f) velocity += Vector3.down * (shot.Gravity * dt);

        Vector3 pos = transform.position;
        Vector3 step = velocity * dt;
        float dist = step.magnitude;

        if (dist > 0f &&
            ProjectileSystem.TryClosestHit(pos, step / dist, dist, sweepRadius, shooterRoot, out RaycastHit hit))
        {
            ProjectileSystem.Impact(hit.collider, hit.point, shot); // 폭발탄은 착탄점 Pulse
            ReleaseToPool();
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
                ProjectileSystem.Pulse(transform.position, shot.ExplosionRadius, shot);
            ReleaseToPool();
        }
    }

    private void ReleaseToPool()
    {
        if (!gameObject.activeSelf) return;

        if (managedPool != null) managedPool.Release(gameObject);
        else Destroy(gameObject); // 풀 밖에서 태어난 총알(직접 Instantiate)은 직접 소멸
    }
}
