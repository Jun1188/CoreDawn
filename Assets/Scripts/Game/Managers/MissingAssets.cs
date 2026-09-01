using UnityEngine;

namespace CoreDawn.Managers
{
    /// <summary>
    /// "없음" 표시 자원 — 코드에 내장(팩 데이터가 아니다). 정의·팩이 무엇을 빠뜨렸는지 <b>눈에 보이게</b> 세운다:
    /// <list type="bullet">
    /// <item>모델 없음(정의에 view.model이 없거나 glb를 못 읽음) → <see cref="Box"/>: 체커 상자.</item>
    /// <item>재질 없음(id가 materials에 없음·셰이더를 못 찾음·슬롯에 재질이 안 적힘) → <see cref="Material"/>: 체커 재질.</item>
    /// <item>텍스처 없음(파일이 없거나 못 읽음) → <see cref="Texture"/>: 체커 텍스처(재질의 나머지 값은 데이터대로).</item>
    /// </list>
    /// 어느 경우든 소리는 부르는 쪽이 낸다(오류 로그) — 이것은 폴백이 아니라 표시다.
    /// 텍스처는 <c>Assets/Resources/Builtin/missing.png</c>(코드가 소유하는 고정 에셋 — 팩 내용이 아니라 Resources가 맞다).
    /// </summary>
    public static class MissingAssets
    {
        const string TexturePath = "Builtin/missing";

        static Texture2D texture;
        static Material material;

        public static Texture2D Texture
        {
            get
            {
                if (texture == null)
                {
                    texture = Resources.Load<Texture2D>(TexturePath);
                    if (texture == null) throw new System.InvalidOperationException($"내장 자원 Resources/{TexturePath}.png가 없습니다 — 빌드가 깨졌습니다.");
                }
                return texture;
            }
        }

        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    material = new Material(shader) { name = "Missing", mainTexture = Texture };
                    material.SetFloat("_Smoothness", 0f);
                }
                return material;
            }
        }

        /// <summary>체커 상자 — <paramref name="size"/>(로컬 단위), 밑면이 부모의 y=0. 콜라이더는 CreatePrimitive의 BoxCollider 그대로.</summary>
        public static GameObject Box(string name, Vector3 size, Transform parent)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localScale = size;
            box.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            box.GetComponent<Renderer>().sharedMaterial = Material;
            return box;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { texture = null; material = null; }
    }
}
