using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Sim;
namespace CoreDawn.Combat
{
    /// <summary>
    /// 몬스터를 세우는 한 관문 — 심 엔티티(MonsterSystem.Spawn)를 먼저 만들고 뷰 프리팹을 붙인다.
    /// 밤 웨이브(WaveSpawnManager)·둥지 보스/방어자(MonsterNest)·세이브 복원이 전부 여기를 지난다.
    /// 건물의 PlacementBridge와 같은 자리: 심이 정본, 뷰는 따라온다.
    /// </summary>
    public static class MonsterSpawner
    {
        /// <param name="def">종류(팩 정의). null이면 소리 내고 스폰하지 않는다 — 기본 종류 폴백 없음.</param>
        public static MonsterView Spawn(EntityDef def, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (def == null)
            {
                Debug.LogError("[MonsterSpawner] 몬스터 정의가 null — 스폰 취소");
                return null;
            }

            var entity = SimRunner.Monsters.Spawn(def, position, rotation * Vector3.forward);
            return AttachView(entity, parent);
        }

        /// <summary>이미 심이 세운 몬스터 엔티티에 프리팹 뷰(카탈로그)를 붙인다 — 밤 웨이브(WaveSystem)가 심에서 먼저 세우는 경우.</summary>
        public static MonsterView AttachView(Entity entity, Transform parent)
        {
            if (entity == null) return null;
            Vector3 position = entity.Position;
            Quaternion rotation = entity.Facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(entity.Facing) : Quaternion.identity;

            // 뷰는 정의(view.type·model·collider…)에서 조립한다 — 프리팹은 없다(5a-4b)
            var go = MonsterAssembler.Build(entity.Def, position, rotation, parent);
            var view = go.GetComponent<MonsterView>();
            view.AttachEntity(entity);
            return view;
        }

        /// <summary>프리팹이 없을 때의 코드 조립 (캡슐) — 테스트 씬 안전.</summary>
        static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayerRecursively(child, layer);
        }
    }
}
