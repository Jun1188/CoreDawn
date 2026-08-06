using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    [Header("Weapon Ob List")]
    public Gun[] weapons; // 하위에 있는 Gun1, Gun2 등을 모두 드래그 앤 드롭
    private int currentIndex = -1; // -1이면 현재 맨손 상태

    // 모듈 참조 (Weapon_Holder에 붙어있는 스크립트들)
    public WeaponMotionManager motionManager;
    public WeaponADS adsModule;
    public WeaponKickback kickbackModule;

    public ProceduralRecoil recoilManager;

    public Gun CurrentWeapon
    {
        get
        {
            if (currentIndex >= 0 && currentIndex < weapons.Length)
                return weapons[currentIndex];
            return null;
        }
    }

    private void Start()
    {
        // 시작할 때 모든 무기를 꺼둡니다 (맨손 상태로 시작)
        foreach (var weapon in weapons)
        {
            weapon.gameObject.SetActive(false);
            weapon.weaponManager = this; 
        }
    }

    // 사격/조준/재장전 입력(선입력 버퍼 포함)은 WeaponController(입력 파이프라인 어댑터)로 이관 —
    // 건설 모드·팝업 중 입력이 이곳까지 오지 않게 하기 위함. 이 클래스는 장착/교체만 소유한다.

    // ⭐️ [핵심] 인벤토리에서 GunData를 넘겨주면 해당 무기를 찾아 장착하는 함수
    public void EquipWeapon(GunData targetData)
    {
        if (targetData == null) return;

        // 매니저가 소지한 무기들을 탐색하며 GunData가 일치하는지 확인
        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i].gunData == targetData)
            {
                // 이미 들고 있는 무기라면 무시
                if (currentIndex == i) return;

                SwapTo(i);
                Debug.Log($"[무기 교체 완료] {targetData.gunName} 장착 (공격력: {targetData.damage})");
                return;
            }
        }

        // 루프를 다 돌았는데 못 찾았다면 에러 로그 (프리팹을 자식으로 안 넣은 경우)
        Debug.LogWarning($"[오류] {targetData.gunName} 데이터를 가진 무기 오브젝트가 WeaponHolder 하위에 없습니다!");
    }

    // 실제 무기 오브젝트를 껐다 켜는 내부 로직
    private void SwapTo(int newIndex)
    {
        // 기존 무기 끄기
        if (CurrentWeapon != null)
        {
            CurrentWeapon.gameObject.SetActive(false);
        }

        // 새 무기 켜기
        currentIndex = newIndex;

        Vector3 tempPos = motionManager.transform.localPosition;
        Quaternion tempRot = motionManager.transform.localRotation;

        motionManager.transform.localPosition = Vector3.zero;
        motionManager.transform.localRotation = Quaternion.identity;

        // 무기별로 다른 가늠자(SightPoint) 위치를 ADS 모듈에 갱신
        adsModule.SetupWeapon(weapons[currentIndex].sightPoint);

        // 원래 흔들리던 위치로 복구 (안 하면 스왑할 때 화면이 튐)
        motionManager.transform.localPosition = tempPos;
        motionManager.transform.localRotation = tempRot;

        CurrentWeapon.gameObject.SetActive(true);
    }

    // (선택) 무기 해제 기능이 필요할 경우
    public void UnequipWeapon()
    {
        if (CurrentWeapon != null)
        {
            CurrentWeapon.gameObject.SetActive(false);
        }
        currentIndex = -1;
        Debug.Log("[무기 해제] 현재 맨손 상태입니다.");
    }
}