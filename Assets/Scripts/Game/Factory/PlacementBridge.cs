using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Data;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 배치/제거의 Unity 쪽 진입점 — 심 배치(FactorySystem)와 뷰(BuildingView) 생성을 묶는다.
    /// 심만 필요하면(테스트 등) FactorySystem.Place/Remove를 직접 호출하면 된다.
    /// </summary>
    public static class PlacementBridge
    {
        /// <param name="portOverride">인스턴스별 포트 형상 (벨트 커브 등). null이면 SO 포트 사용.</param>
        /// <param name="prefabOverride">인스턴스별 프리팹 (벨트 커브 메시 등). null이면 SO 프리팹 사용.</param>
        /// <param name="shape">벨트 모양. 세이브가 그대로 되살릴 수 있도록 심에도 기록된다.</param>
        public static Building Place(BuildingDataSO so, Vector2Int origin, Vector3 pos = default, int rotSteps = 0,
            PortDefinition[] portOverride = null, GameObject prefabOverride = null,
            BeltShape shape = BeltShape.Straight)
        {
            var boot = FactoryBootstrap.Instance;
            var b = boot.Factory.Place(so, origin, rotSteps, portOverride, shape);

            // 뷰 생성 — 벨트 커브는 메시의 뚫린 변을 포트에 맞추는 보정이 붙는다.
            // 프리뷰(PlacementSystem.PreviewYaw)와 반드시 같은 값이어야 한다: 여기만 고치면
            // 미리보기와 실제로 세워진 것이 다른 방향을 보게 된다.
            var prefab = prefabOverride != null ? prefabOverride : so.prefab;
            float yaw = so is BeltDataSO ? BeltDataSO.MeshYaw(shape, rotSteps) : rotSteps * 90f;
            GameObject go = prefab != null
                ? Object.Instantiate(prefab, pos, Quaternion.Euler(0, yaw, 0))
                : new GameObject(so.name);   // 프리팹 누락 시 빈 오브젝트

            // 프리팹에 미리 붙어 있으면(타워의 canAttack 설정 등) 그대로 쓰고, 없으면 부착
            var view = go.GetComponent<BuildingView>();
            if (view == null) view = go.AddComponent<BuildingView>();
            view.Building = b;   // 엔티티(HP·편)도 이때 붙는다
            boot.RegisterView(b, view);

            return b;
        }

        /// <summary>
        /// 씬에 이미 있는 뷰(코어 같은 싱글턴)를 심에 연결한다 — 새 프리팹을 Instantiate하지 않는다.
        /// 배치 자체(Grid/Graph 등록)는 Place와 동일하게 FactorySystem이 담당한다.
        /// </summary>
        public static Building PlaceExisting(BuildingDataSO so, Vector2Int origin, int rotSteps,
            BuildingView existingView, PortDefinition[] portOverride = null)
        {
            var boot = FactoryBootstrap.Instance;
            var b = boot.Factory.Place(so, origin, rotSteps, portOverride);

            existingView.Building = b;
            boot.RegisterView(b, existingView);

            return b;
        }

        /// <summary>
        /// 철거 — 심에서 제거한다. 버퍼 드롭·뷰 파괴는 Removed를 받는 FactoryBootstrap이 하므로
        /// 전투 파괴(심이 스스로 제거)와 철거가 같은 관문을 지난다.
        /// 벨트면 이 안에서 세그먼트 분할 + ItemDiscarded 통지(뷰 파괴 전이라 위치 조회 가능).
        /// </summary>
        public static void Remove(Building b)
        {
            if (b == null || b.IsRemoved) return;
            FactoryBootstrap.Instance.Factory.Remove(b);
        }

        /// <summary>컨테이너 내용물 전체를 위치 주변에 드롭. 스택 상한(64) 단위로 쪼갠다.</summary>
        public static void DropContainer(ItemContainer container, Vector3 position)
        {
            foreach (var (item, total) in container.Snapshot())
            {
                int remain = total;
                while (remain > 0)
                {
                    int n = Mathf.Min(remain, 64);
                    remain -= n;
                    DropAt(item, n, position);
                }
            }
        }

        /// <summary>파괴 지점 주변으로 흩뿌리는 드롭 (벨트 폐기 통지도 이 헬퍼 사용).</summary>
        public static void DropAt(ItemDataSO item, int amount, Vector3 position)
        {
            var scatter = Random.insideUnitCircle * 0.4f;
            var dir = new Vector3(scatter.x, 0.6f, scatter.y).normalized;   // 위로 톡 튀며 흩어짐
            DroppedItem.Spawn(item, amount, position + Vector3.up * 0.6f, dir);
        }
    }
}
