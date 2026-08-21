using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

/// <summary>
/// 화면을 16:9로 유지하는 레터박스 — 화면비가 다르면 위아래(레터)나 좌우(필러)에 검은 띠를 채운다.
///
/// 적용은 세 겹이다:
///  1) 모든 Base 카메라의 뷰포트를 16:9 영역으로 줄인다 (Overlay 카메라는 Base를 따라간다)
///  2) 띠 영역은 아무도 안 그리므로 최하 순위의 블랙 카메라가 전체를 먼저 검게 깐다
///  3) UITK 패널은 뷰포트 개념이 없어 UIDocument 루트에 패딩으로 같은 영역을 잡는다
///
/// 카메라·문서는 씬 전환마다 바뀌므로 소유하지 않고 저빈도로 다시 찾는다.
/// 타이틀·게임 어느 씬에서든 동작해야 하므로 부팅 시 스스로 생겨 영속한다.
/// </summary>
public class AspectLetterbox : MonoBehaviour
{
    const float TargetAspect = 16f / 9f;
    const float Epsilon = 0.001f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<AspectLetterbox>() != null) return;
        var go = new GameObject(nameof(AspectLetterbox));
        DontDestroyOnLoad(go);
        go.AddComponent<AspectLetterbox>();
    }

    Camera blackout;
    Rect viewport = new(0f, 0f, 1f, 1f);
    int lastW, lastH;
    float nextSweep;   // 새로 생긴 카메라·UIDocument를 줍는 저빈도 스캔

    bool HasBars => viewport.width < 1f - Epsilon || viewport.height < 1f - Epsilon;

    void LateUpdate()
    {
        bool changed = Screen.width != lastW || Screen.height != lastH;
        if (!changed && Time.unscaledTime < nextSweep) return;
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
    }

    void ApplyCameras()
    {
        EnsureBlackout();

        foreach (var cam in Camera.allCameras)
        {
            if (cam == blackout) continue;
            // Overlay 카메라(무기 뷰모델)도 직접 줄인다 — URP 스택의 오버레이는 베이스의
            // 뷰포트를 상속하지 않아서, 빼놓으면 무기만 검은 띠 위에 그려진다.
            if (cam.rect != viewport) cam.rect = viewport;
        }
    }

    void EnsureBlackout()
    {
        if (blackout == null)
        {
            var go = new GameObject("LetterboxBlackout");
            go.transform.SetParent(transform, false);
            blackout = go.AddComponent<Camera>();
            blackout.clearFlags = CameraClearFlags.SolidColor;
            blackout.backgroundColor = Color.black;
            blackout.cullingMask = 0;          // 아무것도 그리지 않는다 — 클리어가 일이다
            blackout.depth = -100f;            // 누구보다 먼저 렌더 — 그 위에 본 화면이 얹힌다
            blackout.rect = new Rect(0f, 0f, 1f, 1f);

            var data = blackout.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
        }

        if (blackout.enabled != HasBars) blackout.enabled = HasBars;
    }

    void ApplyDocuments()
    {
        // 띠 두께(스크린 px)를 패널 단위로 환산해 문서 루트를 절대 배치 인셋으로 가둔다.
        // 패딩으로는 안 된다 — 화면들(.hud-root 등)이 position: absolute 라 부모의 패딩 박스를
        // 무시하고 보더 박스에 붙는다. 루트 자체를 16:9 영역 크기로 줄여야 안의 절대 배치가
        // 전부 그 영역 기준이 된다. 패널 스케일 모드가 무엇이든 ScreenToPanel이 배율을 흡수한다.
        foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
        {
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
