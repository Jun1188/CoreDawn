// URP 변환본 (원본: Built-in surface shader — `#pragma surface surf Lambert`)
// 셰이더 이름 / 프로퍼티 이름은 원본과 동일하게 유지 — 기존 머티리얼 호환.
Shader "PolygonArsenal/PolyRimLightTransparent"
{
    Properties
    {
        _InnerColor("Inner Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _RimColor("Rim Color", Color) = (0.26, 0.19, 0.16, 0.0)
        _RimWidth("Rim Width", Range(0.2, 20.0)) = 3.0
        _RimGlow("Rim Glow Multiplier", Range(0.0, 9.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "SimpleLit"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            Blend One One
            // 원본 서피스 셰이더는 ZWrite 를 명시하지 않아 기본값 On 으로 렌더링됐다. 동작 보존.
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex RimVertex
            #pragma fragment RimFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _InnerColor;
                half4 _RimColor;
                half  _RimWidth;
                half  _RimGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4  fogFactorAndVertexLight : TEXCOORD2;
            #else
                half   fogFactor  : TEXCOORD2;
            #endif
                half3  vertexSH   : TEXCOORD3;
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD4;
            #endif
                half4  color      : COLOR;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings RimVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionWS = vertexInput.positionWS;
                output.positionCS = vertexInput.positionCS;
                output.normalWS = normalInput.normalWS;
                output.color = input.color;

                half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                output.fogFactorAndVertexLight = half4(fogFactor, VertexLighting(vertexInput.positionWS, normalInput.normalWS));
            #else
                output.fogFactor = fogFactor;
            #endif

                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz,
                           GetWorldSpaceNormalizeViewDir(vertexInput.positionWS),
                           output.vertexSH, output.probeOcclusion);

                return output;
            }

            void InitializeInputData(Varyings input, out InputData inputData)
            {
                inputData = (InputData)0;

                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif

            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
            #else
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
            #endif

                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

            #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
                inputData.bakedGI = SAMPLE_GI(input.vertexSH,
                    GetAbsolutePositionWS(inputData.positionWS),
                    inputData.normalWS,
                    inputData.viewDirectionWS,
                    input.positionCS.xy,
                    input.probeOcclusion,
                    inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(0.0, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = half4(1, 1, 1, 1);
            #endif
            }

            half4 RimFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                InputData inputData;
                InitializeInputData(input, inputData);

                half rim = 1.0h - saturate(dot(inputData.viewDirectionWS, inputData.normalWS));

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = _InnerColor.rgb;
                surfaceData.emission   = _RimColor.rgb * _RimGlow * pow(rim, _RimWidth);
                surfaceData.specular   = half3(0.0h, 0.0h, 0.0h);
                surfaceData.smoothness = 0.0h;
                surfaceData.occlusion  = 1.0h;
                surfaceData.normalTS   = half3(0.0h, 0.0h, 1.0h);
                // 원본은 opaque 서피스 셰이더(keepalpha 없음)라 알파가 항상 1로 강제됐다. 동작 보존.
                surfaceData.alpha      = 1.0h;

                half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }
    }
}
