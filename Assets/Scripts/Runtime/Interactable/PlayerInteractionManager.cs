using UnityEngine;

/// <summary>
/// 상호작용 조준 서비스 — "지금 조준 중인 IInteractable" 판정의 단일 소유자.
/// 매 프레임 판정을 Current에 캐시하고 프롬프트("[E] ...")를 그린다.
/// E키 실행(PlayerController.TryInteract)도 같은 Current를 사용한다 —
/// 표시와 실행이 각자 레이캐스트하며 판정이 어긋나던 구조를 단일화.
/// </summary>
public class PlayerInteractionManager : MonoBehaviour
{
    [SerializeField] private Transform playerCamera;

    [SerializeField] TMPro.TextMeshProUGUI promptText;

    [Tooltip("상호작용 레이캐스트 대상 레이어. 여기 포함된 비상호작용 오브젝트(벽 등)는 시야를 가린다.")]
    [SerializeField] LayerMask interactableLayers;

    [SerializeField] float interactRange = 4f;

    /// <summary>이번 프레임에 조준 중인 상호작용 대상. 없거나 Prompt가 비었으면 null.</summary>
    public IInteractable Current { get; private set; }

    private PlacementSystem placement;

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

        string prompt = Current?.Prompt;
        if (promptText != null)
        {
            bool show = !string.IsNullOrEmpty(prompt);
            promptText.gameObject.SetActive(show);
            if (show) promptText.text = $"[E] {prompt}";
        }
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
}
