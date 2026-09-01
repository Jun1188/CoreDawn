using UnityEngine;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 건물 뷰 조립기 — 정의의 view 블록만으로 씬 오브젝트를 세운다(5a-4b, 구 GameDataImporter.EnsureContract의 후계).
    ///
    /// 규약: 건물 모델은 <b>칸 단위</b>로 저작된다 — 루트를 칸 크기(<c>cellSize</c>)로 키운다. 모델 인스턴스는 view.pose(루트 기준 자세)에,
    /// 없으면 원점에. 모델이 없으면 풋프린트 크기의 자리표시 큐브(밑면이 지면). 콜라이더는 메시 렌더러마다 MeshCollider(비볼록),
    /// 레이어는 전부 Entity. 컴포넌트는 view.type이 정한다 — Tower: TowerView + TowerVisualController(리그 노드는 이름으로 찾는다),
    /// Building: BuildingView. 유령(배치 미리보기)은 그림만 — 컴포넌트·콜라이더 없음.
    /// </summary>
    public static class BuildingAssembler
    {
        const float PlaceholderFill = 0.9f, PlaceholderHeight = 0.6f;   // 칸 단위 — 구 임포터의 큐브 플레이스홀더와 같은 크기

        /// <summary>실제 건물 — 컴포넌트·콜라이더까지. 심에 잇는 것(BuildingView.Building)은 호출부(PlacementBridge)가 한다.</summary>
        public static GameObject Build(EntityDef def, BeltShape shape, Vector3 position, Quaternion rotation, float cellSize)
        {
            var go = Assemble(def, shape, cellSize, ghost: false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            return go;
        }

        /// <summary>배치 미리보기용 유령 — 그림만(콜라이더·컴포넌트 없음). 호출부가 색을 입히고 움직인다.</summary>
        public static GameObject BuildGhost(EntityDef def, BeltShape shape, float cellSize)
        {
            var go = Assemble(def, shape, cellSize, ghost: true);
            go.SetActive(true);
            return go;
        }

        static GameObject Assemble(EntityDef def, BeltShape shape, float cellSize, bool ghost)
        {
            var view = ViewSchema.Of(def);
            var go = new GameObject(ghost ? "Preview " + PascalKeyOf(def.Id) : PascalKeyOf(def.Id));
            go.SetActive(false);   // 컴포넌트를 다 붙인 뒤 켠다 — Awake에서 서로를 GetComponent로 찾는다(TowerView ↔ TowerVisualController)
            go.transform.localScale = Vector3.one * cellSize;

            var model = ViewCatalogSO.ModelOf(def, shape);
            Transform body;
            if (model != null)
            {
                var inst = Object.Instantiate(model, go.transform);
                inst.name = model.name;
                var (pos, rot, scale) = view.PoseFor(shape);
                inst.transform.localPosition = pos;
                inst.transform.localRotation = rot;
                inst.transform.localScale = Vector3.one * scale;
                body = inst.transform;
            }
            else
            {
                // 모델이 없는 정의 — 자리표시 큐브(풋프린트 × 0.9칸, 높이 0.6칸, 밑면이 지면). 소리 내지 않는다: 아직 모델이 없는 건물이 정상 상태다.
                var size = def.Get<BuildingModuleDef>()?.Size ?? new Vec2i(1, 1);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Mesh";
                cube.transform.SetParent(go.transform, false);
                cube.transform.localScale = new Vector3(size.x * PlaceholderFill, PlaceholderHeight, size.y * PlaceholderFill);
                cube.transform.localPosition = new Vector3(0f, PlaceholderHeight * 0.5f, 0f);
                if (ghost) Object.Destroy(cube.GetComponent<Collider>());
                body = cube.transform;
            }

            int layer = LayerMask.NameToLayer("Entity");
            if (layer >= 0) SetLayerRecursively(go.transform, layer);

            if (ghost)
            {
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
                return go;
            }

            foreach (var mr in body.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.GetComponent<Collider>() == null && mr.GetComponent<MeshFilter>() is { sharedMesh: not null } mf)
                    mr.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))   // 블렌드셰이프 애니(벨트)도 맞아야 한다
                if (smr.GetComponent<Collider>() == null && smr.sharedMesh != null)
                    smr.gameObject.AddComponent<MeshCollider>().sharedMesh = smr.sharedMesh;

            switch (view.Type)
            {
                case "Tower":
                {
                    var visual = go.AddComponent<TowerVisualController>();
                    visual.WireRig(body, view);
                    go.AddComponent<TowerView>();
                    break;
                }
                case "Building":
                    go.AddComponent<BuildingView>();
                    break;
                default:
                    Debug.LogError($"[BuildingAssembler] {def.Id}: view.type '{view.Type}'은 건물 조립기가 모르는 종류입니다 — BuildingView로 세웁니다.");
                    go.AddComponent<BuildingView>();
                    break;
            }
            return go;
        }

        static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayerRecursively(c, layer);
        }

        /// <summary>팩 키("basic_turret") → 오브젝트 이름("BasicTurret") — 구 프리팹 이름과 같은 꼴.</summary>
        public static string PascalKeyOf(string id)
        {
            string key = id.Substring(id.LastIndexOf('/') + 1);
            var sb = new System.Text.StringBuilder(key.Length); bool up = true;
            foreach (char c in key) { if (c == '_') { up = true; continue; } sb.Append(up ? char.ToUpperInvariant(c) : c); up = false; }
            return sb.ToString();
        }
    }
}
