using UnityEngine;
using UnityEngine.EventSystems;

public class UISoundHandler : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        SoundManager.Instance.PlayCommonSFX(CommonSFX.Click);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SoundManager.Instance.PlayCommonSFX(CommonSFX.Hover);
    }
}