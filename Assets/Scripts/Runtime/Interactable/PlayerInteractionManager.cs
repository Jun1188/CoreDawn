using UnityEngine;

/// <summary>
/// 상호작용 조준 서비스 — "지금 조준 중인 IInteractable" 판정의 단일 소유자.
/// 매 프레임 판정을 Current에 캐시한다. E키 실행(PlayerController.TryInteract)과
/// 프롬프트 표시(GameplayHUDView — "[E] ...")가 같은 Current를 사용한다 —
/// 표시와 실행이 각자 레이캐스트하며 판정이 어긋나던 구조를 단일화.
///
/// 홀드 상호작용(<see cref="IHoldInteractable"/> — 손 채굴 등)의 진행도도 여기서 센다.
/// 조준을 이미 매 프레임 판정하고 있으므로, "누르고 있는 동안 같은 것을 계속 보고 있는가"를
/// 알 수 있는 유일한 자리다. HUD는 <see cref="HoldProgress"/>를 읽어 링만 그린다.
/// </summary>
public class PlayerInteractionManager : MonoBehaviour
{
    [SerializeField] private Transform playerCamera;

    [Tooltip("상호작용 레이캐스트 대상 레이어. 여기 포함된 비상호작용 오브젝트(벽 등)는 시야를 가린다.")]
    [SerializeField] LayerMask interactableLayers;

    [SerializeField] float interactRange = 4f;

    /// <summary>E키 상호작용 사거리 — 건설(배치·철거) 조준도 같은 값을 쓴다(PlacementSystem).</summary>
    public float InteractRange => interactRange;

    /// <summary>이번 프레임에 조준 중인 상호작용 대상. 없거나 Prompt가 비었으면 null.</summary>
    public IInteractable Current { get; private set; }

    private PlacementSystem placement;

    // ── 홀드 상태 ────────────────────────────────────────────────
    private IHoldInteractable holdTarget;
    private PlayerController holdPlayer;
    private float holdElapsed;

    /// <summary>지금 홀드 중인 대상. 누르고 있지 않으면 null.</summary>
    public IHoldInteractable HoldTarget => holdTarget;

    /// <summary>홀드 진행도 0~1. 누르고 있지 않으면 0.</summary>
    public float HoldProgress
    {
        get
        {
            if (holdTarget == null) return 0f;
            float need = holdTarget.HoldSeconds;
            return need > 0f ? Mathf.Clamp01(holdElapsed / need) : 0f;
        }
    }

    private void Awake() => placement = FindFirstObjectByType<PlacementSystem>();

    /// <summary>
    /// 건설·철거 모드 중에는 상호작용을 막는다.
    /// 그 모드에서 좌클릭은 배치/철거이고 조준은 그리드를 겨냥하는 중이다 —
    /// 같은 조준선에 "[E] 필터 설정" 같은 프롬프트가 함께 뜨면 무엇이 일어날지 알 수 없어진다.
    /// </summary>
    private bool BuildModeActive =>
        placement != null && placement.Mode != PlacementSystem.BuildMode.None;

    private void Update()
    {
        Current = BuildModeActive ? null : FindAimedInteractable();
        TickHold();
    }

    private IInteractable FindAimedInteractable()
    {
        if (playerCamera == null) return null;

        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayers))
            return null;

        var target = hit.collider.GetComponentInParent<IInteractable>();
        // Prompt가 비어 있으면 "지금은 상호작용 불가" — 대상 없음으로 취급
        return target != null && !string.IsNullOrEmpty(target.Prompt) ? target : null;
    }

    // ── 홀드 ────────────────────────────────────────────────────

    /// <summary>
    /// E를 누른 순간. 조준 대상이 홀드형이면 진행을 시작하고 true를 돌려준다 —
    /// 호출자(PlayerController)는 그때만 즉시 실행(Interact)을 건너뛴다.
    /// </summary>
    public bool BeginHold(PlayerController player)
    {
        if (Current is not IHoldInteractable hold || hold.HoldSeconds <= 0f) return false;

        holdTarget  = hold;
        holdPlayer  = player;
        holdElapsed = 0f;
        return true;
    }

    /// <summary>E를 뗐다. 임계 시간에 못 미친 회차는 버려진다 (철거 홀드와 같은 규칙).</summary>
    public void EndHold()
    {
        holdTarget  = null;
        holdPlayer  = null;
        holdElapsed = 0f;
    }

    private void TickHold()
    {
        if (holdTarget == null) return;

        // 창이 열리면 손을 뗀 것으로 본다 — 액션 맵이 내려가면서 Canceled가 오지만,
        // 그 신호에만 기대면 인벤토리를 열어 둔 채 소리 없이 계속 캐고 있을 수 있다.
        // 조준(Current)과 같은 규칙이다: 커서가 조작 중인 지점은 조준이 아니다.
        if (UIPopup.AnyOpen) { EndHold(); return; }

        // 조준을 딴 데로 옮기면 취소한다. 철거 홀드는 "누른 채 옆 건물로 옮기면 그쪽부터 다시"인데,
        // 여기는 대상이 곧 결과물(무엇을 캐는가)이라 조용히 다른 광물로 갈아타면 안 된다.
        if (!ReferenceEquals(Current, holdTarget) || string.IsNullOrEmpty(holdTarget.Prompt))
        {
            EndHold();
            return;
        }

        // 진행 불가(재고 없음 등)는 취소가 아니라 정지 — 링이 그 자리에 멈춰 이유를 보여준다
        if (!holdTarget.CanHold) return;

        holdElapsed += Time.deltaTime;
        if (holdElapsed < holdTarget.HoldSeconds) return;

        // 누르고 있는 동안 계속 캔다 — 남은 시간은 다음 회차로 넘겨 리듬이 끊기지 않게
        holdElapsed -= holdTarget.HoldSeconds;

        var done = holdTarget;
        done.OnHoldComplete(holdPlayer);

        // 완료 처리가 대상을 없앴을 수도 있다 (고갈된 광맥 등)
        if (done is Object o && o == null) EndHold();
    }
}
