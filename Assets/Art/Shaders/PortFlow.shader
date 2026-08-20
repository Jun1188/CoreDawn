// 포트 흐름 시각화 — 건물 면에서 번지는 반투명 그라디언트.
// 파랑 = 입력, 귤색 = 출력. 화살표 아이콘 대신 흐름 자체를 보여준다.
//
// uv 규약 (PortFlowVisualizer가 이 규약에 맞춰 Quad를 눕힌다):
//   uv.x = 흐름 축. 0 = 건물 면에 붙은 쪽, 1 = 바깥 끝
//   uv.y = 가로 축. 바닥판·립은 좌우 폭, 지느러미는 높이
// 셰이더와 컴포넌트 양쪽을 다 뒤집으면 원위치이므로, 축을 손볼 일이 생기면 한쪽만 건드릴 것.

Shader "LevelUp/PortFlow"
{
    Properties
    {
        _Color    ("Color", Color) = (0.31, 0.847, 0.878, 1)   // 기본 = 입력 파랑
        _Power    ("Intensity", Range(0, 3)) = 1
        _Vertical ("Vertical (0=바닥판, 1=지느러미)", Range(0, 1)) = 0
        _Fade     ("Fade Exponent", Range(0, 4)) = 1.7
        _Alpha    ("Base Alpha", Range(0, 1)) = 0.55

        // 1 = One(가산, 번짐용) · 10 = OneMinusSrcAlpha(알파 합성, 짝대기용).
        // 출력이 알파 프리멀티플라이드라 이 인자 하나만 바꾸면 두 방식이 다 나온다.
        [HideInInspector] _DstBlend ("__dst", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "PortFlow"
            // 번짐은 가산(One One), 짝대기는 알파 합성(One OneMinusSrcAlpha).
            // 전부 가산으로 두면 겹친 자리의 알파가 더해져 채널이 1에 물리고 흰색으로 날아간다.
            Blend One [_DstBlend]
            ZWrite Off
            // 배치 보조 UI다 — 잔디(뎁스를 쓰는 컷아웃)가 바닥 쿼드를 가려서 힌트가
            // 풀숲에 통째로 묻혔다. 깊이를 무시하고 항상 위에 그린다. 건물 뒤 포트가
            // 비쳐 보이는 것은 감수가 아니라 덤이다 — 배치 중엔 그게 더 유용하다.
            ZTest Always
            Cull Off                 // 뒤에서 봐도 보여야 한다

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            // 블렌드 인자도 머티리얼 프로퍼티이므로 여기 들어가야 SRP Batcher 호환이 유지된다
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Power, _Vertical, _Fade, _Alpha, _DstBlend;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float along = 1.0 - i.uv.x;              // 면에서 멀어질수록 옅어진다
                float fade  = pow(along, _Fade);

                // 가장자리를 부드럽게: 바닥판은 좌우로, 지느러미는 위로 흐려진다
                float across = lerp(smoothstep(0.0, 0.22, i.uv.y) * smoothstep(1.0, 0.78, i.uv.y),
                                    pow(1.0 - i.uv.y, 1.2), _Vertical);

                float a = saturate(fade * across * _Alpha * _Power);
                clip(a - 0.004);

                // 알파 프리멀티플라이드 — 가산(One One)과 알파 합성(One OneMinusSrcAlpha) 양쪽에서
                // 그대로 맞는 형태다. half4(_Color.rgb, a)로 두면 가산 쪽 세기가 전혀 안 먹는다.
                return half4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
