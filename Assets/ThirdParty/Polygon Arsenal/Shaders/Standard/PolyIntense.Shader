// URP 변환본 (원본: Built-in 파티클 애디티브 셰이더)
// 셰이더 이름 / 프로퍼티 이름은 원본과 동일하게 유지 — 기존 머티리얼 호환.
Shader "PolygonArsenal/PolyIntense"
{
    Properties
    {
        _TintColor("Tint Color", Color) = (0.5, 0.5, 0.5, 0.5)
        _MainTex("Particle Texture", 2D) = "white" {}
        _InvFade("Soft Particles Factor", Range(0.01, 3.0)) = 1.0
        _Glow("Intensity", Range(0, 5)) = 1

        // 원본의 multi_compile_particles(SOFTPARTICLES_ON)는 Built-in 품질설정 전역 키워드였다.
        // URP에는 대응 전역 키워드가 없어 머티리얼 토글로 옮긴다. (기본 Off = 원본 기본 동작)
        [Toggle(_SOFTPARTICLES_ON)] _SoftParticlesEnabled("Soft Particles", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Blend One One
        ColorMask RGB
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            // vertex/fragment 양쪽에서 projPos 보간자를 켜야 하므로 stage 한정자를 쓰지 않는다.
            #pragma shader_feature_local _SOFTPARTICLES_ON
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _TintColor;
                half   _Glow;
                float  _InvFade;
                float  _SoftParticlesEnabled;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 texcoord   : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
            #if defined(_SOFTPARTICLES_ON)
                float4 projPos    : TEXCOORD2;
            #endif
                half4  color      : COLOR;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;

            #if defined(_SOFTPARTICLES_ON)
                output.projPos = vertexInput.positionNDC;
                output.projPos.z = -TransformWorldToView(vertexInput.positionWS).z; // COMPUTE_EYEDEPTH 대응
            #endif

                output.color = input.color;
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 vertexColor = input.color;

            #if defined(_SOFTPARTICLES_ON)
                // URP 에셋에서 Depth Texture가 켜져 있어야 동작한다.
                float2 screenUV = input.projPos.xy / input.projPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float partZ = input.projPos.z;
                float fade = saturate(_InvFade * (sceneZ - partZ));
                vertexColor.a *= fade;
            #endif

                half4 col = 2.0h * vertexColor;
                col *= _Glow * _TintColor * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texcoord);

                // 애디티브 블렌드이므로 포그는 검정으로 수렴시킨다 (원본과 동일).
                col.rgb = MixFogColor(col.rgb, half3(0.0h, 0.0h, 0.0h), input.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }
}
