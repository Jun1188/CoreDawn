using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 무기 입력 어댑터 — 입력 파이프라인의 Player 리시버.
/// WeaponManager(장착/교체/모듈 소유)와 분리: BuildController↔PlacementSystem과 같은 경계.
/// 사격(선입력 버퍼+자동 연사)·조준(ADS)·재장전을 라우팅으로 받는다 —
/// 건설 모드(BuildTool이 Attack/Aim 소비)·팝업(UI 맵) 중에는 신호가 도달하지 않는다.
/// </summary>
public class WeaponController : MonoBehaviour, IInputReceiver
{
    [SerializeField] private WeaponManager weaponManager;

    [Tooltip("비우면 상위에서 자동으로 찾는다. 사격/조준이 달리기를 끊기 위해 필요.")]
    [SerializeField] private PlayerController player;

    [Tooltip("발사 선입력을 기억하는 시간(초) — 연사 간격 직전에 누른 클릭도 발사되게")]
    [SerializeField] private float fireBufferWindow = 0.15f;

    private bool isFiringHeld;            // 자동화기 연사용 눌림 상태
    private float lastFireInputTime = -1f;

    public int Priority => InputPriority.Player;
    public bool IsInputActive => isActiveAndEnabled && weaponManager != null;

    private void Awake()
    {
        if (weaponManager == null) weaponManager = FindFirstObjectByType<WeaponManager>();
        if (player == null) player = GetComponentInParent<PlayerController>();
    }

    private void Start()
    {
        if (InputManager.Instance != null) InputManager.Instance.Register(this);
        else Debug.LogError("[WeaponController] 씬에 InputManager가 없습니다.", this);
    }

    // 플레이어 사망(GO 비활성화) 후 부활 시 리시버 재등록 — Register는 중복 안전
    private void OnEnable()
    {
        if (InputManager.Instance != null) InputManager.Instance.Register(this);
    }

    private void OnDisable()
    {
        ReleaseHeldStates();
        if (InputManager.Instance != null) InputManager.Instance.Unregister(this);
    }

    private void Update()
    {
        var weapon = weaponManager != null ? weaponManager.CurrentWeapon : null;
        if (weapon == null) return;

        // 자동화기는 눌림 동안 버퍼를 계속 갱신 → 아래 판정이 연사로 이어짐
        if (isFiringHeld && weapon.gunData.isAutomatic)
            lastFireInputTime = Time.time;

        // 버퍼 시간 내라면 사격 시도, 성공하면 버퍼 소비 (발사 간격은 무기가 관리)
        if (lastFireInputTime >= 0f && Time.time - lastFireInputTime <= fireBufferWindow)
        {
            if (weapon.TryFire())
                lastFireInputTime = -1f;
        }
    }

    public bool OnInput(in InputEvent e)
    {
        var weapon = weaponManager.CurrentWeapon;

        switch (e.Id)
        {
            case InputActionId.Attack:
                if (weapon == null) return false;   // 맨손이면 하류로 통과
                if (e.Phase == InputActionPhase.Performed)
                {
                    isFiringHeld = true;
                    lastFireInputTime = Time.time;   // 단발도 버퍼를 거쳐 Update에서 발사
                    // 쏘려면 달리기를 멈춘다 — 총을 내린 채로 발사되는 모순을 원천 차단
                    if (player != null) player.SuppressSprint();
                    return true;
                }
                if (e.Phase == InputActionPhase.Canceled) isFiringHeld = false;
                return false;

            case InputActionId.Aim:
                // 가늠자 없는 무기(근접)는 조준 자체가 없다 — 소비하지 않고 하류로 흘려보낸다
                if (weapon == null || !weaponManager.CanAim) return false;
                if (e.Phase == InputActionPhase.Performed)
                {
                    weaponManager.SetAiming(true); // 줌 FOV는 스왑 때 매니저가 무기 데이터로 이미 설정
                    if (player != null) player.SuppressSprint();
                    return true;
                }
                if (e.Phase == InputActionPhase.Canceled) weaponManager.SetAiming(false);
                return false;

            case InputActionId.Reload:
                if (weapon == null || e.Phase != InputActionPhase.Performed) return false;
                weapon.StartReload();
                return true;

            case InputActionId.SwitchAmmo:
                if (weapon == null || e.Phase != InputActionPhase.Performed) return false;
                weapon.TrySwitchAmmo();   // 인벤토리에 다른 탄종이 없으면 조용히 실패
                return true;
        }
        return false;
    }

    /// <summary>비활성/팝업 전환 시 눌림 상태가 남지 않게 정리</summary>
    private void ReleaseHeldStates()
    {
        isFiringHeld = false;
        lastFireInputTime = -1f;
        if (weaponManager != null) weaponManager.SetAiming(false);
    }
}