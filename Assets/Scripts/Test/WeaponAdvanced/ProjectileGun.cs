using UnityEngine;
using UnityEngine.Pool;

public class ProjectileGun : WeaponBase
{
    private IObjectPool<GameObject> bulletPool;

    protected override void Awake()
    {
        base.Awake(); // 부모의 Awake(PlayerController 찾기) 실행
        InitializePool(); // 이 총기만의 전용 오브젝트 풀 생성
    }

    protected override void ExecuteFire()
    {
        Vector3 spawnPos = muzzlePoint != null ? muzzlePoint.position : transform.position;

        // ⭐️ 탄퍼짐 적용 (정면 방향에 랜덤한 구형 오차를 더함)
        Vector3 fireDirection = (muzzlePoint != null ? muzzlePoint.forward : transform.forward);
        fireDirection += Random.insideUnitSphere * (currentSpread / 100f); // 수치 보정

        Quaternion spawnRot = Quaternion.LookRotation(fireDirection);

        GameObject bullet = bulletPool.Get();
        bullet.transform.position = spawnPos;
        bullet.transform.rotation = spawnRot;
        // 효과는 총알이 명중 시 전달한다 — 대상은 Monster 레이어. 효과 미지정이면 damage만큼의 순수 피해.
        // 공격 버프는 발사 시점 기준 (총알이 날아가는 동안 버프가 끝나도 발사 때 배율 유지)
        float damage = gunData.damage * (OwnerEntity != null ? OwnerEntity.Effects.AttackMultiplier : 1f);
        bullet.GetComponent<Bullet>()?.Setup(gunData.bulletSpeed, 3f, damage, gunData.enemyLayer, gunData.range,
                                             gunData.attackEffects, OwnerEntity);
    }


    #region Object Pooling
    private void InitializePool()
    {
        if (gunData.bulletPrefab == null) return;

        bulletPool = new ObjectPool<GameObject>(
            createFunc: CreateBullet,
            actionOnGet: OnGetBullet,
            actionOnRelease: OnReleaseBullet,
            actionOnDestroy: OnDestroyBullet,
            collectionCheck: true,
            defaultCapacity: 20,
            maxSize: 50
        );
    }

    private GameObject CreateBullet()
    {
        GameObject bullet = Instantiate(gunData.bulletPrefab);
        Bullet bulletScript = bullet.GetComponent<Bullet>();
        bulletScript?.SetPool(bulletPool);
        return bullet;
    }
    private void OnGetBullet(GameObject bullet) => bullet.SetActive(true);
    private void OnReleaseBullet(GameObject bullet) => bullet.SetActive(false);
    private void OnDestroyBullet(GameObject bullet) => Destroy(bullet);
    #endregion
}