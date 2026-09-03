using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>
    /// 정의의 view 블록을 <b>타입으로</b> 읽는 곳 — 섹션마다 클래스가 다르다(<see cref="EntityViewDef"/>·<see cref="GunViewDef"/>·
    /// <see cref="ItemViewDef"/>·<see cref="SoundViewDef"/>·<see cref="MaterialViewDef"/>). 심의 직렬화기(모르는 키 = 오류)로 읽고
    /// 정의당 한 번 캐시한다. 잘못된 값(모르는 키·모르는 type·표에 없는 sfx 이름·팩에 없는 소리)은 첫 조회에서 소리 내어
    /// 알리고 기본 view로 선다 — 조용한 폴백 없음.
    /// </summary>
    public static class ViewSchema
    {
        /// <summary>뷰 종류 표 — view.type 문자열 → 그 종류가 가질 수 있는 소리 자리 이름(SimSchema의 모듈 표와 같은 명시 표).</summary>
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

        static readonly Newtonsoft.Json.JsonSerializer Serializer = SimSchema.CreateSerializer();
        static readonly Dictionary<Def, object> cache = new Dictionary<Def, object>();
        static Dictionary<string, SoundUse> common;
        static SimDatabase builtFrom;
        static readonly HashSet<string> missingCommon = new HashSet<string>();

        public static EntityViewDef Entity(EntityDef def) => Read<EntityViewDef>(def, v =>
        {
            ValidateTyped(def, v.Type, v.Sfx);
            var err = InteractKinds.Validate(def, v.Interact);   // 지정된 상호작용이 요구하는 모듈이 없으면 오류 — 추론 대신 데이터가 말한다
            if (err != null) Debug.LogError("[ViewSchema] " + err);
        });
        public static GunViewDef Gun(GunDef def) => Read<GunViewDef>(def, v => ValidateTyped(def, v.Type, v.Sfx));
        public static ItemViewDef Item(ItemDef def) => Read<ItemViewDef>(def, null);
        public static SoundViewDef Sound(SoundDef def) => Read<SoundViewDef>(def, null);
        public static MaterialViewDef Material(MaterialDef def) => Read<MaterialViewDef>(def, null);

        static T Read<T>(Def def, Action<T> validate) where T : class, new()
        {
            if (def == null) return null;
            Invalidate();
            if (cache.TryGetValue(def, out var cached)) return (T)cached;
            T view;
            if (def.View == null) view = new T();
            else
            {
                try { view = def.View.ToObject<T>(Serializer) ?? new T(); }
                catch (Exception e)
                {
                    Debug.LogError($"[ViewSchema] {def.Id}: view가 {typeof(T).Name} 꼴이 아닙니다 — 기본 view로 섭니다. {e.Message}");
                    view = new T();
                }
            }
            validate?.Invoke(view);
            cache[def] = view;
            return view;
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
                {
                    try { common = sfxObj.ToObject<Dictionary<string, SoundUse>>(Serializer) ?? common; }
                    catch (Exception e) { Debug.LogError($"[ViewSchema] 팩 sfx 섹션을 읽지 못했습니다: {e.Message}"); }
                    foreach (var kv in common)
                        if (kv.Value == null || string.IsNullOrEmpty(kv.Value.Sound)) Debug.LogError($"[ViewSchema] sfx/{kv.Key}: sound가 없습니다");
                        else if (db.Sound(kv.Value.Sound) == null) Debug.LogError($"[ViewSchema] sfx/{kv.Key}: 소리 '{kv.Value.Sound}'가 팩 sounds에 없습니다");
                }
            }
            if (common.TryGetValue(name, out var u)) return u;
            if (missingCommon.Add(name)) Debug.LogError($"[ViewSchema] 팩 sfx에 '{name}' 자리가 없습니다 — 소리 없이 지나갑니다");
            return null;
        }

        static void ValidateTyped(Def def, string type, Dictionary<string, SoundUse> sfx)
        {
            string id = def.Id;
            if (string.IsNullOrEmpty(type))
            {
                Debug.LogError($"[ViewSchema] {id}: view.type이 없습니다 (허용: {string.Join(", ", Types.Keys)})");
                return;
            }
            if (!Types.TryGetValue(type, out var allowed))
            {
                Debug.LogError($"[ViewSchema] {id}: 모르는 view.type '{type}' (허용: {string.Join(", ", Types.Keys)})");
                return;
            }
            var db = SimHost.Database;
            if (sfx == null) return;
            foreach (var kv in sfx)
            {
                if (Array.IndexOf(allowed, kv.Key) < 0)
                    Debug.LogError($"[ViewSchema] {id}: view.sfx '{kv.Key}'는 {type}에 없는 자리입니다 (허용: {string.Join(", ", allowed)})");
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
