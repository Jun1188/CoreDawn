using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 맵 데이터로 Unity Terrain을 굽는다 — `Tools > Factory > Build World Terrain`.
///
/// <b>타일 값을 높이로 직접 바꾸지 않는다.</b> 0→2로 튀는 값을 그대로 쓰면 격자 계단이 남는다.
/// 대신 "가장 가까운 절벽까지의 거리"(거리장)를 구해 부드러운 곡선으로 높이에 매핑한다 —
/// 절벽 중심부는 높고 가장자리로 갈수록 완만해지는, 자연 언덕의 단면이 나온다.
///
/// 여기에 두 겹을 더한다:
///   · <b>도메인 워핑</b> — 샘플 좌표 자체를 노이즈로 흔들어 경계의 직선/직각을 깬다
///   · <b>미세 노이즈</b> — 지면의 "완벽한 평면"을 없앤다
///
/// 게임 규칙은 흐려지지 않는다: 통행·건설·비용 판정은 MapDataSO(타일)가 그대로 들고 있고,
/// Terrain은 <b>보이는 것</b>만 담당한다. 절벽은 급경사로 표현되며 통행 차단은 EnterCost가,
/// 플레이어의 등반 차단은 PlayerController의 경사 제한이 맡는다.
///
/// 물은 맵을 덮는 사각형 통메시 한 장이다 — 지형이 강 자리만 깊게 파여 있어서
/// 거기서만 물이 보이고, 가장자리는 지형이 올라오며 저절로 얕아진다(여울).
/// </summary>
public static class WorldTerrainGenerator
{
    const string TerrainRootName = "Terrain (Generated)";
    const string AssetFolder = "Assets/Data/Maps/Terrain";

    // ── 지형 프로파일 ───────────────────────────────────────────
    // 지형이 담는 높이 폭(m). 절벽은 프리팹이 맡으므로 지형이 표현할 것은 강 깊이뿐이다 —
    // 여유를 조금 둬야 미세 굴곡이 잘리지 않는다.
    const float TerrainHeightRange = 2f;
    // 강바닥 깊이(m). 물 평면보다 넉넉히 깊어야 한다 — 다듬기가 폭 1칸짜리 물길의 바닥을
    // 들어올리기 때문에, 여유가 없으면 그런 구간에서 물이 끊겨 보인다.
    const float RiverDepth = 0.85f;
    const float WaterLevel = -0.15f;   // 물 표면 높이(m)

    // ── 섬 ──────────────────────────────────────────────────────
    const float ShoreWidth = 1.8f;  // 맵 가장자리에서 물에 잠기는 폭(칸)
    const float SeaMargin = 1.5f;   // 물을 맵 밖으로 얼마나 더 깔지 (맵 한 변의 배수)

    // ── 경계벽 ──────────────────────────────────────────────────
    const float BoundsHeight = 40f;      // 점프·넉백으로도 넘지 못할 높이(m)
    const float BoundsThickness = 2f;    // 빠른 이동체가 한 프레임에 뚫지 않을 두께(m)
    const float BoundsSink = 5f;         // 바닥을 지형 아래까지 내려 물가 틈으로 새지 않게

    /// <summary>수면 정점 간격(m). 물 셰이더가 정점을 흔들므로 파도 주기보다 촘촘해야 한다.</summary>
    const float WaterVertexSpacing = 1f;   // 거품 띠는 정점 보간 폭보다 얇아질 수 없다 — 띠 목표 폭보다 촘촘히

    /// <summary>이 수심(m)보다 얕으면 거품이 낀다. 정점 컬러의 빨강 채널이 곧 거품 세기다.
    /// 0.35로 두면 작은 웅덩이는 수면 대부분이 거품 범위에 들어가 흰 원반처럼 렌더된다 —
    /// 물가에 좁은 띠만 남도록 얕게 잡는다.</summary>
    const float FoamDepth = 0.10f;

    // ── 스케일 불변 단위 ────────────────────────────────────────
    // 물가·워핑·해상도는 <b>미터</b>로 선언한다. 계산은 칸 좌표계라 파생값이 Cell로 환산한다 —
    // 셀 크기를 바꿔도 물가의 실제 형상(경사·여울 폭·해안 굴곡·블러 반경)이 유지된다.
    // (셀 2→4m 때 칸 단위 상수가 통째로 어긋나 물가가 두 배 넓어졌던 재발 방지.
    //  절벽은 반대로 바위 폭이 곧 칸이라 칸 비례가 자연스럽다 — 그쪽 m 상수는 셀에 맞춰 튜닝)
    static float Cell = 2f;   // Build 시작에 world.CellSize로 갱신된다

    // 형상이 완성되는 거리 — 타일 <b>경계에서 안쪽으로</b> 얼마 들어가야 제 높이가 되는가.
    // 짧아야 한다. 경사를 칸 하나에 걸쳐 눕히면 폭 1~2칸짜리 절벽·물길은 제 높이에 닿기도
    // 전에 반대쪽 경계를 만나 밋밋한 둔덕이 된다(0.6칸일 때 절벽 43%가 절반 높이도 못 됐다).
    // 절벽·강 타일은 어차피 건설 불가라 칸 안에서는 마음껏 깎아도 된다. 경계선 자체는 이미
    // 블러로 매끈한 곡선이므로, 짧게 세울수록 그 곡선이 또렷한 벼랑·물길이 된다.
    const float RiverFalloffM = 0.6f;   // 골 경사 거리(m) — 크면 완만하다
    static float RiverFalloff => RiverFalloffM / Cell;

    // 물가는 벼랑이 아니라 <b>여울</b>로 들어간다 — 물 앞에서 넓고 얕게 눕다가, 그 다음에
    // 골이 파인다. 한 단짜리 곡선으로는 이 둘을 함께 얻을 수 없다: 짧게 잡으면 물가가
    // 벽이 되고, 길게 잡으면 폭 3칸짜리 물길이 제 깊이에 닿기 전에 반대편을 만나 말라버린다.
    //
    // 두 폭의 합은 <b>강 반폭(1.5칸)에서 한참 모자라야</b> 한다. 다듬기(거리장 블러 + 높이맵
    // 블러)가 좁은 골의 바닥을 들어올리기 때문이다 — 실측으로 유효 침투 거리가 기대치의
    // 약 2/3(1.35칸 → 0.9칸)로 줄었고, 합을 1.25칸으로 잡았을 때 수심이 16cm까지 말랐다.
    const float ShelfWidthM = 0.9f;  // 여울 폭(m) — 물가 경사를 정한다
    static float ShelfWidth => ShelfWidthM / Cell;
    const float ShelfDepth = 0.3f;   // 여울 끝의 파임(m). WaterLevel보다 깊어야 물이 덮는다

    // 형상을 타일 경계에서 이만큼 <b>안쪽으로</b> 물려 시작한다. 경사면이 남의 땅이 아니라
    // 제 타일을 깎으며 생기게 하는 장치다 — 경계에 딱 맞춰 세우면 다듬는 과정에서 높이가
    // 옆 지면으로 흘러넘쳐, 절벽이 깎이는 게 아니라 땅이 차오르는 모양이 된다.
    // 덤으로 경계선이 칸 격자를 벗어나 거리장의 매끈한 등고선 위에 놓인다.
    const float ShapeInsetM = 0.3f;
    static float ShapeInset => ShapeInsetM / Cell;

    // ── 해상도 ──────────────────────────────────────────────────
    // 픽셀·샘플 간격을 미터로 고정한다 — 셀이 커져도 곡선의 잘림·블러 반경(m)이 안 변한다.
    const float FieldPixelM = 0.25f;     // 거리장 픽셀 크기(m) — 셀 4m 기준 칸당 16
    static int FieldSubDiv => Mathf.Max(2, Mathf.RoundToInt(Cell / FieldPixelM));
    const float HeightSampleM = 0.25f;   // 높이맵 샘플 간격(m). 전체는 2ⁿ+1로 올림된다
    static int SamplesPerCell => Mathf.Max(2, Mathf.RoundToInt(Cell / HeightSampleM));

    // 계단 다듬기 — 타일은 네모라 거리장 등고선이 90°·45°로 꺾인다. 그 각을 뭉갠다.
    // 반경도 <b>미터 고정</b>이다. 칸의 절반으로 정의했더니 셀 4m에서 반경이 2m가 되어,
    // 해상도를 올려 담은 잔물결 워핑(±0.7m)을 블러가 도로 지워 해안이 직선이 됐다.
    // 계단은 워핑이 이미 흩뜨려 놓으므로 1m 블러로 모서리만 둥글리면 된다
    // (더 키우면 폭이 좁은 물길·여울의 속살까지 밀린다).
    const float SmoothRadiusM = 0.75f;
    static int SmoothRadius => Mathf.Max(1, Mathf.RoundToInt(SmoothRadiusM / FieldPixelM));
    const int SmoothPasses = 2;             // 박스 블러를 겹쳐 가우시안에 가깝게

    // 높이맵 단계의 2차 다듬기. 거리장을 아무리 뭉개도 "타일 밖은 0" 클램프가
    // 칸 격자에서 일어나 모서리가 되살아나므로, 격자를 떠난 뒤 한 번 더 편다.
    static int HeightSmoothRadius => Mathf.Max(1, Mathf.RoundToInt(SmoothRadiusM / HeightSampleM));
    const int HeightSmoothPasses = 2;

    // ── 불규칙성 ────────────────────────────────────────────────
    // 경계를 흔드는 세기(칸). 직각을 깨는 주역이지만 <b>형상 반폭보다 작아야</b> 한다.
    // 워프 파장(18칸)이 물길 폭(3칸)보다 훨씬 길어서 물길 양쪽이 <b>같은 방향</b>으로 밀리는데,
    // 아래의 "바깥쪽 택하기"는 그 이동분을 양쪽에서 깎아낸다 — 세기가 반폭(1.5칸)을 넘던
    // 3.2칸에서는 그 깎임이 물길을 통째로 지워, 강이 중간중간 웅덩이로 끊겼다.
    const float WarpStrengthM = 2.2f;    // 진폭(m)
    static float WarpStrength => WarpStrengthM / Cell;
    const float WarpWavelengthM = 36f;   // 파장(m)
    static float WarpFrequency => Cell / WarpWavelengthM;
    // 잔물결 옥타브 — 긴 파장 하나로는 흔들림이 너무 완만해 안 흔든 것처럼 보인다.
    // 두 진폭의 합(1.45칸)은 여전히 물길 반폭(1.5칸) 안이다.
    // 중간 옥타브 — <b>물길 폭(≈12m)보다 짧은 파장</b>이라 강에도 쓸 수 있다: 파장이 폭보다
    // 짧으면 양쪽 기슭이 제각각 흔들려, 긴 파장처럼 물길 전체가 한쪽으로 쏠리지 않는다.
    const float WarpMidStrengthM = 1.2f;
    static float WarpMidStrength => WarpMidStrengthM / Cell;
    const float WarpMidWavelengthM = 10f;
    static float WarpMidFrequency => Cell / WarpMidWavelengthM;
    const float WarpFineStrengthM = 0.7f;
    static float WarpFineStrength => WarpFineStrengthM / Cell;
    const float WarpFineWavelengthM = 4.5f;
    static float WarpFineFrequency => Cell / WarpFineWavelengthM;
    const float DetailAmplitude = 0.14f;  // 지면 미세 굴곡(m)
    const float DetailWavelengthM = 22f;
    static float DetailFrequency => Cell / DetailWavelengthM;

    [MenuItem("Tools/Factory/Build World Terrain")]
    public static void Build()
    {
        var world = Object.FindFirstObjectByType<World>();
        if (world == null || world.Map == null)
        {
            EditorUtility.DisplayDialog("지형 생성", "씬에서 World(맵이 배선된 것)를 찾지 못했습니다.", "확인");
            return;
        }

        var map = world.Map;
        Cell = world.CellSize;   // 미터 선언 상수들의 칸 환산 기준 — 반드시 형상 계산 전에
        EnsureFolder();

        // 기존 생성물 정리 — 다시 구울 때 겹치지 않게
        var old = world.transform.Find(TerrainRootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = new GameObject(TerrainRootName);
        root.transform.SetParent(world.transform, false);

        var height = BuildHeightmap(map);
        CreateTerrain(map, world, root.transform, height);
        CreateWater(map, world, root.transform, height);
        PlaceCliffs(map, world, root.transform);
        CreateBounds(map, world, root.transform);

        // 생성물은 전부 정적이다 — 맵이 바뀌기 전에는 움직이지 않으므로
        // 배칭·오클루전·라이트맵을 전부 받을 수 있다.
        MarkStatic(root.transform);

        // 디스크에 굳힌다. SetDirty만으로는 부족하다 — 디테일(풀)과 스캐터 모드는
        // 저장되지 않은 채 에셋이 다시 로드되는 순간 통째로 사라진다(알파맵은 별도
        // 경로라 살아남아서, 풀만 없어지는 것으로 보인다).
        AssetDatabase.SaveAssets();

        EditorUtility.SetDirty(world.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        Debug.Log($"[WorldTerrainGenerator] '{map.Id}' Terrain 생성 완료 ({map.width}×{map.height})", world);
    }

    // ── 높이맵 ──────────────────────────────────────────────────

    /// <summary>
    /// 물가에서 <paramref name="into"/>칸 들어간 지점이 얼마나 파이는가(m). 뭍이면 0.
    ///
    /// 두 단으로 나눈다 — 넓고 완만한 <b>여울</b>이 먼저 물속으로 눕고, 그 뒤에 골이 파인다.
    /// 여울은 걸어 들어갈 수 있는 완경사라 물가가 벼랑으로 보이지 않고,
    /// 골은 짧아서 폭 3칸짜리 물길도 제 깊이에 닿는다.
    /// </summary>
    static float Submerge(float into)
    {
        float shelf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, ShelfWidth, into)) * ShelfDepth;
        float trough = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ShelfWidth, ShelfWidth + RiverFalloff, into))
                     * (RiverDepth - ShelfDepth);
        return shelf + trough;
    }

    /// <summary>
    /// 타일 → 높이맵. 거리장을 부드러운 곡선으로 매핑하고 워핑·노이즈로 격자 흔적을 지운다.
    /// Terrain 높이맵은 0~1 정규화 값이라 마지막에 전체 높이로 나눈다.
    /// </summary>
    static float[,] BuildHeightmap(MapDataSO map)
    {
        int res = HeightmapResolution(map);

        // 두 거리장: 절벽까지, 강까지. 각각 "그 타일 안이면 음수(중심일수록 깊음)"다.
        var cliffField = SignedDistance(map, MapTile.Cliff);
        var riverField = SignedDistance(map, MapTile.River);

        var height = new float[res, res];
        float total = TerrainHeightRange;   // 정규화 기준 (0 = 바닥, 1 = 천장)

        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                // 높이맵 픽셀 → 타일 좌표 (Terrain은 [y, x] 순서로 저장한다)
                float tx = (float)i / (res - 1) * map.width;
                float ty = (float)j / (res - 1) * map.height;

                // 도메인 워핑 — 조회 좌표를 흔들어 경계의 직선을 깬다.
                // 긴 파장(큰 굽이) + 잔물결 두 옥타브.
                float fineX = (Mathf.PerlinNoise(tx * WarpMidFrequency + 58.3f, ty * WarpMidFrequency + 24.1f) - 0.5f) * 2f * WarpMidStrength
                            + (Mathf.PerlinNoise(tx * WarpFineFrequency + 91.7f, ty * WarpFineFrequency + 45.3f) - 0.5f) * 2f * WarpFineStrength;
                float fineY = (Mathf.PerlinNoise(tx * WarpMidFrequency + 13.9f, ty * WarpMidFrequency + 82.7f) - 0.5f) * 2f * WarpMidStrength
                            + (Mathf.PerlinNoise(tx * WarpFineFrequency + 7.9f, ty * WarpFineFrequency + 63.1f) - 0.5f) * 2f * WarpFineStrength;
                float wx = tx + (Mathf.PerlinNoise(tx * WarpFrequency, ty * WarpFrequency) - 0.5f) * 2f * WarpStrength + fineX;
                float wy = ty + (Mathf.PerlinNoise(tx * WarpFrequency + 37.7f, ty * WarpFrequency + 12.3f) - 0.5f) * 2f * WarpStrength + fineY;

                // 워핑된 좌표와 원래 좌표 중 <b>바깥쪽</b>을 택한다(거리장은 안이 음수).
                // 워핑이 형상을 밖으로 밀어 지면을 파는 일을 막는 장치 —
                // 흔들림은 자기 영역을 깎는 방향으로만 남는다.
                float cliff = Mathf.Max(SampleField(cliffField, map, wx, wy),
                                        SampleField(cliffField, map, tx, ty));

                // 강은 <b>중간+잔물결 옥타브만</b> 워핑한다. 긴 파장(36m)은 물길 폭보다 훨씬
                // 길어 양쪽 기슭을 같은 방향으로 미는데, 바깥쪽 택하기가 밀려나간 쪽만
                // 깎아내니 물길이 진폭(2.2m)만큼 통째로 한쪽에 쏠렸다 — 여울(0.9m)이 사라진
                // 기슭은 잔디가 물 위에 걸린다. 파장이 폭보다 짧은 옥타브는 기슭을 따라
                // 들쭉날쭉 번갈아 깎여 쏠림이 아니라 굴곡이 된다. 큰 굽이는 타일 경로가 그린다.
                float river = Mathf.Max(SampleField(riverField, map, tx + fineX, ty + fineY),
                                        SampleField(riverField, map, tx, ty));

                // 절벽은 지형이 아니라 프리팹이 맡는다(PlaceCliffs) — 높이맵은 평평하게 둔다.
                // 높이맵으로 세운 벽은 결국 늘어난 텍스처라 가까이서 암벽으로 보이지 않는다.
                float h = 0f;

                // 강만 판다. 경계에서 ShapeInset만큼 안쪽이 물가고, 거기서부터 여울·골 순으로
                // 들어간다 — 발치가 타일 안이라 지면은 평평하게 남는다.
                float dig = Submerge(-river - ShapeInset);

                // 맵 가장자리도 같은 곡선으로 깎아 물에 잠근다 — 바다에 뜬 섬으로 보이게.
                // 타일은 건드리지 않으므로 길찾기·건설 판정은 그대로다(외형만 바뀐다).
                // 해안선은 <b>워핑된 좌표</b>로 잰다 — 원좌표로 재면 맵 테두리를 따라가는
                // 자로 그은 직선이 된다. 워핑 최대 진폭(1.45칸) < 잠김 폭(1.8칸)이라
                // 맵 최외곽은 언제나 물에 잠긴 채로 남는다(지형 절단면 노출 없음).
                float edge = Mathf.Min(Mathf.Min(wx, wy), Mathf.Min(map.width - wx, map.height - wy));
                dig = Mathf.Max(dig, Submerge(ShoreWidth - edge));

                h -= dig;

                height[j, i] = h;   // 아직 미터 단위, 형상만
            }

        // 높이맵 자체를 한 번 더 다듬는다. 거리장을 아무리 부드럽게 만들어도 "타일 밖은 0"
        // 클램프가 칸 격자에서 일어나기 때문에 경계선이 다시 칸 모서리를 따라간다 —
        // 격자를 떠난 이 단계에서 뭉개야 그 직각이 실제로 없어진다.
        SmoothHeight(height, res, map, cliffField, riverField);

        // 미세 굴곡은 다듬은 뒤에 얹는다(먼저 넣으면 방금 한 블러가 도로 지운다)
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float tx = (float)i / (res - 1) * map.width;
                float ty = (float)j / (res - 1) * map.height;

                // 물 밑은 잔잔하게 — 파임을 흐리지 않도록
                float detail = (Mathf.PerlinNoise(tx * DetailFrequency, ty * DetailFrequency) - 0.5f) * 2f * DetailAmplitude;
                float h = height[j, i] + detail * (height[j, i] < -0.05f ? 0.35f : 1f);

                // 마른 지면은 노이즈가 아래로 파지 못하게 막는다. 굴곡 폭(±0.14m)이 잔디
                // 컷라인(수면+0.08m)과 수면(-0.15m) 사이 틈보다 커서, 평지 곳곳이 "잔디는 안
                // 자라는데 물도 안 덮이는" 깊이로 파였다 — 잔디 빈 얼룩과 모래 색 웅덩이의 원인.
                // 형상(강·여울)으로 파인 곳은 원래 음수라 이 클램프에 걸리지 않는다.
                if (height[j, i] > -0.02f) h = Mathf.Max(h, -0.02f);

                height[j, i] = Mathf.Clamp01((h + RiverDepth) / total);
            }

        return height;
    }

    /// <summary>
    /// 높이맵 다듬기 — 칸 경계에 남은 직각을 편다.
    /// 다만 블러는 절벽·강을 옆 지면으로 흘려보내므로, 타일 밖에서는 원래 높이로 되돌린다.
    /// 되돌림 여부는 <b>거리장</b>으로 판정한다 — 칸 중심/경계로 재면 그 되돌림이 칸 주기로
    /// 반복돼 물가에 가리비 같은 물결이 새로 생긴다(격자를 지우려다 다시 그리는 꼴).
    /// </summary>
    static void SmoothHeight(float[,] height, int res, MapDataSO map, float[,] cliffField, float[,] riverField)
    {
        int r = HeightSmoothRadius;
        if (r <= 0) return;

        var original = (float[,])height.Clone();
        var tmp = new float[res, res];

        for (int pass = 0; pass < HeightSmoothPasses; pass++)
        {
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    float sum = 0f;
                    for (int k = -r; k <= r; k++) sum += height[j, Mathf.Clamp(i + k, 0, res - 1)];
                    tmp[j, i] = sum / (2 * r + 1);
                }

            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    float sum = 0f;
                    for (int k = -r; k <= r; k++) sum += tmp[Mathf.Clamp(j + k, 0, res - 1), i];
                    height[j, i] = sum / (2 * r + 1);
                }
        }

        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float tx = (float)i / (res - 1) * map.width;
                float ty = (float)j / (res - 1) * map.height;

                // 절벽·강 밖이면 원래 높이 그대로. 블러는 형상을 옆으로 흘려보내는데,
                // 그러면 절벽이 깎이는 게 아니라 옆 땅이 차오르는 모양이 된다.
                // 형상은 이미 ShapeInset만큼 안에서 시작하므로 여기서 잘라도 잃을 높이가 없다.
                float outside = Mathf.Min(SampleField(cliffField, map, tx, ty),
                                          SampleField(riverField, map, tx, ty));
                if (outside > 0f) height[j, i] = original[j, i];
            }
    }

    static int HeightmapResolution(MapDataSO map)
    {
        // Terrain 높이맵은 2ⁿ+1이어야 한다. 맵보다 촘촘하게 잡아 곡선이 뭉개지지 않게.
        int target = Mathf.Max(map.width, map.height) * SamplesPerCell;
        int res = 33;
        while (res - 1 < target && res < 2049) res = (res - 1) * 2 + 1;
        return res;
    }

    // ── 거리장 ──────────────────────────────────────────────────

    /// <summary>
    /// 부호 있는 거리장(타일 단위) — 해당 타일 안이면 음수, 밖이면 양수.
    /// 두 번 훑는 체임퍼 변환이라 맵 크기에 선형이다(브루트포스 O(n²)를 피한다).
    ///
    /// 칸이 아니라 <b>칸을 FieldSubDiv로 쪼갠 격자</b>에서 뜬다. 칸 단위로 뜨면 계단 하나가
    /// 곧 한 칸이라 아무리 보간해도 마인크래프트 같은 직각이 남는다.
    /// 뜬 뒤에는 블러로 그 직각을 뭉갠다(<see cref="Smooth"/>). 타일 밖을 잘라내지 않는 이유는
    /// 그 자르기가 칸 격자에서 일어나 경계선을 도로 칸 모서리에 붙여놓기 때문이다 —
    /// 대신 형상 자체를 <see cref="ShapeInset"/>만큼 안쪽에서 시작해 지면을 침범하지 않게 한다.
    /// </summary>
    static float[,] SignedDistance(MapDataSO map, MapTile tile)
    {
        int w = map.width * FieldSubDiv, h = map.height * FieldSubDiv;
        var inside = new float[w, h];
        var outside = new float[w, h];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool isTile = map.TileAt(x / FieldSubDiv, y / FieldSubDiv) == tile;
                outside[x, y] = isTile ? 0f : float.MaxValue;   // 타일까지의 거리
                inside[x, y] = isTile ? float.MaxValue : 0f;    // 타일 밖까지의 거리
            }

        Chamfer(outside, w, h);
        Chamfer(inside, w, h);

        var signed = new float[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                signed[x, y] = (outside[x, y] - inside[x, y]) / FieldSubDiv;   // 안이면 음수, 칸 단위

        Smooth(signed, w, h);
        return signed;
    }

    /// <summary>분리형 박스 블러 — 거리장의 꺾인 등고선을 둥글게 편다.</summary>
    static void Smooth(float[,] d, int w, int h)
    {
        var tmp = new float[w, h];
        int r = SmoothRadius;
        if (r <= 0) return;

        for (int pass = 0; pass < SmoothPasses; pass++)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f; int n = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int sx = Mathf.Clamp(x + k, 0, w - 1);
                        sum += d[sx, y]; n++;
                    }
                    tmp[x, y] = sum / n;
                }

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f; int n = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int sy = Mathf.Clamp(y + k, 0, h - 1);
                        sum += tmp[x, sy]; n++;
                    }
                    d[x, y] = sum / n;
                }
        }
    }

    /// <summary>체임퍼 거리 변환 — 직교 1, 대각 √2로 두 방향 전파.</summary>
    static void Chamfer(float[,] d, int w, int h)
    {
        const float O = 1f, D = 1.41421f;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = d[x, y];
                if (x > 0) v = Mathf.Min(v, d[x - 1, y] + O);
                if (y > 0) v = Mathf.Min(v, d[x, y - 1] + O);
                if (x > 0 && y > 0) v = Mathf.Min(v, d[x - 1, y - 1] + D);
                if (x < w - 1 && y > 0) v = Mathf.Min(v, d[x + 1, y - 1] + D);
                d[x, y] = v;
            }

        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                float v = d[x, y];
                if (x < w - 1) v = Mathf.Min(v, d[x + 1, y] + O);
                if (y < h - 1) v = Mathf.Min(v, d[x, y + 1] + O);
                if (x < w - 1 && y < h - 1) v = Mathf.Min(v, d[x + 1, y + 1] + D);
                if (x > 0 && y < h - 1) v = Mathf.Min(v, d[x - 1, y + 1] + D);
                d[x, y] = v;
            }
    }

    /// <summary>
    /// 거리장을 칸 좌표(실수)에서 읽는다. 격자는 FieldSubDiv배로 촘촘하므로 먼저 환산한다.
    /// 보간 계수에 smoothstep을 물려 격자 선에서 기울기가 끊기지 않게 한다 — 선형 보간만 쓰면
    /// 샘플 사이는 곧게 이어져 미세한 각이 남는다.
    /// </summary>
    static float SampleField(float[,] field, MapDataSO map, float x, float y)
    {
        int w = field.GetLength(0), h = field.GetLength(1);

        x = Mathf.Clamp(x * FieldSubDiv - 0.5f, 0f, w - 1.001f);
        y = Mathf.Clamp(y * FieldSubDiv - 0.5f, 0f, h - 1.001f);

        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
        float fx = Mathf.SmoothStep(0f, 1f, x - x0), fy = Mathf.SmoothStep(0f, 1f, y - y0);

        float a = Mathf.Lerp(field[x0, y0], field[x1, y0], fx);
        float b = Mathf.Lerp(field[x0, y1], field[x1, y1], fx);
        return Mathf.Lerp(a, b, fy);
    }

    // ── Terrain 생성 ────────────────────────────────────────────

    static void CreateTerrain(MapDataSO map, World world, Transform root, float[,] height)
    {
        int res = height.GetLength(0);
        var data = new TerrainData
        {
            heightmapResolution = res,
            alphamapResolution = Mathf.Min(2048, Mathf.NextPowerOfTwo(Mathf.Max(map.width, map.height) * SamplesPerCell * 2)),
            baseMapResolution = 512,
            name = $"{map.Id.Replace(':', '_')}_TerrainData",
        };
        // size는 heightmapResolution 설정 뒤에 줘야 한다 (해상도 변경이 size를 되돌린다)
        data.size = new Vector3(map.width * world.CellSize, TerrainHeightRange, map.height * world.CellSize);
        data.SetHeights(0, 0, height);
        data.terrainLayers = BuildLayers();

        AssetDatabase.CreateAsset(data, $"{AssetFolder}/{data.name}.asset");

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(root, false);
        // 지면(타일 높이 0)이 월드 y=0에 오도록 강 깊이만큼 내린다
        go.transform.localPosition = new Vector3(0f, -RiverDepth, 0f);

        int ground = LayerMask.NameToLayer("Ground");
        if (ground >= 0) go.layer = ground;

        var terrain = go.GetComponent<Terrain>();
        // URP에서는 전용 지형 셰이더를 명시해야 한다 — null로 두면 내장 파이프라인 머티리얼이라
        // 아무것도 그려지지 않는다(물만 보이는 증상).
        terrain.materialTemplate = TerrainMaterial();
        terrain.heightmapPixelError = 3f;
        terrain.drawInstanced = true;

        // 풀은 멀리서 보이지 않아도 된다 — 가까이서 발밑을 덮는 것이 목적이다
        terrain.detailObjectDistance = DetailDistance;
        terrain.detailObjectDensity = 1f;

        // 스플랫은 반드시 에셋으로 굳힌 뒤에 칠한다. TerrainData를 CreateAsset 하는 순간
        // 알파맵 텍스처가 새로 만들어지면서 그 전에 칠한 내용이 전부 첫 레이어로 초기화된다.
        PaintSplat(data, map, height);
        PaintDetails(data, map, height, world.CellSize);
        EditorUtility.SetDirty(data);

        // 여기서 디스크에 굳힌다. 뒤이어 물 메시를 CreateAsset 하는 순간, 아직 저장되지 않은
        // TerrainData의 디테일이 통째로 날아간다 — 다른 에셋을 만드는 것이 이쪽의 미저장
        // 변경을 되돌린다(스플랫이 CreateAsset에 초기화되던 것과 같은 함정의 반대편).
        AssetDatabase.SaveAssets();
    }

    // ── 디테일(풀·꽃) ───────────────────────────────────────────
    //
    // 지형 텍스처만으로는 에셋 홍보 사진 같은 그림이 나오지 않는다. 그 인상의 대부분은
    // 지면을 덮은 <b>잔디 메시</b>에서 온다 — 바닥이 평평한 그림에서 풀이 선 그림으로 바뀐다.

    const string PrefabFolder = "Assets/ThirdParty/Idyllic Fantasy Nature/Prefabs";
    // 풀·꽃은 <b>우리 변형</b>을 심는다 — 머티리얼이 Art/Materials/Vegetation의 우리 사본이라,
    // 시간대 틴트(SkyboxTimeView)가 서드파티 에셋을 건드리지 않고 색을 만질 수 있다.
    const string VegPrefabFolder = "Assets/Prefabs/Vegetation";
    const float DetailPointM = 0.5f;    // 디테일 점 간격 목표(m) — 격자 크기는 맵 실측(m)에서 산정
    // 패치 단위 — 디테일은 <b>패치마다 따로 그려진다</b>. 32면 해상도 1024에서 32×32=1024패치가
    // 되고, 프로토타입 8종을 곱하면 배치가 수천 개로 불어난다(실측: 잔디가 전체 배치의 79%).
    // 64로 키우면 패치가 256개로 줄어 그만큼 드로우콜이 준다 — 컬링 단위가 커지는 것이 대가다.
    const int DetailPatch = 64;
    // 이 거리 밖에서는 그리지 않는다. 실측(1920×1080): 120m면 배치 1258·삼각형 6.3M,
    // 70m면 그 절반 남짓이다. 잔디는 발밑에서만 눈에 띄므로 멀리까지 그릴 값어치가 적다.
    const float DetailDistance = 70f;

    // ── 절벽 프리팹 배치 ────────────────────────────────────────
    // 칸마다 바위 하나. 제약은 "절벽이 아닌 타일 침범 금지" 하나뿐이고, 절벽끼리는
    // 마음껏 겹친다(데모 씬이 그렇게 조립돼 있다 — 이웃 중심거리가 반폭 합의 18%).
    //
    // 높이는 <b>쌓지 않고 배치 고도로</b> 만든다: 가장자리 줄만 땅에 서고, 안쪽 바위는
    // 공중에 띄운다. 바닥은 앞줄이 가리므로 보이지 않는다. 쌓으면 이음매가 오레오처럼
    // 줄무늬가 되고, 세로로 늘리면 옆으로 넓은 바위가 콜라캔 판자가 된다 — 둘 다 겪었다.
    // 능선고(m). 최저값은 플레이어 점프(1.3m)로 올라설 수 없는 선.
    // 절벽 치수는 <b>칸 비례</b>다 — 바위의 xz 폭이 곧 칸 크기라, 높이·후퇴량도 같이
    // 커져야 바위 비율이 유지된다. (물가가 미터 고정인 것과 반대 — 저긴 형상이, 여긴 비율이 불변)
    static float CliffHeightLow => 2.0f * Cell;
    static float CliffHeightHigh => 4.5f * Cell;

    // 변주는 칸별 독립 난수가 아니라 <b>위치 기반 연속 노이즈</b>로 준다. 이웃끼리 아무
    // 상관없는 크기·높이는 "쌓인 지형"이 아니라 "흩뿌린 에셋"으로 읽힌다 — 실제 절벽은
    // 이웃한 바위가 서로 닮았고, 능선이 낮은 주파수로 오르내린다.
    const float CliffRidgeFrequency = 0.11f;   // 높이 파장 ≈ 9칸(18m) — 몇 칸에 걸쳐 솟았다 가라앉는다
    const float CliffFaceFrequency = 0.45f;    // 벽면 굴곡 파장 ≈ 2칸 — 지그재그 대신 물결

    static float CliffBaseSink => 0.3f * Cell;   // 가장자리 줄을 땅에 묻는 깊이 — 데모도 Y 최저 -3.4m
    // 세로 배율은 원본 비율에서 이만큼만 벗어난다 — 변주지 왜곡이 아니다.
    // 데모는 아예 늘리지 않는다(전부 스케일 1).
    const float CliffStretchLow = 0.85f;
    const float CliffStretchHigh = 1.25f;

    /// <summary>지면을 덮는 풀.</summary>
    static readonly string[] GrassSet = { "Grass_01", "Grass_02", "Grass_03" };

    /// <summary>드문드문 섞이는 꽃 — 단조로운 초원을 깬다.</summary>
    static readonly string[] FlowerSet =
    {
        "Flower_White", "Flower_Yellow", "Flower_Blue_01", "Flower_Red", "Flower_Purple",
    };

    /// <summary>절벽 타일에 세우는 암벽 프리팹. 지형을 솟구치게 하는 대신 이것들이 벽이 된다.</summary>
    static readonly string[] CliffSet =
    {
        "SM_Rocks_01", "SM_Rocks_02", "SM_Rocks_03", "SM_Rocks_04","SM_Rocks_05", "SM_Rocks_06", "SM_Rocks_07", "SM_Rocks_08", "SM_Rocks_09", "SM_Rocks_10", "SM_Rocks_11",
    };

    // 디테일은 지형 텍스처가 아니라 <b>정점 색</b>으로 물드므로 색을 여기서 준다 (Demo와 같은 값)
    static readonly Color HealthyTint = new Color(0.263f, 0.976f, 0.165f);
    static readonly Color DryTint = new Color(0.804f, 0.737f, 0.102f);

    /// <summary>물가에서 풀이 멈추는 높이(m). 수면보다 조금 위여야 물속에 잠긴 풀이 없다.</summary>
    const float GrassWaterLine = WaterLevel + 0.08f;

    /// <summary>여기까지만 풀이 자란다. 여울 경사(약 0.17)는 넘고 골 경사는 못 넘는 값.</summary>
    const float GrassMaxSlope = 0.45f;

    /// <summary>
    /// 풀·꽃을 심는다. 심는 자리는 <b>실제 지형이 정한다</b> — 물가 위쪽의 완경사 지면에만
    /// 자란다. 타일로 자르면 안 된다: 워핑·블러를 거친 실제 물가는 타일 경계를 넘나들기
    /// 때문에, 같은 강이라도 한쪽은 물 앞에서 풀이 끊기고 반대쪽은 물속까지 풀이 자란다.
    /// 게다가 잘린 자국이 칸 격자를 그대로 드러낸다.
    ///
    /// 타일을 보는 곳은 절벽 하나뿐이다 — 그 자리는 암벽 프리팹이 덮으므로 비운다.
    /// 절벽에 <b>닿은</b> 칸은 건설 가능한 멀쩡한 지면이므로 비우지 않는다.
    /// 디테일은 콜라이더가 없어 길찾기·건설·사격 판정에 전혀 관여하지 않는다.
    /// </summary>
    static void PaintDetails(TerrainData data, MapDataSO map, float[,] height, float cellSize)
    {
        // 크기는 프리팹 원본에 곱해지는 배율이다. 1인칭 게임이라 무릎 높이여야 시야가 열린다 —
        // Demo 값(0.5~1)을 그대로 쓰면 눈높이를 덮어 앞이 안 보인다.
        var protos = new List<DetailPrototype>();
        foreach (var n in GrassSet) AddProto(protos, n, 0.56f, 1.0f);
        // 꽃은 잔디보다 조금 커야 보인다 — 작으면 풀숲에 통째로 묻힌다
        int flowerStart = protos.Count;
        foreach (var n in FlowerSet) AddProto(protos, n, 0.7f, 1.2f);
        int grassCount = flowerStart;
        int flowerCount = protos.Count - flowerStart;

        if (protos.Count == 0)
        {
            Debug.LogWarning("[WorldTerrainGenerator] 디테일 프리팹을 하나도 찾지 못해 풀을 심지 않았습니다.");
            return;
        }

        // 해상도·모드를 먼저 잡고 <b>프로토타입은 마지막에</b> 대입한다.
        // SetDetailResolution은 디테일 데이터를 초기화하면서 프로토타입 목록까지 비운다 —
        // 순서를 반대로 하면 심을 것이 하나도 없는 상태가 된다.
        int DetailRes = Mathf.Min(2048, Mathf.NextPowerOfTwo(
            Mathf.CeilToInt(Mathf.Max(map.width, map.height) * cellSize / DetailPointM)));
        data.SetDetailResolution(DetailRes, DetailPatch);

        // 밀도 값을 <b>셀당 개수</b>로 해석하게 한다(Demo와 같은 모드).
        // 기본값인 CoverageMode에서는 같은 값이 0~255 커버리지 비율로 읽혀,
        // 2~3을 넣으면 1%도 안 되는 밀도가 되어 풀이 아예 보이지 않는다.
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

        data.detailPrototypes = protos.ToArray();

        var layers = new int[protos.Count][,];
        for (int i = 0; i < layers.Length; i++) layers[i] = new int[DetailRes, DetailRes];

        for (int j = 0; j < DetailRes; j++)
            for (int i = 0; i < DetailRes; i++)
            {
                // 디테일 격자 → 타일 좌표. 디테일 맵은 [y, x] 순서다(높이맵과 같다).
                float tx = (float)i / DetailRes * map.width;
                float ty = (float)j / DetailRes * map.height;
                int cx = Mathf.Clamp(Mathf.FloorToInt(tx), 0, map.width - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(ty), 0, map.height - 1);

                // 절벽 타일은 <b>경계 0.1칸 띠만</b> 심는다 — 벽면 후퇴 노이즈로 암벽이
                // 물러난 자리는 절벽 타일 앞부분이 드러나므로 띠가 필요하지만, 안쪽은
                // 바위가 빈틈없이 덮어(관통 구멍 해결 후) 심어도 보이지 않는 낭비다.
                // 절벽이 맵의 상당분이라 디테일 인스턴스가 그만큼 줄어든다.
                if (map.TileAt(cx, cy) == MapTile.Cliff)
                {
                    const float Fringe = 0.1f;   // 칸 단위 — 벽면 후퇴 최대(0.45칸)보다 얕은 앞줄만
                    bool nearOpen = false;
                    for (int oy = -1; oy <= 1 && !nearOpen; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            int nx = Mathf.FloorToInt(tx + ox * Fringe);
                            int nz = Mathf.FloorToInt(ty + oy * Fringe);
                            if (map.InBounds(nx, nz) && map.TileAt(nx, nz) != MapTile.Cliff) { nearOpen = true; break; }
                        }
                    if (!nearOpen) continue;
                }

                // 물가 아래는 비운다. 판정을 실제 높이로 하므로 풀이 끊기는 선이
                // 칸 모서리가 아니라 물가 곡선을 그대로 따라간다.
                if (SampleHeightAt(height, tx, ty, map) < GrassWaterLine) continue;

                // 급경사에도 두지 않는다 — 비탈에 선 풀은 지면을 뚫고 나온 것처럼 보인다
                if (SlopeAt(height, tx, ty, map, cellSize) > GrassMaxSlope) continue;

                // 풀은 <b>빈 곳 없이</b> 깔되 밀도만 흔든다. 임계값으로 자르면 노이즈 모양 그대로
                // 구멍이 뚫려, 잔디밭이 아니라 얼룩으로 보인다.
                float patch = Mathf.PerlinNoise(tx * 0.06f, ty * 0.06f);
                int amount = patch > 0.7f ? 3 : 2;   // 평균 약 2.3 — 이전(1.45)의 1.5배
                layers[Hash(i, j, 17) % grassCount][j, i] = amount;

                // 꽃 — 셀 단위로 흩뿌린다. 칸 좌표로 종류를 고르면 같은 칸이 통째로 한 색이 되고,
                // 그 칸들이 규칙적으로 늘어서 <b>줄무늬</b>가 생긴다(꽃밭이 아니라 격자무늬가 됐던 이유).
                if (flowerCount > 0)
                {
                    float bloom = Mathf.PerlinNoise(tx * 0.04f + 31.7f, ty * 0.04f + 12.9f);
                    if (bloom > 0.58f && Hash(i, j, 91) % 8 == 0)
                        layers[flowerStart + Hash(i, j, 53) % flowerCount][j, i] = 1;
                }
            }

        for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
    }

    /// <summary>위치를 잘 섞인 값으로 바꾼다 — 좌표를 곱해 더하는 식은 대각선 줄무늬를 만든다.</summary>
    static int Hash(int x, int y, int salt)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + salt * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & 0x7fffffff;
        }
    }

    static void AddProto(List<DetailPrototype> into, string prefabName, float min, float max)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{VegPrefabFolder}/{prefabName}.prefab");
        if (prefab == null)
        {
            Debug.LogWarning($"[WorldTerrainGenerator] 디테일 프리팹 없음: {VegPrefabFolder}/{prefabName} — " +
                             "우리 소유 변형이 필요하다(머티리얼 틴트 대상). 원본만 있다면 변형을 먼저 만들 것.");
            return;
        }

        into.Add(new DetailPrototype
        {
            prototype = prefab,
            usePrototypeMesh = true,
            renderMode = DetailRenderMode.VertexLit,
            useInstancing = true,        // 수만 개가 깔리므로 인스턴싱이 아니면 감당이 안 된다
            minWidth = min, maxWidth = max,
            minHeight = min, maxHeight = max,
            noiseSpread = 20f,
            density = 1f,
            healthyColor = HealthyTint,
            dryColor = DryTint,
        });
    }

    /// <summary>정규화 높이맵을 타일 좌표에서 읽어 미터로 돌려준다 — 경사면 판정용.</summary>
    static float SampleHeightAt(float[,] height, float tx, float ty, MapDataSO map)
    {
        int res = height.GetLength(0);
        int i = Mathf.Clamp(Mathf.RoundToInt(tx / map.width * (res - 1)), 0, res - 1);
        int j = Mathf.Clamp(Mathf.RoundToInt(ty / map.height * (res - 1)), 0, res - 1);
        return height[j, i] * TerrainHeightRange - RiverDepth;
    }

    /// <summary>
    /// 그 지점의 지면 기울기(높이차 ÷ 수평거리). 45°가 1이다.
    /// 높이맵 한 칸 간격의 중앙 차분이라, 실제로 심는 해상도에서 느끼는 경사와 같다.
    /// </summary>
    static float SlopeAt(float[,] height, float tx, float ty, MapDataSO map, float cellSize)
    {
        float d = 1f / SamplesPerCell;   // 높이맵 한 칸(타일 단위)
        float dx = SampleHeightAt(height, tx + d, ty, map) - SampleHeightAt(height, tx - d, ty, map);
        float dy = SampleHeightAt(height, tx, ty + d, map) - SampleHeightAt(height, tx, ty - d, map);
        return Mathf.Sqrt(dx * dx + dy * dy) / (2f * d * cellSize);
    }

    // ── 절벽 ────────────────────────────────────────────────────

    /// <summary>
    /// 절벽 타일에 암벽 프리팹을 세운다. <b>지형을 솟구치게 하지 않는 이유</b>는
    /// 높이맵으로 만든 벽이 결국 늘어난 텍스처 덩어리이기 때문이다 — 가까이서 보면
    /// 풀이 벽면에 발려 있고, 실루엣도 매끄러운 언덕에 가깝다.
    /// 프리팹은 제 형태와 노멀을 갖고 있어 그 자리에서 바로 암벽으로 읽힌다.
    ///
    /// 콜라이더는 <b>남긴다</b> — 이제 이것이 플레이어를 막고 총알을 받는 실체다
    /// (예전에는 Terrain 경사가 그 일을 했다). 길찾기는 여전히 타일이 정한다.
    ///
    /// <b>칸마다 하나씩, 칸 크기에 맞춰</b> 세운다. 프리팹 원본이 5~18m로 제각각이라
    /// 고정 배율을 곱하면 어떤 것은 칸을 넘고 어떤 것은 못 채운다 —
    /// 실제 크기를 재서 필요한 배율을 역산해야 격자에 맞는다.
    /// 회전은 90° 단위다. 임의 각도로 돌리면 축정렬 바운드가 커져 다시 칸을 넘는다.
    /// </summary>
    static void PlaceCliffs(MapDataSO map, World world, Transform root)
    {
        // 프리팹과 그 실측 크기를 함께 들고 다닌다
        var prefabs = new List<GameObject>();
        var footprints = new List<Vector2>();
        var bottoms = new List<float>();
        var heights = new List<float>();
        foreach (var n in CliffSet)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{n}.prefab");
            if (p == null) continue;
            if (!PrefabBounds(p, out Vector2 foot, out float bottom, out float tallness)) continue;

            prefabs.Add(p);
            footprints.Add(foot);
            bottoms.Add(bottom);
            heights.Add(tallness);
        }
        if (prefabs.Count == 0)
        {
            Debug.LogWarning("[WorldTerrainGenerator] 절벽 프리팹을 찾지 못해 절벽을 세우지 못했습니다.");
            return;
        }

        var parent = new GameObject("Cliffs").transform;
        parent.SetParent(root, false);

        // 세로가 긴 프리팹 — 지면에 붙은 가장자리 줄은 폭 예산이 2m뿐이라, 원본 비율로
        // 점프 차단 높이(1.8m+)가 나오려면 높이/폭 비가 큰 바위여야 한다.
        var lofty = new List<int>();
        for (int i = 0; i < prefabs.Count; i++)
            if (heights[i] / Mathf.Max(footprints[i].x, footprints[i].y) >= 0.6f) lofty.Add(i);
        if (lofty.Count == 0)
        {
            int best = 0;
            for (int i = 1; i < prefabs.Count; i++)
                if (heights[i] / Mathf.Max(footprints[i].x, footprints[i].y) >
                    heights[best] / Mathf.Max(footprints[best].x, footprints[best].y)) best = i;
            lofty.Add(best);
        }

        bool CliffAt(int cx, int cy) => map.InBounds(cx, cy) && map.TileAt(cx, cy) == MapTile.Cliff;

        bool ColumnCliff(int cx, int y0, int y1) { for (int i = y0; i <= y1; i++) if (!CliffAt(cx, i)) return false; return true; }
        bool RowCliff(int cy, int x0, int x1) { for (int i = x0; i <= x1; i++) if (!CliffAt(i, cy)) return false; return true; }

        int placed = 0;
        for (int y = 0; y < map.height; y++)
            for (int x = 0; x < map.width; x++)
            {
                // 맵 밖은 TileAt이 절벽으로 돌려주므로(경계 검사를 없애려는 규약) 반드시 거른다
                if (!map.InBounds(x, y) || map.TileAt(x, y) != MapTile.Cliff) continue;

                // 제약은 딱 하나 — <b>절벽이 아닌 타일을 침범하지 않는다</b>. 칸에 맞출 필요는
                // 없다: 이 칸을 품는 절벽 전용 직사각형을 키워, 그 안이면 이웃 바위와 마음껏
                // 겹친다. AABB는 사각인데 바위는 둥글어서 칸에 꼭 맞추면 모서리마다 틈이
                // 생겼다 — 겹침이 그 틈을 지운다. 축별 이어짐 검사로는 안 된다: 바운드는
                // 정사각형이라 <b>대각 칸</b>을 덮는데 그 칸은 어느 축에도 안 잡힌다(침범 2m).
                // 직사각형은 전체 칸을 확인하므로 대각까지 안전하다.
                int x0 = x, x1 = x, y0 = y, y1 = y;
                for (int step = 0; step < 12; step++)
                {
                    bool grew = false;
                    switch ((step + Hash(x, y, 101)) % 4)
                    {
                        case 0 when x - x0 < 3 && ColumnCliff(x0 - 1, y0, y1): x0--; grew = true; break;
                        case 1 when x1 - x < 3 && ColumnCliff(x1 + 1, y0, y1): x1++; grew = true; break;
                        case 2 when y - y0 < 3 && RowCliff(y0 - 1, x0, x1): y0--; grew = true; break;
                        case 3 when y1 - y < 3 && RowCliff(y1 + 1, x0, x1): y1++; grew = true; break;
                    }
                    if (!grew && step >= 4) break;
                }
                float c = world.CellSize;

                // 지면에서 몇 겹 안쪽인가 — 반경 r 고리에 비절벽이 처음 나타나는 r-1.
                // 공중 배치 여부(가장자리 줄은 땅에 서야 한다)를 이것으로 정한다.
                int depth = 2;
                for (int r = 1; r <= 2 && depth == 2; r++)
                    for (int dy = -r; dy <= r && depth == 2; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                            if (!CliffAt(x + dx, y + dy)) { depth = r - 1; break; }
                        }

                // 공중에 뜬 안쪽 바위는 벽면에서 <b>안쪽으로 물러선다</b> — 가장자리 바위와
                // 같은 경계선까지 나오게 두면, 아랫바위의 둥근 어깨 위로 윗바위가 튀어나와
                // 버섯처럼 걸쳐진다. 직사각형의 변이 절벽 지역의 끝(그 너머가 비절벽)인
                // 쪽만 물린다 — 절벽으로 이어지는 쪽은 물릴 이유가 없다.
                float FaceInset = 0.4f * Cell;
                float inXn = 0f, inXp = 0f, inZn = 0f, inZp = 0f;
                if (depth > 0)
                {
                    inXn = ColumnCliff(x0 - 1, y0, y1) ? 0f : FaceInset;
                    inXp = ColumnCliff(x1 + 1, y0, y1) ? 0f : FaceInset;
                    inZn = RowCliff(y0 - 1, x0, x1) ? 0f : FaceInset;
                    inZp = RowCliff(y1 + 1, x0, x1) ? 0f : FaceInset;
                }
                float rectW = (x1 - x0 + 1) * c - inXn - inXp;
                float rectH = (y1 - y0 + 1) * c - inZn - inZp;

                // 능선 노이즈가 이 칸의 목표 능선고를 정한다 — 이웃 절벽끼리 높이가 이어져
                // 몇 칸에 걸쳐 완만하게 솟았다 가라앉고, 칸별 잔결이 그 위에 얹힌다.
                float ridge = Mathf.PerlinNoise(x * CliffRidgeFrequency + 5.3f, y * CliffRidgeFrequency + 9.1f);
                float wantTop = Mathf.Lerp(CliffHeightLow, CliffHeightHigh, ridge)
                              + (Hash(x, y, 31) % 1000 / 1000f - 0.5f) * 0.6f * Cell;

                // <b>쌓지 않는다.</b> 높이는 배치 고도로 만든다: 가장자리 줄(depth 0)만 땅에
                // 서고, 안쪽 바위는 공중에 띄운다 — 바닥은 앞줄이 가리므로 보이지 않고,
                // 띄운 만큼 꼭대기가 능선고에 닿는다. 오레오처럼 포개진 이음매가 없다.
                float baseY = depth == 0
                    ? -CliffBaseSink
                    : (Mathf.Lerp(0.6f, 1.2f, Hash(x, y, 71) % 1000 / 1000f) + (depth - 1) * 0.6f) * Cell;

                int pick = depth == 0
                    ? lofty[Hash(x, y, 23) % lofty.Count]
                    : Hash(x, y, 23) % prefabs.Count;
                // 세로는 넉넉히 늘린다(스케일 자유화) — 안쪽은 물러선 만큼 좁아진 발판으로
                // 능선고를 채워야 하고, 가장자리도 낮으면 담장처럼 보인다.
                float stretch = depth == 0
                    ? Mathf.Lerp(1.15f, 1.75f, Hash(x, y, 37) % 1000 / 1000f)
                    : Mathf.Lerp(1.0f, 1.7f, Hash(x, y, 37) % 1000 / 1000f);

                // <b>크기에도 노이즈</b>. 예산이 배율을 정하게 두면 예산이 벽 두께에서 오는
                // 상수라, 같은 벽을 따라 전부 같은 크기가 된다 — 정렬된 규칙성의 정체.
                // 중간 파장 펄린(큰 흐름) × 칸별 해시(낱개 차이)를 예산에 곱한다.
                float sizeNoise = Mathf.Lerp(0.55f, 1.05f,
                    0.6f * Mathf.PerlinNoise(x * 0.23f + 61.7f, y * 0.23f + 8.9f)
                  + 0.4f * (Hash(x, y, 47) % 1000 / 1000f));

                // 회전 흐름 필드 — 이웃 바위끼리 <b>비슷한 각도로 천천히 도는</b> 연속
                // 노이즈. 칸별 독립 각도는 벽면을 모자이크로 만들고, 고정 각도는 격자를
                // 드러낸다. 흐름을 따라 돌면 벽 전체가 워핑된 것처럼 굽이쳐 보인다.
                float flow = Mathf.PerlinNoise(x * 0.07f + 12.3f, y * 0.07f + 71.9f);

                float yaw, scaleX, scaleZ, tall;
                float rockHeight;
                float? fixedX = null, fixedZ = null;   // 가장자리 줄의 지면 축 — 후퇴 위치로 고정

                if (depth == 0)
                {
                    // ── 가장자리 줄: 90° 기본 + 흐름 기울임(±14°) + 비균등 스케일 ──
                    // 원인 규명이 끝난 틈의 마지막 근원은 <b>둥근 옆구리</b>다 — AABB끼리
                    // 닿아도 실제 메시는 안쪽으로 굽어 V자 틈이 남는다. 답은 벽 방향으로
                    // 이웃 칸 위까지 늘려 서로 파묻는 것.
                    // 기울인 바위의 축정렬 점유 폭은 두 발자국이 섞인 값이 된다 —
                    //   가로 점유 = cosδ·a + sinδ·b,  세로 점유 = sinδ·a + cosδ·b
                    // 원하는 점유(벽 방향 alongSize, 지면 방향 acrossBudget)를 넣고
                    // a·b를 역산하면, 기울여도 예산에 정확히 맞는 배율이 나온다.
                    // 펄린은 0.5 근처를 맴돌아 ±14로 곱해도 실제로는 ±4°쯤만 나온다 —
                    // 눈에 띄려면 배율을 크게 잡고 칸별 지터를 얹은 뒤 한도로 자른다.
                    // 한도 20°는 역산 안전선(tanδ ≤ 지면축/벽축 최악비 0.42 → 22.8°) 안이다.
                    float tiltYaw = Mathf.Clamp(
                        (flow - 0.5f) * 2f * 34f + (Hash(x, y, 167) % 1000 / 1000f - 0.5f) * 14f,
                        -20f, 20f);
                    int quarter = Hash(x, y, 89) % 4;
                    yaw = 90f * quarter + tiltYaw;
                    bool swap = quarter % 2 == 1;
                    float footX = swap ? footprints[pick].y : footprints[pick].x;
                    float footZ = swap ? footprints[pick].x : footprints[pick].y;

                    // 벽이 달리는 축(직사각형이 긴 쪽)으로는 겹치도록 크게,
                    // 지면을 마주보는 축으로는 예산에서 <b>후퇴량</b>을 뺀 만큼.
                    //
                    // 후퇴가 이 벽의 "일자" 를 깨는 장치다: 침범 금지 탓에 앞면이 밖으로는
                    // 못 나가므로, 전부 경계선에 딱 붙으면 자로 그은 벽이 된다. 대신 바위마다
                    // 앞면을 0~0.9m 파도치듯 안으로 물리고(파장 ≈ 8칸), 뒷면은 절벽 쪽에
                    // 밀착시켜 두께만 얇아지게 한다 — 벽을 관통하는 구멍은 생기지 않는다.
                    bool alongX = rectW >= rectH;
                    float recede = Mathf.Lerp(0f, 0.45f * Cell,
                        0.7f * Mathf.PerlinNoise(x * 0.13f + 21.2f, y * 0.13f + 55.8f)
                      + 0.3f * (Hash(x, y, 163) % 1000 / 1000f));
                    float acrossBudget = Mathf.Max((alongX ? rectH : rectW) * 0.98f - recede, c * 0.8f);
                    float alongSize = Mathf.Min(alongX ? rectW : rectH,
                                                c * Mathf.Lerp(1.25f, 1.9f, Mathf.PerlinNoise(x * 0.31f + 3.1f, y * 0.31f + 44.9f)));

                    // 지면이 어느 쪽인가 — 후퇴는 지면 쪽에서만 일어나고 반대쪽은 밀착한다.
                    // 양쪽 다 지면(폭 1칸 능선)이면 가운데 두고 양쪽으로 균등하게 얇아진다.
                    bool groundNeg = alongX ? !RowCliff(y0 - 1, x0, x1) : !ColumnCliff(x0 - 1, y0, y1);
                    bool groundPos = alongX ? !RowCliff(y1 + 1, x0, x1) : !ColumnCliff(x1 + 1, y0, y1);
                    float rectLo = (alongX ? y0 - y : x0 - x) - 0.5f;   // 칸 중심 기준 직사각형 범위(칸)
                    float rectHi = (alongX ? y1 - y : x1 - x) + 0.5f;
                    float acrossCenter;
                    if (groundNeg && !groundPos) acrossCenter = rectHi * c - acrossBudget * 0.5f;
                    else if (groundPos && !groundNeg) acrossCenter = rectLo * c + acrossBudget * 0.5f;
                    else acrossCenter = (rectLo + rectHi) * 0.5f * c;
                    if (alongX) fixedZ = acrossCenter; else fixedX = acrossCenter;

                    float cs = Mathf.Cos(tiltYaw * Mathf.Deg2Rad), sn = Mathf.Abs(Mathf.Sin(tiltYaw * Mathf.Deg2Rad));
                    float det = cs * cs - sn * sn;   // δ≤14°라 0.88 이상 — 역산은 항상 안전
                    float wantX = alongX ? alongSize : acrossBudget;
                    float wantZ = alongX ? acrossBudget : alongSize;
                    float extentX = (cs * wantX - sn * wantZ) / det;   // 월드 X를 향한 발자국의 실제 크기
                    float extentZ = (cs * wantZ - sn * wantX) / det;
                    // 역산이 음수로 떨어지면(짧은 축 대비 기울임이 과할 때) 기울임을 포기한다
                    if (extentX <= 0.05f || extentZ <= 0.05f)
                    {
                        yaw = 90f * quarter;
                        extentX = wantX; extentZ = wantZ;
                    }
                    scaleX = extentX / footX;
                    scaleZ = extentZ / footZ;

                    // 최저 높이를 보장하되, <b>최저선 자체가 노이즈</b>다 — 상수로 두면
                    // 자연 높이가 그보다 낮은 바위들이 전부 정확히 같은 높이에 들러붙어
                    // 계단처럼 보인다(클램프 무더기). 하한 3.0m는 점프 차단선(1.3m)의 두 배.
                    float minTop = Cell * Mathf.Lerp(1.5f, 2.3f,
                        0.5f * Mathf.PerlinNoise(x * 0.19f + 77.3f, y * 0.19f + 15.1f)
                      + 0.5f * (Hash(x, y, 151) % 1000 / 1000f));
                    tall = Mathf.Max(stretch * (scaleX + scaleZ) * 0.5f,
                                     (minTop + CliffBaseSink) / heights[pick]);
                    rockHeight = heights[pick] * tall;
                }
                else
                {
                    // ── 안쪽: 흐름을 따라 도는 자유각 + 균등 발판 ── 흐름 필드가 큰 방향을
                    // 정하고 칸별 해시가 ±25°만 얹는다. 회전한 바위의 축정렬 점유 폭은
                    // 원본의 √2배까지 커지므로 배율은 그 실제 점유 폭에서 역산한다.
                    yaw = flow * 720f + (Hash(x, y, 89) % 1000 / 1000f - 0.5f) * 50f;
                    float span = RotatedSpan(footprints[pick], yaw);
                    float scaleForTop = (wantTop - baseY) / (heights[pick] * stretch);
                    float scaleForBudget = Mathf.Min(rectW, rectH) * 0.98f / span * sizeNoise;
                    float scale = Mathf.Min(scaleForTop, scaleForBudget);
                    scale *= 0.93f;   // 기울일 것이므로(아래) 점유 폭이 커진다 — 미리 줄인다
                    scaleX = scaleZ = scale;
                    tall = scale * stretch;
                    rockHeight = heights[pick] * tall;
                    baseY = Mathf.Min(wantTop - rockHeight, (1.2f + (depth - 1) * 0.6f) * Cell);
                }

                // 축별 실제 반폭(m). 가장자리 줄은 역산의 목표가 곧 월드 점유 폭이라
                // (기울임 포함) 회전각별 재계산이 필요 없다 — 기울임 반폭까지 정확하다.
                float halfX, halfZ;
                if (depth == 0)
                {
                    float cs2 = Mathf.Abs(Mathf.Cos((yaw - 90f * (Hash(x, y, 89) % 4)) * Mathf.Deg2Rad));
                    bool swap2 = (Hash(x, y, 89) % 4) % 2 == 1;
                    float fx = (swap2 ? footprints[pick].y : footprints[pick].x) * scaleX;
                    float fz = (swap2 ? footprints[pick].x : footprints[pick].y) * scaleZ;
                    float sn2 = Mathf.Sqrt(Mathf.Max(0f, 1f - cs2 * cs2));
                    halfX = (cs2 * fx + sn2 * fz) * 0.5f;
                    halfZ = (sn2 * fx + cs2 * fz) * 0.5f;
                }
                else
                {
                    halfX = scaleX * RotatedSpan(footprints[pick], yaw) * 0.5f;
                    halfZ = halfX;
                }

                // 중심의 이동 범위는 두 조건의 교집합이다:
                //   ① 직사각형 안 — 지면 타일을 침범하지 않는다
                //   ② <b>자기 칸을 전부 덮는 위치</b> — 이 보장이 없으면 이웃 두 바위가
                //      반대 방향으로 미끄러진 사이 칸이 아무도 안 덮는 관통 구멍이 된다.
                // 바위가 칸보다 작으면(폭 1칸 벽 + 둥근 프리팹) 이동 여유는 0이다.
                float coverX = Mathf.Max(0f, halfX - c * 0.5f);
                float coverZ = Mathf.Max(0f, halfZ - c * 0.5f);
                float minX = Mathf.Max((x0 - x - 0.5f) * c + inXn + halfX, -coverX);
                float maxX = Mathf.Min((x1 - x + 0.5f) * c - inXp - halfX, coverX);
                float minZ = Mathf.Max((y0 - y - 0.5f) * c + inZn + halfZ, -coverZ);
                float maxZ = Mathf.Min((y1 - y + 0.5f) * c - inZp - halfZ, coverZ);
                float waveX = Mathf.PerlinNoise(x * CliffFaceFrequency + 17.7f, y * CliffFaceFrequency + 3.9f) - 0.5f;
                float waveZ = Mathf.PerlinNoise(x * CliffFaceFrequency + 41.3f, y * CliffFaceFrequency + 27.1f) - 0.5f;

                // 연속 노이즈(벽면의 큰 물결) + 칸별 해시(낱개 어긋남). 크기가 줄어든 만큼
                // 여유가 생겨 클램프에 깎이지 않는다 — 크기 노이즈가 위치 노이즈를 살린다.
                Vector3 pos = world.CellToWorldCenter(new Vector2Int(x, y));
                pos.x += fixedX ?? Mathf.Clamp(waveX * 3f * c + (Hash(x, y, 57) % 1000 / 1000f - 0.5f) * 0.7f * Cell,
                                               minX, Mathf.Max(minX, maxX));
                pos.z += fixedZ ?? Mathf.Clamp(waveZ * 3f * c + (Hash(x, y, 63) % 1000 / 1000f - 0.5f) * 0.7f * Cell,
                                               minZ, Mathf.Max(minZ, maxZ));
                pos.y = world.Origin.y + baseY - bottoms[pick] * tall;

                // 안쪽 바위는 살짝 기울인다 — 데모도 일부를 기울여 놓았다. 수직 이음매가
                // 평행선으로 정렬되는 것을 깨는 데는 몇 도면 충분하다. 기울면 점유 폭이
                // 커지므로 예산이 빠듯한 가장자리 줄(depth 0)은 세우고, 안쪽만 기울인다.
                float tiltX = depth > 0 ? (Hash(x, y, 79) % 1000 / 1000f - 0.5f) * 8f : 0f;
                float tiltZ = depth > 0 ? (Hash(x, y, 83) % 1000 / 1000f - 0.5f) * 8f : 0f;

                var cliff = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[pick], parent);
                cliff.transform.SetPositionAndRotation(pos, Quaternion.Euler(tiltX, yaw, tiltZ));
                // 비균등 스케일은 로컬 축 기준이다 — 90° 회전이면 로컬 X가 월드 Z를 향하므로
                // 월드 축으로 정한 배율을 로컬로 되돌려 넣는다.
                bool swapped = depth == 0 && (Hash(x, y, 89) % 4) % 2 == 1;
                cliff.transform.localScale = swapped
                    ? new Vector3(scaleZ, tall, scaleX)
                    : new Vector3(scaleX, tall, scaleZ);
                placed++;
            }

        // ── 이음새 메움 ──
        // 큰 바위끼리는 어긋난 늘림 축·벽 꺾임 탓에 실틈이 남을 수 있다. 지면에 노출된
        // 이웃 절벽 칸 쌍마다 <b>공유 변의 중점</b>에 작은 바위를 박는다 — 폭이 1칸을
        // 넘지 않으면 두 칸(둘 다 절벽)의 합집합 안이라 침범이 불가능하고,
        // 양옆 큰 바위에 파묻혀 틈만 지운다.
        bool EdgeCell(int cx, int cy)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (!CliffAt(cx + dx, cy + dy)) return true;
            return false;
        }

        int chinks = 0;
        for (int y = 0; y < map.height; y++)
            for (int x = 0; x < map.width; x++)
            {
                if (!CliffAt(x, y)) continue;
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
                {
                    if (!CliffAt(x + dx, y + dy)) continue;
                    // 둘 다 안쪽이면 이미 큰 바위들이 깊이 겹쳐 있다 — 얼굴 쌍만 메운다
                    if (!EdgeCell(x, y) && !EdgeCell(x + dx, y + dy)) continue;

                    int pick = lofty[Hash(x, y, 131 + dx) % lofty.Count];
                    float yaw = 90f * (Hash(x, y, 137 + dy) % 4);
                    bool swap = (Hash(x, y, 137 + dy) % 4) % 2 == 1;
                    float footX = swap ? footprints[pick].y : footprints[pick].x;
                    float footZ = swap ? footprints[pick].x : footprints[pick].y;

                    float size = world.CellSize * Mathf.Lerp(0.8f, 1f, Hash(x, y, 139) % 1000 / 1000f);
                    float sX = size / footX, sZ = size / footZ;
                    // 최저선도 노이즈 — 상수면 이음새들이 전부 같은 높이에 들러붙는다(클램프 무더기)
                    float chinkTop = Cell * Mathf.Lerp(1.2f, 1.95f,
                        0.5f * Mathf.PerlinNoise(x * 0.19f + 33.7f, y * 0.19f + 91.3f)
                      + 0.5f * (Hash(x, y, 157) % 1000 / 1000f));
                    float tall = Mathf.Max((sX + sZ) * 0.5f * Mathf.Lerp(1.1f, 1.6f, Hash(x, y, 149) % 1000 / 1000f),
                                           (chinkTop + CliffBaseSink) / heights[pick]);

                    Vector3 pos = world.CellToWorld(new Vector2Int(x, y))
                                + new Vector3((dx + 1) * 0.5f * world.CellSize, 0f, (dy + 1) * 0.5f * world.CellSize);

                    // 벽면 후퇴 노이즈를 큰 바위와 공유한다 — 이음새가 경계선에 남아 있으면
                    // 물러난 벽의 홈을 도로 메워 일자 벽이 되살아난다. 물러날 방향(지면 반대)의
                    // 두 칸이 절벽일 때만, 그 안으로 들어간다.
                    float rec = Mathf.Min(0.6f * world.CellSize, Mathf.Lerp(0f, 0.45f * Cell,
                        0.7f * Mathf.PerlinNoise(x * 0.13f + 21.2f, y * 0.13f + 55.8f)
                      + 0.3f * (Hash(x, y, 163) % 1000 / 1000f)));
                    if (dx == 1)   // 동서 쌍 — 남북으로 물린다
                    {
                        if (!CliffAt(x, y - 1) || !CliffAt(x + 1, y - 1))
                        { if (CliffAt(x, y + 1) && CliffAt(x + 1, y + 1)) pos.z += rec; }
                        else if (!CliffAt(x, y + 1) || !CliffAt(x + 1, y + 1))
                        { if (CliffAt(x, y - 1) && CliffAt(x + 1, y - 1)) pos.z -= rec; }
                    }
                    else           // 남북 쌍 — 동서로 물린다
                    {
                        if (!CliffAt(x - 1, y) || !CliffAt(x - 1, y + 1))
                        { if (CliffAt(x + 1, y) && CliffAt(x + 1, y + 1)) pos.x += rec; }
                        else if (!CliffAt(x + 1, y) || !CliffAt(x + 1, y + 1))
                        { if (CliffAt(x - 1, y) && CliffAt(x - 1, y + 1)) pos.x -= rec; }
                    }
                    pos.y = world.Origin.y - CliffBaseSink - bottoms[pick] * tall;

                    var chink = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[pick], parent);
                    chink.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                    chink.transform.localScale = swap ? new Vector3(sZ, tall, sX) : new Vector3(sX, tall, sZ);
                    chinks++;
                }
            }

        Debug.Log($"[WorldTerrainGenerator] 절벽 바위 {placed}개 + 이음새 {chinks}개 (칸 {world.CellSize}m)");
    }

    // ── 경계 · 정적 표시 ────────────────────────────────────────

    /// <summary>
    /// 맵 둘레에 보이지 않는 벽을 세운다 — 지형은 맵 크기만큼만 있어서 그 밖은 허공이다.
    ///
    /// <b>총알은 통과해야 한다.</b> 그래서 `Ignore Raycast` 레이어에 둔다:
    /// 사격 판정(<see cref="ProjectileSystem"/>)이 쓰는 `Physics.DefaultRaycastLayers`가
    /// 정확히 이 레이어를 빼고 있어, 총알은 없는 것처럼 지나간다.
    /// 반면 물리 충돌은 레이어 충돌 매트릭스가 따로 정하므로 플레이어는 그대로 막힌다.
    /// </summary>
    static void CreateBounds(MapDataSO map, World world, Transform root)
    {
        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast < 0)
        {
            Debug.LogWarning("[WorldTerrainGenerator] 'Ignore Raycast' 레이어가 없어 경계벽을 세우지 못했습니다.");
            return;
        }

        var parent = new GameObject("Bounds").transform;
        parent.SetParent(root, false);

        float w = map.width * world.CellSize, h = map.height * world.CellSize;
        float t = BoundsThickness, half = BoundsHeight * 0.5f;

        // 벽 안쪽 면이 맵 경계에 딱 맞도록 두께의 절반만큼 바깥으로 민다
        Wall("Bounds_-X", new Vector3(-t * 0.5f, half, h * 0.5f), new Vector3(t, BoundsHeight, h + t * 2f));
        Wall("Bounds_+X", new Vector3(w + t * 0.5f, half, h * 0.5f), new Vector3(t, BoundsHeight, h + t * 2f));
        Wall("Bounds_-Z", new Vector3(w * 0.5f, half, -t * 0.5f), new Vector3(w + t * 2f, BoundsHeight, t));
        Wall("Bounds_+Z", new Vector3(w * 0.5f, half, h + t * 0.5f), new Vector3(w + t * 2f, BoundsHeight, t));

        void Wall(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name) { layer = ignoreRaycast };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center - new Vector3(0f, BoundsSink, 0f);

            var box = go.AddComponent<BoxCollider>();
            box.size = size;
        }
    }

    /// <summary>
    /// 생성물 전체를 정적으로 표시한다 — 배칭·오클루전·라이트맵이 걸리도록.
    /// 플래그를 나열하는 이유: `(StaticEditorFlags)~0`은 아직 없는 비트까지 켠다.
    /// </summary>
    static void MarkStatic(Transform root)
    {
        const StaticEditorFlags All =
            StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic |
            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OffMeshLinkGeneration |
            StaticEditorFlags.ReflectionProbeStatic;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, All);
    }

    /// <summary>
    /// 프리팹의 폭(가로·세로 중 큰 쪽)과 <b>바닥이 원점보다 얼마나 아래인지</b>를 잰다.
    ///
    /// 바닥을 따로 재는 이유: 이 암벽들은 중심이 원점에 있어서 그냥 지면에 놓으면
    /// 절반이 땅에 묻힌다. 스케일을 키울수록 더 묻히므로, 바닥을 기준으로 올려줘야
    /// "키운 만큼 높아진다"가 성립한다.
    /// </summary>
    static bool PrefabBounds(GameObject prefab, out Vector2 footprint, out float bottom, out float height)
    {
        footprint = Vector2.zero; bottom = 0f; height = 0f;

        var rends = prefab.GetComponentsInChildren<MeshRenderer>(true);
        if (rends.Length == 0) return false;

        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        footprint = new Vector2(b.size.x, b.size.z);   // 두 축을 따로 — 회전각별 점유 폭 계산용
        bottom = b.min.y;              // 보통 음수 — 원점 아래로 내려간 깊이
        height = b.size.y;
        return footprint.x > 0.01f && footprint.y > 0.01f && height > 0.01f;
    }

    /// <summary>
    /// yaw로 돌린 프리팹이 축정렬로 차지하는 폭(두 축 중 큰 쪽). 회전한 사각형의 AABB는
    /// |cos|·가로 + |sin|·세로로 원본 최대 폭보다 √2배까지 커진다 — 자유각 회전을 쓰려면
    /// 배율을 반드시 <b>이 값</b>에서 역산해야 칸을 넘지 않는다.
    /// </summary>
    static float RotatedSpan(Vector2 footprint, float yawDeg)
    {
        float r = yawDeg * Mathf.Deg2Rad;
        float c = Mathf.Abs(Mathf.Cos(r)), s = Mathf.Abs(Mathf.Sin(r));
        return Mathf.Max(footprint.x * c + footprint.y * s,
                         footprint.x * s + footprint.y * c);
    }

    /// <summary>URP 지형 머티리얼 — 렌더 파이프라인의 기본값을 쓰되, 없으면 셰이더로 직접 만든다.</summary>
    static Material TerrainMaterial()
    {
        string path = $"{AssetFolder}/Terrain.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (pipeline != null && pipeline.defaultTerrainMaterial != null)
            return pipeline.defaultTerrainMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (shader == null)
        {
            Debug.LogWarning("[WorldTerrainGenerator] URP 지형 셰이더를 찾지 못했습니다 — 지형이 보이지 않을 수 있습니다.");
            return null;
        }

        mat = new Material(shader) { name = "Terrain" };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>
    /// 지면·절벽·강바닥 3종 레이어 — Idyllic Fantasy Nature의 것을 그대로 쓴다.
    /// <b>순서가 곧 스플랫 채널</b>이다(0 지면 / 1 절벽 / 2 강바닥) — <see cref="PaintSplat"/>과 맞물려 있으니
    /// 순서를 바꾸면 칠이 뒤바뀐다.
    ///
    /// 예전에는 단색 텍스처를 구워 썼다. 에셋의 레이어는 노멀맵까지 들어 있어 같은 조명에서
    /// 굴곡이 살고, 무엇보다 나무·풀 프리팹과 같은 팔레트라 지형만 따로 노는 일이 없다.
    /// </summary>
    static TerrainLayer[] BuildLayers()
    {
        const string LayerFolder = "Assets/ThirdParty/Idyllic Fantasy Nature/Terrain Layer";

        var layers = new[]
        {
            Load($"{LayerFolder}/Grass_Layer.terrainlayer"),   // 지면
            Load($"{LayerFolder}/Rock_Layer.terrainlayer"),    // 절벽
            Load($"{LayerFolder}/Sand_Layer.terrainlayer"),    // 강바닥
        };

        foreach (var l in layers)
            if (l == null) return System.Array.Empty<TerrainLayer>();

        return layers;

        static TerrainLayer Load(string path)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
                Debug.LogError($"[WorldTerrainGenerator] 지형 레이어를 찾지 못했습니다: {path}\n" +
                               "Idyllic Fantasy Nature 에셋이 지워졌거나 옮겨졌습니다.");
            return layer;
        }
    }

    /// <summary>
    /// 표면 칠하기. 강바닥은 <b>실제 높이</b>로 정한다 — 워핑으로 흐트러진 물가와 텍스처가
    /// 어긋나지 않는다. 절벽은 이제 지형이 아니라 프리팹이라 높이로 알 수 없으므로
    /// <b>타일</b>로 칠한다(프리팹 사이 틈으로 풀밭이 비치지 않게 하는 것이 목적).
    /// </summary>
    static void PaintSplat(TerrainData data, MapDataSO map, float[,] height)
    {
        int res = data.alphamapResolution;
        int hres = height.GetLength(0);
        var alphas = new float[res, res, 3];
        float total = TerrainHeightRange;

        // 절벽 타일에서 바깥으로 번지는 정도(칸) — 딱 자르면 칸 경계가 그대로 드러난다
        var cliffField = SignedDistance(map, MapTile.Cliff);

        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                // 알파맵 좌표 → 높이맵 좌표
                int hi = Mathf.Clamp(Mathf.RoundToInt((float)i / (res - 1) * (hres - 1)), 0, hres - 1);
                int hj = Mathf.Clamp(Mathf.RoundToInt((float)j / (res - 1) * (hres - 1)), 0, hres - 1);

                float world = height[hj, hi] * total - RiverDepth;   // 월드 높이(m)

                float tx = (float)i / (res - 1) * map.width;
                float ty = (float)j / (res - 1) * map.height;
                float cliff = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.4f, -0.3f, SampleField(cliffField, map, tx, ty)));

                // 모래는 수면(-0.15m) 아래에서 시작한다. -0.05부터 깔면 물 위의 마른 물가까지
                // 모래가 덮여, 밝은 모래 텍스처가 웅덩이마다 흰 테두리 원반처럼 보인다.
                float bed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, -0.45f, world));
                float ground = Mathf.Max(0f, 1f - cliff - bed);

                float sum = cliff + bed + ground;
                alphas[j, i, 0] = ground / sum;
                alphas[j, i, 1] = cliff / sum;
                alphas[j, i, 2] = bed / sum;
            }

        data.SetAlphamaps(0, 0, alphas);
    }

    // ── 물 ──────────────────────────────────────────────────────

    /// <summary>맵을 덮는 사각형 한 장 — 지형이 파인 곳(강)에서만 보이고 가장자리는 저절로 얕아진다.</summary>
    static void CreateWater(MapDataSO map, World world, Transform root, float[,] height)
    {
        float w = map.width * world.CellSize, h = map.height * world.CellSize;

        // 맵 밖으로 한참 더 깔아 <b>바다</b>로 쓴다 — 지형이 물 한가운데 뜬 섬으로 보인다.
        // 맵 크기의 배수라 큰 맵에서도 수평선이 같은 비율로 물러난다.
        float margin = Mathf.Max(w, h) * SeaMargin;
        float x0 = -margin, z0 = -margin;
        float sizeX = w + margin * 2f, sizeZ = h + margin * 2f;

        // <b>격자로 쪼갠다.</b> 물 셰이더가 정점을 흔들어 파도를 만들기 때문에, 사각형 네 점으로는
        // 흔들 것이 없어 수면이 판판한 판때기로 남는다. 파도 주기가 몇 미터 단위라
        // 정점 간격도 그 정도여야 물결이 산다.
        int cols = Mathf.Clamp(Mathf.CeilToInt(sizeX / WaterVertexSpacing), 1, 512);
        int rows = Mathf.Clamp(Mathf.CeilToInt(sizeZ / WaterVertexSpacing), 1, 512);

        var verts = new Vector3[(cols + 1) * (rows + 1)];
        var uvs = new Vector2[verts.Length];
        var norms = new Vector3[verts.Length];
        var colors = new Color[verts.Length];

        for (int j = 0; j <= rows; j++)
            for (int i = 0; i <= cols; i++)
            {
                int v = j * (cols + 1) + i;
                float fx = (float)i / cols, fz = (float)j / rows;
                float wx = x0 + fx * sizeX, wz = z0 + fz * sizeZ;

                verts[v] = new Vector3(wx, 0f, wz);
                norms[v] = Vector3.up;
                // UV는 칸 단위로 — 바다까지 같은 밀도로 이어져야 물결 타일링이 끊기지 않는다
                uvs[v] = new Vector2(fx * sizeX / world.CellSize, fz * sizeZ / world.CellSize);

                // <b>정점 컬러가 거품을 정한다</b> — 빨강이 거품, 검정이 맑은 물이다.
                // 채우지 않으면 Unity 기본값인 흰색이 들어가 수면 전체가 거품 최대치로 렌더돼,
                // 바다가 통째로 흰 판때기가 된다.
                //
                // 거품은 <b>수심</b>으로 정한다. 경계선까지의 거리로 재면 섬 안쪽 수면(강)이
                // 전부 거품이 되고 띠도 두꺼워진다 — 물가가 어디인지는 결국 물이 얕은 곳이다.
                float ttx = wx / world.CellSize, ttz = wz / world.CellSize;
                bool overMap = ttx >= 0f && ttz >= 0f && ttx <= map.width && ttz <= map.height;
                float bed = overMap ? SampleHeightAt(height, ttx, ttz, map) : -99f;   // 맵 밖은 먼 바다
                float depth = WaterLevel - bed;
                // 얕을수록 거품 — 다만 수심 0 부근에서는 다시 0으로 죽인다. 수면과 거의 같은
                // 높이의 지형 위에서는 물 한 겹 없이 거품 100%가 그대로 얹혀, 물가의 얕은
                // 둔덕이 흰색 원반처럼 렌더되던 원인이다. 거품 띠는 물가에서 살짝 떨어져 남는다.
                float foam = Mathf.Clamp01(1f - depth / FoamDepth) * Mathf.Clamp01(depth / 0.05f);
                colors[v] = new Color(foam, 0f, 0f, 1f);
            }

        var tris = new int[cols * rows * 6];
        int ti = 0;
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                int a = j * (cols + 1) + i, b = a + 1, c = a + cols + 1, d = c + 1;
                tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                tris[ti++] = c; tris[ti++] = d; tris[ti++] = b;
            }

        var mesh = new Mesh { name = $"{map.Id.Replace(':', '_')}_Water" };
        if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.RecalculateBounds();

        string meshPath = $"{AssetFolder}/{mesh.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);

        var go = new GameObject("Water (Sea)");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, WaterLevel, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = WaterMaterial();
        // 콜라이더 없음 — 물은 건너다니는 것이고, 바닥은 Terrain이 받는다
    }

    /// <summary>
    /// 물 머티리얼 — Bitgem StylisedWater의 것을 <b>복제해서</b> 쓴다.
    ///
    /// 원본을 직접 참조하지 않는 이유는 색·파도를 우리 맵에 맞게 만지게 되는데,
    /// 그러면 서드파티 에셋을 고치는 셈이라 업데이트 때 덮이거나 예제 씬이 함께 바뀐다.
    ///
    /// 에셋의 WaterVolume 컴포넌트는 쓰지 않는다 — 그쪽은 타일 볼륨(최대 100×100)을 굽는
    /// 방식이라 수백 미터짜리 바다를 담지 못한다. 우리는 메시를 직접 굽고 셰이더만 빌린다.
    /// </summary>
    static Material WaterMaterial()
    {
        string path = $"{AssetFolder}/Water.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        const string SourcePath = "Assets/ThirdParty/Bitgem/StylisedWater/URP/Materials/example-water-01.mat";
        var source = AssetDatabase.LoadAssetAtPath<Material>(SourcePath);
        if (source == null)
        {
            Debug.LogError($"[WorldTerrainGenerator] 물 머티리얼 원본을 찾지 못했습니다: {SourcePath}");
            return null;
        }

        mat = new Material(source) { name = "Water" };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data/Maps"))
            AssetDatabase.CreateFolder("Assets/Data", "Maps");
        if (!AssetDatabase.IsValidFolder(AssetFolder))
            AssetDatabase.CreateFolder("Assets/Data/Maps", "Terrain");
    }
}
