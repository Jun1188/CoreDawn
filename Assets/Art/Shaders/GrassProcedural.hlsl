// CoreDawn 잔디 procedural instancing (5a-4e) — WorldTerrainGrass가 RenderMeshIndirect로 그릴 때
// 컴퓨트 컬링이 살아남은 포기를 담은 _GrassInstances를 읽어 인스턴스 행렬을 만든다.
// 이 경로는 PROCEDURAL_INSTANCING_ON 변형에서만 켜진다 — 프리팹으로 놓인 식생(나무 등)은
// 기존 인스턴싱 그대로다.
#ifndef COREDAWN_GRASS_PROCEDURAL
#define COREDAWN_GRASS_PROCEDURAL

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)

// 16바이트 — WorldTerrainGrass.Instance와 레이아웃이 같아야 한다.
// packed: 하위 16비트 = 배율(1/8192 단위, 최대 8), 상위 16비트 = yaw(0..2π를 0..65535로)
struct GrassInstance
{
    float3 pos;
    uint packed;
};

StructuredBuffer<GrassInstance> _GrassInstances;

void GrassProceduralSetup()
{
    GrassInstance gi = _GrassInstances[unity_InstanceID];
    float scale = (gi.packed & 0xFFFFu) / 8192.0;
    float yaw = (gi.packed >> 16) / 65535.0 * 6.28318530718;
    float s, c;
    sincos(yaw, s, c);

    // TRS(pos, yaw 회전, 균등 배율)
    unity_ObjectToWorld = float4x4(
        c * scale, 0, s * scale, gi.pos.x,
        0, scale, 0, gi.pos.y,
        -s * scale, 0, c * scale, gi.pos.z,
        0, 0, 0, 1);

    // 역행렬 — 균등 배율 + yaw 회전이라 R^T/s 와 -R^T t/s 로 닫힌 형태
    float inv = 1.0 / scale;
    float3 t = gi.pos;
    float3 it = float3(
        -(c * t.x - s * t.z) * inv,
        -t.y * inv,
        -(s * t.x + c * t.z) * inv);
    unity_WorldToObject = float4x4(
        c * inv, 0, -s * inv, it.x,
        0, inv, 0, it.y,
        s * inv, 0, c * inv, it.z,
        0, 0, 0, 1);
}

#endif // UNITY_PROCEDURAL_INSTANCING_ENABLED
#endif // COREDAWN_GRASS_PROCEDURAL
