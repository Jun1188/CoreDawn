#ifndef TEAMPROJ_VEGETATION_LIT_INPUT_INCLUDED
#define TEAMPROJ_VEGETATION_LIT_INPUT_INCLUDED

// ─────────────────────────────────────────────────────────────────────────────
//  VegetationLit — 입력·표면·바람
//
//  URP의 LitInput.hlsl 자리를 대신한다. URP 패스 파일(LitForwardPass·ShadowCasterPass·
//  DepthOnlyPass·DepthNormalsPass)이 이 헤더에 요구하는 것은 넷뿐이다:
//    _BaseMap / _BaseColor / _Cutoff / _Surface 와 InitializeStandardLitSurfaceData().
//  나머지는 우리 것이다.
//
//  왜 LitInput.hlsl을 안 쓰고 새로 쓰는가: 그쪽 CBUFFER(UnityPerMaterial)는 이미 닫혀
//  있어서 우리 프로퍼티를 넣을 자리가 없다. 밖에 선언하면 SRP Batcher 호환이 깨진다.
// ─────────────────────────────────────────────────────────────────────────────

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half4  _Top_Color;
    half4  _Bottom_Color;
    float4 _Wind_Direction;
    half   _Custom_Color;
    half   _Blend_Height;
    half   _Cutoff;
    half   _Smoothness;
    half   _Metallic;
    half   _Normal_Up;
    half   _Wind_Strength;
    half   _Wind_Speed;
    half   _Wind_Variation;
    half   _Wave_Scale;
    half   _Bend_Start;
    half   _Bend_Height;
    half   _Surface;
CBUFFER_END

// ── 표면 ─────────────────────────────────────────────────────────────────────

/// URP LitForwardPass가 부르는 단 하나의 표면 함수.
/// 색은 <b>BaseColor로</b> 나간다 — 원래 에셋 팩 셰이더가 전부 Emission으로 내보내던
/// 것이 조명·조명 프로브·반사 프로브가 하나도 닿지 않던 이유였다. 알베도가 0인 표면에는
/// 곱해질 빛이 없고, Emission은 조명 <em>뒤에</em> 더해지기 때문이다.
void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    outSurfaceData = (SurfaceData)0;

    half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    outSurfaceData.alpha = Alpha(tex.a, _BaseColor, _Cutoff);

    // 위아래 그라데이션 — 잔디의 색은 텍스처가 아니라 이 두 색이 만든다(뿌리 어둡게, 끝 밝게).
    // _Custom_Color가 "텍스처 색 ↔ 그라데이션" 사이의 손잡이다: 잔디는 1(그라데이션만),
    // 꽃은 0(텍스처 그대로). 원본 셰이더의 켬/끔 토글을 연속값으로 편 것뿐이다.
    half t = saturate(uv.y / max(1e-4h, _Blend_Height));
    half3 gradient = lerp(_Bottom_Color.rgb, _Top_Color.rgb, t);

    outSurfaceData.albedo     = lerp(tex.rgb, gradient, saturate(_Custom_Color)) * _BaseColor.rgb;
    outSurfaceData.metallic   = _Metallic;
    outSurfaceData.specular   = half3(0.0h, 0.0h, 0.0h);
    outSurfaceData.smoothness = _Smoothness;
    outSurfaceData.normalTS   = half3(0.0h, 0.0h, 1.0h);
    outSurfaceData.occlusion  = 1.0h;
    outSurfaceData.emission   = half3(0.0h, 0.0h, 0.0h);
}

// ── 바람 ─────────────────────────────────────────────────────────────────────

/// <summary>
/// 굽힘 마스크 — 뿌리 0, 끝 1. <b>오브젝트 공간 높이</b>로 만든다.
///
/// 원본 셰이더는 이걸 정점 색에서 읽었는데, 우리가 쓰는 잔디 메시(GrassBundle_01 등)에는
/// 정점 색이 아예 없다(colors=0). 게다가 터레인 디테일은 프로토타입의 healthy/dry 색을
/// 정점 색에 덮어써서, 있더라도 잎 전체가 같은 값이 된다. 그래서 뿌리까지 같은 양으로
/// 밀려 <b>휘는 대신 통째로 평행이동</b>했고, 그게 "바람이 아니라 진동"으로 보이던 정체다.
/// 높이는 메시가 언제나 갖고 있으므로 이 경로는 정점 색 유무와 무관하게 항상 동작한다.
/// </summary>
half VegetationBendMask(float3 positionOS)
{
    half m = saturate((positionOS.y - _Bend_Start) / max(1e-4h, _Bend_Height - _Bend_Start));
    return m * m;   // 제곱 — 뿌리 쪽이 더 단단히 붙어 있어야 자연스럽다
}

/// <summary>
/// 정점을 바람으로 민다. 오프셋은 <b>오브젝트 공간</b>이라 인스턴스 스케일을 따라간다 —
/// 월드 미터로 밀면 0.56배로 심은 작은 풀이 큰 풀과 같은 거리를 움직여 훨씬 격하게 보인다.
/// 위상은 <b>월드 위치</b>에서 뽑는다 — 안 그러면 풀밭 전체가 한 덩어리로 같이 움직인다.
/// </summary>
float3 VegetationWindOS(float3 positionOS)
{
    half mask = VegetationBendMask(positionOS);

    float2 dir = _Wind_Direction.xy;
    float len = length(dir);
    dir = len > 1e-4 ? dir / len : float2(1.0, 0.0);

    float3 positionWS = TransformObjectToWorld(positionOS);

    // 바람이 지나가는 파(波) — 부는 방향으로 위상이 밀린다
    float travel = dot(positionWS.xz, dir) * _Wave_Scale;
    // 포기별 고정 오프셋. floor로 격자에 스냅해야 <b>같은 포기의 모든 정점이 같은 위상</b>을
    // 갖는다 — 정점마다 다른 난수를 주면 잎이 찢어진다.
    float seed = frac(sin(dot(floor(positionWS.xz * 3.0), float2(12.9898, 78.233))) * 43758.5453);

    float phase = _TimeParameters.x * _Wind_Speed * TWO_PI - travel + seed * _Wind_Variation * TWO_PI;
    // 두 겹 — 단일 사인은 시계추처럼 기계적으로 읽힌다
    float wave = sin(phase) * 0.75 + sin(phase * 2.37 + 1.7) * 0.25;

    positionOS.xz += dir * (wave * _Wind_Strength * mask);
    // 휘면 끝이 조금 내려온다 — 잎 길이가 늘어나 보이지 않게 하는 근사
    positionOS.y  -= abs(wave) * _Wind_Strength * mask * 0.35;
    return positionOS;
}

/// <summary>
/// 잎 노멀을 위쪽으로 섞는다. 두 가지를 한 번에 해결한다:
///   1. <b>양면 렌더링</b>(Cull Off) — 뒷면은 노멀이 반대라 그냥 두면 새까맣게 죽는다.
///      양쪽 다 위를 보게 하면 뒤집힘 자체가 사라진다(VFACE 분기가 필요 없다).
///   2. 잎 한 장 한 장이 각자 다른 방향을 보면 풀밭이 지글거린다 — 위로 모으면
///      덩어리로 부드럽게 받는다. 스타일라이즈드 식생의 표준 처리다.
/// </summary>
float3 VegetationNormalOS(float3 normalOS)
{
    float3 upOS = normalize(TransformWorldToObjectDir(float3(0.0, 1.0, 0.0)));
    return normalize(lerp(normalize(normalOS), upOS, saturate(_Normal_Up)));
}

#endif
