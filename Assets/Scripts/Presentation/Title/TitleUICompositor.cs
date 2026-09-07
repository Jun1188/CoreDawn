using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreDawn.Title
{
    /// <summary>
    /// 타이틀 UI 에 Bloom 을 먹인다(2026-09-07 사용자 "ui 에는 bloom 이 안 들어간다").
    /// UI Toolkit 은 포스트프로세싱 뒤에 화면에 직접 그려져 볼륨 효과 밖이다. 그래서 타이틀 패널을 화면 크기 RenderTexture 로 그리게 하고
    /// (<see cref="PanelSettings.targetTexture"/>), 그 텍스처를 카메라 앞 전체 화면 쿼드(<see cref="Material"/>: 프리멀티플라이 알파 + HDR 부스트)로
    /// 씬 안에 얹는다 — 카메라 컬러 버퍼에 들어가므로 Bloom 이 UI 의 밝은 부분(청록 글자·선·테두리)을 번지게 한다.
    /// <para>레터박스(<see cref="UI.AspectLetterbox"/>)가 카메라 뷰포트를 줄이면 쿼드는 뷰포트만 채우므로, 텍스처의 같은 뷰포트 영역만 샘플한다
    /// (UI 루트도 같은 영역으로 인셋되니 1:1). 마우스는 텍스처가 화면과 같은 크기라 화면 좌표(왼쪽 위 원점)를 그대로 패널 좌표로 넘긴다.</para>
    /// 한 프레임 지연: UITK 는 카메라 뒤에 패널을 그리므로 쿼드는 직전 프레임의 UI 를 보여준다(RT UI 의 통상적 지연).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(-50)]
    public sealed class TitleUICompositor : MonoBehaviour
    {
        [Tooltip("타이틀 전용 PanelSettings — 게임 공용(GameUIPanelSettings)과 나눠야 targetTexture 가 게임 UI 에 번지지 않는다")]
        [SerializeField] PanelSettings panel;
        [Tooltip("UI 합성 재질(CoreDawn/UI Composite) — 프리멀티플라이 알파, _Boost 로 HDR 부스트")]
        [SerializeField] Material material;
        [Tooltip("쿼드 거리(카메라 near 에 더함)")]
        [SerializeField] float distance = 0.05f;

        static readonly int MainTex = Shader.PropertyToID("_MainTex");

        Camera cam;
        RenderTexture rt;
        GameObject quad;
        MeshRenderer quadRenderer;
        Material runtimeMaterial;
        Func<Vector2, Vector2> screenToPanel;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            if (panel == null || material == null)
            {
                Debug.LogError("[TitleUICompositor] PanelSettings 또는 재질이 비었습니다 — UI Bloom 합성을 건너뜁니다.", this);
                enabled = false;
                return;
            }
            runtimeMaterial = new Material(material) { hideFlags = HideFlags.DontSave };
            EnsureTexture();

            // ScreenToPanel 이 넘겨주는 화면 좌표는 이미 왼쪽 위 원점(UI 규약)이고 텍스처가 화면과 같은 크기라 항등이면 된다.
            // y 를 뒤집으면 이중 반전 — 와이어 끝점이 세로로 뒤집혔다(2026-09-07 실측; 가운데 점 검사로는 안 잡힌다)
            screenToPanel = p => p;
            panel.SetScreenToPanelSpaceFunction(screenToPanel);

            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "TitleUIComposite";
            quad.hideFlags = HideFlags.DontSave;
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(cam.transform, false);
            quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = runtimeMaterial;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            quadRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            quadRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            FitQuad();
        }

        void OnDisable()
        {
            if (panel != null)
            {
                if (panel.targetTexture == rt) panel.targetTexture = null;   // 에셋을 더럽히지 않게 되돌린다
                panel.SetScreenToPanelSpaceFunction(null);
            }
            if (quad != null) Destroy(quad);
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
        }

        void LateUpdate()
        {
            EnsureTexture();
            FitQuad();
        }

        // 화면 크기의 텍스처 — 해상도가 바뀌면 다시 만든다
        void EnsureTexture()
        {
            int w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            if (rt != null && rt.width == w && rt.height == h) return;
            if (rt != null) { rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "TitleUI", hideFlags = HideFlags.DontSave };
            rt.Create();
            panel.clearColor = true;
            panel.colorClearValue = Color.clear;
            panel.targetTexture = rt;
            runtimeMaterial.SetTexture(MainTex, rt);
        }

        // 쿼드가 카메라 절두체(=뷰포트)를 꽉 채우고, 텍스처는 뷰포트에 해당하는 영역만 샘플한다
        void FitQuad()
        {
            if (quad == null) return;
            float d = cam.nearClipPlane + distance;
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * cam.aspect;
            quad.transform.localPosition = new Vector3(0f, 0f, d);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(w, h, 1f);
            var r = cam.rect;
            runtimeMaterial.SetTextureOffset(MainTex, new Vector2(r.x, r.y));
            runtimeMaterial.SetTextureScale(MainTex, new Vector2(r.width, r.height));
        }
    }
}
