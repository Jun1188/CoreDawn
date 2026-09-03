using UnityEngine;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 건물 뷰 조립기 — 정의의 view 블록만으로 씬 오브젝트를 세운다(5a-4b, 구 GameDataImporter.EnsureContract의 후계).
    ///
    /// 규약: 건물 모델은 <b>칸 단위</b>로 저작된다 — 루트를 칸 크기(<c>cellSize</c>)로 키운다. 모델 인스턴스는 view.pose(루트 기준 자세)에,
    /// 없으면 원점에. 모델이 없으면 풋프린트 크기의 내장 체커 상자(MissingAssets, 밑면이 지면). 씬에 굳히지 않는다 — 에디터 미리보기는 WorldPreviewDrawer. 콜라이더는 메시 렌더러마다 MeshCollider(비볼록),
    /// 레이어는 전부 Entity. 컴포넌트는 view.type이 정한다 — Tower: TowerView + TowerVisualController(리그 노드는 이름으로 찾는다),
    /// Building: BuildingView. 유령(배치 미리보기)은 그림만 — 컴포넌트·콜라이더 없음.
    /// </summary>
    public static class BuildingAssembler
    {
        const float PlaceholderFill = 0.9f, PlaceholderHeight = 0.6f;   // 칸 단위 — 구 임포터의 큐브 플레이스홀더와 같은 크기

        /// <summary>실제 건물 — 컴포넌트·콜라이더까지. 심에 잇는 것(BuildingView.Building)은 호출부(PlacementBridge)가 한다.</summary>
        public static GameObject Build(EntityDef def, BeltShape shape, Vector3 position, Quaternion rotation, float cellSize, int variant = 0)
        {
            var go = Assemble(def, shape, cellSize, ghost: false, variant: variant);
            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            return go;
        }

        /// <summary>배치 미리보기용 유령 — 그림만(콜라이더·컴포넌트 없음). 호출부가 색을 입히고 움직인다.</summary>
        public static GameObject BuildGhost(EntityDef def, BeltShape shape, float cellSize, int variant = 0)
        {
            var go = Assemble(def, shape, cellSize, ghost: true, variant: variant);
            go.SetActive(true);
            return go;
        }

        static GameObject Assemble(EntityDef def, BeltShape shape, float cellSize, bool ghost, int variant)
        {
            var view = ViewSchema.Entity(def);
            var go = new GameObject(ghost ? "Preview " + PascalKeyOf(def.Id) : PascalKeyOf(def.Id));
            go.SetActive(false);   // 컴포넌트를 다 붙인 뒤 켠다 — Awake에서 서로를 GetComponent로 찾는다(TowerView ↔ TowerVisualController)
            go.transform.localScale = Vector3.one * cellSize;
            var body = AttachBody(go, def, view, shape, variant, ghost);
            if (!ghost) AddViewComponents(go, def, view, body);
            return go;
        }

        /// <summary>모델 인스턴스(팩이면 슬롯 재질까지) 또는 자리표시 큐브를 루트 아래에 붙이고, 레이어·콜라이더를 맞춘다. 반환 = 몸체 트랜스폼.</summary>
        static Transform AttachBody(GameObject go, EntityDef def, EntityViewDef view, BeltShape shape, int variant, bool ghost)
        {
            var model = ResolveModel(def, view, shape, variant, out var chosen);
            Transform body;
            if (model != null)
            {
                var inst = Object.Instantiate(model, go.transform);
                inst.name = model.name;
                inst.SetActive(true);   // 팩 템플릿(PackAssets)은 비활성으로 보관된다
                if (chosen != null) Managers.PackAssets.BindSlots(inst, chosen.Materials, def.Id);
                var (pos, rot, scale) = view.PoseFor(shape);
                inst.transform.localPosition = pos;
                inst.transform.localRotation = rot;
                inst.transform.localScale = Vector3.one * scale;
                body = inst.transform;
                PlayLoop(inst, view, shape, def.Id);
            }
            else
            {
                // 모델 없음 — 내장 체커 상자(풋프린트 × 0.9칸, 높이 0.6칸, 밑면이 지면). 정의에 모델이 없는 건물은 아직 정상 상태라 여기서는 소리 내지 않고,
                // 팩 파일을 못 읽은 경우는 PackAssets가 이미 오류를 냈다.
                var size = def.Get<BuildingModuleDef>()?.Size ?? new Vec2i(1, 1);
                var cube = Managers.MissingAssets.Box("Missing", new Vector3(size.x * PlaceholderFill, PlaceholderHeight, size.y * PlaceholderFill), go.transform);
                if (ghost) Object.Destroy(cube.GetComponent<Collider>());
                body = cube.transform;
            }

            // 레이어 — 기본 Entity, view.layer로 바꿀 수 있다(광맥 몸은 Ground, 둥지는 Nest). 이름이 없는 레이어는 오류
            string layerName = view.Layer ?? "Entity";
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) Debug.LogError($"[BuildingAssembler] {def.Id}: view.layer '{layerName}'이 프로젝트 레이어에 없습니다.");
            else SetLayerRecursively(go.transform, layer);

            if (ghost)
            {
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
                return body;
            }

            // 렌더러마다 MeshCollider — view.meshCollider:false면 안 붙인다(광맥·둥지처럼 데이터 콜라이더만 쓰는 정의)
            if (view.MeshCollider)
            {
                foreach (var mr in body.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.GetComponent<Collider>() == null && mr.GetComponent<MeshFilter>() is { sharedMesh: not null } mf)
                        mr.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))   // 블렌드셰이프 애니(벨트)도 맞아야 한다
                    if (smr.GetComponent<Collider>() == null && smr.sharedMesh != null)
                        smr.gameObject.AddComponent<MeshCollider>().sharedMesh = smr.sharedMesh;
            }
            AddDataColliders(go, def, view);
            return body;
        }

        /// <summary>
        /// 항상 도는 클립(벨트 모프 애니) — view.loop(모양별 loopCurveL/loopCurveR)에 적힌 glb 애니메이션 이름을 반복 재생한다.
        /// 상태에 따라 바뀌는 애니(타워·몬스터)는 각 뷰의 재생기 몫. 이름이 없으면 오류.
        /// </summary>
        static void PlayLoop(GameObject inst, EntityViewDef view, BeltShape shape, string owner)
        {
            string key = shape == BeltShape.CurveL ? "loopCurveL" : shape == BeltShape.CurveR ? "loopCurveR" : "loop";
            string clipName = view.LoopFor(shape);
            if (string.IsNullOrEmpty(clipName)) return;
            var anim = inst.GetComponentInChildren<Animation>(true);
            var clip = anim != null ? anim.GetClip(clipName) : null;
            if (clip == null) { Debug.LogError($"[BuildingAssembler] {owner}: view.{key} '{clipName}' 클립이 모델에 없습니다.", inst); return; }
            clip.wrapMode = WrapMode.Loop;
            anim.wrapMode = WrapMode.Loop;
            anim.Play(clipName);
        }

        /// <summary>
        /// view.colliders — 데이터로 적은 콜라이더(칸 단위, 루트 기준): <c>[{name, layer, box:{center,size}} | {name, layer, capsule:{center,radius,height}}]</c>.
        /// 각각 자식 오브젝트(name)로 붙인다 — 광맥의 Obstacle 상자(상호작용·막힘), 둥지의 NestCore 캡슐(파괴 뒤에도 남는 핵).
        /// </summary>
        static void AddDataColliders(GameObject go, EntityDef def, EntityViewDef view)
        {
            if (view.Colliders == null) return;
            int i = 0;
            foreach (var c in view.Colliders)
            {
                if (c == null) continue;
                var child = new GameObject(c.Name ?? $"Collider_{i}");
                child.transform.SetParent(go.transform, false);
                string layerName = c.Layer;
                int layer = string.IsNullOrEmpty(layerName) ? go.layer : LayerMask.NameToLayer(layerName);
                if (layer < 0) { Debug.LogError($"[BuildingAssembler] {def.Id}: colliders[{i}].layer '{layerName}'이 프로젝트 레이어에 없습니다."); layer = go.layer; }
                child.layer = layer;
                if (c.Box != null)
                {
                    var bc = child.AddComponent<BoxCollider>();
                    bc.center = ViewDefs.Vec3(c.Box.Center, Vector3.zero); bc.size = ViewDefs.Vec3(c.Box.Size, Vector3.one);
                }
                else if (c.Capsule != null)
                {
                    var cc = child.AddComponent<CapsuleCollider>();
                    cc.center = ViewDefs.Vec3(c.Capsule.Center, Vector3.zero); cc.radius = c.Capsule.Radius; cc.height = c.Capsule.Height;
                }
                else Debug.LogError($"[BuildingAssembler] {def.Id}: colliders[{i}]에 box나 capsule이 없습니다.");
                i++;
            }
        }

        /// <summary>view.type별 뷰 컴포넌트 — 없을 때만 붙인다(마커에 이미 있을 수 있다). 타워 리그 배선은 몸체가 있을 때.</summary>
        static void AddViewComponents(GameObject go, EntityDef def, EntityViewDef view, Transform body)
        {
            switch (view.Type)
            {
                case "Tower":
                {
                    var visual = go.GetComponent<TowerVisualController>();
                    if (visual == null) visual = go.AddComponent<TowerVisualController>();
                    if (body != null) visual.WireRig(body, view);
                    if (go.GetComponent<TowerView>() == null) go.AddComponent<TowerView>();
                    break;
                }
                case "Building":
                    if (go.GetComponent<BuildingView>() == null) go.AddComponent<BuildingView>();
                    break;
                case "Deposit":
                    if (go.GetComponent<CoreDawn.ResourceNodes.ResourceDepositView>() == null) go.AddComponent<CoreDawn.ResourceNodes.ResourceDepositView>();
                    break;
                case "Nest":
                {
                    var nest = go.GetComponent<NestView>();
                    if (nest == null) nest = go.AddComponent<NestView>();
                    var core = go.transform.Find("NestCore");   // 파괴 뒤에도 남는 핵 — view.colliders의 NestCore 캡슐
                    if (core != null) nest.indestructibleVisuals = new[] { core.gameObject };
                    if (body != null) nest.destructibleVisuals = new[] { body.gameObject };
                    break;
                }
                default:
                    Debug.LogError($"[BuildingAssembler] {def.Id}: view.type '{view.Type}'은 건물 조립기가 모르는 종류입니다 — BuildingView로 세웁니다.");
                    if (go.GetComponent<BuildingView>() == null) go.AddComponent<BuildingView>();
                    break;
            }
        }

        /// <summary>
        /// 모델 출처 — view.model(배열, [0]이 기본·나머지는 변형)의 팩 glb(PackAssets). 없으면 null(호출부가 체커 상자). 옛 guid 문자열은 오류.
        /// 변형은 <paramref name="variant"/> % 개수로 고른다(나무는 칸에서 결정적으로 뽑는다).
        /// </summary>
        public static GameObject ResolveModel(EntityDef def, EntityViewDef view, BeltShape shape, int variant, out ModelRef chosen)
        {
            var list = view.ModelsFor(shape);
            chosen = list != null && list.Count > 0 ? list[((variant % list.Count) + list.Count) % list.Count] : null;
            if (chosen == null) return null;
            return Managers.PackAssets.ModelOf(chosen.File);
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
