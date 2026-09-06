using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Managers;

namespace CoreDawn.UI
{
    /// <summary>
    /// 부팅(로딩) 씬 화면 — 타이틀 레퍼런스의 <c>#load</c>(LOADING · 퍼센트 · 기울어진 바 · 지금 읽는 항목). 임시 OnGUI 를 대체(2026-09-07).
    /// 값은 전부 남의 것: 진행도·현재 항목은 <see cref="PackAssets"/>, 단계 문구·실패는 <see cref="BootScene"/>.
    /// 빌드에서는 0번 씬이라 첫 화면이고, 새 게임·불러오기 때도 잠깐 지난다(자원은 이미 읽혀 있어 바로 100%).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(100)]
    public sealed class BootScreenView : MonoBehaviour
    {
        [SerializeField] UIDocument document;
        [SerializeField] BootScene boot;

        Label title, pct, msg;
        HoloBar bar;

        void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (boot == null) boot = FindFirstObjectByType<BootScene>();
            var root = document != null ? document.rootVisualElement : null;
            if (root == null)
            {
                Debug.LogError("[BootScreenView] UIDocument.rootVisualElement 가 null 입니다 — Source Asset(BootScreen.uxml)과 Panel Settings 를 확인하세요.", this);
                return;
            }
            title = root.Q<Label>("load-title");
            pct = root.Q<Label>("load-pct");
            msg = root.Q<Label>("load-msg");
            var host = root.Q("load-bar");
            if (host != null) { bar = new HoloBar(); host.Add(bar); }
            Refresh();
        }

        void Update() => Refresh();

        void Refresh()
        {
            var (done, total) = PackAssets.Progress;
            float p = PackAssets.IsReady ? 1f : total > 0 ? (float)done / total : 0f;

            // World 로 갈 때(새 게임·불러오기): 자원은 이미 있고 월드 생성은 진행률이 없다 → "WORLD GENERATING" + 흐르는 바, 퍼센트 숨김
            bool worldGen = boot != null && !boot.ToTitle && PackAssets.IsReady;
            if (title != null) title.text = worldGen ? "WORLD GENERATING" : "LOADING";
            if (bar != null)
            {
                bar.Indeterminate = worldGen;
                if (worldGen) bar.Tick(Time.unscaledTime); else bar.Progress = p;
            }
            if (pct != null)
            {
                pct.text = Mathf.RoundToInt(p * 100f) + "%";
                pct.style.visibility = worldGen ? Visibility.Hidden : Visibility.Visible;
            }

            bool failed = boot != null && boot.Failed;
            string text = failed ? boot.Status
                        : !PackAssets.IsReady && !string.IsNullOrEmpty(PackAssets.Current) ? PackAssets.Current
                        : boot != null ? boot.Status : "";
            if (msg != null)
            {
                msg.text = text.ToUpperInvariant();
                msg.EnableInClassList("load-msg--error", failed);
            }
        }
    }
}
