using UnityEngine;

/// <summary>
/// 모든 데이터 SO(아이템/건물/레시피 등)의 공통 베이스.
/// 정체성·표시 메타데이터만 갖는다 — 도메인 데이터는 서브클래스에.
///
/// 식별 원칙:
///   런타임 구분 = SO 참조 비교 (에셋 = 유일 객체, id 불필요)
///   세이브/조회 = Id — **수동 지정** ("분류:이름" 관례, 예: "Item:IronOre").
///     자동 생성은 폐기 — 한국어 displayName이 id로 굳는 사고를 막고,
///     id는 사람이 의도적으로 정하는 안정 키로 취급한다. 비어 있으면 에디터 경고.
///   표시        = displayName / description / icon
/// </summary>
public abstract class GameDataSO : ScriptableObject
{
    [Header("식별")]
    [Tooltip("세이브/조회용 안정 ID — 직접 지정 (관례: \"분류:이름\", 예: Item:IronOre).\n" +
             "영문·숫자 권장. 세이브 데이터가 존재하는 id는 절대 변경 금지. 프로젝트 전체에서 유일해야 함.")]
    [SerializeField] string id;

    [Header("표시")]
    public string displayName;
    [TextArea]
    public string description;
    public Sprite icon;

    public string Id => id;

    protected virtual void OnValidate()
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(id))
            Debug.LogWarning($"[GameDataSO] id가 비어 있습니다: {name} — \"분류:이름\" 형식으로 지정하세요 (예: Item:IronOre)", this);

        WarnOnDuplicateId();
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// id 중복 검사 — 프로젝트 전체를 훑으므로 임포트 도중에 돌면 안 된다.
    ///
    /// OnValidate는 에셋 임포트 파이프라인 안에서도 불린다. 거기서 FindAssets를 호출하면
    /// 같은 Refresh 배치에 들어 있는(아직 임포트가 안 끝난) 에셋까지 강제로 로드되면서
    /// "scheduled for reimport ... AssetDatabase returning two versions" 경고가 난다.
    /// GameDataSO는 건물 SO → 프리팹까지 참조 사슬이 이어져 프리팹이 딸려 들어온다.
    ///
    /// 그래서 임포트가 끝난 뒤(delayCall)로 미룬다. 경고 목적이라 한 프레임 늦어도 무방하다.
    /// </summary>
    static bool duplicateScanQueued;

    void WarnOnDuplicateId()
    {
        if (string.IsNullOrEmpty(id)) return;
        if (duplicateScanQueued) return;      // 임포트 배치에 SO가 100개여도 스캔은 한 번

        duplicateScanQueued = true;
        UnityEditor.EditorApplication.delayCall += ScanAllForDuplicateIds;
    }

    /// <summary>프로젝트의 모든 GameDataSO id를 한 번에 훑어 중복을 보고한다 (전체 1회 = O(n)).</summary>
    static void ScanAllForDuplicateIds()
    {
        // 아직 임포트/컴파일 중이면 다음 기회로 미룬다 — 예약 플래그는 유지한다
        if (UnityEditor.EditorApplication.isUpdating || UnityEditor.EditorApplication.isCompiling)
        {
            UnityEditor.EditorApplication.delayCall += ScanAllForDuplicateIds;
            return;
        }

        duplicateScanQueued = false;

        var seen = new System.Collections.Generic.Dictionary<string, GameDataSO>();

        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:GameDataSO"))
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var so   = UnityEditor.AssetDatabase.LoadAssetAtPath<GameDataSO>(path);
            if (so == null || string.IsNullOrEmpty(so.id)) continue;

            if (seen.TryGetValue(so.id, out var prev))
                Debug.LogError($"[GameDataSO] id 중복: '{so.id}' — {prev.name} 와 {so.name}", so);
            else
                seen[so.id] = so;
        }
    }
#endif
}
