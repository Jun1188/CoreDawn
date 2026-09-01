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
    /// 섹션 = items · recipes · effects · entities · guns · tutorial · sounds · wave(규칙 하나 — 밤 웨이브 점수식) · dayCycle(주야 시계 하나) · sfx(공용 소리 자리 — 뷰가 Raw로 읽는다).
    /// 로드 뒤 Resolve 패스가 id 문자열을 정의 참조로 잇고, 모르는 id·잘못된 키는 오류로 모은다(strict면 예외).
    /// </summary>
    public sealed class SimDatabase
    {
        static readonly Regex KeyRule = new Regex("^[a-z0-9_]+$");
        static readonly (string section, string singular)[] Sections =
        {
            ("items", "item"), ("recipes", "recipe"), ("effects", "effect"), ("entities", "entity"), ("guns", "gun"), ("tutorial", "tutorial"), ("sounds", "sound"),
        };

        public string Pack { get; }

        readonly Dictionary<string, ItemDef> items = new Dictionary<string, ItemDef>();
        readonly Dictionary<string, RecipeDef> recipes = new Dictionary<string, RecipeDef>();
        readonly Dictionary<string, EffectSpec> effects = new Dictionary<string, EffectSpec>();
        readonly Dictionary<string, EntityDef> entities = new Dictionary<string, EntityDef>();
        WaveRuleDef wave;
        DayCycleDef dayCycle;
        readonly Dictionary<string, GunDef> guns = new Dictionary<string, GunDef>();
        readonly Dictionary<string, TutorialStepDef> tutorial = new Dictionary<string, TutorialStepDef>();
        readonly Dictionary<string, SoundDef> sounds = new Dictionary<string, SoundDef>();
        List<TutorialStepDef> tutorialOrdered;
        readonly List<string> errors = new List<string>();

        public IReadOnlyDictionary<string, ItemDef> Items => items;
        public IReadOnlyDictionary<string, RecipeDef> Recipes => recipes;
        public IReadOnlyDictionary<string, EffectSpec> Effects => effects;
        public IReadOnlyDictionary<string, EntityDef> Entities => entities;
        /// <summary>밤 웨이브 규칙(점수식) — 팩 wave 블록. 없으면 null(밤 웨이브 없음).</summary>
        public WaveRuleDef Wave => wave;
        /// <summary>주야 시계(낮·밤 길이) — 팩 dayCycle 블록. 없으면 null.</summary>
        public DayCycleDef DayCycle => dayCycle;
        public IReadOnlyDictionary<string, GunDef> Guns => guns;
        public IReadOnlyDictionary<string, TutorialStepDef> Tutorial => tutorial;
        /// <summary>소리(표현 전용 정의) — 뷰의 sfx 자리가 id로 가리킨다. 심은 존재만 검증한다.</summary>
        public IReadOnlyDictionary<string, SoundDef> Sounds => sounds;
        /// <summary>튜토리얼 스텝을 order → id 순으로. 게임(TutorialManager)이 이 순서대로 안내한다.</summary>
        public IReadOnlyList<TutorialStepDef> TutorialSteps
        {
            get
            {
                if (tutorialOrdered == null)
                {
                    tutorialOrdered = new List<TutorialStepDef>(tutorial.Values);
                    tutorialOrdered.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
                }
                return tutorialOrdered;
            }
        }

        /// <summary>원본 json 트리 — 에디터 도구(카탈로그 베이커 등)가 view 블록을 읽는다. 심은 쓰지 않는다.</summary>
        public JObject Raw { get; private set; }

        public IReadOnlyList<string> Errors => errors;

        SimDatabase(string pack) => Pack = pack;

        public static string IdOf(string pack, string singular, string key) => $"{pack}:{singular}/{key}";

        static readonly Dictionary<string, string> LegacySections = new Dictionary<string, string>
        {
            ["Item"] = "item", ["Recipe"] = "recipe", ["Effect"] = "effect", ["Building"] = "entity", ["Monster"] = "entity",
            ["Gun"] = "gun", ["Tutorial"] = "tutorial", ["Sound"] = "sound",
        };

        /// <summary>
        /// 옛 id("Item:IronPlate" — 구 SO id 체계) → 이 팩의 v2 id("coredawn:item/iron_plate"). 규칙이 순수해서 표가 필요 없다.
        /// 이미 v2 형식이면 그대로. 호출처는 세이브 마이그레이션(SaveMigrations)뿐이다 — 런타임 정의 조회에 쓰지 말 것.
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
            if (root["wave"] is JObject waveObj)
            {
                try { db.wave = waveObj.ToObject<WaveRuleDef>(serializer); db.wave.Id = IdOf(pack, "wave", "rule"); }
                catch (Exception e) { db.errors.Add($"wave: {e.Message}"); }
            }
            if (root["dayCycle"] is JObject dcObj)
            {
                try { db.dayCycle = dcObj.ToObject<DayCycleDef>(serializer); db.dayCycle.Id = IdOf(pack, "dayCycle", "rule"); }
                catch (Exception e) { db.errors.Add($"dayCycle: {e.Message}"); }
            }
            db.LoadSection(root, "guns", "gun", db.guns, serializer);
            db.LoadSection(root, "tutorial", "tutorial", db.tutorial, serializer);
            db.LoadSection(root, "sounds", "sound", db.sounds, serializer);
            db.Raw = root;

            foreach (var d in db.guns.Values) d.Resolve(db, db.errors);     // 총 → 탄 아이템. 아이템의 Weapon 모듈은 총을 가리키므로 총이 먼저
            foreach (var d in db.items.Values) d.Resolve(db, db.errors);
            foreach (var d in db.recipes.Values) d.Resolve(db, db.errors);
            foreach (var d in db.effects.Values) d.Resolve(db, db.errors);
            foreach (var d in db.entities.Values) d.Resolve(db, db.errors);
            db.wave?.Resolve(db, db.errors);
            db.dayCycle?.Resolve(db, db.errors);
            foreach (var d in db.tutorial.Values) d.Resolve(db, db.errors);

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
        public GunDef ResolveGun(string id, List<string> errs, string owner) => Resolve(guns, id, errs, owner, "gun");

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
        public GunDef Gun(string id) => guns.TryGetValue(id, out var d) ? d : null;
        public TutorialStepDef TutorialStep(string id) => tutorial.TryGetValue(id, out var d) ? d : null;
        public SoundDef Sound(string id) => sounds.TryGetValue(id, out var d) ? d : null;
    }
}
