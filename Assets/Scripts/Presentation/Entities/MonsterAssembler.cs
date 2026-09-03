using UnityEngine;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 몬스터 뷰 조립기 — 정의의 view 블록만으로 세운다(5a-4b, 구 MonsterRigBuilder가 프리팹에 굽던 것을 런타임이 한다).
    /// 루트: Monster 레이어 + CapsuleCollider(view.collider, 세계 단위) + kinematic Rigidbody(플레이어 밀기 — 이동은 심) +
    /// MonsterVisualController(모델·Animator·연출 값) + MonsterView. 모델(카탈로그 model — Animator·아바타·머티리얼을 안에 든 모델 프리팹)은
    /// view.pose에 세운다. 소리 자리는 아직 없다(ViewSchema 표에 이름을 더하면 된다).
    /// </summary>
    public static class MonsterAssembler
    {
        public static GameObject Build(EntityDef def, Vector3 position, Quaternion rotation, Transform parent)
        {
            var view = ViewSchema.Entity(def);
            var go = new GameObject(BuildingAssembler.PascalKeyOf(def.Id));
            go.SetActive(false);   // 컴포넌트를 다 붙인 뒤 켠다
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            int layer = LayerMask.NameToLayer("Monster");
            if (layer >= 0) go.layer = layer;

            var col = go.AddComponent<CapsuleCollider>();
            var c = view.Collider;
            col.radius = c?.Radius ?? 0.3f;
            col.height = c?.Height ?? 1.2f;
            col.center = ViewDefs.Vec3(c?.Center, Vector3.zero);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;

            // 모델 — 팩 glb(view.model[0] {file, materials}: 스킨 + 클립)
            Transform body = null; Animation animation = null;
            var refs = view.Model;
            var chosen = refs != null && refs.Count > 0 ? refs[0] : null;
            var model = chosen != null ? Managers.PackAssets.ModelOf(chosen.File) : null;
            if (model != null)
            {
                var inst = Object.Instantiate(model, go.transform);
                inst.name = model.name;
                inst.SetActive(true);   // 팩 템플릿은 비활성으로 보관된다
                Managers.PackAssets.BindSlots(inst, chosen.Materials, def.Id);
                var (pos, rot, scale) = view.PoseFor(BeltShape.Straight);
                inst.transform.localPosition = pos; inst.transform.localRotation = rot; inst.transform.localScale = Vector3.one * scale;
                body = inst.transform;
                animation = inst.GetComponentInChildren<Animation>(true);
                if (animation == null) Debug.LogError($"[MonsterAssembler] {def.Id}: 모델에 클립(Animation)이 없습니다 — 연출 없이 섭니다.");
            }
            else
            {
                Debug.LogError($"[MonsterAssembler] {def.Id}: 모델(view.model)이 없습니다 — 내장 체커 상자로 섭니다.");
                body = Managers.MissingAssets.Box("Missing", new Vector3(1f, 1.8f, 1f), go.transform).transform;
            }

            var visual = go.AddComponent<MonsterVisualController>();
            visual.Wire(body, animation, MonsterVisualController.ClipMap.From(view.Anim),
                System.Enum.TryParse(view.DeathStyle, out MonsterVisualController.DeathStyle style) ? style : MonsterVisualController.DeathStyle.AnimationClip,
                view.SinkDepth);
            var mv = go.AddComponent<MonsterView>();
            mv.SetDeathBehavior(true, view.DeathDelay);
            go.SetActive(true);
            return go;
        }
    }
}
