using UnityEditor;
using UnityEngine;
namespace CoreDawn.EditorTools
{
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
        // 경계선을 따라 벽 조각을 <b>한 줄</b> 세운다. 쌓지 않는다 — 조각 하나가 이미
        // 목표 벽 높이(약 10m)라서 쌓을 이유가 없다.
        //
        // 위와 뒤는 꾸미지 않는다. 게임 카메라가 눈높이(y≈2.2, 부감 없음)라 고원 위도
        // 벽 뒤도 보이지 않는다. 지형도 융기시키지 않는다.

        [Header("절벽 — 프리팹")]
        [Tooltip("경계에 세우는 벽 조각. 한 개가 벽 높이 전체를 맡으므로 키가 큰 것을 넣는다 " +
                 "(이 에셋은 Cliff_01~05).\n" +
                 "<b>앞면이 로컬 −Z</b>인 프리팹을 전제한다. 뒤집혀 보이면 프리팹을 180° 돌려 저장한다.")]
        public GameObject[] cliffWallSet = new GameObject[0];

        [Tooltip("발치에 흩는 납작한 판(이 에셋은 RockBuried_*). 벽과 잔디가 만나는 선을 깬다.\n" +
                 "이 판들은 이미 반쯤 묻힌 모습이라 따로 파묻을 필요가 없다.")]
        public GameObject[] cliffFootSet = new GameObject[0];

        [Header("절벽 — 벽")]

        [Tooltip("이웃 조각과 얼마나 겹칠지(0~1). 0이면 서로 닿기만 하고, 클수록 파고들어 " +
                 "사이가 메워진다. 조각 수는 여기에 반비례한다.")]
        [Range(0f, 0.85f)] public float cliffWallOverlap = 0.6f;

        [Tooltip("벽을 몇 줄 세울지. 한 줄이면 조각 사이로 하늘이 비쳐 <b>따로 선 돌</b>로 읽힌다. " +
                 "뒷줄이 앞줄의 틈을 막아 하나의 덩어리가 된다.")]
        [Range(1, 4)] public int cliffWallRows = 2;

        [Tooltip("줄 사이 간격(m) — 뒷줄이 안쪽으로 물러나는 거리.")]
        public float cliffWallRowStepM = 4.5f;

        [Tooltip("조각 배율의 범위. 키가 들쭉날쭉해야 윗선이 살아난다 — 눈높이에서 하늘과 " +
                 "만나는 선이 절벽의 인상을 거의 다 정한다.")]
        public Vector2 cliffWallScale = new Vector2(0.85f, 1.25f);

        [Tooltip("조각의 <b>앞면</b>을 경계에서 얼마나 밀지(m). 양수면 바깥(잔디 쪽), 음수면 안쪽.\n" +
                 "0이 기본이다 — 절벽 타일 경계가 곧 벽면이다. 그래도 귀퉁이 검사가 하드 제약이라 " +
                 "여기서 양수를 줘도 타일 밖으로는 못 나간다.")]
        public float cliffWallOverhangM = 0f;

        [Tooltip("조각을 땅에 얼마나 묻을지(m). 밑면이 드러나지 않을 만큼만.")]
        public float cliffWallSinkM = 0.4f;

        [Tooltip("조각을 세울 때 방향에 주는 흔들림(도). 크면 벽이 들쭉날쭉하고, " +
                 "너무 크면 앞면이 옆을 본다.")]
        [Range(0f, 25f)] public float cliffWallYawJitter = 7f;

        [Tooltip("경계가 이 각도(도) 이상 꺾이면 모서리로 본다. 모서리에서는 조각을 끊고 다시 시작한다 — " +
                 "넘어가면 조각이 모서리 안쪽에서 겹치고 바깥으로는 잔디에 걸친다.")]
        [Range(20f, 90f)] public float cliffWallCornerDeg = 45f;

        [Tooltip("모서리 판정에서 앞뒤로 얼마나 떨어진 점을 볼지(m). 작으면 잔주름도 모서리가 되고, " +
                 "크면 완만한 굽이를 놓친다.")]
        public float cliffWallCornerLookM = 3f;

        [Tooltip("자리에 안 들어가는 조각을 배율 하한의 몇 배까지 줄여서 넣어 볼지. " +
                 "그래도 안 되면 앞모서리만 지키고 뒤는 넘어가게 둔다.")]
        [Range(0.2f, 1f)] public float cliffWallMinShrink = 0.45f;

        [Tooltip("발자국 귀퉁이가 절벽 타일 밖으로 나가는 것을 이만큼(칸)까지 봐준다. 0이면 " +
                 "한 치도 못 나간다.\n" +
                 "절벽 밖은 건설·통행이 되는 칸이라 바위가 걸치면 보이는 것과 게임이 아는 것이 " +
                 "어긋난다. 안 맞는 자리는 좁은 조각으로, 그래도 안 되면 배율을 줄여 맞춘다.")]
        [Range(0f, 0.5f)] public float cliffWallSlackCells = 0.05f;

        [Header("절벽 — 발치")]

        [Tooltip("발치 판을 놓을 확률(0~1).")]
        [Range(0f, 1f)] public float cliffFootDensity = 0.35f;

        [Tooltip("경계 <b>바깥으로</b> 이만큼(칸)까지 흩는다. 안쪽으로는 이 값의 0.4배까지만.")]
        public float cliffFootBandCells = 0.9f;

        [Tooltip("발치 판 배율의 범위.")]
        public Vector2 cliffFootScale = new Vector2(0.6f, 1.3f);

        [Tooltip("발치 판끼리의 최소 거리 = (두 반지름의 합) x 이 값.")]
        [Range(0.2f, 5f)] public float cliffFootSpacing = 2.2f;

        [Tooltip("발치 판을 땅에 묻는 깊이(m). 이 판들은 이미 납작하므로 조금이면 된다.")]
        public float cliffFootSinkM = 0.08f;

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
}
