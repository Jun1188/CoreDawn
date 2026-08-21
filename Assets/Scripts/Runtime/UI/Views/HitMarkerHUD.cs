using UnityEngine;
using UnityEngine.UIElements;
using DG.Tweening;
using System.Collections;

public class HitMarkerHUD : MonoBehaviour
{
    [SerializeField] private string hitMarkerName = "hit-marker"; 
    public float fadeDuration = 0.2f;

    private VisualElement hitMarker;
    private Tween fadeTween;

    private void Start()
    {
        StartCoroutine(FindUIDocumentRoutine());
    }

    private IEnumerator FindUIDocumentRoutine()
    {
        float timeout = 2f;
        while (hitMarker == null && timeout > 0f)
        {
            // 씬에 있는 모든 UIDocument를 전수조사하여 'hit-marker'가 있는 진짜 HUD를 찾습니다.
            UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            foreach (var doc in documents)
            {
                if (doc != null && doc.rootVisualElement != null)
                {
                    hitMarker = doc.rootVisualElement.Q<VisualElement>(hitMarkerName);
                    if (hitMarker != null)
                    {
                        Debug.Log("[HitMarkerHUD] Hit-marker UI를 성공적으로 찾았습니다!");
                        break;
                    }
                }
            }

            if (hitMarker == null)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
        }

        if (hitMarker != null)
        {
            hitMarker.style.opacity = 0f; // 초기 상태는 투명하게
        }
        else
        {
            Debug.LogWarning($"[HitMarkerHUD] 모든 UIDocument를 뒤졌으나 '{hitMarkerName}' 요소를 찾지 못했습니다. UXML 이름을 확인해주세요.");
        }
    }

    private void OnEnable()
    {
        CombatEvents.OnPlayerHitEnemy += ShowHitMarker;
        print("HitMarker - CombatEvents 등록");
    }

    private void OnDisable()
    {
        if (CombatEvents.OnPlayerHitEnemy != null)
            CombatEvents.OnPlayerHitEnemy -= ShowHitMarker;
    }

    private void ShowHitMarker()
    {
        if (hitMarker == null)
        {
            Debug.LogWarning(
                "[HitMarkerHUD] hit-marker를 찾지 못했습니다."
            );
            return;
        }

        print("HitMarker - 실행");

        fadeTween?.Kill();

        hitMarker.style.display = DisplayStyle.Flex;
        hitMarker.style.visibility = Visibility.Visible;
        hitMarker.style.opacity = 1f;

        fadeTween = DOTween.To(
            () => hitMarker.style.opacity.value,
            x => hitMarker.style.opacity = x,
            0f,
            fadeDuration
        )
        .SetEase(Ease.OutQuad);
    }
}