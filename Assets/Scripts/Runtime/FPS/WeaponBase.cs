using System.Collections;
using UnityEngine;

public abstract class WeaponBase : MonoBehaviour
{
    [Header("Core References")]
    public GunData gunData;
    public Transform muzzlePoint;
    protected PlayerController playerController;
    [HideInInspector]
    public WeaponManager weaponManager;

    [Header("ADS Settings")]
    public Transform sightPoint; // ⭐️ 추가: 이 총의 가늠자(눈 위치) 앵커
    public float zoomFOV = 50f;  // ⭐️ 추가: 이 총을 정조준했을 때의 시야각

    [Header("Current States")]
    protected int currentAmmo;

    /// <summary>현재 장전 수 — HUD 표시용 읽기 전용 (SCR-02).</summary>
    public int CurrentAmmo => currentAmmo;

    /// <summary>세이브 복원 전용 — 장전된 탄수를 되돌린다. 재장전 중이었다면 그 상태는 취소한다.</summary>
    public void RestoreAmmo(int ammo)
    {
        currentAmmo = Mathf.Max(0, ammo);
        isReloading = false;
    }

    public bool isReloading = false;
    protected float lastFireTime = 0f;
    protected float currentSpread;
    protected Rigidbody playerRb; // 플레이어의 속도 측정을 위함




    protected virtual void Awake()
    {
        playerController = GetComponentInParent<PlayerController>();
        playerRb = GetComponentInParent<Rigidbody>();
    }

    protected virtual void Start()
    {
        currentAmmo = gunData.magSize;
    }

    protected virtual void Update()
    {
        // ⭐️ 안 쏠 때는 에임이 다시 모임 (이동 속도에 따라 기본 탄퍼짐 증가)
        float speedFactor = (playerRb != null && playerRb.linearVelocity.magnitude > 1f) ? 2f : 1f; // 달리면 2배
        float targetSpread = gunData.baseSpread * speedFactor;

        currentSpread = Mathf.Lerp(currentSpread, targetSpread, Time.deltaTime * gunData.spreadRecoveryRate);
    }

    protected virtual void OnEnable()
    {
        // 무기를 스왑해서 꺼낼 때마다 상태 초기화
        isReloading = false;
    }

    protected virtual void OnDisable()
    {
        // 무기를 집어넣을 때 코루틴 안전하게 정지
        StopAllCoroutines();
    }

    // 매니저가 호출하는 사격 시도 함수
    public virtual bool TryFire()
    {
        if (isReloading) return false;

        // 탄약 부족 처리
        if (currentAmmo <= 0)
        {
            StartReload();
            return false;
        }

        // 연사속도 제어
        if (Time.time < lastFireTime + gunData.fireRate) return false;

        // 실제 사격 로직 실행 (탄약 차감, 쿨타임 갱신)
        currentAmmo--;
        lastFireTime = Time.time;

        currentSpread = Mathf.Min(currentSpread + gunData.spreadIncreasePerShot, gunData.maxSpread);

        ExecuteFire(); // 자식 클래스에서 구현된 진짜 발사(레이캐스트 or 총알생성) 실행
        ApplyRecoil();


        return true;
    }

    // 자식 클래스(ProjectileGun 등)에서 반드시 구현해야 하는 발사 로직
    protected abstract void ExecuteFire();

    protected void ApplyRecoil()
    {
        CameraShakeManager.Instance.ShakeOnPlayerShoot(gunData.damage);
        weaponManager.recoilManager.FireRecoil(gunData.xRecoil, gunData.yRecoil, gunData.zRecoil);
        bool currentAimState = weaponManager.adsModule.isAiming; // ADS 모듈에서 현재 조준 상태 가져오기
        weaponManager.kickbackModule.Fire(gunData.visualKickbackZ, gunData.visualKickbackRot, currentAimState);
    }

    public void StartReload()
    {
        if (isReloading || currentAmmo == gunData.magSize || !gameObject.activeSelf) return;
        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;
        // 필요 시 애니메이션 트리거 호출
        // anim.SetTrigger("Reload");

        yield return new WaitForSeconds(gunData.reloadTime);

        currentAmmo = gunData.magSize;
        isReloading = false;
        Debug.Log($"{gunData.gunName} 재장전 완료!");
    }
}