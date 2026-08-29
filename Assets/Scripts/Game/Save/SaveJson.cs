using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CoreDawn.Save
{
    /// <summary>
    /// 세이브 전용 JSON 설정 한 곳.
    ///
    /// Unity의 Vector3/Quaternion을 그대로 직렬화하면 normalized·magnitude 같은 파생 프로퍼티까지
    /// 딸려 나와 파일이 부풀고 역직렬화가 깨진다. 그래서 좌표 타입은 전부 짧은 배열로 쓰는
    /// 변환기를 붙였다 — 사람이 읽기도 쉽다: "pos": [12.5, 0.0, -3.25]
    /// </summary>
    public static class SaveJson
    {
        public static readonly JsonSerializerSettings Settings = Build(indented: true);
        static readonly JsonSerializer Serializer = JsonSerializer.Create(Build(indented: false));

        static JsonSerializerSettings Build(bool indented) => new()
        {
            Formatting = indented ? Formatting.Indented : Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            // 순환 참조가 생기면 조용히 잘라내지 말고 터뜨린다 — DTO에 Unity 객체가 새어든 신호다
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            Converters =
            {
                new Vector3Converter(),
                new Vector2IntConverter(),
                new QuaternionConverter(),
            },
        };

        public static string Serialize(object o) => JsonConvert.SerializeObject(o, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        /// <summary>모듈이 돌려준 객체를 JToken으로. 변환기 설정이 동일하게 적용된다.</summary>
        public static JToken ToToken(object o) => o == null ? null : JToken.FromObject(o, Serializer);

        /// <summary>모듈이 받은 JToken을 DTO로. 토큰이 없거나 형태가 다르면 default.</summary>
        public static T FromToken<T>(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return default;
            try { return t.ToObject<T>(Serializer); }
            catch (Exception e)
            {
                Debug.LogError($"[Save] '{typeof(T).Name}' 역직렬화 실패 — 이 부분은 건너뜁니다: {e.Message}");
                return default;
            }
        }

        // ── 좌표 변환기 ───────────────────────────────────────────────

        class Vector3Converter : JsonConverter<Vector3>
        {
            public override void WriteJson(JsonWriter w, Vector3 v, JsonSerializer s)
            {
                w.WriteStartArray();
                w.WriteValue(v.x); w.WriteValue(v.y); w.WriteValue(v.z);
                w.WriteEndArray();
            }

            public override Vector3 ReadJson(JsonReader r, Type t, Vector3 existing, bool hasExisting, JsonSerializer s)
            {
                var a = JArray.Load(r);
                return a.Count < 3 ? Vector3.zero
                    : new Vector3((float)a[0], (float)a[1], (float)a[2]);
            }
        }

        class Vector2IntConverter : JsonConverter<Vector2Int>
        {
            public override void WriteJson(JsonWriter w, Vector2Int v, JsonSerializer s)
            {
                w.WriteStartArray();
                w.WriteValue(v.x); w.WriteValue(v.y);
                w.WriteEndArray();
            }

            public override Vector2Int ReadJson(JsonReader r, Type t, Vector2Int existing, bool hasExisting, JsonSerializer s)
            {
                var a = JArray.Load(r);
                return a.Count < 2 ? Vector2Int.zero
                    : new Vector2Int((int)a[0], (int)a[1]);
            }
        }

        class QuaternionConverter : JsonConverter<Quaternion>
        {
            public override void WriteJson(JsonWriter w, Quaternion q, JsonSerializer s)
            {
                w.WriteStartArray();
                w.WriteValue(q.x); w.WriteValue(q.y); w.WriteValue(q.z); w.WriteValue(q.w);
                w.WriteEndArray();
            }

            public override Quaternion ReadJson(JsonReader r, Type t, Quaternion existing, bool hasExisting, JsonSerializer s)
            {
                var a = JArray.Load(r);
                return a.Count < 4 ? Quaternion.identity
                    : new Quaternion((float)a[0], (float)a[1], (float)a[2], (float)a[3]);
            }
        }
    }
}
