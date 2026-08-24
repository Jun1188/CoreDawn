using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="WorldTerrainGenerator"/>가 읽는 수치 전부 — 지형을 굽는 "레시피".
///
/// 왜 상수에서 에셋으로 뺐나: 이 값들은 코드가 아니라 <b>튜닝 대상</b>이다. 물가 경사를
/// 한 번 보려고 스크립트를 고치면 도메인 리로드가 돌고, 되돌리려면 diff를 봐야 하고,
/// 무엇보다 아트 쪽에서 만질 수가 없다. 에셋이면 인스펙터에서 바꾸고 바로 다시 구우면 된다.
///
/// 이 클래스는 <b>에디터 어셈블리</b>에 있다 — 지형 생성은 메뉴에서 도는 저작 도구지
/// 런타임 기능이 아니므로, 런타임 World 컴포넌트에 에디터 전용 설정을 달지 않는다.
/// 그래서 씬에 배선하지 않고 <see cref="LoadOrCreate"/>가 프로젝트에서 찾아 쓴다.
///
/// <b>단위 규칙</b>(생성기 머리말과 같은 규칙):
///   · 물가·워핑·해상도는 <b>미터</b>다(이름 끝의 M). 계산은 칸 좌표계라 생성기가
///     Cell로 환산한다 — 셀 크기를 바꿔도 물가의 실제 형상이 유지된다.
///   · 절벽은 반대로 <b>칸 비례</b>다. 바위의 xz 폭이 곧 칸이라, 높이·후퇴량이 같이
///     커져야 바위 비율이 유지된다.
/// </summary>
public class TerrainGenSettings : ScriptableObject
{
    /// <summary>이 에셋이 사는 자리. 하나만 두고 생성기가 여기서 찾는다.</summary>
    public const string AssetPath = "Assets/Data/Maps/TerrainGenSettings.asset";

    // ── 경로 ────────────────────────────────────────────────────

    [Header("경로")]
    [Tooltip("구워낸 TerrainData·물 메시·머티리얼이 저장되는 폴더. 없으면 만든다.")]
    public string assetFolder = "Assets/Data/Maps/Terrain";

    // 아래는 전부 <b>직접 참조</b>다 — 이름+폴더 문자열로 찾던 것을 걷어냈다.
    // 문자열은 에셋을 옮기거나 이름을 바꾸면 조용히 끊기고(런타임 경고 하나로 끝난다),
    // 인스펙터에서 끌어다 놓을 수도, 어떤 것이 쓰이는지 눈으로 볼 수도 없다.
    // 참조면 Unity가 의존성으로 추적하므로 이름을 바꿔도 따라온다.

    [Tooltip("지면·절벽·강바닥 순서. <b>이 순서가 곧 스플랫 채널 번호</b>라 바꾸면 칠이 뒤바뀐다.")]
    public TerrainLayer[] terrainLayers = new TerrainLayer[3];

    [Tooltip("물 머티리얼의 원본. 이걸 복제해 맵 전용 사본을 만든다.")]
    public Material waterMaterialSource;

    // ── 지형 프로파일 ───────────────────────────────────────────

    [Header("지형 프로파일")]
    [Tooltip("지형이 담는 높이 폭(m). 절벽은 프리팹이 맡으므로 지형이 표현할 것은 강 깊이뿐이다 — " +
             "여유를 조금 둬야 미세 굴곡이 잘리지 않는다.")]
    public float terrainHeightRange = 2f;

    [Tooltip("강바닥 깊이(m). 물 평면보다 넉넉히 깊어야 한다 — 다듬기가 폭 1칸짜리 물길의 바닥을 " +
             "들어올리기 때문에, 여유가 없으면 그런 구간에서 물이 끊겨 보인다.")]
    public float riverDepth = 0.85f;

    [Tooltip("물 표면 높이(m).")]
    public float waterLevel = -0.15f;

    // ── 섬 ──────────────────────────────────────────────────────

    [Header("섬")]
    [Tooltip("맵 가장자리에서 물에 잠기는 폭(칸).")]
    public float shoreWidth = 1.8f;

    [Tooltip("물을 맵 밖으로 얼마나 더 깔지 (맵 한 변의 배수).")]
    public float seaMargin = 1.5f;

    // ── 경계벽 ──────────────────────────────────────────────────

    [Header("경계벽")]
    [Tooltip("점프·넉백으로도 넘지 못할 높이(m).")]
    public float boundsHeight = 40f;

    [Tooltip("빠른 이동체가 한 프레임에 뚫지 않을 두께(m).")]
    public float boundsThickness = 2f;

    [Tooltip("바닥을 지형 아래까지 내려 물가 틈으로 새지 않게 하는 깊이(m).")]
    public float boundsSink = 5f;

    // ── 물 ──────────────────────────────────────────────────────

    [Header("물")]
    [Tooltip("수면 정점 간격(m). 물 셰이더가 정점을 흔들므로 파도 주기보다 촘촘해야 하고, " +
             "거품 띠는 정점 보간 폭보다 얇아질 수 없어 띠 목표 폭보다도 촘촘해야 한다.")]
    public float waterVertexSpacing = 1f;

    [Tooltip("이 수심(m)보다 얕으면 거품이 낀다(정점 컬러 빨강 채널 = 거품 세기). " +
             "0.35로 두면 작은 웅덩이는 수면 대부분이 거품 범위에 들어가 흰 원반처럼 렌더된다 — " +
             "물가에 좁은 띠만 남도록 얕게 잡는다.")]
    public float foamDepth = 0.10f;

    // ── 형상 (미터 고정) ────────────────────────────────────────

    [Header("형상 (미터 고정)")]
    // 형상이 완성되는 거리 — 타일 경계에서 안쪽으로 얼마 들어가야 제 높이가 되는가.
    // 짧아야 한다. 경사를 칸 하나에 걸쳐 눕히면 폭 1~2칸짜리 절벽·물길은 제 높이에 닿기도
    // 전에 반대쪽 경계를 만나 밋밋한 둔덕이 된다(0.6칸일 때 절벽 43%가 절반 높이도 못 됐다).
    // 절벽·강 타일은 어차피 건설 불가라 칸 안에서는 마음껏 깎아도 된다.
    [Tooltip("골 경사 거리(m) — 크면 완만하다.")]
    public float riverFalloffM = 0.6f;

    // 물가는 벼랑이 아니라 여울로 들어간다 — 물 앞에서 넓고 얕게 눕다가, 그 다음에 골이 파인다.
    // 한 단짜리 곡선으로는 이 둘을 함께 얻을 수 없다: 짧게 잡으면 물가가 벽이 되고,
    // 길게 잡으면 폭 3칸짜리 물길이 제 깊이에 닿기 전에 반대편을 만나 말라버린다.
    //
    // 여울 폭 + 골 경사 거리의 합은 강 반폭(1.5칸)에서 한참 모자라야 한다. 다듬기(거리장
    // 블러 + 높이맵 블러)가 좁은 골의 바닥을 들어올리기 때문 — 실측으로 유효 침투 거리가
    // 기대치의 약 2/3(1.35칸 → 0.9칸)로 줄었고, 합을 1.25칸으로 잡았을 때 수심이 16cm까지 말랐다.
    [Tooltip("여울 폭(m) — 물가 경사를 정한다.")]
    public float shelfWidthM = 0.9f;

    [Tooltip("여울 끝의 파임(m). 물 표면 높이보다 깊어야 물이 덮는다.")]
    public float shelfDepth = 0.3f;

    // 형상을 타일 경계에서 이만큼 안쪽으로 물려 시작한다. 경사면이 남의 땅이 아니라 제 타일을
    // 깎으며 생기게 하는 장치다 — 경계에 딱 맞춰 세우면 다듬는 과정에서 높이가 옆 지면으로
    // 흘러넘쳐, 절벽이 깎이는 게 아니라 땅이 차오르는 모양이 된다.
    [Tooltip("형상을 타일 경계에서 안쪽으로 물리는 거리(m).")]
    public float shapeInsetM = 0.3f;

    // ── 해상도 ──────────────────────────────────────────────────

    [Header("해상도 (미터 고정)")]
    // 픽셀·샘플 간격을 미터로 고정한다 — 셀이 커져도 곡선의 잘림·블러 반경(m)이 안 변한다.
    [Tooltip("거리장 픽셀 크기(m) — 셀 4m 기준 칸당 16.")]
    public float fieldPixelM = 0.25f;

    [Tooltip("높이맵 샘플 간격(m). 전체 해상도는 2ⁿ+1로 올림된다.")]
    public float heightSampleM = 0.25f;

    // ── 다듬기 ──────────────────────────────────────────────────

    [Header("다듬기")]
    // 계단 다듬기 — 타일은 네모라 거리장 등고선이 90°·45°로 꺾인다. 그 각을 뭉갠다.
    // 반경도 미터 고정이다. 칸의 절반으로 정의했더니 셀 4m에서 반경이 2m가 되어,
    // 해상도를 올려 담은 잔물결 워핑(±0.7m)을 블러가 도로 지워 해안이 직선이 됐다.
    // 계단은 워핑이 이미 흩뜨려 놓으므로 1m 블러로 모서리만 둥글리면 된다
    // (더 키우면 폭이 좁은 물길·여울의 속살까지 밀린다).
    [Tooltip("블러 반경(m). 거리장·높이맵 양쪽이 이 값을 쓴다.")]
    public float smoothRadiusM = 0.75f;

    [Tooltip("거리장 박스 블러 횟수 — 겹쳐서 가우시안에 가깝게.")]
    public int smoothPasses = 2;

    // 높이맵 단계의 2차 다듬기. 거리장을 아무리 뭉개도 "타일 밖은 0" 클램프가
    // 칸 격자에서 일어나 모서리가 되살아나므로, 격자를 떠난 뒤 한 번 더 편다.
    [Tooltip("높이맵 박스 블러 횟수.")]
    public int heightSmoothPasses = 2;

    // ── 불규칙성 ────────────────────────────────────────────────

    [Header("불규칙성 (도메인 워핑)")]
    // 경계를 흔드는 세기(m). 직각을 깨는 주역이지만 형상 반폭보다 작아야 한다.
    // 워프 파장(18칸)이 물길 폭(3칸)보다 훨씬 길어서 물길 양쪽이 같은 방향으로 밀리는데,
    // "바깥쪽 택하기"가 그 이동분을 양쪽에서 깎아낸다 — 세기가 반폭(1.5칸)을 넘던 3.2칸에서는
    // 그 깎임이 물길을 통째로 지워, 강이 중간중간 웅덩이로 끊겼다.
    [Tooltip("큰 옥타브 진폭(m).")] public float warpStrengthM = 2.2f;
    [Tooltip("큰 옥타브 파장(m).")] public float warpWavelengthM = 36f;

    // 잔물결 옥타브 — 긴 파장 하나로는 흔들림이 너무 완만해 안 흔든 것처럼 보인다.
    // 세 진폭의 합은 여전히 물길 반폭(1.5칸) 안이어야 한다.
    // 중간 옥타브는 물길 폭(≈12m)보다 짧은 파장이라 강에도 쓸 수 있다: 파장이 폭보다 짧으면
    // 양쪽 기슭이 제각각 흔들려, 긴 파장처럼 물길 전체가 한쪽으로 쏠리지 않는다.
    [Tooltip("중간 옥타브 진폭(m).")] public float warpMidStrengthM = 1.2f;
    [Tooltip("중간 옥타브 파장(m).")] public float warpMidWavelengthM = 10f;
    [Tooltip("잔물결 옥타브 진폭(m).")] public float warpFineStrengthM = 0.7f;
    [Tooltip("잔물결 옥타브 파장(m).")] public float warpFineWavelengthM = 4.5f;

    [Tooltip("지면 미세 굴곡 진폭(m) — 완벽한 평면을 없앤다.")]
    public float detailAmplitude = 0.14f;
    [Tooltip("지면 미세 굴곡 파장(m).")]
    public float detailWavelengthM = 22f;

    // ── 디테일(풀·꽃) ───────────────────────────────────────────

    [Header("디테일 — 풀·꽃")]
    // 우리 소유 변형을 심는다 — 머티리얼이 Art/Materials/Vegetation의 우리 사본이라
    // 서드파티 에셋을 건드리지 않고 색·바람을 만질 수 있다. 서드파티 원본을 직접 꽂으면
    // 그 사본 경로가 통째로 무의미해지므로 주의.
    [Tooltip("지면을 덮는 풀 프리팹.")]
    public GameObject[] grassSet = new GameObject[0];

    [Tooltip("드문드문 섞이는 꽃 프리팹 — 단조로운 초원을 깬다.")]
    public GameObject[] flowerSet = new GameObject[0];

    // 크기는 프리팹 원본에 곱해지는 배율이다. 1인칭 게임이라 무릎 높이여야 시야가 열린다 —
    // 에셋 데모 값(0.5~1)을 그대로 쓰면 눈높이를 덮어 앞이 안 보인다.
    [Tooltip("풀 크기 배율 범위.")] public Vector2 grassSize = new Vector2(0.56f, 1.0f);
    [Tooltip("꽃 크기 배율 범위. 잔디보다 조금 커야 한다 — 작으면 풀숲에 통째로 묻힌다.")]
    public Vector2 flowerSize = new Vector2(0.7f, 1.2f);

    [Tooltip("디테일 점 간격 목표(m) — 격자 크기는 맵 실측(m)에서 산정한다.")]
    public float detailPointM = 0.5f;

    // 디테일은 패치마다 따로 그려진다. 32면 해상도 1024에서 32×32=1024패치가 되고,
    // 프로토타입 8종을 곱하면 배치가 수천 개로 불어난다(실측: 잔디가 전체 배치의 79%).
    // 64로 키우면 패치가 256개로 줄어 그만큼 드로우콜이 준다 — 컬링 단위가 커지는 것이 대가다.
    [Tooltip("디테일 패치 단위. 키우면 드로우콜이 줄고 컬링 단위가 커진다.")]
    public int detailPatch = 64;

    // 실측(1920×1080): 120m면 배치 1258·삼각형 6.3M, 70m면 그 절반 남짓이다.
    // 잔디는 발밑에서만 눈에 띄므로 멀리까지 그릴 값어치가 적다.
    [Tooltip("이 거리(m) 밖에서는 디테일을 그리지 않는다.")]
    public float detailDistance = 70f;

    // 디테일은 지형 텍스처가 아니라 정점 색으로 물드므로 색을 여기서 준다 (에셋 데모와 같은 값)
    [Tooltip("디테일 정점 색 — 건강한 쪽.")]
    public Color healthyTint = new Color(0.263f, 0.976f, 0.165f);
    [Tooltip("디테일 정점 색 — 마른 쪽.")]
    public Color dryTint = new Color(0.804f, 0.737f, 0.102f);

    [Tooltip("물 표면 높이에서 이만큼 위(m)까지만 풀이 자란다 — 물속에 잠긴 풀이 없도록.")]
    public float grassWaterLineOffset = 0.08f;

    [Tooltip("여기까지만 풀이 자란다. 여울 경사(약 0.17)는 넘고 골 경사는 못 넘는 값.")]
    public float grassMaxSlope = 0.45f;

    // ── 절벽 ────────────────────────────────────────────────────
    //
    // 배치는 칸이 아니라 <b>수평 바운딩 원</b>이 정한다. 규칙은 하나 —
    // 바위의 반지름 ≤ 그 자리에서 비절벽 타일까지의 거리(클리어런스).
    // 그래서 크기가 벽 두께를 따라 저절로 변한다: 두꺼운 곳엔 큰 바위가 듬성듬성,
    // 얇은 능선엔 작은 바위가 촘촘히. 원은 회전에 불변이라 각도는 공짜다.

    [Header("절벽 — 프리팹")]
    [Tooltip("절벽 타일에 세우는 암벽 프리팹. 지형을 솟구치게 하는 대신 이것들이 벽이 된다.")]
    public GameObject[] cliffSet = new GameObject[0];

    [Header("절벽 — 채우기")]
    // 반지름은 <b>절대</b> 클리어런스를 넘지 않는다 — 절벽 영역 밖으로 안 나가는 것이
    // 이 알고리즘의 유일한 하드 제약이다. 그래서 <b>상한은 두지 않는다</b>: 클리어런스가
    // 이미 물리적 상한이라(이 맵 실측 최대 7.5m) 별도 캡은 한 번도 걸리지 않는 죽은 값이었다.
    // 크기를 줄이고 싶으면 cliffSizeNoise 로 줄이는 편이 의미가 분명하다.
    //
    // 아래는 <b>하한</b>이다 — 이보다 작으면 아예 놓지 않는다. 낮추면 벽 가장자리(클리어런스가
    // 0으로 떨어지는 띠)와 쌓기 꼭대기에 조약돌이 깔린다. 벽이 얇아 아무것도 못 서는 자리는
    // 그냥 비워 두는 편이 낫다.
    [Tooltip("바위 내접 반지름의 하한(m). 이보다 작으면 놓지 않는다.")]
    public float cliffMinRadius = 1.8f;

    // 한때 "클리어런스가 이미 상한이니 캡은 죽은 값"이라고 판단해 지웠는데, 틀렸다.
    // 그때 캡이 안 걸린 건 cliffMaxScale이 낮아 <b>대신 죄고 있었기</b> 때문이고,
    // 그 배율을 풀자마자 두꺼운 구간(클리어런스 7.5m)이 지름 15m 덩어리가 됐다 —
    // 벽이 아니라 산봉우리가 된다. 클리어런스는 "여기까지 들어갈 수 있다"일 뿐
    // "여기까지 커도 좋다"가 아니다. 그 둘은 다른 판단이라 값도 따로 있어야 한다.
    [Tooltip("바위 내접 반지름의 상한(m). 두꺼운 구간에서 한 덩어리가 산이 되는 것을 막는다.")]
    public float cliffMaxRadius = 4.5f;

    // ── 구 패킹 ──
    // 부피를 구로 채우고, 구마다 프리팹 하나를 <b>균등 배율</b>로 맞춘다.
    // 예전처럼 발자국을 눌러 자유 회전을 사는 거래가 아니다 — 원본 비율이 그대로 남는다.

    [Tooltip("채울 껍질의 깊이(m). 절벽 안쪽은 앞의 바위에 가려 보이지 않으므로 테두리 띠만 채운다. " +
             "키우면 벽이 두꺼워지고 바위 수가 그만큼 는다.")]
    public float cliffShellDepthM = 6f;

    [Tooltip("구끼리 얼마나 파고들게 둘지 — 중심 거리 하한 = (반지름 합) x 이 값. " +
             "1이면 딱 맞닿고, 낮을수록 깊이 물려 틈이 사라진다. 0.5~0.7 권장.")]
    [Range(0.2f, 1f)] public float cliffPackTightness = 0.6f;

    [Tooltip("크기 등급 수. 큰 등급부터 채워야 위계가 생긴다 — 한 번에 임의 크기로 뿌리면 " +
             "작은 것이 먼저 자리를 차지해 큰 것이 들어갈 곳이 없어진다.")]
    [Range(1, 8)] public int cliffSizeTiers = 5;


    // 1이면 서로 닿기만 하고, 작을수록 파고든다. 겹침은 <b>틈을 지우는 장치</b>다 —
    // 바위는 둥글어서 외접원끼리 닿아도 실제 메시 사이엔 V자 틈이 남는다.
    [Tooltip("중심 간 최소 거리 = (두 반지름의 합) × 이 값. 1 미만이면 서로 파고든다.")]
    [Range(0.2f, 1f)] public float cliffPackSpacing = 0.62f;

    // 침범 판정은 외접원(크게), 커버 판정은 그보다 작게 — 양쪽 다 보수적으로 잡는다.
    // 바위는 원이 아니라 그 원 안에 든 덩어리라, 원으로 덮였다고 실제로 막혔다는
    // 보장이 없기 때문이다. 낮출수록 구멍 메움이 더 촘촘해진다(= 바위가 는다).
    [Tooltip("구멍 검사에서 바위가 실제로 막는다고 보는 반지름 비율.")]
    [Range(0.3f, 1f)] public float cliffCoverFactor = 0.8f;


    // 배치 전에 <b>축별로 눌러 발자국을 정사각형에 맞춘다.</b> 이유는 순전히 기하다:
    // 외접원으로 재면 정사각 발자국도 √2배 손해라 두께 2r 벽에 폭 1.41r짜리만 들어가
    // 70%밖에 못 채운다. 정사각으로 만든 뒤 내접(D/2 = r)으로 재면 100%를 채운다.
    // 정사각이면 회전해도 내접원이 그대로라 자유 회전도 그대로 쓸 수 있다.
    //
    // 1이면 완전한 정육면체 — 가장 둥글고 가장 크게 들어가지만, 원본이 납작한 프리팹은
    // 그만큼 일그러진다. 0이면 원본 비율 그대로(대신 작아진다).
    [Tooltip("0 = 프리팹 원본 비율, 1 = 정육면체로 눌러 가장 둥글게.")]
    [Range(0f, 1f)] public float cliffRoundness = 0.85f;

    // 클리어런스만으로 크기를 정하면 <b>벽 두께가 일정한 구간에서 전부 같은 크기</b>가 된다
    // (실측: 가로폭 25~75%가 2.1~3.6m에 몰렸다). "지형이 크기를 정한다"는 두께가 변하는
    // 곳에서만 참이다. 클리어런스에 이 계수를 곱해 변주를 준다 — 줄이는 방향이라 침범은
    // 여전히 불가능하다. 큰 흐름(펄린) × 낱개 차이(해시)로 섞는다: 칸별 독립 난수는
    // "쌓인 지형"이 아니라 "흩뿌린 에셋"으로 읽힌다.
    [Tooltip("반지름에 곱하는 크기 변주 범위. (x=최소, y=최대)")]
    public Vector2 cliffSizeNoise = new Vector2(0.45f, 1.0f);

    [Header("절벽 — 높이")]

    // 크기를 클리어런스가 정하게 두면(= 상한을 없애면) 두꺼운 구간에서 반지름이 7m까지 가고,
    // 둥글게 눌러 놨으니 높이도 같이 올라가 <b>산봉우리</b>가 된다. 절벽은 벽이지 봉우리가
    // 아니다 — 여기서 잘라 스카이라인을 잡는다. 수평은 그대로 두고 세로만 자르므로,
    // 너무 낮게 잡으면 두꺼운 구간의 바위가 팬케이크가 된다.
    [Tooltip("바위 하나의 절대 높이 상한 — 칸 크기의 배수.")]
    public float cliffMaxHeightCells = 2.6f;

    [Tooltip("쌓아 올려 닿으려는 능선고 범위 — 칸 크기의 배수. (x=최저, y=최고)")]
    public Vector2 cliffHeightCells = new Vector2(2.0f, 4.5f);

    // 변주는 칸별 독립 난수가 아니라 위치 기반 연속 노이즈로 준다. 이웃끼리 아무 상관없는
    // 크기·높이는 "쌓인 지형"이 아니라 "흩뿌린 에셋"으로 읽힌다 — 실제 절벽은 이웃한 바위가
    // 서로 닮았고, 능선이 낮은 주파수로 오르내린다.
    [Tooltip("능선 노이즈 주파수. 0.11 ≈ 9칸(18m) 파장 — 몇 칸에 걸쳐 솟았다 가라앉는다.")]
    public float cliffRidgeFrequency = 0.11f;

    [Tooltip("지면 층을 땅에 묻는 깊이 — 칸 크기의 배수. 바닥이 지면에 딱 붙으면 떠 보인다.")]
    public float cliffBaseSinkCells = 0.3f;


    [Header("절벽 — 쌓기")]
    [Tooltip("지면 층 포함 최대 층수. 능선고에 닿으면 그 전에 멈춘다.")]
    [Range(1, 6)] public int cliffStackLayers = 4;


    [Tooltip("윗바위를 아랫바위 속으로 파묻는 정도(아래 바위 높이 기준). " +
             "0이면 꼭대기에 얹혀 이음매가 드러나고, 크면 층이 안 보인다.")]
    [Range(0f, 0.6f)] public float cliffStackSink = 0.22f;

    // 정확히 위로만 얹으면 탑이 된다 — 실제 너덜은 옆으로 기대고 흘러내린다.
    // 옆으로 민 만큼 접점이 낮아지므로(아래 바위는 둥글다) 높이도 자연히 들쭉날쭉해진다.
    [Tooltip("윗바위를 옆으로 미는 정도 — 두 반지름 합의 배수. 0이면 수직 탑만 쌓인다.")]
    [Range(0f, 1f)] public float cliffStackOffset = 0.55f;


    // 기울기는 <b>크기를 정하기 전에</b> 뽑고 그만큼 크기를 줄인다 — 눕히면 수평 점유가
    // 늘어나기 때문이다. 순서를 반대로 두면 기울이는 순간 절벽 밖으로 나가서, 예전에는
    // ±4°로 죄어 둘 수밖에 없었고 그래서 바위가 전부 위로만 섰다.
    // 크게 줄수록 눕는 바위가 늘지만 그만큼 작아진다(같은 자리에 들어가야 하므로).
    [Tooltip("바위를 기울이는 각도 폭(도). 이 값의 절반이 최대 기울기다.")]
    [Range(0f, 90f)] public float cliffTilt = 44f;

    [Tooltip("급한 절리 세트의 비율. 기울기를 두 종류로만 두는 것이 '지층' 느낌의 대부분이다 — " +
             "조각들이 저마다 다른 각도로 기울면 무질서지만 두 각도로만 기울면 층리로 읽힌다. " +
             "급한 쪽은 Cliff Tilt의 0.85배, 완만한 쪽은 0.25배를 쓴다.")]
    [Range(0f, 1f)] public float cliffDipSteepShare = 0.3f;

    [Tooltip("슬롯 장축 / 단축의 상한. 단축은 벽 두께(클리어런스)에 묶이고 장축만 접선 방향으로 " +
             "이만큼까지 늘어난다.\n" +
             "이것이 있어야 발자국을 정사각으로 누를 이유가 사라진다 — 얇은 벽에도 " +
             "조약돌 대신 벽을 따라 길게 누운 바위가 들어간다.")]
    [Range(1f, 4f)] public float cliffSlotAspect = 2.2f;

    [Tooltip("한 자리에 위로 쌓아 올릴 층수. 절벽 높이는 여기서 나온다 — " +
             "안쪽으로 줄을 놓아 봐야 보이지 않고, 절벽을 절벽으로 만드는 것은 수직면이다.\n" +
             "Cliff Height Cells가 정한 목표 높이에 닿으면 층수가 남아도 멈춘다.")]
    [Range(1, 10)] public int cliffCourses = 5;

    [Tooltip("층마다 안쪽으로 물러나는 정도 — 그 층 반두께의 배수(안식각). " +
             "0이면 수직 기둥, 크면 계단식으로 눕는다.")]
    [Range(0f, 1f)] public float cliffCourseSetback = 0.22f;

    [Tooltip("위 층을 아래 층 꼭대기보다 얼마나 내려 앉힐지(0~1). 이음매를 파묻는다.")]
    [Range(0f, 0.6f)] public float cliffCourseSink = 0.18f;

    [Tooltip("층마다 벽을 <b>따라</b> 어긋나는 정도 — 그 층 반두께의 배수. 0이면 모든 층이 " +
             "같은 수직선에 포개져 돌탑이 된다. 실제 절벽은 조각이 서로 어긋나 물려 있다.")]
    [Range(0f, 1.5f)] public float cliffCourseSway = 0.55f;




    [Tooltip("얇은 벽에서 길이로 벌충할 때의 장/단축 상한. 두께로 못 채우는 몫을 길이로 메워 " +
             "'벽을 따라 누운 납작한 바위'를 만든다. 프리팹 발자국 비를 넘는 몫은 눌러서 채운다.")]
    [Range(1f, 6f)] public float cliffThinAspectMax = 4f;

    [Tooltip("벽을 가로지르는 방향의 간격 계수 — Cliff Pack Spacing의 배수. " +
             "1보다 크면 줄끼리는 벽을 따라서보다 더 떨어진다.\n" +
             "벽을 따라서는 겹쳐야 관통이 안 생기고, 가로질러서는 떨어져야 줄이 하나로 " +
             "녹지 않고 두께가 생긴다. 방향마다 원하는 바가 달라서 계수를 나눴다.")]
    [Range(0.5f, 3f)] public float cliffRowSeparation = 1.5f;

    // ── 발치 애추(talus) ──
    // 구멍 메움 조약돌을 걷어낸 것과는 다른 이야기다. 그쪽은 큰 바위 사이의 그늘을 메우려던
    // 것이라 자갈 무더기가 됐지만, 발치의 조약돌은 메움이 아니라 <b>퇴적물</b>이다.
    // 바위와 지면이 선으로 만나는 것이 눈에 제일 먼저 걸리는데, 이 띠가 그 선을 깬다.
    [Tooltip("애추를 뿌리는 띠의 폭(m) — 클리어런스가 이 값 이하인 자리, 즉 벽의 바깥 테두리.")]
    public float cliffTalusBandM = 0.6f;

    [Tooltip("띠 안에서 애추를 시도하는 비율. 0이면 애추를 두지 않는다.")]
    [Range(0f, 1f)] public float cliffTalusDensity = 0.35f;

    [Tooltip("애추 반지름 — Cliff Min Radius의 배수 범위.")]
    public Vector2 cliffTalusRadius = new Vector2(0.25f, 0.5f);

    [Tooltip("애추끼리의 최소 간격 계수. 작을수록 촘촘하다.")]
    [Range(0.2f, 1f)] public float cliffTalusSpacing = 0.8f;

    [Tooltip("애추를 지면에 파묻는 깊이 — 반지름의 배수. 얹혀 있으면 굴러온 공처럼 보인다.")]
    [Range(0f, 0.8f)] public float cliffTalusSink = 0.35f;

    // ── 인스턴스별 명도 ──
    // 로우폴리는 면 방향 대비가 전부인데, 알베도가 전부 같으면 겹친 부분이 흰 덩어리로
    // 뭉갠다. 단계별 머티리얼을 구워 인스턴스마다 골라 물린다 — MaterialPropertyBlock은
    // 직렬화되지 않아 에디터가 만든 씬에서는 쓸 수 없고, SRP Batcher도 깨뜨린다.
    [Tooltip("명도 단계 수. 1이면 명도 변주를 쓰지 않는다. 단계마다 머티리얼 에셋 하나가 생긴다.")]
    [Range(1, 16)] public int cliffShadeSteps = 8;

    [Tooltip("인스턴스별 명도 흔들림(±비율).")]
    [Range(0f, 0.4f)] public float cliffShadeRange = 0.12f;

    [Tooltip("낮은 것을 어둡게 하는 정도 — 습기·이끼가 앉는 발치가 어두워야 높이가 읽힌다.")]
    [Range(0f, 0.6f)] public float cliffShadeDepthDarken = 0.18f;

    // ── 찾기 ────────────────────────────────────────────────────

    /// <summary>
    /// 설정 에셋을 읽는다. 없으면 기본값으로 만들어 준다 — 저장소를 새로 받은 사람이
    /// 메뉴를 눌렀을 때 "에셋이 없다"로 막히지 않게 하는 것이 목적이다.
    /// </summary>
    public static TerrainGenSettings LoadOrCreate()
    {
        var s = AssetDatabase.LoadAssetAtPath<TerrainGenSettings>(AssetPath);
        if (s != null) return s;

        // 같은 타입의 에셋을 다른 자리에 만들어 뒀을 수 있다 — 경로보다 타입을 먼저 믿는다
        var found = AssetDatabase.FindAssets("t:TerrainGenSettings");
        if (found.Length > 0)
            return AssetDatabase.LoadAssetAtPath<TerrainGenSettings>(AssetDatabase.GUIDToAssetPath(found[0]));

        s = CreateInstance<TerrainGenSettings>();
        var dir = System.IO.Path.GetDirectoryName(AssetPath).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(dir).Replace('\\', '/'),
                                       System.IO.Path.GetFileName(dir));
        AssetDatabase.CreateAsset(s, AssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[TerrainGenSettings] 설정 에셋이 없어 기본값으로 만들었습니다: {AssetPath}", s);
        return s;
    }
}
