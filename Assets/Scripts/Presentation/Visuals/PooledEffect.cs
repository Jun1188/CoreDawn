using UnityEngine;
using UnityEngine.Pool;

namespace CoreDawn.Visuals
{
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
        private bool live;   // 풀에서 꺼내 재생 중인가 — 이중 반환(풀의 collectionCheck)을 막는다

        public void SetPool(IObjectPool<GameObject> pool) => managedPool = pool;

        /// <summary>풀에서 꺼낸 직후 호출 — 파티클을 처음부터 다시 재생한다.</summary>
        public void Play()
        {
            if (systems == null) systems = GetComponentsInChildren<ParticleSystem>(true);
            live = true;

            if (lifetime < 0f)
            {
                lifetime = 0f;
                foreach (var ps in systems)
                {
                    var main = ps.main;

                    // 풀 오브젝트에 playOnAwake는 재앙이다 — 활성화되는 것만으로 터진다.
                    // 총구 화염은 총의 자식이라, 무기를 다시 꺼내는 순간 쏘지도 않았는데
                    // 격발 이펙트가 재생됐다. 재생 시점은 오직 Play()가 정한다.
                    main.playOnAwake = false;

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
            if (age >= lifetime) Recycle();
        }

        /// <summary>
        /// 부모가 꺼지면 우리 Update도 멈춘다 — 그대로 두면 영영 반환되지 않는다.
        ///
        /// 총구 화염은 총구(무기)의 자식으로 붙어 총과 함께 움직인다. 그래서 무기를 집어넣는
        /// 순간(SetActive(false)) 재생 중이던 이펙트가 무기 자식으로 얼어붙고, 다음에 그 무기를
        /// 꺼낼 때 <b>파티클의 playOnAwake가 혼자 발동해 쏘지도 않았는데 총구 화염이 터졌다.</b>
        /// 여기서 회수하면 이펙트는 풀 루트로 돌아가므로 무기와 함께 되살아날 일이 없다.
        /// </summary>
        private void OnDisable()
        {
            // 재생 중이던 입자를 지운다 — 남겨두면 다시 켜질 때 멈춰 있던 화염이 그대로 떠오른다.
            if (systems != null)
                foreach (var ps in systems)
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // 풀 밖 인스턴스는 여기서 Destroy하지 않는다 — 비활성화·씬 정리 중의 파괴는 위험하다.
            // 그쪽은 Update가 수명을 채우고 처리한다.
            if (!live || managedPool == null) return;
            live = false;
            managedPool.Release(gameObject);
        }

        private void Recycle()
        {
            if (!live) return;
            live = false;

            if (managedPool != null) managedPool.Release(gameObject);
            else Destroy(gameObject); // 풀 밖에서 태어난 경우
        }
    }
}
