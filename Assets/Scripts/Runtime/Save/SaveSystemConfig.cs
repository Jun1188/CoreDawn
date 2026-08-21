using UnityEngine;

/// <summary>
/// 세이브 시스템 설정 — 씬에 배선하지 않고 Resources에서 읽는다
/// (ItemDatabase/RecipeDatabase와 같은 방식. 씬 파일을 건드리지 않기 위한 선택).
///
/// 에셋 위치: Assets/Resources/SaveSystemConfig.asset
/// 없으면 코드 기본값으로 동작하므로 에셋을 만들지 않아도 게임은 돌아간다.
/// </summary>
[CreateAssetMenu(fileName = "SaveSystemConfig", menuName = "Factory/Save System Config")]
public class SaveSystemConfig : ScriptableObject
{
    public const string ResourcePath = "SaveSystemConfig";

    [Header("슬롯")]
    [Tooltip("플레이어가 직접 저장하는 슬롯 수.")]
    public int manualSlotCount = 3;

    [Tooltip("자동 저장 슬롯 수. 오래된 것부터 순환하며 덮어쓴다.")]
    public int autoSlotCount = 2;

    [Header("파일")]
    [Tooltip("끄면 세이브가 압축되지 않은 .json으로 저장돼 텍스트 에디터로 열어볼 수 있다 (디버깅용).")]
    public bool compress = true;

    [Header("씬")]
    [Tooltip("타이틀 화면 씬. New Game/Load 진입점이자 '메뉴로 돌아가기'의 목적지.")]
    public string titleScenePath = "Assets/Scenes/Title.unity";

    [Tooltip("New Game이 시작할 씬. 팀 병합이 끝나면 통합된 씬으로 바꾸면 된다.")]
    public string newGameScenePath = "Assets/Scenes/World.unity";

    [Header("자동 저장")]
    [Tooltip("인게임 날짜가 넘어갈 때(아침) 자동 저장.")]
    public bool autoSaveOnDayStart = true;

    [Tooltip("밤이 시작될 때 자동 저장. 코어가 부서질 수 있는 유일한 시간이라, 게임오버 후 " +
             "'마지막 지점에서 다시'가 돌아갈 지점이 된다.")]
    public bool autoSaveOnNightStart = true;

    [Tooltip("메뉴로 돌아가거나 게임을 종료하기 직전에 자동 저장.")]
    public bool autoSaveOnQuit = true;

    // ── 슬롯 id 규칙 ──────────────────────────────────────────────

    public static string ManualSlotId(int index) => $"slot_{index + 1:00}";
    public static string AutoSlotId(int index) => $"auto_{index + 1:00}";
    public static bool IsAutoSlot(string slotId) => slotId != null && slotId.StartsWith("auto_");

    // ── 로딩 ─────────────────────────────────────────────────────

    static SaveSystemConfig _cached;

    /// <summary>Resources의 설정. 에셋이 없으면 기본값 인스턴스를 만들어 쓴다.</summary>
    public static SaveSystemConfig Load()
    {
        if (_cached != null) return _cached;

        _cached = Resources.Load<SaveSystemConfig>(ResourcePath);
        if (_cached == null)
        {
            _cached = CreateInstance<SaveSystemConfig>();
            _cached.name = "SaveSystemConfig (기본값)";
            Debug.LogWarning($"[Save] Resources/{ResourcePath}.asset 이 없어 기본 설정으로 동작합니다 — " +
                             "씬 경로를 바꾸려면 에셋을 만드세요 (Create ▸ Factory ▸ Save System Config).");
        }
        return _cached;
    }
}
