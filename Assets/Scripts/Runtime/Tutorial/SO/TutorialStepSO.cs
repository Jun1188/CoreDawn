using UnityEngine;

/// <summary>
/// 튜토리얼 한 단계를 끝냈다고 볼 조건.
///
/// 조건을 SO 계층으로 쪼개지 않고 enum + 파라미터로 둔 이유: 종류가 십수 개로 고정적이라
/// 에셋 수를 두 배로 늘릴 값이 없다. 평가는 <see cref="TutorialConditions"/> 한 곳의
/// switch에 전부 모여 있으므로, 새 조건을 더할 때 볼 곳도 한 곳이다.
/// </summary>
public enum TutorialTrigger
{
    /// <summary>조건 없음 — 영영 끝나지 않는다. 임시 저작 중인 스텝을 막아 두는 용도.</summary>
    None = 0,

    /// <summary>이동과 시점 회전을 seconds만큼 누적했다.</summary>
    MoveAndLook = 1,

    /// <summary>itemType 분류의 아이템을 count개 이상 갖고 있다 (핫바 + 가방 합산).</summary>
    AcquireItemType = 2,

    /// <summary>item을 count개 이상 갖고 있다 (핫바 + 가방 합산).</summary>
    AcquireItem = 3,

    /// <summary>인벤토리 화면을 한 번 이상 열었다.</summary>
    OpenInventory = 4,

    /// <summary>손으로 자원을 count개 이상 캤다.</summary>
    MineResource = 5,

    /// <summary>무기를 장착한 상태다.</summary>
    EquipWeapon = 6,

    /// <summary>건설 모드에 들어갔다.</summary>
    EnterBuildMode = 7,

    /// <summary>건물을 count개 이상 설치했다.</summary>
    PlaceBuilding = 8,

    /// <summary>건물을 count개 이상 철거했다.</summary>
    DemolishBuilding = 9,

    /// <summary>코어 티어가 count 이상이다 (= 수리를 count번 끝냈다).</summary>
    CoreTierReached = 10,

    /// <summary>이 안내가 뜬 뒤로 밤을 count번 맞았다 (앞선 안내를 보는 사이 밤이 지나가도 안 놓친다).</summary>
    NightReached = 11,

    /// <summary>이 안내가 뜬 뒤로 밤을 count번 넘겨 아침을 맞았다.</summary>
    SurviveNight = 12,

    /// <summary>
    /// 이 안내가 뜬 뒤로 itemType 분류의 물건을 <b>손으로</b> count번 만들었다.
    /// 갖고 있는지(AcquireItemType)가 아니라 만들었는지를 묻는다 — 처음부터 무기를 쥐고
    /// 시작하는 씬에서도 제작을 반드시 한 번 거치게 하려면 이쪽이어야 한다.
    /// </summary>
    CraftItemType = 13,

    /// <summary>이 안내가 뜬 뒤로 벨트를 든 채 T를 눌러 모양을 count번 바꿨다.</summary>
    CycleBeltShape = 14,
}

/// <summary>
/// 안내 카드 한 장 = 에셋 한 개.
///
/// <see cref="GameDataSO.Id"/>가 세이브에 기록되는 키다 — 관례대로 "Tutorial:이름" 형식으로
/// 직접 지정할 것. <b>세이브가 존재하는 id는 바꾸면 안 된다</b>(그 스텝이 미완료로 되살아난다).
///
/// 본문을 <see cref="GameDataSO.description"/>이 아니라 별도 <see cref="body"/>에 두는 이유:
/// description은 툴팁 등 다른 표시 경로가 이미 쓰고 있어 성격이 다르다.
/// </summary>
[CreateAssetMenu(fileName = "TutorialStep", menuName = "Tutorial/Step")]
public class TutorialStepSO : GameDataSO
{
    [Header("순서")]
    [Tooltip("작을수록 먼저. 동률이면 Id 사전순으로 갈린다.")]
    public int order;

    [Header("표시")]
    [Tooltip("카드 왼쪽 위 영문 배지. 대문자 짧은 낱말 (GUIDE / BUILD / NIGHT …).")]
    public string tag = "GUIDE";

    [Tooltip("카드 본문. 한 문장 두 줄 안쪽으로. 줄바꿈은 그대로 표시된다.")]
    [TextArea(2, 5)] public string body;

    [Tooltip("본문 아래 키캡으로 그릴 문자열들. 예: W A S D / E / B. 비우면 줄 자체가 사라진다.")]
    public string[] keyHints;

    [Header("완료 조건")]
    public TutorialTrigger trigger = TutorialTrigger.None;

    [Tooltip("AcquireItem 전용 — 이 아이템을 가지면 완료.")]
    public ItemDataSO item;

    [Tooltip("AcquireItemType 전용 — 이 분류의 아이템을 가지면 완료.")]
    public ItemType itemType = ItemType.Salvage;

    [Tooltip("반복 횟수 / 코어 티어 / 일차. 누적형 조건은 '이 스텝이 뜬 뒤로 count번 더'를 뜻한다.")]
    public int count = 1;

    [Tooltip("MoveAndLook 전용 — 이동을 누적해야 하는 초. 시점 회전은 이 값의 1/4만 요구한다.")]
    public float seconds = 2f;
}
