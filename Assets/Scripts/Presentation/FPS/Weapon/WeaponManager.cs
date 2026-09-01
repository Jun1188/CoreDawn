using UnityEngine;
using CoreDawn.Combat;
using CoreDawn.Data;
using CoreDawn.Inventories;
using CoreDawn.Sim;

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
        [Header("조립 — 총은 팩 정의(guns.view)에서 만든다")]
        [Tooltip("총 오브젝트를 세울 부모. 비우면 이 오브젝트(Weapon_Holder).")]
        [SerializeField] private Transform gunRoot;
        [Tooltip("탄이 맞힐 레이어 — 모든 총이 같다(뷰 값).")]
        [SerializeField] private LayerMask enemyLayer;
        private Gun current;   // 지금 든 총 — 장착할 때 정의에서 조립하고, 내리면 지운다

        [Header("연출 모듈 (Weapon_Holder)")]
        [SerializeField] private WeaponMotionManager motionManager;
        [SerializeField] private WeaponADS adsModule;
        [SerializeField] private WeaponKickback kickbackModule;
        [SerializeField] private ProceduralRecoil recoilManager;
        [SerializeField] private WeaponSwing swingModule;


        public Gun CurrentWeapon => current;

        /// <summary>정조준 중인가 — 조준 상태의 원본. 연출(ADS·킥백)은 이 값을 받아 반응한다.</summary>
        public bool IsAiming { get; private set; }

        /// <summary>소지자(플레이어 엔티티)의 무기 모듈 — 든 총·탄창·재장전의 정본. 세이브는 심 상태를 저장한다(PlayerSaveModule).</summary>
        static WeaponModule Weapon => PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.Entity?.Get<WeaponModule>() : null;
        static float Now => SimRunner.Players.Now;

        static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayerRecursively(c, layer);
        }

        /// <summary>모델 안의 이름 노드(리그 규약) 또는 view의 좌표(서드파티 모델)로 앵커를 만든다. 둘 다 없으면 null(근접 등).</summary>
        static Transform Anchor(Transform model, string nodeName, Vector3? local)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == nodeName) return t;
            if (!local.HasValue) return null;
            var a = new GameObject(nodeName).transform;
            a.SetParent(model, false);
            a.localPosition = local.Value;
            return a;
        }

        static string PascalKeyOf(string id)
        {
            string key = id.Substring(id.LastIndexOf('/') + 1);
            var sb = new System.Text.StringBuilder(key.Length); bool up = true;
            foreach (char c in key) { if (c == '_') { up = true; continue; } sb.Append(up ? char.ToUpperInvariant(c) : c); up = false; }
            return sb.ToString();
        }

        private void OnDestroy()
        {
            if (current != null) current.Fired -= OnWeaponFired;
        }

        // ── 공개 API

        /// <summary>아이템 정의의 총(GunDef)을 든다 — 그 순간 정의에서 조립하고, 들고 있던 총은 지운다. 같은 총이면 아무것도 안 한다.</summary>
        public void EquipWeapon(GunDef target)
        {
            if (target == null) return;
            if (current != null && ReferenceEquals(current.Def, target)) return;   // 이미 들고 있는 무기
            var gun = AssembleGun(target);
            if (gun == null) return;   // 조립 실패는 AssembleGun이 소리 냈다 — 들고 있던 총은 그대로
            DropCurrent();
            current = gun;
            current.Fired += OnWeaponFired;
            SwapIn(current);
            Weapon?.Equip(current.Def, Now);   // 심에 든 총을 알린다 — 하던 재장전은 취소된다
        }

        public void UnequipWeapon()
        {
            DropCurrent();
            Weapon?.Equip(null, Now);
            SetAiming(false);
        }

        /// <summary>든 총을 내리고 오브젝트를 지운다 — 탄창 상태는 심(WeaponModule)에 있어 잃는 것이 없다.</summary>
        private void DropCurrent()
        {
            if (current == null) return;
            current.Fired -= OnWeaponFired;
            Destroy(current.gameObject);
            current = null;
        }

        /// <summary>
        /// 총 하나를 정의에서 조립한다 — 장착하는 순간 만들고 내리면 지운다(미리 만들어 두지 않는다). 정의의 view가 전부 정한다:
        /// model(카탈로그), pose(홀더 기준 자세), muzzle·sight(모델 기준 앵커 — 모델 안에 MuzzlePoint/SightPos 노드가 있으면 그것),
        /// knockback, sfx(Gun이 읽는다). view.type이 Gun이 아니거나 모델이 없으면 소리 내고 null.
        /// </summary>
        private Gun AssembleGun(GunDef def)
        {
            var parent = gunRoot != null ? gunRoot : transform;
            var view = ViewSchema.Of(def);
            if (view.Type != "Gun") { Debug.LogError($"[WeaponManager] {def.Id}: view.type이 Gun이 아닙니다('{view.Type}') — 조립하지 않습니다."); return null; }
            var model = ViewCatalogSO.ModelOf(def);
            if (model == null) Debug.LogError($"[WeaponManager] {def.Id}: 모델(view.model)이 카탈로그에 없습니다 — 내장 체커 상자로 조립합니다.");

            var go = new GameObject(PascalKeyOf(def.Id));
            go.SetActive(false);   // 자세·앵커를 다 잡은 뒤 켠다 — Gun.Awake가 카메라를 찾는다
            go.transform.SetParent(parent, false);
            var (pos, rot, scale) = view.Pose;
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = Vector3.one * scale;

            var body = model != null ? Instantiate(model, go.transform) : Managers.MissingAssets.Box("Missing", new Vector3(0.1f, 0.1f, 0.4f), go.transform);
            body.name = model.name;
            body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity; body.transform.localScale = Vector3.one;
            // 뷰모델 레이어 — 홀더(Weapon_Holder, Weapon 레이어)의 것을 그대로 물려받는다. 오버레이 카메라가 이 레이어만 그리고 메인 카메라·조명은 뺀다
            SetLayerRecursively(go.transform, parent.gameObject.layer);

            var gun = go.AddComponent<Gun>();
            gun.gunId = def.Id;
            gun.enemyLayer = enemyLayer;
            gun.muzzlePoint = Anchor(body.transform, "MuzzlePoint", view.Vec3("muzzle"));
            gun.sightPoint = Anchor(body.transform, "SightPos", view.Vec3("sight"));
            SetLayerRecursively(go.transform, parent.gameObject.layer);   // 새로 만든 앵커까지
            var kb = view.Object("knockback");
            if (kb != null) { gun.knockbackEffectId = (string)kb["effect"]; gun.knockbackPerDamage = (float?)kb["perDamage"] ?? gun.knockbackPerDamage; }
            return gun;
        }

        // 새로 조립한 총을 손에 맞춘다 — 가늠자 오프셋 계산은 홀더가 원점일 때 해야 한다(스왑 순간의 흔들림이 섞이지 않게)
        private void SwapIn(Gun weapon)
        {
            if (motionManager != null && adsModule != null)
            {
                Vector3 tempPos = motionManager.transform.localPosition;
                Quaternion tempRot = motionManager.transform.localRotation;
                motionManager.transform.localPosition = Vector3.zero;
                motionManager.transform.localRotation = Quaternion.identity;

                adsModule.SetupWeapon(weapon.sightPoint, weapon.Def.ZoomMultiplier);

                motionManager.transform.localPosition = tempPos;
                motionManager.transform.localRotation = tempRot;
            }
            weapon.gameObject.SetActive(true);
        }

        /// <summary>
        /// 지금 든 무기로 조준할 수 있는가 — 가늠자가 없는 무기(근접)는 false.
        /// 입력(WeaponController)은 이 값이 false면 조준 입력을 소비하지 않고 흘려보낸다.
        /// </summary>
        public bool CanAim => CurrentWeapon != null && !CurrentWeapon.Def.BlockAim;

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

        // 발사 연출 팬아웃 — Gun은 "쐈다"만 알리고, 무엇이 흔들릴지는 여기서 정한다
        private void OnWeaponFired(Gun weapon)
        {
            var data = weapon.Def;

            if (CameraShakeManager.Instance != null)
                CameraShakeManager.Instance.ShakeOnPlayerShoot(data.BaseDamage);
            if (recoilManager != null)
                recoilManager.FireRecoil(data.XRecoil, data.YRecoil, data.ZRecoil);
            if (kickbackModule != null)
                kickbackModule.Fire(data.VisualKickbackZ, Vec3(data.VisualKickbackRot, Vector3.one), IsAiming);
            // 근접무기는 킥백(뒤로 밀림) 대신 호를 그린다 — 어느 쪽인지는 무기 수치가 정한다
            // (swingTime 0 = 스윙 없음, 킥백 0 = 반동 없음). 코드에는 총/근접 분기가 없다.
            if (swingModule != null && data.SwingTime > 0f)
                swingModule.Swing(data.SwingTime, Vec3(data.SwingRotation, new Vector3(35f, -55f, 40f)), Vec3(data.SwingPosition, new Vector3(-0.1f, 0.04f, 0.1f)),
                                  Mathf.Max(0f, data.SwingWindup), data.SwingAlternate);
        }

        /// <summary>팩의 float[3] → Vector3. 정의가 값을 안 실었으면(옛 SO 기본값이던 자리) 기본값.</summary>
        static Vector3 Vec3(float[] a, Vector3 fallback)
            => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;
    }
}
