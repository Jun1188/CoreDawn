using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ================================================================
//  BuildingData.cs
//  ScriptableObject 정의 + 포트 시스템 지원 타입
//
//  이 파일만 있으면 Inspector에서 건물/아이템/레시피를 전부 정의할 수 있다.
//  건물 배치, 적, 게임 로직은 이 파일과 무관하다.
// ================================================================

// ─── 기본 열거형 ────────────────────────────────────────────────

public enum Direction { North, East, South, West }

/// <summary>
/// 빌드 메뉴 분류 — BuildingDatabaseSO가 이 순서대로 그룹·정렬한다.
/// (예전 YAGNI로 제거했던 카테고리의 부활 — 이제 UI 정렬이라는 실소비자가 있다)
/// </summary>
public enum BuildingCategory
{
    Production,   // 생산 — 채굴기, 조립기
    Logistics,    // 물류 — 벨트, 분배기, 합류기
    Storage,      // 저장 — 보관소
    Defense,      // 방어 — 포탑 (밤 웨이브)
}

/// <summary>카테고리 표시명 — 빌드 메뉴·인스펙터가 공용.</summary>
public static class BuildingCategoryNames
{
    public static string Korean(BuildingCategory c) => c switch
    {
        BuildingCategory.Production => "생산",
        BuildingCategory.Logistics  => "물류",
        BuildingCategory.Storage    => "저장",
        BuildingCategory.Defense    => "방어",
        _ => c.ToString(),
    };
}

// ─── 방향 헬퍼 ─────────────────────────────────────────────────

public static class Dir
{
    static readonly Vector2Int[] _v = { new(0,1), new(1,0), new(0,-1), new(-1,0) };

    public static Vector2Int ToVec(Direction d) => _v[(int)d];
    public static Direction   Opposite(Direction d) => (Direction)(((int)d + 2) % 4);

    // 시계 방향으로 steps만큼 회전 (건물 회전 지원용)
    public static Direction RotateCW(Direction d, int steps = 1) =>
        (Direction)(((int)d + steps % 4 + 4) % 4);

    /// <summary>
    /// 풋프린트 내 셀 좌표의 시계 방향 90° 회전.
    /// 원점 기준 수학 회전 (x,y)→(y,−x)가 아니라, 회전 후에도 origin이
    /// 왼쪽 아래를 유지하도록 재앵커링한다: (x,y) → (y, w−1−x).
    /// w = 회전 전 풋프린트의 가로 크기.
    /// </summary>
    public static Vector2Int RotateCellCW(Vector2Int v, int footprintWidth)
        => new(v.y, footprintWidth - 1 - v.x);
}

// ─── 포트 정의 ──────────────────────────────────────────────────

/// <summary>
/// 건물의 입출력 연결점.
/// BuildingDataSO.ports[] 배열에 Inspector로 설정.
///
/// 예 — Miner (1×1, 오른쪽 출력):
///   ports[0]: IsInput=false, LocalOffset=(0,0), Direction=East
///
/// 예 — Belt (1×1, 왼쪽 입력→오른쪽 출력):
///   ports[0]: IsInput=true,  LocalOffset=(0,0), Direction=West
///   ports[1]: IsInput=false, LocalOffset=(0,0), Direction=East
///
/// 예 — Assembler 2×1 (왼쪽 두 입력, 오른쪽 출력):
///   ports[0]: IsInput=true,  LocalOffset=(0,0), Direction=West
///   ports[1]: IsInput=true,  LocalOffset=(0,1), Direction=West
///   ports[2]: IsInput=false, LocalOffset=(1,0), Direction=East
/// </summary>
[Serializable]
public class PortDefinition
{
    public Vector2Int LocalOffset;    // 건물 Origin 기준 상대 그리드 좌표
    public Direction  Direction;      // 포트가 향하는 방향 (아이템 흐름 방향)
    public bool       IsInput;        // true = 수신 포트,  false = 배출 포트

    // 아이템 필터링은 포트가 아니라 수신자의 ItemContainer.AcceptFilter가 담당한다
    // (예: 어셈블러 입력 = 현재 레시피의 재료만). 포트 필터를 두면 레시피와
    // 이중 장부가 되어 어긋날 수 있어 제거했다.
}

// ─── 행동 인터페이스 ────────────────────────────────────────────

public interface IBuildingBehavior
{
    /// <summary>
    /// FactorySim이 이 건물이 깨어 있는 틱에 호출.
    /// (MarkDirty로 등록됐거나 ScheduleWake 예약 시각이 됐을 때)
    /// </summary>
    void Tick(float dt);

    /// <summary>
    /// BuildingGraph.OnPlaced() 완료 후 1회 호출.
    /// 이 시점에서는 InputConnections / OutputConnections가 모두 확정되어 있다.
    /// 자원 조회, 레시피 결정 등 연결 기반 초기화에 사용.
    /// </summary>
    void OnAfterPlaced();
}

/// <summary>
/// 플레이어 상호작용(E)이 있는 행동만 추가로 구현하는 opt-in 인터페이스.
/// 심 계약(IBuildingBehavior·Tick)과 분리된 뷰 이벤트 — 시나리오 테스트는 이것을 모른다.
/// 조준 시 BuildingEntity(IInteractable)이 여기로 위임한다.
/// 새 상호작용 건물 추가 = 행동 클래스에 이 인터페이스 구현 (기존 코드 무수정).
/// </summary>
public interface IInteractiveBehavior
{
    /// <summary>조준 프롬프트. null/빈 문자열 = 지금은 상호작용 불가.</summary>
    string InteractPrompt { get; }

    void Interact(PlayerController player);
}

// ─── ScriptableObjects ──────────────────────────────────────────

/// <summary>
/// 건물 종류를 정의하는 ScriptableObject의 공통 베이스.
/// 씬에 배치된 건물 100개가 같은 SO 1개를 공유한다 (메모리 효율).
///
/// 건물 종류별 데이터·행동은 서브클래스가 정의한다:
///   MinerDataSO / BeltDataSO / AssemblerDataSO / StorageDataSO
/// 새 건물 종류 추가 = 서브클래스 SO + 행동 클래스 1쌍 (기존 코드 무수정).
/// </summary>
public abstract class BuildingDataSO : GameDataSO
{
    // 식별·표시(id/displayName/description/icon)는 GameDataSO가 담당

    [Header("분류 — 빌드 메뉴 그룹")]
    public BuildingCategory category = BuildingCategory.Production;

    [Header("프리팹")]
    public GameObject prefab;

    [Header("그리드 크기")]
    public Vector2Int size = Vector2Int.one; // 타일 단위 (1×1, 2×1 등)

    [Header("포트 — 건물 간 연결의 핵심")]
    public PortDefinition[] ports;

    [Header("버퍼 — 슬롯 기반 (플레이어 인벤토리와 같은 모델)")]
    [Tooltip("입력 버퍼 슬롯 수. 벨트/기계 1, 어셈블러 2(재료 종류만큼) 권장.")]
    public int inputSlots  = 1;
    [Tooltip("출력 버퍼 슬롯 수.")]
    public int outputSlots = 1;
    [Tooltip("버퍼 스택 상한. 0 = 아이템 기본값(64). 기계는 5~10 권장 — 과잉 보관 방지.")]
    public int bufferStackCap = 0;

    [Header("위협도 — 몬스터가 무엇부터 노리는가")]
    [Tooltip("플로우필드 목표의 시드 비용(월드 칸=10 단위 — 길찾기 격자가 칸을 쪼개도 FlowFieldManager가 배율을 맞춘다). 낮을수록 먼저 노린다.\n" +
             "코어 0 = 최종 목표 · 공격 타워 10 = 가는 길에 먼저 부순다 · 일반 건물 80 = 굳이 돌아가지 않는다.\n" +
             "어떤 칸에서 코어까지 100·타워까지 50이면 타워 경로가 50+10=60이라 타워를 먼저 친다.")]
    public int threatSeedCost = 80;

    [Header("진행도 게이트 — 코어 티어")]
    [Tooltip("이 값보다 GameManager.UnlockedTier가 낮으면 빌드 메뉴에서 숨겨진다.")]
    public int requiredCoreTier = 0;
    [Tooltip("코어처럼 씬에 직접 배치되는 단일 건물 — 빌드 메뉴에 항상 숨김.")]
    public bool hideFromBuildMenu = false;

    [Tooltip("같은 티어 안에서의 표시 순서 — 작을수록 먼저. 같으면 표시명순. " +
             "공정 단계를 그대로 적을 것 (채굴 0 · 제련 1 · 제작 2 · 조립 3 · 제조 4). " +
             "그러면 티어가 올라도 차례가 유지된다 — 채굴기 Mk.2는 조립기보다 앞에 온다. " +
             "티어만으로는 가를 수 없다: 채굴기·제련로·제작기가 전부 게이트 1이라 " +
             "이 값이 없으면 이름순(제련로·제작기·채굴기)으로 흩어진다.")]
    public int menuOrder = 0;

    [Header("건설 비용 — 배치 시 차감, 철거 시 전액 환급")]
    [Tooltip("레시피의 슬롯 타입을 그대로 쓴다 — 임포터의 아이템 해석 코드와 UI의 재료 표시를 재사용하기 위함.")]
    public RecipeDataSO.Slot[] buildCost;

    [Header("전투")]
    [Tooltip("밤 웨이브의 몬스터가 때릴 때 버티는 내구도. 프리팹의 Entity에 주입된다.")]
    public int maxHp = 200;

    [Header("파괴 규칙 — 무엇으로 없앨 수 있는가")]
    // 두 플래그를 나눠 둔 이유: 없애는 손이 둘이라 규칙도 둘이다.
    //   플레이어가 짓는 건물은 철거 O · 플레이어 공격 X — 제 공장을 오발로 부술 일은 없어야 한다.
    //   코어는 철거 X · 플레이어 공격 X — 실수로 밀면 그대로 게임오버다(몬스터에게는 최종 목표).
    //   둥지는 철거 X · 플레이어 공격 O — 몬스터의 것이라 철거 대상이 아니지만 부숴야 진행된다.
    //   나무 같은 지형물도 공격 O 로 "베어낼 수 있는가"를 켠다.
    // 그래서 기본값이 다르다: 철거는 켜고(플레이어가 짓는 것이 다수), 공격은 끈다(둥지·지형물만 켠다).
    // 예전에는 코어만 `Data is CoreDataSO` 로 하드코딩해 걸렀다 — 종류가 늘 때마다 조건이 붙어야 했다.
    [Tooltip("철거 모드로 부술 수 있는가. 끄면 조준 하이라이트도, 홀드 카운트도 걸리지 않는다.")]
    public bool isDemolishable = true;
    [Tooltip("플레이어의 공격이 통하는가. 기본은 꺼짐 — 총·근접 모두 이 건물을 지나친다. " +
             "둥지·나무처럼 플레이어가 부수는 것만 켠다 " +
             "(몬스터의 공격은 이 값과 무관하다 — 그쪽은 밤 웨이브의 목표 선정이 정한다).")]
    public bool isAttackable = false;

    /// <summary>이 건물의 런타임 행동 생성. Building 생성자에서 호출.</summary>
    public abstract IBuildingBehavior CreateBehavior(Building building);

    // ── 회전 지원 (배치 시 사용, 상호작용 로직과 무관)
    //    4방향 포트 배열을 최초 요청 시 1회 계산해 캐싱한다.
    //    (매 조회마다 새 배열을 할당하던 방식 + 재앵커링 누락 버그 대체)

    [NonSerialized] PortDefinition[][] _portsByRotation;

    public Vector2Int GetRotatedSize(int cwSteps) =>
        cwSteps % 2 == 0 ? size : new Vector2Int(size.y, size.x);

    public PortDefinition[] GetRotatedPorts(int cwSteps)
    {
        int steps = (cwSteps % 4 + 4) % 4;
        if (ports == null || steps == 0) return ports;
        _portsByRotation ??= BuildPortRotations();
        return _portsByRotation[steps];
    }

    PortDefinition[][] BuildPortRotations()
    {
        var table = new PortDefinition[4][];
        table[0] = ports;
        for (int s = 1; s < 4; s++)
        {
            int prevWidth = GetRotatedSize(s - 1).x; // 이번 스텝 회전 전의 가로 크기
            table[s] = table[s - 1].Select(p => new PortDefinition
            {
                IsInput     = p.IsInput,
                Direction   = Dir.RotateCW(p.Direction),
                LocalOffset = Dir.RotateCellCW(p.LocalOffset, prevWidth),
            }).ToArray();
        }
        return table;
    }

    protected override void OnValidate()
    {
        base.OnValidate();        // id 자동 부여 (GameDataSO)
        _portsByRotation = null;  // 인스펙터에서 포트 수정 시 캐시 무효화
        ValidatePorts();
    }

    /// <summary>
    /// 포트 배치 검사. 에디터 툴이 잡아주는 규칙이지만 인스펙터에서 직접 만질 때도 필요하다.
    ///
    /// 특히 "안쪽을 향한 포트"가 위험하다 — 2×1 조립기의 (0,0) East는 바로 옆 칸 (1,0)이
    /// 자기 자신이라 아무와도 연결되지 않는데, 런타임에는 조용히 stall만 난다.
    /// </summary>
    void ValidatePorts()
    {
        if (ports == null) return;

        var seen = new HashSet<(Vector2Int, Direction)>();
        foreach (var p in ports)
        {
            if (p == null) continue;

            if (p.LocalOffset.x < 0 || p.LocalOffset.y < 0 ||
                p.LocalOffset.x >= size.x || p.LocalOffset.y >= size.y)
                Debug.LogError($"[{name}] 포트 LocalOffset {p.LocalOffset} 가 풋프린트 {size} 밖입니다.", this);

            var n = p.LocalOffset + Dir.ToVec(p.Direction);
            if (n.x >= 0 && n.y >= 0 && n.x < size.x && n.y < size.y)
                Debug.LogError($"[{name}] 포트 {p.LocalOffset}/{p.Direction} 가 건물 안쪽을 향합니다 — " +
                               "이웃 칸이 자기 자신이라 연결되지 않습니다.", this);

            if (!seen.Add((p.LocalOffset, p.Direction)))
                Debug.LogError($"[{name}] 포트 {p.LocalOffset}/{p.Direction} 가 중복됩니다.", this);
        }
    }
}

