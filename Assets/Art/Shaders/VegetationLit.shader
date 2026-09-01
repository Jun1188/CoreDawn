// ─────────────────────────────────────────────────────────────────────────────
//  TeamProj/Vegetation Lit — 바람에 흔들리는 식생을 URP 조명으로 그린다.
//
//  왜 새로 만들었나
//    에셋 팩의 "Idyllic Fantasy Nature/Vegetation"은 Lit 서브타깃이면서도 색을 전부
//    SurfaceDescription.Emission으로 내보내고 BaseColor는 (0,0,0)으로 비워 둔 커스텀
//    라이팅 셰이더였다. URP 입장에서 그 표면은 완전한 검정이라 곱해질 알베도가 없고,
//    Emission은 조명 뒤에 더해지므로 <b>조명 프로브도 반사 프로브도 그림자도 닿지
//    않는다</b>. 머티리얼 값으로는 손댈 수 없는 구조라 셰이더를 새로 세웠다.
//
//  구조 — URP 원본 패스를 그대로 쓴다
//    조명·GI·프로브·포그·인스턴싱 처리를 손으로 다시 쓰면 변형(키워드 조합)마다
//    조용히 어긋난다. 그래서 URP의 LitForwardPass.hlsl 등을 그대로 include 하고,
//    정점 함수 이름만 바꿔치기해 그 앞에 바람을 얹는다:
//        #define LitPassVertex LitPassVertexBase   → URP 원본이 Base라는 이름을 갖고
//        #undef  LitPassVertex                     → 우리가 진짜 진입점을 정의
//    표면(색·알파)만 VegetationLitInput.hlsl이 LitInput.hlsl 대신 제공한다.
//
//  같이 고친 것 — 정점 색 대신 높이로 굽힘 마스크를 만든다.
//  자세한 이유는 VegetationLitInput.hlsl의 VegetationBendMask 주석 참고.
// ─────────────────────────────────────────────────────────────────────────────
Shader "CoreDawn/Vegetation Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("잎 텍스처 (알파 = 모양)", 2D) = "white" {}
        [MainColor]   _BaseColor("전체 틴트", Color) = (1, 1, 1, 1)
        _Cutoff("알파 컷오프", Range(0.0, 1.0)) = 0.5

        [Header(Color)][Space(4)]
        _Custom_Color("텍스처 색 0  ←→  1 그라데이션", Range(0.0, 1.0)) = 0.0
        _Top_Color("끝 색", Color) = (1, 1, 1, 1)
        _Bottom_Color("뿌리 색", Color) = (1, 1, 1, 1)
        _Blend_Height("그라데이션 높이 (UV)", Range(0.01, 1.0)) = 0.5

        [Header(Surface)][Space(4)]
        _Smoothness("매끄러움", Range(0.0, 1.0)) = 0.12
        _Metallic("메탈릭", Range(0.0, 1.0)) = 0.0
        _Normal_Up("노멀을 위로 모으기", Range(0.0, 1.0)) = 0.6

        [Header(Wind)][Space(4)]
        _Wind_Strength("세기 (오브젝트 단위, 잎 끝 기준)", Range(0.0, 0.5)) = 0.07
        _Wind_Speed("속도", Range(0.0, 2.0)) = 0.25
        _Wind_Variation("포기별 위상 차이", Range(0.0, 1.0)) = 0.3
        _Wave_Scale("파장 (클수록 촘촘)", Range(0.0, 4.0)) = 1.0
        _Wind_Direction("방향 (XZ)", Vector) = (1, 0.35, 0, 0)
        _Bend_Start("굽힘 시작 높이 (오브젝트 Y)", Float) = 0.0
        _Bend_Height("굽힘 기준 높이 (오브젝트 Y)", Float) = 0.7

        // 숨김 — URP 공통 패스가 요구하는 값. 인스펙터에 노출할 이유가 없다.
        [HideInInspector] _Surface("__surface", Float) = 0.0
        [HideInInspector] _Cull("__cull", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "ShaderModel" = "2.0"
        }
        LOD 300

        // ── 본 패스 ─────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            // 머티리얼 키워드 — 잎은 언제나 알파 컷아웃이다
            #pragma shader_feature_local _ALPHATEST_ON

            // 파이프라인 키워드 — URP Lit.shader의 ForwardLit과 같은 집합.
            // 하나라도 빠지면 그 기능(추가 광원·프로브 블렌딩·SSAO 등)이 이 셰이더에서만
            // 조용히 죽는다. 목록을 줄이지 말 것.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // _SCREEN_SPACE_OCCLUSION은 일부러 선언하지 않는다 — 잔디는 SSAO를 받지 않는다.
            //
            // 잎은 알파 컷아웃으로 뚫린 얇은 판이라 SSAO가 보는 깊이·노멀이 실제 형상과 다르다.
            // 게다가 잎이 서로 촘촘히 겹쳐 있어 자기들끼리 가린 것으로 계산되고, 이 프로젝트의
            // SSAO는 Intensity 2로 세다 — 결과가 거의 0까지 내려간다. 이 씬은 앰비언트(0.82)가
            // 주광(1.15)과 맞먹는 광원이라 그게 죽으면 풀밭이 통째로 새까매진다(실측).
            //
            // 키워드를 선언하지 않으면 URP가 이 셰이더에 SSAO 변형을 만들지 않아
            // UniversalFragmentPBR의 SSAO 분기 자체를 타지 않는다. 머티리얼 값으로는 못 끈다 —
            // 적용이 SurfaceData가 아니라 그 분기 안에서 일어나기 때문이다.
            // (SSAO 설정의 After Opaque가 켜지면 화면 전체에 곱해지므로 이 방법이 안 통한다.
            //  지금은 꺼져 있다. 켜게 되면 여기 주석부터 다시 읽을 것.)
            //
            // <b>만드는</b> 쪽은 그대로다 — DepthNormals 패스가 살아 있어 잔디 아래 지면에는
            // 여전히 그늘이 진다. 그것까지 빼려면 DepthNormals 패스를 지워야 한다.
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_instancing

            #include "VegetationLitInput.hlsl"

            // URP 원본 정점 함수를 다른 이름으로 받아 두고, 그 앞에 바람을 얹는다
            #define LitPassVertex LitPassVertexBase
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            #undef LitPassVertex

            Varyings LitPassVertex(Attributes input)
            {
            // UNITY_SETUP_INSTANCE_ID를 먼저 부른다 — 바람이 TransformObjectToWorld로
            // 월드 위치를 읽는데, 인스턴싱이 켜져 있으면 unity_ObjectToWorld는 인스턴스
            // 인덱스가 정해진 뒤에야 유효하다. 순서를 어기면 쓰레기 행렬을 읽어 정점이
            // 화면 밖으로 날아가고, 컴파일은 멀쩡히 통과하므로 <b>아무것도 안 그려진다</b>.
            // (URP 원본 정점 함수도 안에서 한 번 더 부르지만, 두 번 불러도 무해하다.)
                UNITY_SETUP_INSTANCE_ID(input);
                input.positionOS.xyz = VegetationWindOS(input.positionOS.xyz);
                input.normalOS       = VegetationNormalOS(input.normalOS);
                return LitPassVertexBase(input);
            }
            ENDHLSL
        }

        // ── 그림자 ──────────────────────────────────────────────────────────
        //  그림자도 같은 바람을 먹여야 한다 — 안 그러면 흔들리는 잎이 가만히 있는
        //  그림자를 드리운다. (지금 식생 머티리얼은 이 패스를 꺼 두고 쓴다:
        //   수만 포기의 그림자는 얻는 것에 비해 너무 비싸다. 머티리얼별로 켤 수 있다.)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "VegetationLitInput.hlsl"

            #define ShadowPassVertex ShadowPassVertexBase
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            #undef ShadowPassVertex

            Varyings ShadowPassVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);   // ForwardLit 패스의 주석 참고
                input.positionOS.xyz = VegetationWindOS(input.positionOS.xyz);
                return ShadowPassVertexBase(input);
            }
            ENDHLSL
        }

        // ── 깊이 ────────────────────────────────────────────────────────────
        //  깊이도 흔들린 자리에서 써야 SSAO·안개·소프트 파티클이 잎을 제자리로 본다.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            #include "VegetationLitInput.hlsl"

            #define DepthOnlyVertex DepthOnlyVertexBase
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            #undef DepthOnlyVertex

            Varyings DepthOnlyVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);   // ForwardLit 패스의 주석 참고
                input.position.xyz = VegetationWindOS(input.position.xyz);
                return DepthOnlyVertexBase(input);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            #include "VegetationLitInput.hlsl"

            #define DepthNormalsVertex DepthNormalsVertexBase
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            #undef DepthNormalsVertex

            Varyings DepthNormalsVertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);   // ForwardLit 패스의 주석 참고
                input.positionOS.xyz = VegetationWindOS(input.positionOS.xyz);
                input.normal         = VegetationNormalOS(input.normal);
                return DepthNormalsVertexBase(input);
            }
            ENDHLSL
        }
    }

    // URP 관례대로 오류 셰이더로 떨어뜨린다. 예전엔 "Universal Render Pipeline/Lit"이었는데
    // 그러면 SubShader가 못 쓰이는 상황에서 <b>바람도 없고 SSAO도 받는</b> 전혀 다른 그림이
    // 조용히 대신 나온다 — 게다가 폴백의 키워드가 이 셰이더의 키워드 공간에까지 섞여 들어와
    // "SSAO를 빼 두었는가"를 검증할 수 없게 만든다. 안 되면 안 되는 티가 나는 편이 낫다.
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
