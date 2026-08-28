using UnityEngine;
using UnityEngine.UIElements;
using CoreDawn.Inputs;

namespace CoreDawn.UI
{
    /// <summary>
    /// UI Toolkit 팝업 공통 베이스 — 레퍼런스 문서 §05 "기존 입력 시스템과 연결".
    ///
    /// 입력 소유권(맵 Push/Pop · depth 우선순위 · ESC 처리)과 커서 해제·재잠금은
    /// <see cref="UIPopup"/>이 이미 갖고 있으므로 그대로 쓴다. 이 클래스가 추가로 맡는 것은
    /// UIDocument 루트의 표시/숨김 하나뿐이다.
    ///
    /// 파생 클래스는 <see cref="Bind"/>에서 이벤트를 구독하고 초기 1회 갱신하며,
    /// <see cref="Unbind"/>에서 반드시 같은 것을 해제한다.
    /// Update 폴링으로 갱신하지 않는다 (문서 §05 "주의할 것").
    ///
    /// UIDocument보다 늦게 OnEnable이 돌아야 rootVisualElement가 준비되므로
    /// 실행 순서를 뒤로 밀어 둔다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(UIDocument))]
    public abstract class UITKPopup : UIPopup
    {
        [SerializeField] protected UIDocument document;

        /// <summary>UXML 최상단 요소가 붙는 컨테이너. OnEnable 이후에만 유효하다.</summary>
        protected VisualElement Root => document != null ? document.rootVisualElement : null;

        protected virtual void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
        }

        protected override void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();

            base.OnEnable();   // 리시버 등록 + UI 맵 Push + 커서 해제 (기존 계약 유지)

            var root = Root;
            if (root == null)
            {
                // UIDocument가 아직 패널을 만들지 않았다 — sourceAsset 미지정이 대부분의 원인
                Debug.LogError($"[{GetType().Name}] UIDocument.rootVisualElement가 null입니다. " +
                               "Source Asset(UXML)과 Panel Settings가 지정됐는지 확인하세요.", this);
                return;
            }

            root.style.display = DisplayStyle.Flex;
            Bind();
        }

        protected override void OnDisable()
        {
            Unbind();

            var root = Root;
            if (root != null) root.style.display = DisplayStyle.None;

            base.OnDisable();  // 리시버 해제 + UI 맵 Pop + 커서 재잠금(마지막 창일 때만)
        }

        /// <summary>이벤트 구독 + 초기 1회 갱신. Root는 여기서 반드시 non-null이다.</summary>
        protected abstract void Bind();

        /// <summary>Bind에서 건 구독을 전부 해제. 비활성 상태에서도 안전해야 한다.</summary>
        protected abstract void Unbind();

        // ───────────────────── 파생 클래스용 헬퍼 ─────────────────────

        /// <summary>클래스 하나를 조건부로 켜고 끈다 — 수식 클래스(--selected 등) 토글용.</summary>
        protected static void ToggleClass(VisualElement e, string className, bool on)
        {
            if (e == null) return;
            if (on) e.AddToClassList(className);
            else e.RemoveFromClassList(className);
        }

        /// <summary>진행바 채움 — 런타임 값이라 인라인 스타일이 허용되는 예외 (문서 §05).</summary>
        protected static void SetBarFill(VisualElement fill, float normalized)
        {
            if (fill == null) return;
            fill.style.width = Length.Percent(Mathf.Clamp01(normalized) * 100f);
        }
    }
}
