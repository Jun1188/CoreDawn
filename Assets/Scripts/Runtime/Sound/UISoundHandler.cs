using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Managers;

namespace CoreDawn.Sound
{
    /// <summary>
    /// UI Toolkit의 버튼·토글·슬라이더에 클릭음과 호버음을 붙인다.
    ///
    /// 홀드 반복음은 없다 — 예전에는 누르고 있으면 0.1초마다 소리를 냈는데, UI 버튼은
    /// 대부분 홀드로 반복 실행되지 않으니 소리를 낼 이유가 없었고, 그마저 Hover 클립으로
    /// 나가는 바람에 "클릭음이 계속 난다"로만 들렸다.
    /// </summary>
    public class UISoundHandler : MonoBehaviour
    {
        // UIDocument는 패널이 열릴 때 생긴다 — 매 프레임 씬 전체를 훑을 이유가 없고,
        // 이 정도 지연은 창이 뜨고 마우스가 버튼에 닿기까지의 시간에 묻힌다
        private const float ScanInterval = 0.25f;
        private float nextScanTime;

        // 버튼 안 Label로 포인터가 넘어가도 Enter가 다시 온다 — 대상이 실제로 바뀌었을
        // 때만 내지 않으면 버튼 하나에서 호버음이 여러 번 울린다
        private VisualElement lastHovered;
        private readonly HashSet<int> pressedPointers = new();

        private void Update()
        {
            if (Time.unscaledTime < nextScanTime) return;
            nextScanTime = Time.unscaledTime + ScanInterval;

            var uiDocs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            foreach (var doc in uiDocs)
            {
                if (doc == null || doc.rootVisualElement == null) continue;
                RegisterSoundEvents(doc.rootVisualElement);
            }
        }

        /// <summary>
        /// 등록 전에 해제한다 — 같은 인스턴스의 메서드 그룹은 UIElements가 같은 콜백으로
        /// 취급하므로 해제가 정확히 걸리고, 덕분에 몇 번을 불러도 콜백은 하나만 남는다.
        /// 등록 여부를 따로 적어 두지 않으니 rootVisualElement가 다시 만들어져도
        /// 다음 스캔이 알아서 복구한다.
        /// </summary>
        private void RegisterSoundEvents(VisualElement root)
        {
            root.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);

            root.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);

            root.UnregisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);

            root.UnregisterCallback<PointerEnterEvent>(OnPointerEnter, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerEnterEvent>(OnPointerEnter, TrickleDown.TrickleDown);

            root.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(OnPointerLeave, TrickleDown.TrickleDown);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            int pointerId = evt.pointerId;

            // 같은 포인터가 눌린 상태에서는 클릭음 재생 금지
            if (pressedPointers.Contains(pointerId))
                return;

            VisualElement clickable = GetClickableTarget(evt.target as VisualElement);
            if (clickable == null)
                return;

            pressedPointers.Add(pointerId);
            PlaySound(CommonSFX.Click);
        }
        private void OnPointerUp(PointerUpEvent evt)
        {
            pressedPointers.Remove(evt.pointerId);
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            pressedPointers.Remove(evt.pointerId);
        }
        private void OnPointerEnter(PointerEnterEvent evt)
        {
            VisualElement clickable = GetClickableTarget(evt.target as VisualElement);
            if (clickable == null || clickable == lastHovered) return;

            lastHovered = clickable;
            PlaySound(CommonSFX.Hover);
        }

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            // 버튼과 그 자식 전부에서 벗어났을 때만 잊는다 — 자식(Label)에서 나온 Leave까지
            // 받아 지우면 버튼 안에서 마우스를 흔들 때마다 호버음이 다시 난다
            if (evt.target as VisualElement == lastHovered) lastHovered = null;
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

        private void PlaySound(CommonSFX sfx)
        {
            if (SoundManager.Instance == null) return;
            SoundManager.Instance.PlayCommonSFX(sfx);
        }
        private void OnDisable()
        {
            pressedPointers.Clear();
            lastHovered = null;
        }
    }
}
