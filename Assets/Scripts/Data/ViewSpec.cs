using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>
    /// 소리를 <b>쓰는 자리</b> 하나 — <c>{sound, volume, spatial}</c>. 소리 자체(클립 묶음)는 팩 sounds 섹션(<see cref="SoundDef"/>)이고,
    /// 얼마나 크게·어디서 나는가는 쓰는 자리가 정한다(EffectSpec ↔ EffectUse와 같은 구분). view.sfx와 팩 최상위 sfx가 이 꼴이다.
    /// </summary>
    public sealed class SoundUse
    {
        public string Sound;
        public float Volume = 1f;
        public bool Spatial = true;

        public static SoundUse From(JObject o)
        {
            if (o == null) return null;
            return new SoundUse
            {
                Sound = (string)o["sound"],
                Volume = (float?)o["volume"] ?? 1f,
                Spatial = (bool?)o["spatial"] ?? true,
            };
        }
    }

    /// <summary>
    /// 정의의 view 블록을 읽은 것 — 뷰 종류(<see cref="Type"/>, <see cref="ViewSchema"/> 표의 키)와 소리 자리(<see cref="Sfx"/>).
    /// 모델·프리팹·아이콘 같은 에셋 참조는 카탈로그(<see cref="ViewCatalogSO"/>)가 굽고, 여기는 값만 든다.
    /// 잘못된 값(모르는 type·표에 없는 sfx 이름·팩에 없는 소리)은 첫 조회에서 소리 내어 알린다 — 조용한 폴백 없음.
    /// </summary>
    public sealed class ViewSpec
    {
        public Def Def { get; }
        public string Type { get; }
        public JObject Raw { get; }
        readonly Dictionary<string, SoundUse> sfx = new Dictionary<string, SoundUse>();

        public IReadOnlyDictionary<string, SoundUse> Sfx => sfx;

        /// <summary>이름의 소리 자리. 정의에 없으면 null — 그 연출은 소리 없이 지나간다(빠진 이름은 이미 로드 때 검증됐다).</summary>
        public SoundUse SfxOf(string name) => sfx.TryGetValue(name, out var u) ? u : null;

        /// <summary>
        /// 모델 한 항목 — 팩 glb(<c>{file, materials[]}</c>: materials[i]는 glb 재질 슬롯 i에 꽂을 팩 재질 id)이거나, 옛 guid 참조 문자열(과도기 — ViewCatalog가 읽는다).
        /// </summary>
        public sealed class ModelRef
        {
            public readonly string File;
            public readonly IReadOnlyList<string> Materials;
            public readonly bool IsPack;
            public ModelRef(string file, IReadOnlyList<string> materials, bool isPack) { File = file; Materials = materials; IsPack = isPack; }
        }

        /// <summary>모델 목록 — view.model은 배열([0]이 기본, 나머지는 변형)이고 옛 형식은 문자열 하나. 없으면 빈 목록.</summary>
        public IReadOnlyList<ModelRef> Models(string key = "model") => ModelsOf(Raw, key);

        /// <summary>검증 없이 원시 view에서 모델 목록만 읽는다 — 부팅 preload가 쓴다.</summary>
        public static IReadOnlyList<ModelRef> ModelsOf(JObject raw, string key)
        {
            var list = new List<ModelRef>();
            void Add(JToken x)
            {
                if (x is JObject o)
                {
                    var mats = new List<string>();
                    if (o["materials"] is JArray ma) foreach (var m in ma) if (m.Type == JTokenType.String) mats.Add((string)m);
                    list.Add(new ModelRef((string)o["file"], mats, true));
                }
                else if (x != null && x.Type == JTokenType.String) list.Add(new ModelRef((string)x, Array.Empty<string>(), false));
            }
            var t = raw?[key];
            if (t is JArray arr) foreach (var x in arr) Add(x);
            else if (t != null) Add(t);
            return list;
        }

        /// <summary>view의 [x, y, z] 배열. 없거나 짧으면 null.</summary>
        public Vector3? Vec3(string key)
        {
            if (Raw[key] is not JArray a || a.Count < 3) return null;
            return new Vector3((float)a[0], (float)a[1], (float)a[2]);
        }
        public float Float(string key, float fallback) => (float?)Raw[key] ?? fallback;
        public string String(string key) => (string)Raw[key];
        /// <summary>하위 객체(예: pose·knockback). 없으면 null.</summary>
        public JObject Object(string key) => Raw[key] as JObject;

        /// <summary>view.pose{position, rotation(오일러), scale} — 부모(손·홀더·건물 루트) 기준 자세. 없으면 원점·단위.</summary>
        public (Vector3 position, Quaternion rotation, float scale) Pose => PoseOf("pose");

        /// <summary>벨트 모양별 자세 — 커브 모델은 view.poseCurveL/poseCurveR(없으면 pose).</summary>
        public (Vector3 position, Quaternion rotation, float scale) PoseFor(BeltShape shape)
            => shape == BeltShape.CurveL && Raw["poseCurveL"] != null ? PoseOf("poseCurveL")
             : shape == BeltShape.CurveR && Raw["poseCurveR"] != null ? PoseOf("poseCurveR")
             : Pose;

        (Vector3 position, Quaternion rotation, float scale) PoseOf(string key)
        {
            if (Raw[key] is not JObject p) return (Vector3.zero, Quaternion.identity, 1f);
            Vector3 V(string k) { if (p[k] is JArray a && a.Count >= 3) return new Vector3((float)a[0], (float)a[1], (float)a[2]); return Vector3.zero; }
            return (V("position"), Quaternion.Euler(V("rotation")), (float?)p["scale"] ?? 1f);
        }

        internal ViewSpec(Def def, JObject raw)
        {
            Def = def;
            Raw = raw ?? new JObject();
            Type = (string)Raw["type"];
            if (Raw["sfx"] is JObject sfxObj)
                foreach (var p in sfxObj.Properties())
                    if (p.Value is JObject uo) sfx[p.Name] = SoundUse.From(uo);
        }
    }

    /// <summary>
    /// 뷰 종류 표 — 팩 view.type 문자열 → 그 종류가 가질 수 있는 소리 자리 이름(SimSchema의 모듈 표와 같은 명시 표).
    /// 5a-4b의 조립기가 같은 키로 컴포넌트·콜라이더·레이어를 결정한다. 조회는 정의당 한 번 읽어 캐시한다.
    /// </summary>
    public static class ViewSchema
    {
        public static readonly IReadOnlyDictionary<string, string[]> Types = new Dictionary<string, string[]>
        {
            ["Building"] = new[] { "destroy" },
            ["Tower"]    = new[] { "fire", "destroy", "starved" },
            ["Deposit"]  = Array.Empty<string>(),
            ["Nest"]     = Array.Empty<string>(),
            ["Monster"]  = Array.Empty<string>(),
            ["Player"]   = Array.Empty<string>(),
            ["Gun"]      = new[] { "fire", "reload" },
        };

        static readonly Dictionary<Def, ViewSpec> cache = new Dictionary<Def, ViewSpec>();
        static Dictionary<string, SoundUse> common;
        static SimDatabase builtFrom;

        /// <summary>정의의 뷰 사양. 정의가 null이면 null.</summary>
        public static ViewSpec Of(Def def)
        {
            if (def == null) return null;
            Invalidate();
            if (cache.TryGetValue(def, out var spec)) return spec;
            spec = new ViewSpec(def, def.View);
            Validate(spec);
            cache[def] = spec;
            return spec;
        }

        /// <summary>팩 최상위 sfx — 특정 정의에 속하지 않는 공용 소리 자리(ui_click·construct·mine…). 없으면 null(오류 로그 1회).</summary>
        public static SoundUse Common(string name)
        {
            Invalidate();
            var db = SimHost.Database;
            if (common == null)
            {
                common = new Dictionary<string, SoundUse>();
                if (db?.Raw?["sfx"] is JObject sfxObj)
                    foreach (var p in sfxObj.Properties())
                        if (p.Value is JObject uo)
                        {
                            var use = SoundUse.From(uo);
                            if (db.Sound(use.Sound) == null) Debug.LogError($"[ViewSchema] sfx/{p.Name}: 소리 '{use.Sound}'가 팩 sounds에 없습니다");
                            common[p.Name] = use;
                        }
            }
            if (common.TryGetValue(name, out var u)) return u;
            if (missingCommon.Add(name)) Debug.LogError($"[ViewSchema] 팩 sfx에 '{name}' 자리가 없습니다 — 소리 없이 지나갑니다");
            return null;
        }
        static readonly HashSet<string> missingCommon = new HashSet<string>();

        static void Validate(ViewSpec spec)
        {
            string id = spec.Def.Id;
            if (string.IsNullOrEmpty(spec.Type))
            {
                Debug.LogError($"[ViewSchema] {id}: view.type이 없습니다 (허용: {string.Join(", ", Types.Keys)})");
                return;
            }
            if (!Types.TryGetValue(spec.Type, out var allowed))
            {
                Debug.LogError($"[ViewSchema] {id}: 모르는 view.type '{spec.Type}' (허용: {string.Join(", ", Types.Keys)})");
                return;
            }
            var db = SimHost.Database;
            foreach (var kv in spec.Sfx)
            {
                if (Array.IndexOf(allowed, kv.Key) < 0)
                    Debug.LogError($"[ViewSchema] {id}: view.sfx '{kv.Key}'는 {spec.Type}에 없는 자리입니다 (허용: {string.Join(", ", allowed)})");
                if (kv.Value == null || string.IsNullOrEmpty(kv.Value.Sound))
                    Debug.LogError($"[ViewSchema] {id}: view.sfx '{kv.Key}'에 sound가 없습니다");
                else if (db != null && db.Sound(kv.Value.Sound) == null)
                    Debug.LogError($"[ViewSchema] {id}: view.sfx '{kv.Key}'의 소리 '{kv.Value.Sound}'가 팩 sounds에 없습니다");
            }
        }

        /// <summary>팩이 다시 로드됐으면(편집기 저장·새 게임) 캐시를 버린다.</summary>
        static void Invalidate()
        {
            var db = SimHost.Database;
            if (ReferenceEquals(db, builtFrom)) return;
            cache.Clear(); common = null; missingCommon.Clear();
            builtFrom = db;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { cache.Clear(); common = null; missingCommon.Clear(); builtFrom = null; }
    }
}
