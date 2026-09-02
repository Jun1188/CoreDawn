// ─────────────────────────────────────────────────────────────────────────────
//  CoreDawn/Ground — 런타임 지면 청크(WorldTerrainBuilder) 전용.
//
//  Unity Terrain의 스플랫 3층을 물가 한 겹으로 줄인 후계다(사용자 결정 2026-09-02:
//  "정점색은 물가에만 있으면 될 것 같은데"): 정점색 R(강바닥 가중치 — 빌더가 높이에서 굽는다)로
//  잔디 ↔ 강바닥(모래) 두 텍스처만 섞는다. 절벽 바위색 번짐은 폐기 — 바위 틈에 잔디가
//  자라는 것이 오히려 자연스럽다.
//
//  조명은 손으로 쓴 램버트 + SH + 그림자 + 추가 광원(총구 화염 등) + 포그 — 매트한 지면이라
//  URP Lit의 전체 PBR과 달라도 티가 안 난다. 물 셰이더가 깊이 텍스처를 읽을 수 있어
//  DepthOnly, 그림자 드리움용 ShadowCaster 패스를 같이 둔다.
//  UV는 빌더가 월드 좌표 ÷ 잔디 타일 크기로 채운다 — _BedUvScale은 그 비(잔디 타일/모래 타일).
// ─────────────────────────────────────────────────────────────────────────────
Shader "CoreDawn/Ground"
{
    Properties
    {
        [MainTexture] _BaseMap("잔디 텍스처", 2D) = "white" {}
        _BedMap("강바닥(모래) 텍스처", 2D) = "white" {}
        _BedUvScale("모래 UV 배율 (잔디 타일 / 모래 타일)", Float) = 1.0
        [MainColor] _BaseColor("전체 틴트", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            // 이 프로젝트의 URP 렌더러는 디퍼드다 — GBuffer 패스가 없는 셰이더는 UniversalForward로는
            // 스킵된다(지면이 통째로 안 그려지던 원인). ForwardOnly면 디퍼드에서도 포워드 단계에 그려진다.
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex GroundVertex
            #pragma fragment GroundFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BedMap); SAMPLER(sampler_BedMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BedUvScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4 color       : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
                half fogFactor    : TEXCOORD3;
                half bed          : TEXCOORD4;
            };

            Varyings GroundVertex(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                o.bed = input.color.r;
                return o;
            }

            half4 GroundFragment(Varyings input) : SV_Target
            {
                half3 grass = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 bed = SAMPLE_TEXTURE2D(_BedMap, sampler_BedMap, input.uv * _BedUvScale).rgb;
                half3 albedo = lerp(grass, bed, input.bed) * _BaseColor.rgb;

                half3 normal = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 light = SampleSH(normal);
                light += mainLight.color * mainLight.shadowAttenuation
                       * saturate(dot(normal, mainLight.direction));

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, input.positionWS);
                    light += l.color * l.distanceAttenuation * l.shadowAttenuation
                           * saturate(dot(normal, l.direction));
                }
                #endif

                half3 color = albedo * light;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                float3 lightDir = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half DepthOnlyFragment(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }
}
