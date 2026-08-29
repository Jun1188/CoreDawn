using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 정의의 정본 — 팩 json(data.json)에서 한 번 읽어 불변으로 든다. 심은 여기서만 정의를 얻는다(에셋·UnityEngine.Object 없음).
    ///
    /// id는 저장하지 않고 위치에서 파생한다: <c>팩:섹션/키</c>(소문자 snake, 예 <c>coredawn:item/iron_plate</c>).
    /// 섹션 = items · recipes · effects · entities · waves(+ guns · tutorial은 아직 원본 JObject로 보관 — 심이 안 쓴다).
    /// 로드 뒤 Resolve 패스가 id 문자열을 정의 참조로 잇고, 모르는 id·잘못된 키는 오류로 모은다(strict면 예외).
    /// </summary>
    public sealed class SimDatabase
    {
        static readonly Regex KeyRule = new Regex("^[a-z0-9_]+$");
        static readonly (string section, string singular)[] Sections =
        {
            ("items", "item"), ("recipes", "recipe"), ("effects", "effect"), ("entities", "entity"), ("waves", "wave"),
        };

        public string Pack { get; }

        readonly Dictionary<string, ItemDef> items = new Dictionary<string, ItemDef>();
        readonly Dictionary<string, RecipeDef> recipes = new Dictionary<string, RecipeDef>();
        readonly Dictionary<string, EffectSpec> effects = new Dictionary<string, EffectSpec>();
        readonly Dictionary<string, EntityDef> entities = new Dictionary<string, EntityDef>();
        readonly Dictionary<string, WaveDef> waves = new Dictionary<string, WaveDef>();
        readonly List<string> errors = new List<string>();

        public IReadOnlyDictionary<string, ItemDef> Items => items;
        public IReadOnlyDictionary<string, RecipeDef> Recipes => recipes;
        public IReadOnlyDictionary<string, EffectSpec> Effects => effects;
        public IReadOnlyDictionary<string, EntityDef> Entities => entities;
        public IReadOnlyDictionary<string, WaveDef> Waves => waves;

        /// <summary>심이 아직 안 읽는 섹션(guns·tutorial) — 표현·FPS·튜토리얼이 가져간다.</summary>
        public JObject Raw { get; private set; }

        public IReadOnlyList<string> Errors => errors;

        SimDatabase(string pack) => Pack = pack;

        public static string IdOf(string pack, string singular, string key) => $"{pack}:{singular}/{key}";

        static readonly Dictionary<string, string> LegacySections = new Dictionary<string, string>
        {
            ["Item"] = "item", ["Recipe"] = "recipe", ["Effect"] = "effect", ["Building"] = "entity", ["Monster"] = "entity",
            ["Wave"] = "wave", ["Gun"] = "gun", ["Tutorial"] = "tutorial",
        };

        /// <summary>
        /// 옛 id("Item:IronPlate", SO·세이브가 아직 쓴다) → 이 팩의 v2 id("coredawn:item/iron_plate"). 규칙이 순수해서 표가 필요 없다.
        /// 이미 v2 형식이면 그대로. SO가 퇴역하고 세이브 마이그레이션이 끝나면(5a-1c·5a-3) 사라진다.
        /// </summary>
        public string LegacyId(string oldId)
        {
            if (string.IsNullOrEmpty(oldId) || oldId.Contains("/")) return oldId;
            int i = oldId.IndexOf(':');
            if (i < 0 || !LegacySections.TryGetValue(oldId.Substring(0, i), out var section)) return oldId;
            var name = Regex.Replace(oldId.Substring(i + 1), "^Recipe_", "");
            name = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "_");
            name = Regex.Replace(name, "(?<=[A-Z])(?=[A-Z][a-z])", "_");
            return IdOf(Pack, section, name.ToLowerInvariant());
        }

        /// <summary>json 문자열에서 로드. strict면 오류가 하나라도 있을 때 예외(개발 중 기본), 아니면 Errors에 모으고 계속.</summary>
        public static SimDatabase Load(string json, string pack, bool strict = true)
        {
            var db = new SimDatabase(pack);
            var root = JObject.Parse(json);
            int format = (int?)root["format"] ?? 0;
            if (format != SimSchema.Format)
                db.errors.Add($"format {format} — 이 빌드는 {SimSchema.Format}만 읽는다");

            var serializer = SimSchema.CreateSerializer();
            db.LoadSection(root, "items", "item", db.items, serializer);
            db.LoadSection(root, "recipes", "recipe", db.recipes, serializer);
            db.LoadSection(root, "effects", "effect", db.effects, serializer);
            db.LoadSection(root, "entities", "entity", db.entities, serializer);
            db.LoadSection(root, "waves", "wave", db.waves, serializer);
            db.Raw = root;

            foreach (var d in db.items.Values) d.Resolve(db, db.errors);
            foreach (var d in db.recipes.Values) d.Resolve(db, db.errors);
            foreach (var d in db.effects.Values) d.Resolve(db, db.errors);
            foreach (var d in db.entities.Values) d.Resolve(db, db.errors);
            foreach (var d in db.waves.Values) d.Resolve(db, db.errors);

            if (strict && db.errors.Count > 0)
                throw new InvalidOperationException($"SimDatabase({pack}) 로드 실패 {db.errors.Count}건:\n  " + string.Join("\n  ", db.errors));
            return db;
        }

        void LoadSection<T>(JObject root, string section, string singular, Dictionary<string, T> into, Newtonsoft.Json.JsonSerializer serializer) where T : Def
        {
            if (!(root[section] is JObject obj)) return;
            foreach (var prop in obj.Properties())
            {
                if (!KeyRule.IsMatch(prop.Name))
                {
                    errors.Add($"{section}/{prop.Name}: 키는 소문자·숫자·_만 (id가 파일·경로가 된다)");
                    continue;
                }
                string id = IdOf(Pack, singular, prop.Name);
                try
                {
                    var def = prop.Value.ToObject<T>(serializer);
                    def.Id = id;
                    into[id] = def;
                }
                catch (Exception e)
                {
                    errors.Add($"{id}: {e.Message}");
                }
            }
        }

        // ── 참조 잇기 ───────────────────────────────────────────────
        public ItemDef ResolveItem(string id, List<string> errs, string owner) => Resolve(items, id, errs, owner, "item");
        public RecipeDef ResolveRecipe(string id, List<string> errs, string owner) => Resolve(recipes, id, errs, owner, "recipe");
        public EffectSpec ResolveEffect(string id, List<string> errs, string owner) => Resolve(effects, id, errs, owner, "effect");
        public EntityDef ResolveEntity(string id, List<string> errs, string owner) => Resolve(entities, id, errs, owner, "entity");

        static T Resolve<T>(Dictionary<string, T> dict, string id, List<string> errs, string owner, string what) where T : Def
        {
            if (string.IsNullOrEmpty(id)) { errs.Add($"{owner}: {what} id가 비어 있다"); return null; }
            if (dict.TryGetValue(id, out var d)) return d;
            errs.Add($"{owner}: 모르는 {what} \"{id}\"");
            return null;
        }

        public ItemDef Item(string id) => items.TryGetValue(id, out var d) ? d : null;
        public RecipeDef Recipe(string id) => recipes.TryGetValue(id, out var d) ? d : null;
        public EffectSpec Effect(string id) => effects.TryGetValue(id, out var d) ? d : null;
        public EntityDef Entity(string id) => entities.TryGetValue(id, out var d) ? d : null;
        public WaveDef Wave(string id) => waves.TryGetValue(id, out var d) ? d : null;
    }
}
