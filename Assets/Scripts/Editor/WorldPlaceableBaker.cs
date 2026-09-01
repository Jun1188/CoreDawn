using UnityEditor;
using UnityEngine;
using CoreDawn.Worlds;
using CoreDawn.Data;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 맵의 배치물(광맥·둥지·나무·코어)을 <b>에디터에서</b> 씬에 세운다 — 맵을 임포트할 때 불린다.
    ///
    /// <b>왜 런타임이 아니라 여기인가.</b> 플레이를 눌러야만 숲과 둥지가 보이면 맵을 눈으로 고칠 수가
    /// 없다. 배치물이 씬에 있어야 아트도 레벨 디자인도 에디터에서 그대로 확인하고 손댈 수 있다.
    /// 런타임은 그것들을 만들지 않고 <b>심에 잇기만</b> 한다(<see cref="WorldPopulator.Populate"/>).
    ///
    /// <b>왜 임포트에 붙였나.</b> 배치의 정본은 맵 데이터다. 맵이 바뀌는 순간이 곧 씬이 따라가야 할
    /// 순간이라, 별도의 버튼을 두면 누르는 것을 잊은 채로 씬과 맵이 갈린다.
    ///
    /// 프리팹 연결을 남긴다(<see cref="PrefabUtility.InstantiatePrefab"/>) — 연결이 없으면 씬 파일에
    /// 메시 참조까지 통째로 복사되어 크게 불어나고, 프리팹을 고쳐도 씬의 것이 따라오지 않는다.
    /// </summary>
    public static class WorldPlaceableBaker
    {
        const string RootName = "Spawned";

        /// <summary>열려 있는 씬의 World 가 이 맵을 쓰고 있으면 배치물을 다시 세운다.</summary>
        public static void BakeIfOpen(MapDataSO map)
        {
            if (map == null) return;

            var world = Object.FindFirstObjectByType<World>();
            if (world == null || world.Map != map) return;   // 다른 맵을 보고 있으면 남의 씬이다

            Bake(world);
        }

        public static void Bake(World world)
        {
            if (world == null || world.Map == null) return;

            // 배치물의 정의(코어·둥지·나무·광맥 자원)는 팩에서 온다 — 에디트 모드엔 런타임 로더 등록이 없어 직접 꽂는다
            if (SimHost.Database == null) SimHost.DatabaseLoader = () => PackLoader.Load();
            var old = world.transform.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var root = new GameObject(RootName).transform;
            root.SetParent(world.transform, false);

            // 프리팹 연결을 남기는 생성기로 갈아끼운다. 반드시 되돌린다 — 런타임 경로가
            // 에디터 전용 API 를 물고 있으면 빌드에서 터진다.
            WorldPopulator.SpawnOverride = (prefab, pos, rot, parent) =>
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.SetPositionAndRotation(pos, rot);
                return go;
            };
            try { WorldPopulator.BakeIntoScene(world, root); }
            finally { WorldPopulator.SpawnOverride = null; }

            ClearStaticFlags(root);

            int count = root.GetComponentsInChildren<PlacedMapObject>(true).Length;
            EditorUtility.SetDirty(world.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            Debug.Log($"[WorldPlaceableBaker] '{world.Map.Id}' 배치물 {count}개를 씬에 세웠습니다 — " +
                      "런타임은 이것들을 다시 만들지 않고 심에 잇습니다. 씬을 저장해야 남습니다.", world);
        }

        /// <summary>
        /// 배치물은 <b>static 이 아니다.</b>
        ///
        /// 두 가지 이유가 겹친다:
        ///   ① 부서진다 — 나무는 베어지고 둥지·코어는 파괴된다. 오클루전에 구워 두면 사라진 뒤에도
        ///      그 자리가 계속 시야를 가린 것으로 취급되어, 뒤에 있는 것이 안 그려진다.
        ///   ② 맵이 바뀌면 통째로 다시 세운다 — 구워 둔 데이터는 그 순간 낡은 것이 된다.
        ///
        /// 나무 프리팹은 서드파티 원본에서 플래그를 물려받아 122(Occluder·Navigation·Occludee·
        /// OffMeshLink·ReflectionProbe)로 켜져 있었다. 프리팹 쪽도 껐지만, 여기서 한 번 더 지운다 —
        /// 아트가 새 프리팹을 넣을 때 이 규칙을 다시 지키게 하는 것보다 굽는 쪽이 보장하는 편이 낫다.
        /// </summary>
        static void ClearStaticFlags(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }
    }
}
