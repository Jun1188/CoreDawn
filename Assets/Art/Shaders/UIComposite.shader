// 타이틀 UI 합성 — UI Toolkit 패널을 그린 RenderTexture(프리멀티플라이 알파)를 카메라 앞 쿼드에 얹는다.
// 포스트프로세싱(Bloom) 앞에 씬 컬러 버퍼로 들어가게 하는 것이 목적. _Boost 로 밝은 부분을 1 넘게 밀어 Bloom 문턱을 넘긴다.
// TitleUICompositor 가 쓴다(2026-09-07). 재질 에셋(Assets/Data/Rendering/TitleUIComposite.mat)이 참조하므로 빌드에 실린다.
Shader "CoreDawn/UI Composite"
{
    Properties
    {
        _MainTex ("UI (premultiplied)", 2D) = "black" {}
        _Boost ("HDR Boost", Float) = 1.6
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "UIComposite"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            Fog { Mode Off }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Boost;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;   // 레터박스 뷰포트 영역만 샘플(오프셋·스케일)
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                c.rgb *= _Boost;
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
