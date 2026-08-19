using UnityEngine;
using UnityEngine.UI;

// 대상 머리 위에 떠서 플레이어 카메라를 바라보는 월드스페이스 HP 바.
// 체력 반영은 기존 HealthBarUI(Entity.OnHealthChanged → fillImage.fillAmount)를 그대로 쓰고,
// 이 클래스는 캔버스 생성·높이 배치·빌보드·표시 여부만 맡는다.
// 프리팹 없이 코드로 세우는 이유: 몬스터·둥지 프리팹을 건드리지 않고
// Entity 쪽 코드만으로 전 대상에 일괄 부착하기 위해서다.
public class WorldHealthBar : MonoBehaviour
{
    const float BarWidth = 1.1f;        // 일반 몬스터 바 폭(m)
    const float BossBarWidth = 1.8f;    // 보스·둥지 코어 바 폭(m)
    const float BarHeight = 0.14f;
    const float SecondaryBarHeight = 0.07f; // 보조 게이지(인내심)는 체력바보다 얇게
    const float BarGap = 0.03f;             // 두 바 사이 간격(m)
    const float MaxVisibleDistance = 45f;   // 이보다 멀면 숨김 — 원거리 픽셀 노이즈·드로우 절약

    private Entity entity;
    private Transform anchor;       // 바가 따라다닐 기준 (몬스터 루트 또는 둥지 코어)
    private float heightOffset;
    private GameObject visualRoot;  // 표시/숨김 토글 대상 (컴포넌트 자신은 계속 살아 판정한다)
    private Camera cam;
    private float nextCameraSearch; // 카메라 교체(사망 리스폰 등) 대비 저빈도 재탐색
    private float barWidth;         // 보조 바를 같은 폭으로 세우기 위해 기억해 둔다

    // 보조 게이지(인내심 등) — 값의 출처는 소유자가 넘긴 델리게이트다.
    // 체력처럼 이벤트로 밀어 넣지 않고 폴링하는 이유는, 인내심이 매 프레임 연속적으로
    // 변해 이벤트를 쏘면 오히려 낭비이기 때문이다. 바는 하나뿐이라 폴링이 더 싸다.
    private System.Func<float> secondaryRatio;
    private GameObject secondaryRoot;
    private Image secondaryFill;

    // Image에 스프라이트가 없으면 Filled 타입 채우기가 그려지지 않으므로 1x1 흰색을 만들어 공유한다
    private static Sprite whiteSprite;
    private static Sprite WhiteSprite
    {
        get
        {
            if (whiteSprite == null)
            {
                var tex = Texture2D.whiteTexture;
                whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                            new Vector2(0.5f, 0.5f));
            }
            return whiteSprite;
        }
    }

    /// <summary>
    /// 대상 엔티티 위에 HP 바를 세운다. anchor를 주면 그 위(둥지 코어 등), 없으면 엔티티 루트 위.
    /// 이미 붙어 있으면 그대로 반환한다 — 리스폰·복구 흐름에서 중복 생성 방지.
    /// </summary>
    public static WorldHealthBar Attach(Entity target, Transform anchor = null, bool large = false)
    {
        if (target == null) return null;

        var existing = target.GetComponentInChildren<WorldHealthBar>(true);
        if (existing != null) return existing;

        var go = new GameObject("WorldHealthBar");
        go.transform.SetParent(target.transform, false);

        var bar = go.AddComponent<WorldHealthBar>();
        bar.entity = target;
        bar.anchor = anchor != null ? anchor : target.transform;

        // 머리 높이 — 앵커 소속 콜라이더의 꼭대기에서 약간 위. 콜라이더가 없으면 2m.
        var col = bar.anchor.GetComponentInChildren<Collider>();
        if (col == null) col = target.GetComponentInChildren<Collider>();
        bar.heightOffset = col != null
            ? (col.bounds.max.y - bar.anchor.position.y) + 0.35f
            : 2f;

        bar.Build(large ? BossBarWidth : BarWidth);
        return bar;
    }

    /// <summary>
    /// 체력바 바로 아래에 보조 게이지를 단다(보스의 인내심 등). ratio01이 1이면 자동으로 숨는다 —
    /// 평시엔 만땅이라 계속 떠 있으면 잡음일 뿐이고, 닳기 시작할 때 나타나야 의미가 전달된다.
    /// 같은 바에 두 번 부르면 마지막 것으로 갱신된다.
    /// </summary>
    public void EnableSecondaryBar(System.Func<float> ratio01, Color color)
    {
        if (ratio01 == null) return;
        secondaryRatio = ratio01;

        if (secondaryRoot == null) BuildSecondary(color);
        else if (secondaryFill != null) secondaryFill.color = color;
    }

    private void BuildSecondary(Color color)
    {
        secondaryRoot = new GameObject("SecondaryCanvas");
        secondaryRoot.transform.SetParent(transform, false);
        // 메인 캔버스와 같은 로컬 스케일(0.01)이므로 오프셋은 m 단위 그대로 쓴다.
        secondaryRoot.transform.localPosition = new Vector3(0f, -(BarHeight * 0.5f + BarGap + SecondaryBarHeight * 0.5f), 0f);

        var canvas = secondaryRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRect = secondaryRoot.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(barWidth * 100f, SecondaryBarHeight * 100f);
        canvasRect.localScale = Vector3.one * 0.01f;   // 100px = 1m

        var bg = CreateImage(secondaryRoot.transform, "BG", new Color(0f, 0f, 0f, 0.55f));
        Stretch(bg.rectTransform, 0f);

        secondaryFill = CreateImage(secondaryRoot.transform, "Fill", color);
        Stretch(secondaryFill.rectTransform, 1.5f);
        secondaryFill.type = Image.Type.Filled;
        secondaryFill.fillMethod = Image.FillMethod.Horizontal;
        secondaryFill.fillOrigin = (int)Image.OriginHorizontal.Left;
    }

    private void Build(float width)
    {
        barWidth = width;
        visualRoot = new GameObject("Canvas");
        visualRoot.transform.SetParent(transform, false);

        var canvas = visualRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRect = visualRoot.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(width * 100f, BarHeight * 100f);
        canvasRect.localScale = Vector3.one * 0.01f;   // 100px = 1m

        var bg = CreateImage(visualRoot.transform, "BG", new Color(0f, 0f, 0f, 0.55f));
        Stretch(bg.rectTransform, 0f);

        var fill = CreateImage(visualRoot.transform, "Fill", new Color(0.85f, 0.15f, 0.12f, 0.95f));
        Stretch(fill.rectTransform, 1.5f);   // 배경 테두리가 살짝 보이게 안쪽으로
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;

        // 체력 반영은 기존 컴포넌트에 맡긴다 — 갱신 경로를 한 곳(HealthBarUI)으로 유지
        gameObject.AddComponent<HealthBarUI>().Bind(entity, fill);
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = WhiteSprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    private void LateUpdate()
    {
        if (entity == null || anchor == null) { Destroy(gameObject); return; }

        if (cam == null && Time.time >= nextCameraSearch)
        {
            nextCameraSearch = Time.time + 1f;
            cam = Camera.main;
        }

        bool visible = !entity.IsDead && cam != null;
        if (visible)
        {
            Vector3 pos = anchor.position + Vector3.up * heightOffset;
            float dist = Vector3.Distance(cam.transform.position, pos);
            visible = dist <= MaxVisibleDistance;
            if (visible)
            {
                transform.position = pos;
                transform.rotation = cam.transform.rotation;   // 카메라 정면 빌보드
            }
        }

        if (visualRoot != null && visualRoot.activeSelf != visible)
            visualRoot.SetActive(visible);

        UpdateSecondary(visible);
    }

    private void UpdateSecondary(bool ownerVisible)
    {
        if (secondaryRoot == null) return;

        float ratio = secondaryRatio != null ? Mathf.Clamp01(secondaryRatio()) : 1f;
        // 만땅이면 보여줄 게 없다 — 메인 바가 숨을 때도 같이 숨는다.
        bool show = ownerVisible && ratio < 0.999f;

        if (secondaryRoot.activeSelf != show) secondaryRoot.SetActive(show);
        if (show && secondaryFill != null) secondaryFill.fillAmount = ratio;
    }
}
