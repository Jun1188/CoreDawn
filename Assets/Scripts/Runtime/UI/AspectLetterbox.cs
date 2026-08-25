using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 화면을 16:9로 유지하는 레터박스 — 화면비가 다르면 위아래(레터)나 좌우(필러)에 검은 띠를 채운다.
///
/// 적용은 세 겹이다:
///  1) 모든 Base 카메라의 뷰포트를 16:9 영역으로 줄인다 (Overlay 카메라도 직접 줄인다)
///  2) 띠 자리는 <b>최상위 UITK 패널의 검은 상자</b>가 매 프레임 덮는다
///  3) 게임 UITK 문서는 뷰포트 개념이 없어 루트를 같은 16:9 영역으로 인셋한다
///
/// 2)가 카메라가 아니라 UI인 이유: 예전에는 최하 순위 블랙 카메라가 띠를 칠했는데, 프레임당
/// 수 ms를 먹어 뷰포트가 바뀐 직후 0.5초만 켰다. 그러면 그 뒤로 띠 자리는 아무도 다시 그리지
/// 않는 화면이 된다 — 인셋된 UI 루트는 오버플로를 자르지 않으므로, 카드가 미끄러져 들어오고
/// 나갈 때 띠 위를 지나간 픽셀이 영영 남았다(잔상). UI 상자는 매 프레임 그려지니 무엇이 지나가든
/// 다음 프레임에 덮이고, 카메라 하나 몫의 URP 패스도 사라진다.
///
/// 카메라·문서는 씬 전환마다 바뀌므로 소유하지 않고 저빈도로 다시 찾는다.
/// 타이틀·게임 어느 씬에서든 동작해야 하므로 부팅 시 스스로 생겨 영속한다.
/// </summary>
public class AspectLetterbox : MonoBehaviour
{
    const float TargetAspect = 16f / 9f;
    const float Epsilon = 0.001f;

    /// <summary>게임 패널들보다 뒤에(위에) 그려지도록 — GameUI 패널 정렬값보다 확실히 크게.</summary>
    const float BarsSortingOrder = 1000f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<AspectLetterbox>() != null) return;
        var go = new GameObject(nameof(AspectLetterbox));
        DontDestroyOnLoad(go);
        go.AddComponent<AspectLetterbox>();
    }

    Rect viewport = new(0f, 0f, 1f, 1f);
    int lastW, lastH;
    float nextSweep;   // 새로 생긴 카메라·UIDocument를 줍는 저빈도 스캔

    // 띠 — 자체 패널(최상위)에 사는 검은 상자 넷. 필러면 좌우, 레터면 상하만 두께가 생긴다
    UIDocument barsDoc;
    VisualElement barTop, barBottom, barLeft, barRight;

    bool HasBars => viewport.width < 1f - Epsilon || viewport.height < 1f - Epsilon;

    void LateUpdate()
    {
        bool changed = Screen.width != lastW || Screen.height != lastH;

        if (changed || Time.unscaledTime >= nextSweep)
        {
            nextSweep = Time.unscaledTime + 0.5f;

            if (changed)
            {
                lastW = Screen.width;
                lastH = Screen.height;
                Recompute();
            }

            ApplyCameras();
            ApplyDocuments();
        }

        // 띠 패널은 UIDocument가 루트를 만들 때까지 한두 프레임 걸릴 수 있어 매 프레임 시도한다
        if (barTop == null) EnsureBars();
    }

    void Recompute()
    {
        float aspect = lastH > 0 ? (float)lastW / lastH : TargetAspect;

        if (aspect > TargetAspect + Epsilon)        // 더 넓다 — 좌우 필러박스
        {
            float w = TargetAspect / aspect;
            viewport = new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
        else if (aspect < TargetAspect - Epsilon)   // 더 좁다 — 상하 레터박스
        {
            float h = aspect / TargetAspect;
            viewport = new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }
        else viewport = new Rect(0f, 0f, 1f, 1f);

        LayoutBars();
    }

    void ApplyCameras()
    {
        foreach (var cam in Camera.allCameras)
        {
            // Overlay 카메라(무기 뷰모델)도 직접 줄인다 — URP 스택의 오버레이는 베이스의
            // 뷰포트를 상속하지 않아서, 빼놓으면 무기만 검은 띠 위에 그려진다.
            if (cam.rect != viewport) cam.rect = viewport;
        }
    }

    // ───────────────────────── 띠 패널 ─────────────────────────

    void EnsureBars()
    {
        if (barsDoc == null)
        {
            // 패널 설정은 에셋 참조 없이 런타임에 만든다 — 이 컴포넌트는 씬 배선 없이 스스로 생기므로.
            // ConstantPixelSize(배율 1) = 패널 단위가 곧 화면 px라 띠 두께를 그대로 놓을 수 있다.
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "LetterboxPanelSettings";
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.sortingOrder = BarsSortingOrder;
            settings.clearColor = false;   // 띠 밖은 투명 — 아래 패널·카메라가 그대로 보인다

            // 테마는 인라인 배경색만 쓰는 상자에 필요 없지만, 비어 있으면 경고를 내는 버전이 있어 빌려 둔다
            var any = FindFirstObjectByType<UIDocument>();
            if (any != null && any.panelSettings != null) settings.themeStyleSheet = any.panelSettings.themeStyleSheet;

            var go = new GameObject("LetterboxBars");
            go.transform.SetParent(transform, false);
            barsDoc = go.AddComponent<UIDocument>();
            barsDoc.panelSettings = settings;
        }

        var root = barsDoc.rootVisualElement;
        if (root == null) return;   // 아직 패널이 안 만들어졌다 — 다음 프레임에

        root.pickingMode = PickingMode.Ignore;   // 띠는 입력을 가로채지 않는다
        root.style.position = Position.Absolute;
        root.style.left = 0; root.style.right = 0; root.style.top = 0; root.style.bottom = 0;

        barTop    = MakeBar(root);
        barBottom = MakeBar(root);
        barLeft   = MakeBar(root);
        barRight  = MakeBar(root);

        LayoutBars();
    }

    static VisualElement MakeBar(VisualElement root)
    {
        var bar = new VisualElement { name = "letterbox-bar", pickingMode = PickingMode.Ignore };
        bar.style.position = Position.Absolute;
        bar.style.backgroundColor = Color.black;
        root.Add(bar);
        return bar;
    }

    /// <summary>뷰포트(정규화)를 화면 px 두께로 바꿔 상자 넷을 놓는다. 두께 0인 쪽은 감춘다.</summary>
    void LayoutBars()
    {
        if (barTop == null) return;

        float barH = viewport.y * lastH;   // 레터박스 — 위아래 두께
        float barW = viewport.x * lastW;   // 필러박스 — 좌우 두께

        Place(barTop,    left: 0, right: 0, top: 0,    bottom: null, width: null, height: barH);
        Place(barBottom, left: 0, right: 0, top: null, bottom: 0,    width: null, height: barH);
        Place(barLeft,   left: 0, right: null, top: 0, bottom: 0,    width: barW, height: null);
        Place(barRight,  left: null, right: 0, top: 0, bottom: 0,    width: barW, height: null);
    }

    static void Place(VisualElement bar, float? left, float? right, float? top, float? bottom, float? width, float? height)
    {
        bar.style.left   = left.HasValue   ? left.Value   : StyleKeyword.Auto;
        bar.style.right  = right.HasValue  ? right.Value  : StyleKeyword.Auto;
        bar.style.top    = top.HasValue    ? top.Value    : StyleKeyword.Auto;
        bar.style.bottom = bottom.HasValue ? bottom.Value : StyleKeyword.Auto;
        bar.style.width  = width.HasValue  ? width.Value  : StyleKeyword.Auto;
        bar.style.height = height.HasValue ? height.Value : StyleKeyword.Auto;

        float thickness = width ?? height ?? 0f;
        bar.style.display = thickness > 0.5f ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ───────────────────────── 게임 UI 문서 인셋 ─────────────────────────

    void ApplyDocuments()
    {
        // 띠 두께(스크린 px)를 패널 단위로 환산해 문서 루트를 절대 배치 인셋으로 가둔다.
        // 패딩으로는 안 된다 — 화면들(.hud-root 등)이 position: absolute 라 부모의 패딩 박스를
        // 무시하고 보더 박스에 붙는다. 루트 자체를 16:9 영역 크기로 줄여야 안의 절대 배치가
        // 전부 그 영역 기준이 된다. 패널 스케일 모드가 무엇이든 ScreenToPanel이 배율을 흡수한다.
        foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
        {
            if (doc == barsDoc) continue;   // 띠 패널은 전체 화면이어야 한다

            var root = doc.rootVisualElement;
            if (root == null || root.panel == null) continue;

            Vector2 origin = RuntimePanelUtils.ScreenToPanel(root.panel, Vector2.zero);
            Vector2 corner = RuntimePanelUtils.ScreenToPanel(root.panel,
                new Vector2(viewport.x * lastW, viewport.y * lastH));

            float padX = Mathf.Abs(corner.x - origin.x);
            float padY = Mathf.Abs(corner.y - origin.y);

            root.style.position = Position.Absolute;
            root.style.left = padX;
            root.style.right = padX;
            root.style.top = padY;
            root.style.bottom = padY;
        }
    }
}
