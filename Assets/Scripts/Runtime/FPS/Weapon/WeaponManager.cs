using UnityEngine;

/// <summary>
/// 무기 스택의 허브 — 장착/교체와 조준 상태를 소유하고, 발사 연출을 팬아웃한다.
///
/// 접근 규칙: 외부(입력 WeaponController, UI, 인벤토리)는 이 클래스의 공개 API만 쓴다.
/// 연출 모듈(ADS·킥백·리코일·모션)은 전부 private — 모듈 필드를 밖에서 찌르는 사슬
/// (컨트롤러 → adsModule.isAiming 등)을 만들지 않는다.
/// Gun은 연출을 모른 채 Fired 이벤트만 쏘고, 여기서 반동·킥백·셰이크로 반응한다.
/// </summary>
public class WeaponManager : MonoBehaviour
{
    [Header("Weapon Ob List")]
    [SerializeField] private Gun[] weapons; // 하위에 있는 Gun1, Gun2 등을 모두 드래그 앤 드롭

    [Header("연출 모듈 (Weapon_Holder)")]
    [SerializeField] private WeaponMotionManager motionManager;
    [SerializeField] private WeaponADS adsModule;
    [SerializeField] private WeaponKickback kickbackModule;
    [SerializeField] private ProceduralRecoil recoilManager;

    private int currentIndex = -1; // -1이면 현재 맨손 상태

    public Gun CurrentWeapon =>
        currentIndex >= 0 && currentIndex < weapons.Length ? weapons[currentIndex] : null;

    /// <summary>정조준 중인가 — 조준 상태의 원본. 연출(ADS·킥백)은 이 값을 받아 반응한다.</summary>
    public bool IsAiming { get; private set; }

    private void Start()
    {
        // 시작할 때 모든 무기를 꺼둔다 (맨손 상태로 시작)
        foreach (var weapon in weapons)
        {
            weapon.gameObject.SetActive(false);
            weapon.Fired += OnWeaponFired;
        }
    }

    private void OnDestroy()
    {
        foreach (var weapon in weapons)
            if (weapon != null) weapon.Fired -= OnWeaponFired;
    }

    // ── 공개 API ───────────────────────────────────────────────

    /// <summary>인벤토리에서 GunData를 넘겨주면 해당 무기를 찾아 장착한다.</summary>
    public void EquipWeapon(GunData targetData)
    {
        if (targetData == null) return;

        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i].gunData != targetData) continue;
            if (currentIndex == i) return; // 이미 들고 있는 무기

            SwapTo(i);
            return;
        }

        Debug.LogWarning($"[WeaponManager] {targetData.displayName} 데이터를 가진 무기 오브젝트가 WeaponHolder 하위에 없습니다!");
    }

    public void UnequipWeapon()
    {
        if (CurrentWeapon != null) CurrentWeapon.gameObject.SetActive(false);
        currentIndex = -1;
        SetAiming(false);
    }

    /// <summary>조준 시작/해제 — 입력(WeaponController)이 호출한다. 연출 반영은 여기서 전파.</summary>
    public void SetAiming(bool aiming)
    {
        if (CurrentWeapon == null) aiming = false;
        IsAiming = aiming;
        if (adsModule != null) adsModule.SetAiming(aiming);
    }

    // ── 내부 ───────────────────────────────────────────────────

    // 실제 무기 오브젝트를 껐다 켜는 내부 로직
    private void SwapTo(int newIndex)
    {
        if (CurrentWeapon != null) CurrentWeapon.gameObject.SetActive(false);

        currentIndex = newIndex;
        var weapon = weapons[currentIndex];

        // 가늠자 오프셋 계산은 홀더가 원점일 때 해야 한다 — 스왑 순간의 흔들림(모션 오프셋)이
        // 섞이지 않게 잠시 초기화했다가 복구한다 (안 하면 스왑할 때 화면이 튐)
        if (motionManager != null && adsModule != null)
        {
            Vector3 tempPos = motionManager.transform.localPosition;
            Quaternion tempRot = motionManager.transform.localRotation;
            motionManager.transform.localPosition = Vector3.zero;
            motionManager.transform.localRotation = Quaternion.identity;

            adsModule.SetupWeapon(weapon.sightPoint, weapon.gunData.zoomFOV);

            motionManager.transform.localPosition = tempPos;
            motionManager.transform.localRotation = tempRot;
        }

        weapon.gameObject.SetActive(true);
    }

    // 발사 연출 팬아웃 — Gun은 "쐈다"만 알리고, 무엇이 흔들릴지는 여기서 정한다
    private void OnWeaponFired(Gun weapon)
    {
        var data = weapon.gunData;

        if (CameraShakeManager.Instance != null)
            CameraShakeManager.Instance.ShakeOnPlayerShoot(data.BaseDamage);
        if (recoilManager != null)
            recoilManager.FireRecoil(data.xRecoil, data.yRecoil, data.zRecoil);
        if (kickbackModule != null)
            kickbackModule.Fire(data.visualKickbackZ, data.visualKickbackRot, IsAiming);
    }
}
