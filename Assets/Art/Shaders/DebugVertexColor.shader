// 디버그 라인 메시 전용 — 정점 색을 그대로 뿌린다.
//
// 플로우필드 시각화(FlowFieldDebugView)가 화살표 수천 개를 선분 메시 하나로 뭉쳐 그린다.
// 색이 칸마다 다르므로(비용 그라디언트) 머티리얼이 아니라 정점 색으로 실어 보낸다 —
// 그래야 드로우콜이 하나로 끝난다.
//
// 깊이를 무시하는 이유는 PortFlow와 같다: 잔디·건물에 묻히면 디버그 표시의 값이 없다.
Shader "CoreDawn/DebugVertexColor"
{
    Properties
    {
        _Alpha ("Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "DebugVertexColor"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
            };

            float _Alpha;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return half4(i.color.rgb, i.color.a * _Alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
