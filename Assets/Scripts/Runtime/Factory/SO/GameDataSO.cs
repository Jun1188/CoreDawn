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
    void WarnOnDuplicateId()
    {
        if (string.IsNullOrEmpty(id)) return;
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:GameDataSO"))
        {
            var path  = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var other = UnityEditor.AssetDatabase.LoadAssetAtPath<GameDataSO>(path);
            if (other != null && other != this && other.id == id)
            {
                Debug.LogError($"[GameDataSO] id 중복: '{id}' — {name} 와 {other.name}", this);
                return;
            }
        }
    }
#endif
}
