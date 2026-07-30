using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 광맥 오브젝트 하나를 씬에 세우는 공용 저작 코드 — ResourceNodeTest와 PlayLoopTest가 공유한다.
///
/// 광맥은 건물이 아니라 지형지물이다: HP도 피격 판정도 없고 몬스터의 공격 대상도 아니다.
/// 대신 길을 막는 장애물이라 Obstacle 레이어 콜라이더를 단다.
///
/// 콜라이더 두 장을 쓰는 이유 (GameObject 하나에 레이어는 하나뿐):
///   Visual   — Ground 레이어. PlacementSystem이 배치 높이를 Ground 레이캐스트로 재므로,
///              이게 있어야 채굴기가 광맥 윗면에 올라앉는다.
///   Obstacle — Obstacle 레이어. GridManager가 셀 중앙(y=0) 반지름 0.5 구로 장애물을 훑기 때문에
///              슬래브(y 0.5~0.7)만으로는 안 잡힌다 → 지면 아래까지 내려 덮는다.
///              PlayerInteractionManager의 interactableLayers에 Obstacle이 포함돼 있어
///              이 콜라이더가 "[E] 채굴기 설치" 프롬프트 판정도 겸한다.
/// </summary>
public static class ResourceNodeAuthoring
{
    /// <summary>광맥 슬래브 두께(m). 지면 위로 이만큼 솟고, 그 윗면이 채굴기의 바닥이 된다.</summary>
    public const float SlabThickness = 0.2f;

    /// <summary>채굴기 아이템 에셋 — 있으면 설치·회수에 쓰이도록 광맥에 꽂아 준다.</summary>
    public const string MinerItemPath = "Assets/Data/Item/MinerItem.asset";

    /// <summary>현재 HUD가 쓰는 임시 아이콘 시트 (인벤토리 슬롯 테두리, 64px 격자).</summary>
    public const string IconSheet = "Assets/Art/Textures/Inventory/testetstsets.png";

    /// <summary>진짜 낮/밤 아트 — 좌=해+공장, 우=달+몬스터. DayIcon/NightIcon으로 슬라이스해 둠(미연결).</summary>
    public const string DayNightArt = "Assets/Art/Textures/Day/noBackNightDay.PNG";

    /// <summary>장애물 콜라이더가 지면 아래로 내려가는 깊이 — GridManager의 구 검사에 확실히 걸리게.</summary>
    const float ObstacleDepth  = 0.6f;
    const float ObstacleHeight = 1.2f;

    public static ResourceNode Create(string name, ItemDataSO ore, Vector2Int cell, Vector2Int size,
                                      float interval, int amount, int max, GridSystem grid)
    {
        var go = new GameObject(name);

        Vector3 center = grid.GetFootprintCenter(cell, size);
        center.y = SampleGroundTop(center);      // 오브젝트 원점 = 지면 표면
        go.transform.position = center;

        var node = go.AddComponent<ResourceNode>();
        var so = new SerializedObject(node);
        so.FindProperty("resource").objectReferenceValue = ore;
        so.FindProperty("size").vector2IntValue          = size;
        so.FindProperty("productionInterval").floatValue = interval;
        so.FindProperty("amountPerCycle").intValue       = amount;
        so.FindProperty("maxStock").intValue             = max;
        so.FindProperty("initialStock").intValue         = 0;
        so.FindProperty("snapToGrid").boolValue          = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 설치는 B키 빌드 메뉴로 통일한다 — 광맥은 "채굴기만 지을 수 있는 자리"일 뿐,
        // 자체 상호작용(E)은 두지 않는다. 배치 규칙은 ResourceNodeRegistry.CanPlace가 담당.

        float w = size.x * grid.CellSize * 0.95f;
        float d = size.y * grid.CellSize * 0.95f;

        // ① 보이는 몸체 — 지면 위에 올라앉는 슬래브
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name  = "Visual";
        visual.layer = LayerMask.NameToLayer("Ground");
        visual.transform.SetParent(go.transform, false);
        visual.transform.localPosition = new Vector3(0f, SlabThickness * 0.5f, 0f);
        visual.transform.localScale    = new Vector3(w, SlabThickness, d);

        // ② 장애물 + 상호작용 판정 — 보이지 않고, 지면 아래까지 덮는다
        var blocker = new GameObject("Obstacle") { layer = LayerMask.NameToLayer("Obstacle") };
        blocker.transform.SetParent(go.transform, false);
        blocker.transform.localPosition = new Vector3(0f, (ObstacleHeight * 0.5f) - ObstacleDepth, 0f);
        var box = blocker.AddComponent<BoxCollider>();
        box.size = new Vector3(size.x * grid.CellSize, ObstacleHeight, size.y * grid.CellSize);

        return node;
    }

    /// <summary>
    /// 낮/밤 HUD 아이콘의 "깨져 보이는" 문제를 고친다.
    ///
    /// 임시 아이콘(인벤토리 슬롯 테두리 시트)을 쓰는 것 자체는 문제가 아니다. 문제는 밤 아이콘으로
    /// 꽂힌 testetstsets_0이 64px 격자 밖 조각(29x26)이라는 것 — 프레임 위쪽 일부만 걸려 있어
    /// 흰 체커 배경 + 테두리 한 조각이 100x100으로 늘어난다. 낮 아이콘(64x64)은 멀쩡하다.
    /// 그래서 밤 아이콘만 같은 시트의 제대로 잘린 칸으로 갈아끼운다.
    ///
    /// 덤으로 씬에 스프라이트를 미리 넣어 둔다 — SystemUIManager.UpdateHUD는 TimeManager가
    /// 아직 없으면 그냥 return하므로, 실행 순서에 따라 시작 프레임에 sprite가 null(=흰 사각형)로 남는다.
    /// </summary>
    public static void FixDayHud()
    {
        var ui = Object.FindFirstObjectByType<SystemUIManager>(FindObjectsInactive.Include);
        if (ui == null) return;

        // 진짜 낮/밤 아트를 쓴다. 임시 시트(testetstsets)는 64px 격자가 그림과 어긋나 있어서
        // 어느 칸을 골라도 체커 배경 + 테두리 조각이 나올 수 있다 (testetstsets_1만 우연히 맞았다).
        var art   = AssetDatabase.LoadAllAssetsAtPath(DayNightArt).OfType<Sprite>().ToArray();
        var day   = art.FirstOrDefault(s => s.name == "DayIcon");
        var night = art.FirstOrDefault(s => s.name == "NightIcon");

        if (day != null && night != null)
        {
            ui.morningSprite = day;
            ui.nightSprite   = night;
        }
        else
        {
            Debug.LogWarning($"[HUD] 낮/밤 아트를 못 찾아 임시 아이콘을 유지합니다: {DayNightArt}");
        }

        if (ui.timeIconImage != null)
        {
            ui.timeIconImage.preserveAspect = true;                 // 비율 왜곡 방지
            if (ui.timeIconImage.sprite == null)                    // 시작 프레임 흰 사각형 방지
                ui.timeIconImage.sprite = ui.morningSprite;
            EditorUtility.SetDirty(ui.timeIconImage);
        }

        if (ui.dayText != null && ui.dayText.text == "New Text")
        {
            ui.dayText.text = "Day 1";
            EditorUtility.SetDirty(ui.dayText);
        }

        EditorUtility.SetDirty(ui);
        Debug.Log($"[HUD] 낮 아이콘={(ui.morningSprite != null ? ui.morningSprite.name : "없음")}, " +
                  $"밤 아이콘={(ui.nightSprite != null ? ui.nightSprite.name : "없음")} (격자에 맞는 칸으로 보정)");
    }

    /// <summary>격자에 맞게 온전히 잘린 칸인가 — 조각이면 늘어나 깨져 보인다.</summary>
    static bool IsWholeCell(Sprite s) => s != null && s.rect.width == 64f && s.rect.height == 64f;

    /// <summary>지면(Ground 레이어) 표면의 y. 못 찾으면 0.</summary>
    public static float SampleGroundTop(Vector3 at)
    {
        int mask = LayerMask.GetMask("Ground");
        if (mask != 0 && Physics.Raycast(at + Vector3.up * 50f, Vector3.down,
                                         out RaycastHit hit, 100f, mask))
            return hit.point.y;

        Debug.LogWarning($"[광맥] {at}에서 지면을 못 찾았습니다 — 광맥을 y=0에 둡니다.");
        return 0f;
    }
}
