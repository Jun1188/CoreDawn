using UnityEditor;
using UnityEngine;
using CoreDawn.Worlds;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 에디터에서 런타임 지형을 미리 세워 본다(5a-4e) — 구 "Build World Terrain"(씬에 굽기)의 후계.
    /// 세운 것은 <see cref="HideFlags.DontSave"/>라 씬에 저장되지 않는다 — 정본은 맵이고 지형은 부팅 때 선다.
    /// </summary>
    public static class TerrainPreviewMenu
    {
        [MenuItem("Tools/CoreDawn/Terrain preview — build (runtime)")]
        public static void Build()
        {
            var world = Object.FindFirstObjectByType<World>();
            if (world == null) { Debug.LogError("[TerrainPreview] 씬에 World가 없습니다."); return; }
            Clear();
            var root = WorldTerrainBuilder.Build(world);
            if (root != null) root.hideFlags = HideFlags.DontSave;
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/CoreDawn/Terrain preview — clear")]
        public static void Clear()
        {
            var world = Object.FindFirstObjectByType<World>();
            if (world == null) return;
            var root = world.transform.Find(WorldTerrainBuilder.RootName);
            if (root != null) Object.DestroyImmediate(root.gameObject);
            SceneView.RepaintAll();
        }
    }
}
