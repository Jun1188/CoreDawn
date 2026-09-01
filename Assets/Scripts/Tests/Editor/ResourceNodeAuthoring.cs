using System.Linq;
using UnityEditor;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Inventories;
using CoreDawn.Placement;
using CoreDawn.ResourceNodes;
using CoreDawn.Data;

namespace CoreDawn.Tests
{
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

        /// <summary>현재 HUD가 쓰는 임시 아이콘 시트 (인벤토리 슬롯 테두리, 64px 격자).</summary>
        public const string IconSheet = "Assets/Art/Textures/Inventory/testetstsets.png";

        /// <summary>진짜 낮/밤 아트 — 좌=해+공장, 우=달+몬스터. DayIcon/NightIcon으로 슬라이스해 둠(미연결).</summary>
        public const string DayNightArt = "Assets/Art/Textures/Day/noBackNightDay.PNG";

        /// <summary>장애물 콜라이더가 지면 아래로 내려가는 깊이 — GridManager의 구 검사에 확실히 걸리게.</summary>
        const float ObstacleDepth  = 0.6f;
        const float ObstacleHeight = 1.2f;

        public static ResourceDepositView Create(string name, string oreId, Vector2Int cell, Vector2Int size,
                                          float interval, int amount, int max, GridSystem grid)
        {
            var go = new GameObject(name);

            Vector3 center = grid.GetFootprintCenter(cell, size);
            center.y = SampleGroundTop(center);      // 오브젝트 원점 = 지면 표면
            go.transform.position = center;

            // 광맥은 한 칸짜리 심 엔티티 — 수치(재생·상한)는 팩의 광맥 정의가 갖는다. 뷰는 자원만 안다(size·interval·amount·max 인자는 호환용, 무시).
            size = Vector2Int.one;
            var node = go.AddComponent<ResourceDepositView>();
            var so = new SerializedObject(node);
            so.FindProperty("resourceId").stringValue = oreId;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 채굴기 설치는 B키 빌드 메뉴로 통일한다 (배치 규칙은 ResourceNodeRegistry.CanPlace 담당).
            // E 상호작용은 손 채굴 전용이다 — ResourceNode가 IHoldInteractable을 직접 구현하므로
            // 여기서 따로 컴포넌트를 붙일 것은 없고, 아래 Obstacle 콜라이더가 그 조준 판정을 겸한다.

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
}
