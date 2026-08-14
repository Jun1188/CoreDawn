using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 일회성 파티클 연출의 풀 반환기 — ProjectileSystem.PlayEffect가 붙인다(프리팹에 미리 달 필요 없다).
///
/// 총구 화염·착탄 이펙트는 연사 중 초당 수십 번 재생되므로 Instantiate/Destroy면
/// 그때마다 GC 쓰레기가 쌓인다. 탄(Bullet)과 같은 풀을 쓰되, 소멸 조건만 다르다:
/// 탄은 명중·사거리, 이펙트는 "파티클이 다 끝났을 때".
///
/// 수명은 최초 재생 때 한 번만 계산해 캐시한다 — 같은 프리팹은 항상 같은 길이다.
/// </summary>
[DisallowMultipleComponent]
public class PooledEffect : MonoBehaviour
{
    private IObjectPool<GameObject> managedPool;
    private ParticleSystem[] systems;
    private float lifetime = -1f;
    private float age;

    public void SetPool(IObjectPool<GameObject> pool) => managedPool = pool;

    /// <summary>풀에서 꺼낸 직후 호출 — 파티클을 처음부터 다시 재생한다.</summary>
    public void Play()
    {
        if (systems == null) systems = GetComponentsInChildren<ParticleSystem>(true);

        if (lifetime < 0f)
        {
            lifetime = 0f;
            foreach (var ps in systems)
            {
                var main = ps.main;
                // 루프 파티클은 스스로 끝나지 않는다 — 아래 기본값(2초)이 회수한다
                if (main.loop) continue;
                lifetime = Mathf.Max(lifetime, main.duration + main.startLifetime.constantMax);
            }
            if (lifetime <= 0f) lifetime = 2f;
        }

        age = 0f;
        foreach (var ps in systems)
        {
            ps.Clear(true);   // 풀 재사용 — 이전 재생의 잔여 입자를 지운다
            ps.Play(true);
        }
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age < lifetime) return;

        if (managedPool != null) managedPool.Release(gameObject);
        else Destroy(gameObject); // 풀 밖에서 태어난 경우
    }
}
