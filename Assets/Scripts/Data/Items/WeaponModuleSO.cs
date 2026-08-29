using UnityEngine;
using CoreDawn.FPS;

namespace CoreDawn.Data
{
    /// <summary>
    /// 무기 모듈 — 이 아이템을 핫바에서 선택하면 어느 총(GunData)이 장착되는지를 정의한다
    /// (구 WeaponItemSO의 대체).
    ///
    /// 아이템(인벤토리에 든 것)과 씬의 Gun 오브젝트(손에 들리는 것)는 다른 존재고,
    /// GunData가 그 둘을 잇는 매칭 키다 — InventoryManager가 이 모듈의 gun으로
    /// WeaponManager.EquipWeapon을 부르면, 매니저가 같은 GunData를 참조하는
    /// 씬 Gun을 찾아 활성화한다.
    /// </summary>
    public class WeaponModuleSO : ItemModuleSO
    {
        [Tooltip("장착할 총 데이터. json의 gun 필드(예: \"Gun:Rifle\")로 임포터가 배선한다.")]
        public GunData gun;
    }
}
