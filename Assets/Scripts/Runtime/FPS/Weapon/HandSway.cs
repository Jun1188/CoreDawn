using UnityEngine;

/// <summary>
/// WeaponMotionManager에 의해 관리되는 Weapon Sway 모듈.
/// IWeaponModule 인터페이스를 상속받아, transform을 직접 조작하지 않고
/// 계산된 순수 Offset 값만 매니저에게 전달합니다.
///
/// ■ 동작 방식
///   Input.GetAxisRaw로 raw 마우스 델타를 읽어 EMA(Exponential Moving Average)로 smoothing합니다.
///   Position Sway, Rotation Sway, Movement Bob, Movement Sway, Landing Bob 로직이 포함됩니다.
/// </summary>
public class HandSway : MonoBehaviour, IWeaponMotionModule
{
    // ─── Position Sway ───────────────────────────────────────────────
    [Header("Position Sway (Mouse)")]
    [Tooltip("마우스 이동에 따른 최대 위치 흔들림 (m). 권장: 0.04~0.08")]
    public float posSwayAmount = 0.06f;

    [Tooltip("위치가 원점으로 돌아오는 속도. 클수록 빠름. 권장: 6~12")]
    public float posSwaySmooth = 8f;

    // ─── Rotation Sway ───────────────────────────────────────────────
    [Header("Rotation Sway (Mouse)")]
    [Tooltip("마우스 이동에 따른 최대 회전 흔들림 (도). 권장: 2~6")]
    public float rotSwayAmount = 4f;

    [Tooltip("회전이 원점으로 돌아오는 속도. 클수록 빠름. 권장: 6~12")]
    public float rotSwaySmooth = 8f;

    // ─── Movement Bob ─────────────────────────────────────────────────
    [Header("Movement Bob")]
    public bool bobEnabled = true;

    [Tooltip("Bob 기본 주기 (Hz). 이동 속도에 따라 자동 스케일됩니다. 권장: 1.5~2.5")]
    public float bobFrequency = 1.8f;

    [Tooltip("좌우 Bob 진폭 (m). 권장: 0.005~0.012")]
    public float bobAmountX = 0.008f;

    [Tooltip("상하 Bob 진폭 (m). 권장: 0.004~0.010")]
    public float bobAmountY = 0.006f;

    [Tooltip("정지 시 bobTimer를 0으로 감쇠시키는 속도. 권장: 8~14")]
    public float bobResetSmooth = 10f;

    // ─── Movement Sway (Inertia) ──────────────────────────────────────
    [Header("Movement Sway (Inertia)")]
    public bool moveSwayEnabled = true;

    [Tooltip("캐릭터 이동에 따른 위치 관성 최대치. (X: 좌우 밀림, Y: 상하 밀림, Z: 앞뒤 밀림)")]
    public Vector3 movePosSwayAmount = new Vector3(0.02f, 0.02f, 0.02f);

    [Tooltip("캐릭터 이동에 따른 회전 관성 최대치 (도). (X: 앞뒤 기울기, Y: 좌우 회전, Z: 좌우 기울기)")]
    public Vector3 moveRotSwayAmount = new Vector3(3f, 0f, 2f);

    [Tooltip("이동 관성이 적용/복구되는 속도. 권장: 4~8")]
    public float moveSwaySmooth = 6f;

    [Tooltip("속도 정규화를 위한 기준 최고 이동 속도. 이 속도에 도달할 때 관성 최대치가 적용됩니다.")]
    public float maxReferenceSpeed = 5f;

    // ─── Input Smoothing ──────────────────────────────────────────────
    [Header("Mouse Input Smoothing")]
    [Tooltip("EMA 계수 (1/60s 프레임 기준). 작을수록 부드럽고 느림. 권장: 0.10~0.20")]
    [Range(0.01f, 1f)]
    public float inputSmoothing = 0.15f;

    // ─── IWeaponModule 인터페이스 구현부 (매니저가 가져갈 결과값) ────────────
    public Vector3 PositionOffset { get; private set; }
    public Quaternion RotationOffset { get; private set; } = Quaternion.identity;

    // ─── 내부 상태 ────────────────────────────────────────────────────
    private Vector2 _smoothedDelta;
    private float _bobTimer;

    private Vector3 _smoothedMovePos;
    private Quaternion _smoothedMoveRot = Quaternion.identity;

    private bool _wasGrounded = true;
    private float _fallDistance = 0f;

    private Rigidbody _rb;

    // ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponentInParent<Rigidbody>();
    }

    private void Update()
    {
        SmoothMouseInput();
        CalculateMovementSway();

        Vector3 bobOffset = ComputeBobOffset();
        ApplyLandingBob();

        // ★ 변경점: 직접 Transform을 조작하지 않고 Offset 프로퍼티만 갱신
        CalculatePositionOffset(bobOffset);
        CalculateRotationOffset();
    }

    // ─── 마우스 입력 EMA ──────────────────────────────────────────────
    private void SmoothMouseInput()
    {
        float rawX = Input.GetAxisRaw("Mouse X");
        float rawY = Input.GetAxisRaw("Mouse Y");

        float t = 1f - Mathf.Pow(1f - inputSmoothing, Time.deltaTime * 60f);
        _smoothedDelta = Vector2.Lerp(_smoothedDelta, new Vector2(rawX, rawY), t);
    }

    // ─── 캐릭터 이동 관성 연산 ──────────────────────────────────────────
    private void CalculateMovementSway()
    {
        if (!moveSwayEnabled || _rb == null)
        {
            _smoothedMovePos = Vector3.Lerp(_smoothedMovePos, Vector3.zero, Time.deltaTime * moveSwaySmooth);
            _smoothedMoveRot = Quaternion.Slerp(_smoothedMoveRot, Quaternion.identity, Time.deltaTime * moveSwaySmooth);
            return;
        }

        Transform refTransform = transform.parent != null ? transform.parent : transform;
        Vector3 localVelocity = refTransform.InverseTransformDirection(_rb.linearVelocity);

        Vector3 moveInput = localVelocity / maxReferenceSpeed;
        moveInput.x = Mathf.Clamp(moveInput.x, -1f, 1f);
        moveInput.y = Mathf.Clamp(moveInput.y, -1f, 1f);
        moveInput.z = Mathf.Clamp(moveInput.z, -1f, 1f);

        Vector3 targetMovePos = new Vector3(
            -moveInput.x * movePosSwayAmount.x,
            -moveInput.y * movePosSwayAmount.y,
            -moveInput.z * movePosSwayAmount.z
        );

        Vector3 targetMoveRotEuler = new Vector3(
            moveInput.z * moveRotSwayAmount.x,
            -moveInput.x * moveRotSwayAmount.y,
            -moveInput.x * moveRotSwayAmount.z
        );

        _smoothedMovePos = Vector3.Lerp(_smoothedMovePos, targetMovePos, Time.deltaTime * moveSwaySmooth);
        _smoothedMoveRot = Quaternion.Slerp(_smoothedMoveRot, Quaternion.Euler(targetMoveRotEuler), Time.deltaTime * moveSwaySmooth);
    }

    // ─── Bob 오프셋 계산 ──────────────────────────────────────────────
    private Vector3 ComputeBobOffset()
    {
        if (!bobEnabled || _rb == null) return Vector3.zero;

        Vector3 v = _rb.linearVelocity;
        float horizSpeed = new Vector3(v.x, 0f, v.z).magnitude;
        bool isMoving = horizSpeed > 0.5f && Mathf.Abs(_rb.linearVelocity.y) < 0.001f;

        if (isMoving)
        {
            float speedFactor = Mathf.Clamp01(horizSpeed / 8f);
            _bobTimer += Time.deltaTime * bobFrequency * (1f + speedFactor);
        }
        else
        {
            _bobTimer = Mathf.Lerp(_bobTimer, 0f, Time.deltaTime * bobResetSmooth);
        }

        float bx = Mathf.Sin(_bobTimer * Mathf.PI) * bobAmountX;
        float by = Mathf.Sin(_bobTimer * Mathf.PI * 2f) * bobAmountY;
        return new Vector3(bx, by, 0f);
    }

    // ─── 착지 반동 연산 ───────────────────────────────────────────────
    private void ApplyLandingBob()
    {
        if (_rb == null) return;

        bool isGrounded = Physics.Raycast(_rb.transform.position, Vector3.down, 1.2f);

        if (!isGrounded)
        {
            _fallDistance += Time.deltaTime;
        }
        else if (!_wasGrounded && isGrounded)
        {
            if (_fallDistance > 0.2f)
            {
                _smoothedMovePos += new Vector3(0f, -0.15f, 0f);
                _smoothedMoveRot *= Quaternion.Euler(15f, 0f, 0f);
            }
            _fallDistance = 0f;
        }

        _wasGrounded = isGrounded;
    }

    // ─── ★ 최종 Position Offset 산출 ★ ───────────────────────────────
    private void CalculatePositionOffset(Vector3 bobOffset)
    {
        float swayX = Mathf.Clamp(-_smoothedDelta.x * posSwayAmount, -posSwayAmount, posSwayAmount);
        float swayY = Mathf.Clamp(-_smoothedDelta.y * posSwayAmount, -posSwayAmount, posSwayAmount);

        // OriginPos를 더하지 않고, (0,0,0)을 기준으로 한 순수 Offset 타겟값만 산출합니다.
        Vector3 targetOffset = new Vector3(swayX, swayY, 0f) + bobOffset + _smoothedMovePos;

        PositionOffset = Vector3.Lerp(
            PositionOffset,
            targetOffset,
            Time.deltaTime * posSwaySmooth
        );
    }

    // ─── ★ 최종 Rotation Offset 산출 ★ ───────────────────────────────
    private void CalculateRotationOffset()
    {
        float tiltZ = Mathf.Clamp(_smoothedDelta.x * rotSwayAmount, -rotSwayAmount, rotSwayAmount);
        float pitchX = Mathf.Clamp(-_smoothedDelta.y * rotSwayAmount, -rotSwayAmount, rotSwayAmount);

        // OriginRot을 곱하지 않고, 순수 회전 Offset 타겟값만 산출합니다.
        Quaternion targetOffset = Quaternion.Euler(pitchX, 0f, -tiltZ) * _smoothedMoveRot;

        RotationOffset = Quaternion.Slerp(
            RotationOffset,
            targetOffset,
            Time.deltaTime * rotSwaySmooth
        );
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        posSwayAmount = Mathf.Max(0f, posSwayAmount);
        rotSwayAmount = Mathf.Max(0f, rotSwayAmount);
        bobFrequency = Mathf.Max(0.1f, bobFrequency);
        posSwaySmooth = Mathf.Max(0.1f, posSwaySmooth);
        rotSwaySmooth = Mathf.Max(0.1f, rotSwaySmooth);
        bobResetSmooth = Mathf.Max(0.1f, bobResetSmooth);
        moveSwaySmooth = Mathf.Max(0.1f, moveSwaySmooth);
        maxReferenceSpeed = Mathf.Max(0.1f, maxReferenceSpeed);
    }
#endif
}