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
            var view = ViewSchema.Of(def);
            var go = new GameObject(BuildingAssembler.PascalKeyOf(def.Id));
            go.SetActive(false);   // 컴포넌트를 다 붙인 뒤 켠다
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            int layer = LayerMask.NameToLayer("Monster");
            if (layer >= 0) go.layer = layer;

            var col = go.AddComponent<CapsuleCollider>();
            var c = view.Object("collider");
            col.radius = (float?)c?["radius"] ?? 0.3f;
            col.height = (float?)c?["height"] ?? 1.2f;
            col.center = c?["center"] is Newtonsoft.Json.Linq.JArray ca && ca.Count >= 3 ? new Vector3((float)ca[0], (float)ca[1], (float)ca[2]) : Vector3.zero;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;

            Transform body = null; Animator animator = null;
            var model = ViewCatalogSO.ModelOf(def);
            if (model != null)
            {
                var inst = Object.Instantiate(model, go.transform);
                inst.name = model.name;
                var (pos, rot, scale) = view.Pose;
                inst.transform.localPosition = pos; inst.transform.localRotation = rot; inst.transform.localScale = Vector3.one * scale;
                body = inst.transform;
                animator = inst.GetComponentInChildren<Animator>(true);
            }
            else Debug.LogError($"[MonsterAssembler] {def.Id}: 모델(view.model)이 카탈로그에 없습니다 — 몸 없이 섭니다.");

            var visual = go.AddComponent<MonsterVisualController>();
            visual.Wire(body, animator,
                (int?)view.Raw["attackVariants"] ?? 1, (int?)view.Raw["hitVariants"] ?? 1,
                System.Enum.TryParse((string)view.Raw["deathStyle"], out MonsterVisualController.DeathStyle style) ? style : MonsterVisualController.DeathStyle.AnimationClip,
                view.Float("sinkDepth", 1.5f));
            var mv = go.AddComponent<MonsterView>();
            mv.SetDeathBehavior(true, view.Float("deathDelay", 2f));
            go.SetActive(true);
            return go;
        }
    }
}
