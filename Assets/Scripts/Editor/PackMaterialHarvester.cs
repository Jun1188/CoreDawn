using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 5a-4c — Unity 머티리얼 에셋을 v1 <c>materials</c> 항목(편집 형식)으로 거둔다. 셰이더 이름 + 셰이더 기본값과 <b>다른</b> 값만(색·벡터·float) +
    /// 꽂힌 텍스처(guid — v2 내보내기가 팩 textures/로 복사) + 켜진 키워드 + 커스텀 렌더 큐 + 태그 오버라이드.
    /// 편집기에 재질 UI가 생길 때(3e-2)까지 재질을 팩에 넣는 길이다. 사용: <c>ToV1(material, "Material:TreeBark")</c> → v1 json 항목.
    /// </summary>
    public static class PackMaterialHarvester
    {
        public static JObject ToV1(Material m, string v1Id)
        {
            var shader = m.shader;
            var o = new JObject { ["id"] = v1Id, ["displayName"] = m.name, ["shader"] = shader.name };
            var textures = new JArray(); var colors = new JArray(); var vectors = new JArray(); var floats = new JArray();
            int n = shader.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string name = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Texture:
                    {
                        var t = m.GetTexture(name);
                        if (t == null) break;
                        string path = AssetDatabase.GetAssetPath(t);
                        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                        bool linear = imp != null && (!imp.sRGBTexture || imp.textureType == TextureImporterType.NormalMap);
                        textures.Add(new JObject { ["name"] = name, ["texture"] = t.name, ["textureGuid"] = AssetDatabase.AssetPathToGUID(path), ["linear"] = linear });
                        break;
                    }
                    case ShaderPropertyType.Color:
                    {
                        var c = m.GetColor(name); var d = (Color)shader.GetPropertyDefaultVectorValue(i);
                        if (c != d) colors.Add(new JObject { ["name"] = name, ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a });
                        break;
                    }
                    case ShaderPropertyType.Vector:
                    {
                        var v = m.GetVector(name); var d = shader.GetPropertyDefaultVectorValue(i);
                        if (v != d) vectors.Add(new JObject { ["name"] = name, ["r"] = v.x, ["g"] = v.y, ["b"] = v.z, ["a"] = v.w });
                        break;
                    }
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                    {
                        float f = m.GetFloat(name);
                        if (!Mathf.Approximately(f, shader.GetPropertyDefaultFloatValue(i))) floats.Add(new JObject { ["name"] = name, ["value"] = f });
                        break;
                    }
                    case ShaderPropertyType.Int:
                    {
                        int v = m.GetInteger(name);
                        if (v != shader.GetPropertyDefaultIntValue(i)) floats.Add(new JObject { ["name"] = name, ["value"] = v });
                        break;
                    }
                }
            }
            if (textures.Count > 0) o["textures"] = textures;
            if (colors.Count > 0) o["colors"] = colors;
            if (vectors.Count > 0) o["vectors"] = vectors;
            if (floats.Count > 0) o["floats"] = floats;

            var keywords = new JArray();
            foreach (var k in m.enabledKeywords) keywords.Add(k.name);
            if (keywords.Count > 0) o["keywords"] = keywords;

            o["renderQueue"] = m.renderQueue != shader.renderQueue ? m.renderQueue : -1;

            var tags = new JArray();
            var so = new SerializedObject(m);
            var map = so.FindProperty("stringTagMap");
            if (map != null && map.isArray)
                for (int i = 0; i < map.arraySize; i++)
                {
                    var e = map.GetArrayElementAtIndex(i);
                    var first = e.FindPropertyRelative("first"); var second = e.FindPropertyRelative("second");
                    if (first != null && second != null) tags.Add(new JObject { ["name"] = first.stringValue, ["value"] = second.stringValue });
                }
            if (tags.Count > 0) o["tags"] = tags;
            return o;
        }
    }
}
