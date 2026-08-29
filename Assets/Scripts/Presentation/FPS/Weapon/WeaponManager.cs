using UnityEngine;
using CoreDawn.Data;

namespace CoreDawn.FPS
{
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
        [SerializeField] private WeaponSwing swingModule;

        private int currentIndex = -1; // -1이면 현재 맨손 상태

        public Gun CurrentWeapon =>
            currentIndex >= 0 && currentIndex < weapons.Length ? weapons[currentIndex] : null;

        /// <summary>정조준 중인가 — 조준 상태의 원본. 연출(ADS·킥백)은 이 값을 받아 반응한다.</summary>
        public bool IsAiming { get; private set; }

        // ── 세이브 표면 ───────────────────────────────────────────────
        //
        // weapons 배열을 밖으로 열지 않고 여기서 훑는다 — 이 클래스의 접근 규칙(공개 API만)을
        // 세이브 때문에 깨면, 무기 스택 구성이 바뀔 때마다 세이브 모듈까지 따라 고쳐야 한다.
        // 순서는 인스펙터에 꽂힌 배열 순서다: 무기를 중간에 끼워 넣으면 옛 세이브의 탄수가
        // 한 칸씩 밀린다 — 탄수뿐이라 치명적이지 않지만, 무기 목록을 손볼 때 알고 있을 것.

        /// <summary>세이브 저장 전용 — 무기별 장전 탄수를 배열 순서대로 읽는다.</summary>
        public int[] CaptureAmmo()
        {
            if (weapons == null) return System.Array.Empty<int>();

            var result = new int[weapons.Length];
            for (int i = 0; i < weapons.Length; i++)
                result[i] = weapons[i] != null ? weapons[i].CurrentAmmo : 0;
            return result;
        }

        /// <summary>세이브 복원 전용 — <see cref="CaptureAmmo"/>가 남긴 순서대로 장전 탄수를 되돌린다.</summary>
        public void RestoreAmmo(System.Collections.Generic.IReadOnlyList<int> ammo)
        {
            if (weapons == null || ammo == null) return;

            for (int i = 0; i < weapons.Length && i < ammo.Count; i++)
                if (weapons[i] != null) weapons[i].RestoreAmmo(ammo[i]);
        }

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

        /// <summary>
        /// 지금 든 무기로 조준할 수 있는가 — 가늠자가 없는 무기(근접)는 false.
        /// 입력(WeaponController)은 이 값이 false면 조준 입력을 소비하지 않고 흘려보낸다.
        /// </summary>
        public bool CanAim => CurrentWeapon != null && CurrentWeapon.gunData != null && !CurrentWeapon.gunData.blockAim;

        /// <summary>
        /// 조준 시작/해제 — 입력(WeaponController)이 호출한다. 연출 반영은 여기서 전파.
        /// 조준 불가 무기는 여기서 한 번에 막는다: 줌(FOV)·이동속도 감속·스웨이 억제·달리기 금지가
        /// 전부 WeaponADS가 게시하는 AimWeight 하나를 보고 있어서, 이 관문이면 전부 함께 멈춘다.
        /// </summary>
        public void SetAiming(bool aiming)
        {
            if (!CanAim) aiming = false;
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

                adsModule.SetupWeapon(weapon.sightPoint, weapon.gunData.zoomMultiplier);

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
            // 근접무기는 킥백(뒤로 밀림) 대신 호를 그린다 — 어느 쪽인지는 무기 수치가 정한다
            // (swingTime 0 = 스윙 없음, 킥백 0 = 반동 없음). 코드에는 총/근접 분기가 없다.
            if (swingModule != null && data.swingTime > 0f)
                swingModule.Swing(data.swingTime, data.swingRotation, data.swingPosition,
                                  data.swingWindup, data.swingAlternate);
        }
    }
}
