using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 팩(materials.view.shader)이 이름으로 가리킬 수 있는 <b>내장 셰이더 목록</b> — <c>Resources/Builtin/Shaders.asset</c>.
    /// 팩 재질은 런타임에 코드로 만들어지므로 씬·재질 에셋 어디에도 이 셰이더들의 참조가 없다 → 빌드에서 스트리핑된다.
    /// 이 에셋이 Resources에서 셰이더를 참조하는 것이 빌드에 싣는 유일한 근거이고, 동시에 팩이 쓸 수 있는 셰이더의 허용 목록이다.
    /// 목록에 없는 이름은 오류(팩 재질은 MissingAssets 체커로).
    /// </summary>
    public sealed class BuiltinShaders : ScriptableObject
    {
        public const string ResourcePath = "Builtin/Shaders";

        [SerializeField] Shader[] shaders;

        static Dictionary<string, Shader> byName;

        public static Shader Of(string name)
        {
            if (byName == null)
            {
                var asset = Resources.Load<BuiltinShaders>(ResourcePath);
                if (asset == null) { Debug.LogError($"[BuiltinShaders] Resources/{ResourcePath}.asset이 없습니다 — 내장 셰이더 목록 에셋이 있어야 팩 재질을 만들 수 있고 빌드에 셰이더가 실립니다."); return null; }
                byName = new Dictionary<string, Shader>();
                foreach (var s in asset.shaders) if (s != null) byName[s.name] = s;
            }
            if (string.IsNullOrEmpty(name) || !byName.TryGetValue(name, out var shader))
            {
                Debug.LogError($"[BuiltinShaders] 셰이더 '{name}'는 내장 목록(Resources/{ResourcePath}: {string.Join(", ", byName.Keys)})에 없습니다 — 팩은 목록의 이름만 쓸 수 있습니다.");
                return null;
            }
            return shader;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { byName = null; }
    }
}
