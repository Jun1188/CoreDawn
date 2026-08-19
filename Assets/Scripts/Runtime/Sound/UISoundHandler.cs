using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class UISoundHandler : MonoBehaviour
{
    [Header("Hold Sound Settings")]
    [SerializeField] private float holdThreshold = 0.4f;
    [SerializeField] private float holdInterval = 0.15f;

    private Coroutine _holdCoroutine;

    private void Update()
    {
        var uiDocs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
        foreach (var doc in uiDocs)
        {
            if (doc == null || doc.rootVisualElement == null) continue;
            
            if (doc.rootVisualElement.userData == null)
            {
                doc.rootVisualElement.userData = true;
                RegisterSoundEvents(doc.rootVisualElement);
            }
        }
    }

    private void RegisterSoundEvents(VisualElement root)
    {
        root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        root.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
        root.RegisterCallback<PointerCancelEvent>(OnPointerUp, TrickleDown.TrickleDown);
        root.RegisterCallback<PointerLeaveEvent>(OnPointerUp, TrickleDown.TrickleDown);
        
        // Hover(마우스 올림) 이벤트 추가
        root.RegisterCallback<PointerEnterEvent>(OnPointerEnter, TrickleDown.TrickleDown);
    }

    private void OnPointerEnter(PointerEnterEvent evt)
    {
        VisualElement clickable = GetClickableTarget(evt.target as VisualElement);
        if (clickable == null) return;

        // 마우스를 올렸을 때 Hover 사운드 재생
        PlaySound("Hover", clickable.name);
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        VisualElement clickable = GetClickableTarget(evt.target as VisualElement);
        if (clickable == null) return;

        PlaySound("Click", clickable.name);

        StopHoldRoutine();
        _holdCoroutine = StartCoroutine(CheckHoldRoutine(clickable));
    }

    private void OnPointerUp(EventBase evt)
    {
        StopHoldRoutine();
    }

    private void StopHoldRoutine()
    {
        if (_holdCoroutine != null)
        {
            StopCoroutine(_holdCoroutine);
            _holdCoroutine = null;
        }
    }

    private VisualElement GetClickableTarget(VisualElement element)
    {
        while (element != null)
        {
            if (element is Button || 
                element is Toggle || 
                element is BaseSlider<float> || 
                element is BaseSlider<int> ||
                element is Scroller)
            {
                return element;
            }

            if (element.ClassListContains("ui-btn") || 
                element.ClassListContains("ui-tab") || 
                element.ClassListContains("ui-panel__x") ||
                element.ClassListContains("unity-base-slider") ||
                element.ClassListContains("unity-slider"))
            {
                return element;
            }

            element = element.parent;
        }
        return null;
    }

    private IEnumerator CheckHoldRoutine(VisualElement target)
    {
        yield return new WaitForSecondsRealtime(holdThreshold);

        while (true)
        {
            PlaySound("Hold", target.name);
            yield return new WaitForSecondsRealtime(holdInterval);
        }
    }

    private void PlaySound(string soundType, string elementName)
    {
        if (SoundManager.Instance == null) return;

        if (soundType == "Click") SoundManager.Instance.PlayCommonSFX(CommonSFX.Click);
        else if (soundType == "Hover") SoundManager.Instance.PlayCommonSFX(CommonSFX.Hover);
        else if (soundType == "Hold") SoundManager.Instance.PlayCommonSFX(CommonSFX.Hover);
    }
}