using System.Collections.Generic;
using EPOOutline;
using UnityEngine;

namespace CoreDawn.Visuals
{
    /// <summary>
    /// 아웃라인 풀 — 강조할 개체에 <see cref="Outlinable"/>을 붙이지 않고, 풀에 둔 Outlinable이 그 개체의 렌더러를 <b>대상으로 가리킨다</b>
    /// (EPO의 OutlineTarget은 렌더러 참조라 Outlinable이 대상 오브젝트에 있을 필요가 없다). 핑·근접 강조가 같은 풀을 쓴다.
    /// 빌리면 대상의 렌더러(Mesh·Skinned·Sprite)를 전부 등록하고, 돌려주면 비우고 끈다. 대상이 파괴되면 돌려주는 쪽이 정리한다.
    /// </summary>
    public static class OutlinePool
    {
        static readonly Stack<Outlinable> free = new Stack<Outlinable>();
        static readonly HashSet<Outlinable> rented = new HashSet<Outlinable>();
        static Transform root;

        public static Outlinable Rent(GameObject target, Color color, float dilateShift, float blurShift)
        {
            if (target == null) return null;
            var o = free.Count > 0 ? free.Pop() : Create();
            if (o == null) return null;
            Retarget(o, target);
            o.RenderStyle = RenderStyle.Single;
            o.DrawingMode = OutlinableDrawingMode.Normal;
            var p = o.OutlineParameters;
            p.Enabled = true; p.Color = color; p.DilateShift = dilateShift; p.BlurShift = blurShift;
            o.gameObject.name = "Outline(" + target.name + ")";
            o.enabled = true;
            rented.Add(o);
            return o;
        }

        /// <summary>대상 렌더러 목록을 다시 잡는다 — 대상이 모델을 나중에 입었을 때(마커 → Dress).</summary>
        public static void Retarget(Outlinable o, GameObject target)
        {
            ClearTargets(o);
            if (target == null) return;
            foreach (var r in target.GetComponentsInChildren<Renderer>(true))
            {
                if (r is MeshRenderer || r is SkinnedMeshRenderer || r is SpriteRenderer)
                    o.TryAddTarget(new OutlineTarget(r));
            }
        }

        public static void Return(Outlinable o)
        {
            if (o == null || !rented.Remove(o)) return;
            ClearTargets(o);
            o.enabled = false;
            o.gameObject.name = "Outline(free)";
            free.Push(o);
        }

        static void ClearTargets(Outlinable o)
        {
            for (int i = o.OutlineTargetsCount - 1; i >= 0; i--) o.RemoveTarget(o.OutlineTargets[i]);
        }

        static Outlinable Create()
        {
            if (root == null)
            {
                var go = new GameObject("[OutlinePool]");
                Object.DontDestroyOnLoad(go);
                root = go.transform;
            }
            var holder = new GameObject("Outline(free)");
            holder.transform.SetParent(root, false);
            var o = holder.AddComponent<Outlinable>();
            o.enabled = false;
            return o;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { free.Clear(); rented.Clear(); root = null; }
    }
}
