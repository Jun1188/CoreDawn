using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// 발사체 — 효과 전달의 주체.
/// 피격 판정은 맞은 쪽(Monster 등)이 아니라 총알이 수행한다:
/// 충돌 상대가 대상 레이어(targetMask)의 Entity면 실린 효과들을 적용(ApplyEffects) 후 소멸.
/// 효과가 없으면 damage만큼의 순수 피해다.
/// 플레이어 총(ProjectileGun 풀)과 타워(BattleTower, 풀 없이 Instantiate) 모두 이 클래스를 쓴다.
/// </summary>
public class Bullet : MonoBehaviour
{
    private IObjectPool<GameObject> managedPool;
    private float speed;
    private float lifetime;
    private float damage;
    private int targetMask; // 효과를 적용할 레이어 마스크 (0이면 적용 없음, 소멸만)
    private float range;
    private Vector3 start;
    private EffectSO[] effects; // 명중 시 적용할 효과 (null이면 순수 피해)
    private Entity source;      // 발사자 — 효과의 출처(Source)로 전달

    public void SetPool(IObjectPool<GameObject> pool)
    {
        managedPool = pool;
    }

    public void Setup(float speed, float lifetime, float damage = 0f, int targetMask = 0, float range = 10,
                      EffectSO[] effects = null, Entity source = null)
    {
        this.speed = speed;
        this.lifetime = lifetime;
        this.damage = damage;
        this.targetMask = targetMask;
        this.range = range;
        this.effects = effects;
        this.source = source;
        start = transform.position;

        // 이전 예치된 Invoke 취소 후 재등록
        CancelInvoke(nameof(ReleaseToPool));
        Invoke(nameof(ReleaseToPool), lifetime);
    }

    private void Update()
    {
        // 전방으로 비행
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
        if(Vector3.Distance(start, transform.position) >= range)
        {
            ReleaseToPool();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryApplyDamage(collision.collider);
        ReleaseToPool();
    }

    private void OnTriggerEnter(Collider other)
    {
        TryApplyDamage(other);
        ReleaseToPool();
    }

    // 대상 레이어의 Entity에게만 효과 적용 (콜라이더가 자식 모델에 있는 구조 지원)
    private void TryApplyDamage(Collider hit)
    {
        bool hasEffects = effects != null && effects.Length > 0;
        if ((damage <= 0f && !hasEffects) || targetMask == 0) return; // 피해 0이어도 감속탄 등 효과탄은 유효
        if ((targetMask & (1 << hit.gameObject.layer)) == 0) return;

        Entity entity = hit.GetComponentInParent<Entity>();
        if (entity != null && !entity.IsDead)
        {
            entity.ApplyEffects(effects, new EffectContext(source, damage, transform.position));
        }
    }

    private void ReleaseToPool()
    {
        if (!gameObject.activeSelf) return;

        if (managedPool != null) managedPool.Release(gameObject);
        else Destroy(gameObject); // 풀 없이 발사된 총알(타워 등)은 직접 소멸
    }
}
