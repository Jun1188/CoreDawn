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
        /// <param name="data">종류. null이면 MonsterDatabase의 기본 종류(구 세이브·종류 없는 웨이브).</param>
        public static MonsterView Spawn(MonsterDataSO data, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (data == null) data = MonsterDatabaseSO.LoadDefault()?.Default;
            var spec = data != null ? data.ToSpec() : MonsterSpec.Default;

            var entity = SimRunner.Monsters.Spawn(spec, position, rotation * Vector3.forward);

            GameObject go = data != null && data.prefab != null
                ? Object.Instantiate(data.prefab, position, rotation, parent)
                : CreateFallback(position, parent);
            go.SetActive(true);

            // 레이어는 물리·렌더링용이다(적대 판정은 편). 프리팹이 Default면 몬스터 레이어로 — 총알 스윕·타워 오라가 이 마스크를 쓴다
            int monsterLayer = LayerMask.NameToLayer("Monster");
            if (monsterLayer >= 0 && go.layer == 0) SetLayerRecursively(go.transform, monsterLayer);

            // 플레이어를 몸으로 밀기 위한 kinematic 몸체 — 이동은 심이 하므로 물리는 접촉만 맡는다
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var view = go.GetComponent<MonsterView>();
            if (view == null) view = go.AddComponent<MonsterView>();
            view.AttachEntity(entity);
            view.Configure(data);
            return view;
        }

        /// <summary>프리팹이 없을 때의 코드 조립 (캡슐) — 테스트 씬 안전.</summary>
        static GameObject CreateFallback(Vector3 position, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Monster(Spawned)";
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
            return go;
        }

        static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayerRecursively(child, layer);
        }
    }
}
