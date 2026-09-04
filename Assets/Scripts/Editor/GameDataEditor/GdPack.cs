#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CoreDawn.EditorTools
{
    // ═══════════════════════════════════════════════════════════
    //  GdPack — 편집기의 팩 입출력과 두 형식 사이의 변환(3e-2 ③, 2026-09-03).
    //
    //  정본은 팩 data.json 하나다(v1 GameData.json·exporter·python 도구 퇴역). 폼 탭들은 예전 편집 형식(v1 DTO,
    //  GameDataJson)으로 쓰여 있어 그대로 두고, 대신 변환을 양방향으로 둔다:
    //    ToV1(pack)  — data.json → v1 꼴 JObject → DTO(탭이 편집)
    //    ToPack(v1)  — DTO → v1 꼴 JObject → data.json
    //  ToV1은 무손실이어야 한다: 프리셋(kind·fireMode)이 못 담는 모듈은 extraModules로, view의 나머지 키는
    //  view 객체에 그대로 실어 ToPack이 되붙인다. 검증 기준: ToPack(ToV1(pack)) ≡ pack(키 순서 무관).
    //  에셋은 전부 팩 폴더 안의 파일(경로)이다 — guid·복사 없음.
    // ═══════════════════════════════════════════════════════════
    public static class GdPack
    {
        public const string Pack = "coredawn";
        public static string PackFolder => $"Assets/StreamingAssets/packs/{Pack}";
        public static string DataPath => $"{PackFolder}/data.json";
        public static string MapsFolder => $"{PackFolder}/maps";

        static readonly JsonSerializerSettings ParseSettings = new() { DateParseHandling = DateParseHandling.None };

        public static JObject ReadPack(out bool crlf)
        {
            var text = File.ReadAllText(DataPath);
            crlf = text.Contains("\r\n");
            return JsonConvert.DeserializeObject<JObject>(text, ParseSettings);
        }

        /// <summary>data.json 쓰기 — 2칸 들여쓰기, 파일이 쓰던 개행 유지, 끝 개행.</summary>
        public static void WritePack(JObject pack, bool crlf)
        {
            string nl = crlf ? "\r\n" : "\n";
            var text = pack.ToString(Formatting.Indented).Replace("\r\n", "\n").Replace("\n", nl) + nl;
            Directory.CreateDirectory(PackFolder);
            File.WriteAllText(DataPath, text);
            AssetDatabase.ImportAsset(DataPath);
        }

        /// <summary>심 로더로 곧장 읽어 본다 — 깨진 참조를 저장 시점에 잡는다. 오류 목록(없으면 빈 목록).</summary>
        public static List<string> Validate(JObject pack)
        {
            var db = Sim.SimDatabase.Load(pack.ToString(Formatting.None), Pack, strict: false);
            var errors = db.Errors.ToList();
            foreach (var d in db.Entities.Values)   // view.interact 가 요구하는 모듈 — 로드(ViewSchema)와 같은 검증을 저장 전에
            {
                var err = Data.InteractKinds.Validate(d, (string)(d.View as JObject)?["interact"]);
                if (err != null) errors.Add(err);
            }
            return errors;
        }

        // ── v1 id 관례(옛 "Item:IronOre") — 팩 id를 그대로 쓰지만 탭이 옛 형식을 만들면 풀어 준다 ──

        static readonly Dictionary<string, string> SectionOf = new()
        {
            ["Item"] = "item", ["Recipe"] = "recipe", ["Effect"] = "effect", ["Building"] = "entity", ["Monster"] = "entity",
            ["Gun"] = "gun", ["Tutorial"] = "tutorial", ["Sound"] = "sound", ["Material"] = "material", ["Map"] = "map",
        };

        static string Snake(string name)
        {
            name = Regex.Replace(name, "^Recipe_", "");
            var s = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "_");
            s = Regex.Replace(s, "(?<=[A-Z])(?=[A-Z][a-z])", "_");
            return s.ToLowerInvariant();
        }

        /// <summary>어떤 id든 팩 id로 — 이미 팩 id("coredawn:item/x")면 그대로, 옛 "Item:X"면 풀어서.</summary>
        public static string PackIdOf(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Contains("/")) return id;
            int i = id.IndexOf(':');
            if (i < 0 || !SectionOf.TryGetValue(id.Substring(0, i), out var section)) return id;
            return $"{Pack}:{section}/{Snake(id.Substring(i + 1))}";
        }

        static string KeyOf(string id) { var p = PackIdOf(id); int i = p.IndexOf('/'); return i >= 0 ? p.Substring(i + 1) : p; }
        static string IdOf(string singular, string key) => $"{Pack}:{singular}/{key}";

        /// <summary>탭이 새 항목 id를 만들 때 — "coredawn:&lt;단수&gt;/&lt;이름&gt;".</summary>
        public static string Id(string singular, string bare) => IdOf(singular, bare);
        /// <summary>id의 이름 부분(마지막 '/' 뒤). 옛 "Item:X"면 X.</summary>
        public static string Bare(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int i = id.LastIndexOf('/');
            if (i >= 0) return id.Substring(i + 1);
            int c = id.IndexOf(':');
            return c >= 0 ? id.Substring(c + 1) : id;
        }
        static JArray Arr(JToken t) => t as JArray ?? new JArray();

        // ═════════════════════════════════════════════════════════
        //  v1 → pack
        // ═════════════════════════════════════════════════════════

        /// <summary>MonsterBrain 모듈 정의의 json 키(JsonProperty 이름) — 편집기 v1 평면 몬스터 ↔ 팩 모듈 변환이 공유한다.</summary>
        public static readonly string[] MonsterBrainKeys = typeof(CoreDawn.Sim.MonsterBrainModuleDef)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(f => f.GetCustomAttributes(typeof(JsonPropertyAttribute), true).OfType<JsonPropertyAttribute>().FirstOrDefault()?.PropertyName)
            .Where(n => !string.IsNullOrEmpty(n)).ToArray();

        public static JObject ToPack(JObject d)
        {
            string NewId(string old) => PackIdOf(old);

            JArray Uses(JToken list)
            {
                var a = new JArray();
                foreach (var u in Arr(list))
                {
                    var o = new JObject { ["effect"] = NewId((string)u["effect"]), ["value"] = u["value"] ?? 0 };
                    if ((float?)u["duration"] > 0f) o["duration"] = u["duration"];
                    if ((float?)u["tickInterval"] > 0f) o["tickInterval"] = u["tickInterval"];
                    a.Add(o);
                }
                return a;
            }
            JArray Amounts(JToken list)
            {
                var a = new JArray();
                foreach (var x in Arr(list)) a.Add(new JObject { ["item"] = NewId((string)x["item"]), ["amount"] = x["amount"] });
                return a;
            }
            JObject Head(JToken e)
            {
                var h = new JObject { ["displayName"] = (string)e["displayName"] ?? "" };
                var desc = (string)e["description"];
                if (!string.IsNullOrEmpty(desc)) h["description"] = desc;
                return h;
            }
            JToken Remap(JToken x)
            {
                switch (x)
                {
                    case JValue v when v.Type == JTokenType.String: return PackIdOf((string)v);
                    case JArray a: return new JArray(a.Select(Remap));
                    case JObject obj:
                    {
                        var r = new JObject();
                        foreach (var p in obj.Properties()) if (p.Name != "id") r[p.Name] = Remap(p.Value);
                        return r;
                    }
                    default: return x.DeepClone();
                }
            }
            // v1 view{type, sfx, …} 객체를 v2 view 블록에 얹는다 — 키를 전부 옮긴다(모르는 키도 보존). 소리 id는 팩 id로
            void MergeView(JObject view, JToken e)
            {
                if (e["view"] is not JObject v) return;
                foreach (var p in v.Properties()) view[p.Name] = Remap(p.Value);
            }
            JObject IconRef(JToken e)
            {
                string file = (string)e["iconFile"], frame = (string)e["icon"];
                if (string.IsNullOrEmpty(file)) return null;
                return new JObject { ["file"] = file, ["frame"] = frame ?? "" };
            }
            // 프리셋이 못 담아 실어 온 모듈들 — 되붙인다(id 치환만)
            void AppendExtra(JArray mods, JToken e)
            {
                foreach (var m in Arr(e["extraModules"])) mods.Add(Remap(m));
            }

            var materials = new JObject();
            JArray PackModels(JToken packModels, string owner)
            {
                var arr = new JArray();
                foreach (var pm in Arr(packModels))
                {
                    if (!(pm is JObject po) || string.IsNullOrEmpty((string)po["file"])) throw new InvalidOperationException($"{owner}: model 항목은 {{file, materials}} 객체여야 합니다");
                    var mats = new JArray();
                    foreach (var m in Arr(po["materials"]))
                    {
                        string nid = NewId((string)m);
                        if (materials[nid.Split('/').Last()] == null) throw new InvalidOperationException($"{owner}: 재질 '{m}'가 materials에 없습니다");
                        mats.Add(nid);
                    }
                    arr.Add(new JObject { ["file"] = po["file"], ["materials"] = mats });
                }
                return arr;
            }

            // 재질 — 셰이더는 내장, 값·텍스처(팩 경로)는 팩
            foreach (var mat in Arr(d["materials"]))
            {
                string mid = (string)mat["id"];
                if (string.IsNullOrEmpty((string)mat["shader"])) throw new InvalidOperationException($"materials/{mid}: shader가 비었습니다");
                var v = new JObject { ["shader"] = mat["shader"] };
                var texs = new JObject();
                foreach (var t in Arr(mat["textures"]))
                {
                    string file = (string)t["file"];
                    if (string.IsNullOrEmpty(file)) throw new InvalidOperationException($"materials/{mid}: 텍스처 '{t["name"]}'의 file이 비었습니다");
                    texs[(string)t["name"]] = new JObject { ["file"] = file, ["linear"] = (bool?)t["linear"] ?? false };
                }
                if (texs.Count > 0) v["textures"] = texs;
                // 숫자는 토큰 그대로 옮긴다 — (float) 캐스트를 거치면 같은 값이 float/double로 갈려 왕복 비교와 diff가 흔들린다
                JObject Vec4s(JToken arr) { var o = new JObject(); foreach (var c in Arr(arr)) o[(string)c["name"]] = new JArray(c["r"]?.DeepClone() ?? 0, c["g"]?.DeepClone() ?? 0, c["b"]?.DeepClone() ?? 0, c["a"]?.DeepClone() ?? 1); return o; }
                if (Arr(mat["colors"]).Count > 0) v["colors"] = Vec4s(mat["colors"]);
                if (Arr(mat["vectors"]).Count > 0) v["vectors"] = Vec4s(mat["vectors"]);
                if (Arr(mat["floats"]).Count > 0) { var o = new JObject(); foreach (var f in Arr(mat["floats"])) o[(string)f["name"]] = f["value"]?.DeepClone() ?? 0; v["floats"] = o; }
                if (Arr(mat["keywords"]).Count > 0) v["keywords"] = mat["keywords"].DeepClone();
                if ((int?)mat["renderQueue"] is int rq && rq >= 0) v["renderQueue"] = rq;
                if (Arr(mat["tags"]).Count > 0) { var o = new JObject(); foreach (var t in Arr(mat["tags"])) o[(string)t["name"]] = t["value"]; v["tags"] = o; }
                var mo = new JObject();
                if (!string.IsNullOrEmpty((string)mat["displayName"])) mo["displayName"] = mat["displayName"];
                mo["view"] = v;
                materials[KeyOf(mid)] = mo;
            }

            var items = new JObject(); var recipes = new JObject(); var effects = new JObject();
            var entities = new JObject(); var guns = new JObject(); var tutorial = new JObject(); var sounds = new JObject();

            foreach (var it in Arr(d["items"]))
            {
                var o = Head(it);
                o["type"] = it["type"]; o["line"] = it["line"]; o["maxStack"] = it["maxStack"];
                if ((bool?)it["hideFromMenu"] == true) o["hideFromMenu"] = true;
                var mods = new JArray();
                bool hasAmmo = Arr(it["attackEffects"]).Count > 0 || ((float?)it["speed"] ?? -1f) >= 0f;
                if (hasAmmo)
                    mods.Add(new JObject { ["type"] = "Ammo", ["speed"] = it["speed"], ["gravity"] = it["gravity"], ["explosionRadius"] = it["explosionRadius"],
                                           ["lifetime"] = it["lifetime"], ["pierce"] = it["pierce"], ["effects"] = Uses(it["attackEffects"]) });
                if (!string.IsNullOrEmpty((string)it["gun"])) mods.Add(new JObject { ["type"] = "Weapon", ["gun"] = NewId((string)it["gun"]) });
                AppendExtra(mods, it);
                o["modules"] = mods;
                var itemView = new JObject();
                var iconRef = IconRef(it);
                if (iconRef != null) itemView["icon"] = iconRef;
                foreach (var nk in new[] { "bullet", "muzzleFlash", "hitEffect" })
                    if (!string.IsNullOrEmpty((string)it[nk])) itemView[nk] = it[nk];
                MergeView(itemView, it);
                if (itemView.Count > 0) o["view"] = itemView;
                items[KeyOf((string)it["id"])] = o;

                // 광맥 — Ore 아이템마다 하나, entities/<item>_deposit(매장량 없음·Health 없음). 공용 뷰는 v1 root.deposit
                if ((string)it["type"] == "Ore")
                {
                    float interval = (float?)it["extractInterval"] ?? -1f;
                    if (!(interval > 0f)) throw new InvalidOperationException($"Ore 아이템 '{it["id"]}'에 extractInterval(>0)이 없습니다");
                    string key = KeyOf((string)it["id"]) + "_deposit";
                    if (entities[key] != null) throw new InvalidOperationException($"entities/{key} id 충돌");
                    var depositView = new JObject { ["type"] = "Deposit" };
                    if (d["deposit"] is JObject dep)
                    {
                        if (dep["view"] is JObject dv) foreach (var pr in dv.Properties()) if (pr.Name != "type") depositView[pr.Name] = Remap(pr.Value);
                        if (dep["models"] is JArray dm && dm.Count > 0) depositView["model"] = PackModels(dm, "deposit");
                    }
                    entities[key] = new JObject
                    {
                        ["displayName"] = (string)it["displayName"] + " 광맥",
                        ["faction"] = "Neutral",
                        ["view"] = depositView,
                        ["modules"] = new JArray { new JObject { ["type"] = "ResourceDeposit", ["resource"] = NewId((string)it["id"]), ["extractInterval"] = interval } },
                    };
                }
                else if (((float?)it["extractInterval"] ?? 0f) > 0f)
                    throw new InvalidOperationException($"아이템 '{it["id"]}'은 Ore가 아닌데 extractInterval이 있습니다 — 채굴 시간은 원광(Ore)만 갖는다");
            }

            foreach (var r in Arr(d["recipes"]))
            {
                var o = Head(r);
                o["tier"] = r["tier"]; o["seconds"] = r["craftTime"]; o["inputs"] = Amounts(r["inputs"]); o["outputs"] = Amounts(r["outputs"]);
                recipes[KeyOf((string)r["id"])] = o;
            }

            foreach (var e in Arr(d["effects"]))
            {
                var o = Head(e);
                o["type"] = e["kind"];
                if ((float?)e["duration"] > 0f) o["duration"] = e["duration"];
                if ((float?)e["tickInterval"] > 0f) o["tickInterval"] = e["tickInterval"];
                var st = (string)e["stacking"]; if (!string.IsNullOrEmpty(st) && st != "Refresh") o["stacking"] = st;
                var km = (string)e["knockbackMode"]; if (!string.IsNullOrEmpty(km) && km != "Directional") o["knockbackMode"] = km;
                if (Arr(e["affects"]).Count > 0) o["affects"] = new JArray(Arr(e["affects"]).Select(a => NewId((string)a)));
                effects[KeyOf((string)e["id"])] = o;
            }

            foreach (var b in Arr(d["buildings"]))
            {
                string kind = (string)b["kind"];
                var o = Head(b);
                o["faction"] = (string)b["faction"] ?? (kind == "Nest" ? "Monster" : kind == "Tree" ? "Neutral" : "Player");
                var mods = new JArray();
                var building = new JObject { ["type"] = "Building", ["size"] = b["size"]?.DeepClone(), ["category"] = b["category"] };
                if ((bool?)b["hideFromBuildMenu"] == true) building["placeable"] = false;
                if ((bool?)b["isDemolishable"] == false) building["isDemolishable"] = false;
                if ((bool?)b["isAttackable"] == false) building["isAttackable"] = false;
                if ((bool?)b["walkable"] == true) building["walkable"] = true;
                foreach (var k in new[] { "requiredCoreTier", "threatSeedCost", "menuOrder" })
                    if (((int?)b[k] ?? 0) > 0) building[k] = b[k];   // 0·음수 = 기본(생략)
                if (Arr(b["buildCost"]).Count > 0) building["cost"] = Amounts(b["buildCost"]);
                mods.Add(building);
                if (b["maxHp"] != null) mods.Add(new JObject { ["type"] = "Health", ["maxHp"] = b["maxHp"] });
                if ((bool?)b["noEffects"] != true) mods.Add(new JObject { ["type"] = "Effects" });
                if (Arr(b["ports"]).Count > 0) mods.Add(new JObject { ["type"] = "Ports", ["ports"] = b["ports"].DeepClone() });
                int inSlots = (int?)b["inputSlots"] ?? 0, outSlots = (int?)b["outputSlots"] ?? 0, cap = (int?)b["bufferStackCap"] ?? 0;
                if (inSlots > 0 || outSlots > 0)
                {
                    var inv = new JObject { ["type"] = "Inventory" };
                    if (inSlots > 0) inv["input"] = inSlots;
                    if (outSlots > 0) inv["output"] = outSlots;
                    if (cap > 0) inv["stackCap"] = cap;
                    mods.Add(inv);
                }
                JObject Ammo() => new JObject { ["type"] = "AmmoConsumer", ["ammoFilter"] = new JArray(Arr(b["ammoFilter"]).Select(a => NewId((string)a))), ["damageMultiplier"] = b["damageMultiplier"] ?? 1.0 };
                switch (kind)
                {
                    case "Belt": mods.Add(new JObject { ["type"] = "Conveyor", ["speedTilesPerSec"] = b["speedTilesPerSec"] ?? 1.0 }); break;
                    case "Miner": mods.Add(new JObject { ["type"] = "Extractor", ["speedMultiplier"] = b["speedMultiplier"] ?? 1.0 }); break;
                    case "Assembler":
                        mods.Add(new JObject { ["type"] = "Crafter", ["manual"] = false, ["speed"] = ((float?)b["speedMultiplier"] ?? 0f) > 0f ? b["speedMultiplier"] : 1.0,
                                               ["recipes"] = new JArray(Arr(b["availableRecipes"]).Select(r => NewId((string)r))) });
                        break;
                    case "Splitter": mods.Add(new JObject { ["type"] = "Router", ["mode"] = "split" }); break;
                    case "Merger": mods.Add(new JObject { ["type"] = "Router", ["mode"] = "merge" }); break;
                    case "Core":
                    {
                        var tiers = new JArray();
                        foreach (var t in Arr(b["tiers"]))
                        {
                            var tier = new JObject { ["name"] = t["name"], ["description"] = t["description"], ["requirements"] = Amounts(t["requirements"]),
                                                     ["unlocks"] = t["unlocks"]?.DeepClone() ?? new JArray(), ["maxHpBonus"] = t["maxHpBonus"] ?? 0, ["isFinal"] = t["isFinal"] ?? false };
                            if (t["maxShieldBonus"] != null) tier["maxShieldBonus"] = t["maxShieldBonus"];
                            tiers.Add(tier);
                        }
                        var coreMod = new JObject { ["type"] = "Core", ["tiers"] = tiers };
                        foreach (var k in new[] { "burnSurplusIntoShield", "shieldPerItem", "shieldValueByType", "baseMaxShield" })
                            if (b[k] != null) coreMod[k] = b[k].DeepClone();
                        mods.Add(coreMod);
                        break;
                    }
                    case "Nest": mods.Add(new JObject { ["type"] = "Nest" }); break;
                    case "Tower":
                    {
                        string fm = (string)b["fireMode"];
                        if (string.IsNullOrEmpty(fm)) throw new InvalidOperationException($"'{b["id"]}': 타워는 fireMode(Projectile|Hitscan|Aura|Trigger|None)가 필요합니다");
                        JObject Source()
                        {
                            if (Arr(b["ammoFilter"]).Count > 0) return Ammo();
                            if (Arr(b["attackEffects"]).Count == 0) throw new InvalidOperationException($"'{b["id"]}': ammoFilter도 attackEffects도 없습니다 — 무엇으로 쏘는지 적으세요");
                            return new JObject { ["type"] = "FixedAmmo", ["effects"] = Uses(b["attackEffects"]) };
                        }
                        switch (fm)
                        {
                            case "None": mods.Add(new JObject { ["type"] = "Blocker" }); break;
                            case "Trigger":
                                mods.Add(new JObject { ["type"] = "Trigger", ["radius"] = b["range"] ?? 2.0, ["once"] = b["triggerOnce"] ?? true, ["cooldown"] = b["triggerCooldown"] ?? 1.0 });
                                mods.Add(Source());
                                break;
                            case "Aura":
                            {
                                float fr = (float?)b["fireRate"] ?? 0f;
                                mods.Add(new JObject { ["type"] = "AuraEmitter", ["radius"] = b["range"] ?? 5.0, ["interval"] = fr > 0f ? 1.0 / fr : 1.0 });
                                mods.Add(Source());
                                break;
                            }
                            case "Projectile":
                            case "Hitscan":
                                double Or(string key, double dflt) { var v = (double?)b[key]; return v.HasValue && v.Value >= 0 ? v.Value : dflt; }
                                mods.Add(new JObject { ["type"] = "Turret", ["range"] = Or("range", 8.0), ["minRange"] = Or("minRange", 0.0), ["fireRate"] = Or("fireRate", 1.0),
                                                       ["turnSpeed"] = Or("turnSpeed", 180.0), ["aimTolerance"] = Or("aimTolerance", 3.0),
                                                       ["preferHighArc"] = b["preferHighArc"] ?? false, ["muzzleHeight"] = Or("muzzleHeight", 1.2),
                                                       ["aimHeight"] = Or("aimHeight", 0.0), ["hitscan"] = fm == "Hitscan" });
                                mods.Add(Source());
                                break;
                            default: throw new InvalidOperationException($"'{b["id"]}': 알 수 없는 fireMode '{fm}'");
                        }
                        break;
                    }
                    case "DronePort":
                        mods.Add(new JObject { ["type"] = "DronePort", ["carryCapacity"] = b["carryCapacity"] ?? 10, ["droneRange"] = b["droneRange"] ?? 20.0, ["travelSpeed"] = b["travelSpeed"] ?? 5.0 });
                        break;
                    case "Tree": case "Storage": break;
                    default: throw new InvalidOperationException("unknown building kind " + kind);
                }
                if (Arr(b["drops"]).Count > 0 || ((inSlots > 0 || outSlots > 0) && (bool?)b["noLoot"] != true))
                {
                    var loot = new JObject { ["type"] = "Loot" };
                    if (Arr(b["drops"]).Count > 0) loot["drops"] = Amounts(b["drops"]);
                    mods.Add(loot);
                }
                AppendExtra(mods, b);
                o["modules"] = mods;
                var view = new JObject();
                var bIcon = IconRef(b); if (bIcon != null) view["icon"] = bIcon;
                MergeView(view, b);
                if (b["models"] is JArray packModels && packModels.Count > 0) view["model"] = PackModels(packModels, (string)b["id"]);
                if (b["modelsCurveL"] is JArray curveL && curveL.Count > 0) view["modelCurveL"] = PackModels(curveL, (string)b["id"]);
                if (b["modelsCurveR"] is JArray curveR && curveR.Count > 0) view["modelCurveR"] = PackModels(curveR, (string)b["id"]);
                if (view.Count > 0) o["view"] = view;
                entities[KeyOf((string)b["id"])] = o;
            }

            foreach (var m in Arr(d["monsters"]))
            {
                var o = Head(m);
                o["faction"] = (string)m["faction"] ?? "Monster";
                var brain = new JObject { ["type"] = "MonsterBrain" };
                // 두뇌 키는 정의 클래스(MonsterBrainModuleDef)의 JsonProperty 이름에서 — 손으로 적은 목록은 필드를 더할 때마다
                // 빠뜨려 편집기 저장에서 값이 조용히 사라졌다(corpseSeconds, 2026-09-04)
                foreach (var k in MonsterBrainKeys)
                    if (m[k] != null) brain[k] = m[k];
                var mods = new JArray
                {
                    new JObject { ["type"] = "Health", ["maxHp"] = m["maxHp"] },
                    new JObject { ["type"] = "Effects" },
                    new JObject { ["type"] = "Movement", ["moveSpeed"] = m["moveSpeed"], ["rotateSpeed"] = m["rotateSpeed"], ["crowdRadius"] = m["crowdRadius"],
                                  ["knockbackDamping"] = m["knockbackDamping"], ["stickToGround"] = m["stickToGround"] },
                    new JObject { ["type"] = "Attack", ["range"] = m["attackRange"], ["cooldown"] = m["attackCooldown"], ["effects"] = Uses(m["attackEffects"]) },
                    brain,
                };
                AppendExtra(mods, m);
                o["modules"] = mods;
                var mview = new JObject();
                MergeView(mview, m);
                if (m["models"] is JArray monsterModels && monsterModels.Count > 0) mview["model"] = PackModels(monsterModels, (string)m["id"]);
                o["view"] = mview;
                entities[KeyOf((string)m["id"])] = o;
            }

            if (d["player"] is JObject playerJson)
            {
                var o = new JObject { ["displayName"] = playerJson["displayName"] ?? "플레이어", ["faction"] = "Player" };
                var mods = new JArray
                {
                    new JObject { ["type"] = "Health", ["maxHp"] = playerJson["maxHp"] ?? 300 },
                    new JObject { ["type"] = "Effects" },
                    new JObject { ["type"] = "Inventory", ["main"] = playerJson["main"] ?? 25, ["hotbar"] = playerJson["hotbar"] ?? 7 },
                    new JObject { ["type"] = "Crafter", ["manual"] = true, ["speed"] = 1.0, ["recipes"] = new JArray() },
                    new JObject { ["type"] = "Weapon" },
                };
                AppendExtra(mods, playerJson);
                o["modules"] = mods;
                var pview = new JObject { ["type"] = "Player" };
                MergeView(pview, playerJson);
                o["view"] = pview;
                entities["player"] = o;
            }

            JObject waveRule = null;
            if (d["wave"] is JObject wr)
            {
                waveRule = new JObject
                {
                    ["basePoints"] = wr["basePoints"] ?? 0.0, ["dayPoints"] = wr["dayPoints"] ?? 40.0, ["gatePoints"] = wr["gatePoints"] ?? 80.0,
                    ["stimulusAmplitude"] = wr["stimulusAmplitude"] ?? 2.0, ["stimulusExponent"] = wr["stimulusExponent"] ?? 4.0, ["stimulusLinear"] = wr["stimulusLinear"] ?? 0.1,
                    ["stimulusBuffs"] = new JArray(Arr(wr["stimulusBuffs"]).Select(b => new JObject
                    {
                        ["effect"] = NewId((string)b["effect"]), ["base"] = b["baseValue"] ?? 1.0, ["perStimulus"] = b["perStimulus"] ?? 0.0,
                        ["min"] = b["min"] ?? 0.05, ["max"] = b["max"] ?? 10.0,
                    })),
                    ["nestsPerNightMin"] = wr["nestsPerNightMin"] ?? 1, ["nestsPerNightMax"] = wr["nestsPerNightMax"] ?? 0,
                    ["targetNightLength"] = wr["targetNightLength"] ?? 60.0, ["burstsPerNight"] = wr["burstsPerNight"] ?? 4, ["burstSpread"] = wr["burstSpread"] ?? 2.0,
                    ["roster"] = new JArray(Arr(wr["roster"]).Select(r => new JObject
                    {
                        ["monster"] = NewId((string)r["monster"]), ["cost"] = r["cost"] ?? 10.0, ["weight"] = r["weight"] ?? 1.0,
                        ["minDay"] = r["minDay"] ?? 1, ["minGate"] = r["minGate"] ?? 0,
                    })),
                };
                if (wr["trickle"] is JObject tr && !string.IsNullOrEmpty((string)tr["monster"]))
                    waveRule["trickle"] = new JObject { ["monster"] = NewId((string)tr["monster"]), ["group"] = tr["group"] ?? 3, ["interval"] = tr["interval"] ?? 20.0, ["untilKilledFraction"] = tr["untilKilledFraction"] ?? 0.9 };
                if (Arr(wr["roster"]).Count == 0) throw new InvalidOperationException("wave: roster가 비었습니다 — 무엇을 스폰할지 없다");
            }

            foreach (var g in Arr(d["guns"]))
            {
                var o = Remap(g) as JObject;
                if (o?["view"] is JObject gv && gv["model"] != null) gv["model"] = PackModels(gv["model"], (string)g["id"]);
                guns[KeyOf((string)g["id"])] = GroupGun(o);
            }
            foreach (var t in Arr(d["tutorial"])) tutorial[KeyOf((string)t["id"])] = Remap(t);
            foreach (var snd in Arr(d["sounds"]))
            {
                var o = new JObject();
                if (!string.IsNullOrEmpty((string)snd["displayName"])) o["displayName"] = snd["displayName"];
                var clips = new JArray();
                foreach (var c in Arr(snd["clips"]))
                {
                    string file = c is JObject co ? (string)co["clip"] : (string)c;
                    if (string.IsNullOrEmpty(file)) continue;
                    clips.Add(file);
                }
                if (clips.Count == 0) throw new InvalidOperationException($"sounds/{snd["id"]}: clips가 비었습니다");
                o["view"] = new JObject { ["clips"] = clips };
                sounds[KeyOf((string)snd["id"])] = o;
            }
            void CheckView(string owner, JToken view)
            {
                if (view is not JObject v) return;
                string type = (string)v["type"];
                if (string.IsNullOrEmpty(type)) return;
                if (!Data.ViewSchema.Types.TryGetValue(type, out var allowed))
                    throw new InvalidOperationException($"{owner}: 모르는 view.type '{type}' (허용: {string.Join(", ", Data.ViewSchema.Types.Keys)})");
                if (v["sfx"] is JObject sfx)
                    foreach (var p in sfx.Properties())
                    {
                        if (Array.IndexOf(allowed, p.Name) < 0) throw new InvalidOperationException($"{owner}: view.sfx '{p.Name}'는 {type}에 없는 자리 (허용: {string.Join(", ", allowed)})");
                        string sid = (string)p.Value["sound"];
                        if (string.IsNullOrEmpty(sid) || sounds[sid.Split('/').Last()] == null) throw new InvalidOperationException($"{owner}: view.sfx '{p.Name}'의 소리 '{sid}'가 sounds에 없습니다");
                    }
            }
            foreach (var p in entities.Properties()) CheckView("entities/" + p.Name, p.Value["view"]);
            foreach (var p in guns.Properties()) CheckView("guns/" + p.Name, p.Value["view"]);
            foreach (var p in items.Properties()) CheckView("items/" + p.Name, p.Value["view"]);
            JObject sfxRoot = null;
            if (d["sfx"] is JObject sfxIn)
            {
                sfxRoot = (JObject)Remap(sfxIn);
                foreach (var p in sfxRoot.Properties())
                {
                    string sid = (string)p.Value["sound"];
                    if (string.IsNullOrEmpty(sid) || sounds[sid.Split('/').Last()] == null) throw new InvalidOperationException($"sfx/{p.Name}: 소리 '{sid}'가 sounds에 없습니다");
                }
            }

            var outRoot = new JObject
            {
                ["format"] = 2, ["pack"] = Pack, ["items"] = items, ["recipes"] = recipes, ["effects"] = effects,
                ["entities"] = entities, ["guns"] = guns, ["tutorial"] = tutorial, ["sounds"] = sounds, ["materials"] = materials,
            };
            if (waveRule != null) outRoot["wave"] = waveRule;
            if (sfxRoot != null) outRoot["sfx"] = sfxRoot;
            if (d["dayCycle"] is JObject dc)
                outRoot["dayCycle"] = new JObject { ["dayDuration"] = dc["dayDuration"] ?? 360.0, ["nightDuration"] = dc["nightDuration"] ?? 10.0 };
            return outRoot;
        }

        /// <summary>v1 평면 총 수치 → GunDef 묶음(fire·ammo·aim·recoil·spread·swing). 기본값과 같은 키·빈 묶음은 적지 않는다.</summary>
        static JObject GroupGun(JObject flat)
        {
            if (flat == null) return null;
            var o = new JObject();
            foreach (var p in flat.Properties())
                if (p.Name is "displayName" or "description") o[p.Name] = p.Value;

            static float F(JObject s, string k, float def) => (float?)s[k] ?? def;
            static bool B(JObject s, string k) => (bool?)s[k] ?? false;
            static bool Zero(JToken t) => t is JArray a ? a.All(x => (float?)x == 0f) : t == null;
            static void Put(JObject g, string k, JToken v, JToken def) { if (v != null && !JToken.DeepEquals(v, def)) g[k] = v; }
            static void Add(JObject o, string k, JObject g) { if (g.Count > 0) o[k] = g; }

            var fire = new JObject();
            Put(fire, "mode", flat["fireMode"], "Projectile");
            Put(fire, "interval", flat["fireRate"], 0.2f);
            Put(fire, "range", flat["range"], 100f);
            int pellets = (int?)flat["pellets"] ?? 1;
            if (pellets > 1) fire["pellets"] = pellets;
            if (B(flat, "isAutomatic")) fire["automatic"] = true;
            if (((float?)flat["damageMultiplier"] ?? -1f) >= 0f) Put(fire, "damageMultiplier", flat["damageMultiplier"], 1f);
            Add(o, "fire", fire);

            var ammo = new JObject();
            bool unlimited = B(flat, "unlimitedAmmo");
            if (unlimited) ammo["unlimited"] = true;
            else { if (((int?)flat["magSize"] ?? 0) > 0) Put(ammo, "magSize", flat["magSize"], 30); if (((float?)flat["reloadTime"] ?? 0f) > 0f) Put(ammo, "reloadTime", flat["reloadTime"], 1.5f); }
            if (flat["ammoFilter"] is JArray af && af.Count > 0) ammo["filter"] = af;
            Add(o, "ammo", ammo);

            var aim = new JObject();
            bool block = B(flat, "blockAim");
            if (block) aim["block"] = true;
            else if (((float?)flat["zoomMultiplier"] ?? 0f) > 0f) Put(aim, "zoom", flat["zoomMultiplier"], 1.3f);
            Add(o, "aim", aim);

            var recoil = new JObject();
            foreach (var (k, src) in new[] { ("x", "xRecoil"), ("y", "yRecoil"), ("z", "zRecoil"), ("kickbackZ", "visualKickbackZ") })
                if (((float?)flat[src] ?? -1f) > 0f) recoil[k] = flat[src];
            if (!Zero(flat["visualKickbackRot"])) recoil["kickbackRot"] = flat["visualKickbackRot"];
            Add(o, "recoil", recoil);

            if (F(flat, "baseSpread", 0f) > 0f || F(flat, "maxSpread", 0f) > 0f)
            {
                var spread = new JObject();
                foreach (var (k, src) in new[] { ("base", "baseSpread"), ("max", "maxSpread"), ("perShot", "spreadIncreasePerShot"), ("recovery", "spreadRecoveryRate") })
                    if (((float?)flat[src] ?? -1f) > 0f) spread[k] = flat[src];
                Add(o, "spread", spread);
            }

            if (F(flat, "swingTime", -1f) > 0f)
            {
                var swing = new JObject { ["time"] = flat["swingTime"] };
                if (F(flat, "swingWindup", -1f) >= 0f) swing["windup"] = flat["swingWindup"];
                if (B(flat, "swingAlternate")) swing["alternate"] = true;
                if (flat["swingRotation"] != null) swing["rotation"] = flat["swingRotation"];
                if (flat["swingPosition"] != null) swing["position"] = flat["swingPosition"];
                o["swing"] = swing;
            }

            if (flat["view"] != null) o["view"] = flat["view"];
            return o;
        }

        // ═════════════════════════════════════════════════════════
        //  pack → v1 (역변환, 무손실)
        // ═════════════════════════════════════════════════════════

        public static JObject ToV1(JObject pack)
        {
            var d = new JObject();
            JArray Arr2(JToken t) => t as JArray ?? new JArray();
            JObject Obj(JToken t) => t as JObject;

            // 모듈 표 — 정의 하나의 modules를 종류별로 꺼내 쓰고, 안 쓴 것은 extraModules로
            List<JObject> Modules(JObject e) => Arr2(e["modules"]).OfType<JObject>().ToList();
            JObject Take(List<JObject> mods, string type)
            {
                var m = mods.FirstOrDefault(x => (string)x["type"] == type);
                if (m != null) mods.Remove(m);
                return m;
            }
            void PutExtra(JObject v1, List<JObject> rest)
            {
                if (rest.Count > 0) v1["extraModules"] = new JArray(rest.Select(x => x.DeepClone()));
            }
            void Head(JObject v1, string id, JObject e)
            {
                v1["id"] = id;
                v1["displayName"] = (string)e["displayName"] ?? "";
                if (e["description"] != null) v1["description"] = e["description"];
            }
            // view: icon → icon/iconFile, model → models(별도 필드), 나머지 키 → view 객체(그대로)
            JObject SplitView(JObject e, JObject v1, string modelsKey, bool takeIcon)
            {
                var view = Obj(e["view"]);
                if (view == null) return null;
                var rest = new JObject();
                foreach (var p in view.Properties())
                {
                    if (takeIcon && p.Name == "icon" && p.Value is JObject ic) { v1["icon"] = ic["frame"] ?? ""; v1["iconFile"] = ic["file"] ?? ""; continue; }
                    if (modelsKey != null && p.Name == "model") { v1[modelsKey] = p.Value.DeepClone(); continue; }
                    if (modelsKey != null && p.Name == "modelCurveL") { v1["modelsCurveL"] = p.Value.DeepClone(); continue; }
                    if (modelsKey != null && p.Name == "modelCurveR") { v1["modelsCurveR"] = p.Value.DeepClone(); continue; }
                    rest[p.Name] = p.Value.DeepClone();
                }
                return rest;
            }

            var entitiesIn = Obj(pack["entities"]) ?? new JObject();
            // 광맥 — <item>_deposit 엔티티는 Ore 아이템에서 다시 만들어지므로 v1엔 없다. 채굴 시간은 아이템으로, 공용 뷰는 root.deposit으로
            var depositIntervals = new Dictionary<string, JToken>();
            JObject depositCommon = null;
            var monstersIn = new List<(string key, JObject e)>();
            var buildingsIn = new List<(string key, JObject e)>();
            JObject playerIn = null;
            foreach (var p in entitiesIn.Properties())
            {
                var e = (JObject)p.Value;
                var mods = Modules(e);
                if (p.Name == "player") { playerIn = e; continue; }
                var dep = mods.FirstOrDefault(m => (string)m["type"] == "ResourceDeposit");
                if (dep != null)
                {
                    depositIntervals[(string)dep["resource"]] = dep["extractInterval"];
                    if (depositCommon == null && e["view"] is JObject dv)
                    {
                        depositCommon = new JObject();
                        var view = new JObject();
                        foreach (var vp in dv.Properties())
                        {
                            if (vp.Name == "type") continue;
                            if (vp.Name == "model") { depositCommon["models"] = vp.Value.DeepClone(); continue; }
                            view[vp.Name] = vp.Value.DeepClone();
                        }
                        if (view.Count > 0) depositCommon["view"] = view;
                    }
                    continue;
                }
                if (mods.Any(m => (string)m["type"] == "MonsterBrain")) monstersIn.Add((p.Name, e));
                else buildingsIn.Add((p.Name, e));
            }

            // items
            var items = new JArray();
            foreach (var p in Obj(pack["items"])?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var e = (JObject)p.Value;
                var v1 = new JObject();
                string id = IdOf("item", p.Name);
                Head(v1, id, e);
                v1["type"] = e["type"]; v1["line"] = e["line"]; v1["maxStack"] = e["maxStack"] ?? 50;
                if ((bool?)e["hideFromMenu"] == true) v1["hideFromMenu"] = true;
                var rest = SplitView(e, v1, null, true);
                if (rest != null)
                {
                    foreach (var nk in new[] { "bullet", "muzzleFlash", "hitEffect" })
                        if (rest[nk] != null) { v1[nk] = rest[nk]; rest.Remove(nk); }
                    if (rest.Count > 0) v1["view"] = rest;
                }
                var mods = Modules(e);
                var ammo = Take(mods, "Ammo");
                if (ammo != null)
                {
                    foreach (var k in new[] { "speed", "gravity", "explosionRadius", "lifetime", "pierce" }) v1[k] = ammo[k] ?? (k == "pierce" ? (JToken)0 : 0f);
                    v1["attackEffects"] = ammo["effects"]?.DeepClone() ?? new JArray();
                }
                var weapon = Take(mods, "Weapon");
                if (weapon != null) v1["gun"] = weapon["gun"];
                if ((string)e["type"] == "Ore" && depositIntervals.TryGetValue(id, out var interval)) v1["extractInterval"] = interval;
                PutExtra(v1, mods);
                items.Add(v1);
            }
            d["items"] = items;

            var recipes = new JArray();
            foreach (var p in Obj(pack["recipes"])?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var e = (JObject)p.Value;
                var v1 = new JObject();
                Head(v1, IdOf("recipe", p.Name), e);
                v1["tier"] = e["tier"] ?? 1; v1["craftTime"] = e["seconds"] ?? 1f;
                v1["inputs"] = e["inputs"]?.DeepClone() ?? new JArray(); v1["outputs"] = e["outputs"]?.DeepClone() ?? new JArray();
                recipes.Add(v1);
            }
            d["recipes"] = recipes;

            var effects = new JArray();
            foreach (var p in Obj(pack["effects"])?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var e = (JObject)p.Value;
                var v1 = new JObject();
                Head(v1, IdOf("effect", p.Name), e);
                v1["kind"] = e["type"];
                if (e["duration"] != null) v1["duration"] = e["duration"];
                if (e["tickInterval"] != null) v1["tickInterval"] = e["tickInterval"];
                if (e["stacking"] != null) v1["stacking"] = e["stacking"];
                if (e["knockbackMode"] != null) v1["knockbackMode"] = e["knockbackMode"];
                if (e["affects"] != null) v1["affects"] = e["affects"].DeepClone();
                effects.Add(v1);
            }
            d["effects"] = effects;

            var buildings = new JArray();
            foreach (var (key, e) in buildingsIn)
            {
                var v1 = new JObject();
                string id = IdOf("entity", key);
                Head(v1, id, e);
                if (e["faction"] != null) v1["faction"] = e["faction"];
                var mods = Modules(e);
                var building = Take(mods, "Building");
                if (building != null)
                {
                    v1["size"] = building["size"]?.DeepClone() ?? new JObject { ["x"] = 1, ["y"] = 1 };
                    if (building["category"] != null) v1["category"] = building["category"];
                    v1["hideFromBuildMenu"] = (bool?)building["placeable"] == false;
                    v1["isDemolishable"] = (bool?)building["isDemolishable"] ?? true;
                    v1["isAttackable"] = (bool?)building["isAttackable"] ?? true;
                    v1["walkable"] = (bool?)building["walkable"] ?? false;
                    v1["requiredCoreTier"] = building["requiredCoreTier"] ?? 0;
                    v1["threatSeedCost"] = building["threatSeedCost"] ?? 0;
                    v1["menuOrder"] = building["menuOrder"] ?? 0;
                    v1["buildCost"] = building["cost"]?.DeepClone() ?? new JArray();
                }
                var health = Take(mods, "Health");
                if (health != null) v1["maxHp"] = health["maxHp"];
                if (Take(mods, "Effects") == null) v1["noEffects"] = true;
                var ports = Take(mods, "Ports");
                v1["ports"] = ports?["ports"]?.DeepClone() ?? new JArray();
                var inv = Take(mods, "Inventory");
                if (inv != null) { v1["inputSlots"] = inv["input"] ?? 0; v1["outputSlots"] = inv["output"] ?? 0; v1["bufferStackCap"] = inv["stackCap"] ?? 0; }
                string kind = null;
                JObject m;
                if ((m = Take(mods, "Conveyor")) != null) { kind = "Belt"; v1["speedTilesPerSec"] = m["speedTilesPerSec"] ?? 1f; }
                else if ((m = Take(mods, "Extractor")) != null) { kind = "Miner"; v1["speedMultiplier"] = m["speedMultiplier"] ?? 1f; }
                else if ((m = Take(mods, "Crafter")) != null) { kind = "Assembler"; v1["speedMultiplier"] = m["speed"] ?? 1f; v1["availableRecipes"] = m["recipes"]?.DeepClone() ?? new JArray(); }
                else if ((m = Take(mods, "Router")) != null) kind = (string)m["mode"] == "merge" ? "Merger" : "Splitter";
                else if ((m = Take(mods, "Core")) != null)
                {
                    kind = "Core"; v1["tiers"] = m["tiers"]?.DeepClone() ?? new JArray();
                    foreach (var k in new[] { "burnSurplusIntoShield", "shieldPerItem", "shieldValueByType", "baseMaxShield" }) if (m[k] != null) v1[k] = m[k].DeepClone();
                }
                else if (Take(mods, "Nest") != null) kind = "Nest";
                else if ((m = Take(mods, "DronePort")) != null) { kind = "DronePort"; v1["carryCapacity"] = m["carryCapacity"]; v1["droneRange"] = m["droneRange"]; v1["travelSpeed"] = m["travelSpeed"]; }
                else if ((m = Take(mods, "Turret")) != null)
                {
                    kind = "Tower"; v1["fireMode"] = (bool?)m["hitscan"] == true ? "Hitscan" : "Projectile";
                    foreach (var k in new[] { "range", "minRange", "fireRate", "turnSpeed", "aimTolerance", "muzzleHeight", "aimHeight" }) if (m[k] != null) v1[k] = m[k];
                    v1["preferHighArc"] = m["preferHighArc"] ?? false;
                }
                else if ((m = Take(mods, "Trigger")) != null) { kind = "Tower"; v1["fireMode"] = "Trigger"; v1["range"] = m["radius"]; if (m["once"] != null) v1["triggerOnce"] = m["once"]; if (m["cooldown"] != null) v1["triggerCooldown"] = m["cooldown"]; }
                else if ((m = Take(mods, "AuraEmitter")) != null) { kind = "Tower"; v1["fireMode"] = "Aura"; v1["range"] = m["radius"]; float iv = (float?)m["interval"] ?? 1f; v1["fireRate"] = iv > 0f ? 1f / iv : 1f; }
                else if (Take(mods, "Blocker") != null) { kind = "Tower"; v1["fireMode"] = "None"; }
                else if (inv != null) kind = "Storage";
                else if ((string)e["faction"] == "Neutral") kind = "Tree";
                else kind = "Storage";
                v1["kind"] = kind;
                var ammoC = Take(mods, "AmmoConsumer");
                if (ammoC != null) { v1["ammoFilter"] = ammoC["ammoFilter"]?.DeepClone() ?? new JArray(); v1["damageMultiplier"] = ammoC["damageMultiplier"] ?? 1f; }
                var fixedAmmo = Take(mods, "FixedAmmo");
                if (fixedAmmo != null) v1["attackEffects"] = fixedAmmo["effects"]?.DeepClone() ?? new JArray();
                var loot = Take(mods, "Loot");
                if (loot != null) { if (loot["drops"] != null) v1["drops"] = loot["drops"].DeepClone(); }
                else if (inv != null) v1["noLoot"] = true;   // 그릇이 있는데 Loot이 없다 — 되붙이지 않게 표시
                PutExtra(v1, mods);
                var rest = SplitView(e, v1, "models", true);
                if (rest != null && rest.Count > 0) v1["view"] = rest;
                buildings.Add(v1);
            }
            d["buildings"] = buildings;
            if (depositCommon != null) d["deposit"] = depositCommon;

            var monsters = new JArray();
            foreach (var (key, e) in monstersIn)
            {
                var v1 = new JObject();
                Head(v1, IdOf("entity", key), e);
                if (e["faction"] != null && (string)e["faction"] != "Monster") v1["faction"] = e["faction"];
                var mods = Modules(e);
                var health = Take(mods, "Health"); if (health != null) v1["maxHp"] = health["maxHp"];
                Take(mods, "Effects");
                var mv = Take(mods, "Movement");
                if (mv != null) foreach (var (k, src) in new[] { ("moveSpeed", "moveSpeed"), ("rotateSpeed", "rotateSpeed"), ("crowdRadius", "crowdRadius"), ("knockbackDamping", "knockbackDamping"), ("stickToGround", "stickToGround") }) if (mv[src] != null) v1[k] = mv[src];
                var atk = Take(mods, "Attack");
                if (atk != null) { v1["attackRange"] = atk["range"]; v1["attackCooldown"] = atk["cooldown"]; v1["attackEffects"] = atk["effects"]?.DeepClone() ?? new JArray(); }
                var brain = Take(mods, "MonsterBrain");
                if (brain != null) foreach (var bp in brain.Properties()) if (bp.Name != "type") v1[bp.Name] = bp.Value.DeepClone();
                PutExtra(v1, mods);
                var rest = SplitView(e, v1, "models", false);
                if (rest != null && rest.Count > 0) v1["view"] = rest;
                monsters.Add(v1);
            }
            d["monsters"] = monsters;

            if (playerIn != null)
            {
                var v1 = new JObject { ["displayName"] = playerIn["displayName"] ?? "플레이어" };
                var mods = Modules(playerIn);
                var health = Take(mods, "Health"); if (health != null) v1["maxHp"] = health["maxHp"];
                Take(mods, "Effects");
                var inv = Take(mods, "Inventory"); if (inv != null) { v1["main"] = inv["main"] ?? 25; v1["hotbar"] = inv["hotbar"] ?? 7; }
                Take(mods, "Crafter"); Take(mods, "Weapon");
                PutExtra(v1, mods);
                var rest = SplitView(playerIn, v1, null, false);
                if (rest != null) { rest.Remove("type"); if (rest.Count > 0) v1["view"] = rest; }
                d["player"] = v1;
            }

            var guns = new JArray();
            foreach (var p in Obj(pack["guns"])?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var v1 = UngroupGun((JObject)p.Value);
                v1.AddFirst(new JProperty("id", IdOf("gun", p.Name)));
                guns.Add(v1);
            }
            d["guns"] = guns;

            var tutorial = new JArray();
            foreach (var p in Obj(pack["tutorial"])?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var v1 = (JObject)p.Value.DeepClone();
                v1.AddFirst(new JProperty("id", IdOf("tutorial", p.Name)));
                tutorial.Add(v1);
            }
            d["tutorial"] = tutorial;

            var sounds = new JArray();
            foreach (var p in Obj(pack["sounds"])?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var e = (JObject)p.Value;
                var v1 = new JObject { ["id"] = IdOf("sound", p.Name) };
                if (e["displayName"] != null) v1["displayName"] = e["displayName"];
                var clips = new JArray();
                foreach (var c in Arr2(Obj(e["view"])?["clips"])) clips.Add(new JObject { ["clip"] = c });
                v1["clips"] = clips;
                sounds.Add(v1);
            }
            d["sounds"] = sounds;

            var materials = new JArray();
            foreach (var p in Obj(pack["materials"])?.Properties() ?? Enumerable.Empty<JProperty>())
            {
                var e = (JObject)p.Value;
                var v = Obj(e["view"]) ?? new JObject();
                var v1 = new JObject { ["id"] = IdOf("material", p.Name) };
                if (e["displayName"] != null) v1["displayName"] = e["displayName"];
                v1["shader"] = v["shader"];
                var texs = new JArray();
                foreach (var t in Obj(v["textures"])?.Properties() ?? Enumerable.Empty<JProperty>())
                    texs.Add(new JObject { ["name"] = t.Name, ["file"] = t.Value["file"], ["linear"] = (bool?)t.Value["linear"] ?? false });
                if (texs.Count > 0) v1["textures"] = texs;
                JArray Vec4s(JToken o) { var a = new JArray(); foreach (var c in Obj(o)?.Properties() ?? Enumerable.Empty<JProperty>()) { var arr = Arr2(c.Value); a.Add(new JObject { ["name"] = c.Name, ["r"] = arr.Count > 0 ? arr[0] : 0f, ["g"] = arr.Count > 1 ? arr[1] : 0f, ["b"] = arr.Count > 2 ? arr[2] : 0f, ["a"] = arr.Count > 3 ? arr[3] : 1f }); } return a; }
                if (v["colors"] != null) v1["colors"] = Vec4s(v["colors"]);
                if (v["vectors"] != null) v1["vectors"] = Vec4s(v["vectors"]);
                if (v["floats"] is JObject fl) { var a = new JArray(); foreach (var f in fl.Properties()) a.Add(new JObject { ["name"] = f.Name, ["value"] = f.Value }); v1["floats"] = a; }
                if (v["keywords"] != null) v1["keywords"] = v["keywords"].DeepClone();
                v1["renderQueue"] = v["renderQueue"] ?? -1;
                if (v["tags"] is JObject tg) { var a = new JArray(); foreach (var t in tg.Properties()) a.Add(new JObject { ["name"] = t.Name, ["value"] = t.Value }); v1["tags"] = a; }
                materials.Add(v1);
            }
            d["materials"] = materials;

            if (pack["sfx"] is JObject sfx) d["sfx"] = sfx.DeepClone();
            if (pack["wave"] is JObject wr)
            {
                var v1 = (JObject)wr.DeepClone();
                if (v1["stimulusBuffs"] is JArray sb)
                    foreach (var b in sb.OfType<JObject>()) if (b["base"] != null) { b["baseValue"] = b["base"]; b.Remove("base"); }
                d["wave"] = v1;
            }
            if (pack["dayCycle"] is JObject dc) d["dayCycle"] = dc.DeepClone();
            return d;
        }

        /// <summary>GunDef 묶음 → v1 평면(음수 = 생략 규약).</summary>
        static JObject UngroupGun(JObject g)
        {
            var o = new JObject();
            if (g["displayName"] != null) o["displayName"] = g["displayName"];
            if (g["description"] != null) o["description"] = g["description"];
            JObject Sec(string k) => g[k] as JObject ?? new JObject();
            var fire = Sec("fire"); var ammo = Sec("ammo"); var aim = Sec("aim"); var recoil = Sec("recoil"); var spread = Sec("spread"); var swing = Sec("swing");
            o["isAutomatic"] = (bool?)fire["automatic"] ?? false;
            o["unlimitedAmmo"] = (bool?)ammo["unlimited"] ?? false;
            o["blockAim"] = (bool?)aim["block"] ?? false;
            o["fireMode"] = fire["mode"] ?? "Projectile";
            o["fireRate"] = fire["interval"] ?? 0.2f;
            o["range"] = fire["range"] ?? 100f;
            o["reloadTime"] = ammo["reloadTime"] ?? 0f;
            o["zoomMultiplier"] = aim["zoom"] ?? 0f;
            o["magSize"] = ammo["magSize"] ?? 0;
            o["pellets"] = fire["pellets"] ?? 0;
            o["ammoFilter"] = ammo["filter"]?.DeepClone() ?? new JArray();
            o["damageMultiplier"] = fire["damageMultiplier"] ?? -1f;
            o["xRecoil"] = recoil["x"] ?? -1f; o["yRecoil"] = recoil["y"] ?? -1f; o["zRecoil"] = recoil["z"] ?? -1f;
            o["visualKickbackZ"] = recoil["kickbackZ"] ?? -1f;
            if (recoil["kickbackRot"] != null) o["visualKickbackRot"] = recoil["kickbackRot"].DeepClone();
            o["baseSpread"] = spread["base"] ?? -1f; o["maxSpread"] = spread["max"] ?? -1f;
            o["spreadIncreasePerShot"] = spread["perShot"] ?? -1f; o["spreadRecoveryRate"] = spread["recovery"] ?? -1f;
            o["swingTime"] = swing["time"] ?? -1f; o["swingWindup"] = swing["windup"] ?? -1f;
            if (swing["rotation"] != null) o["swingRotation"] = swing["rotation"].DeepClone();
            if (swing["position"] != null) o["swingPosition"] = swing["position"].DeepClone();
            o["swingAlternate"] = (bool?)swing["alternate"] ?? false;
            if (g["view"] != null) o["view"] = g["view"].DeepClone();
            return o;
        }

        /// <summary>built 의 객체 키 순서를 reference(디스크 문서)의 같은 경로에 맞춘다 — 저장 diff 에 값 변화만 남도록.
        /// 새 키는 뒤에 붙고, 배열은 같은 자리끼리 맞춘다(폼 탭이 순서를 바꿨으면 그 순서가 이긴다).</summary>
        internal static JToken OrderLike(JToken built, JToken reference)
        {
            if (built is JObject bo && reference is JObject ro)
            {
                var res = new JObject();
                foreach (var p in ro.Properties()) if (bo.TryGetValue(p.Name, out var v)) res[p.Name] = OrderLike(v, p.Value);
                foreach (var p in bo.Properties()) if (!res.ContainsKey(p.Name)) res[p.Name] = p.Value;
                return res;
            }
            if (built is JArray ba && reference is JArray ra)
            {
                for (int i = 0; i < ba.Count && i < ra.Count; i++) { var o = OrderLike(ba[i], ra[i]); if (!ReferenceEquals(o, ba[i])) ba[i] = o; }
                return ba;
            }
            return built;
        }

        // ═════════════════════════════════════════════════════════
        //  왕복 검증 — 키 순서를 무시하고 값이 같은가
        // ═════════════════════════════════════════════════════════

        public static bool SemanticEquals(JToken a, JToken b, string path, List<string> diffs, int max = 30)
        {
            if (diffs.Count >= max) return false;
            if (a is JObject oa && b is JObject ob)
            {
                var keys = oa.Properties().Select(p => p.Name).Union(ob.Properties().Select(p => p.Name)).ToList();
                bool ok = true;
                foreach (var k in keys)
                {
                    if (oa[k] == null) { diffs.Add($"{path}/{k}: 원본에만 없음(변환 결과에 추가됨) = {Short(ob[k])}"); ok = false; continue; }
                    if (ob[k] == null) { diffs.Add($"{path}/{k}: 변환 결과에 없음(원본 = {Short(oa[k])})"); ok = false; continue; }
                    if (!SemanticEquals(oa[k], ob[k], path + "/" + k, diffs, max)) ok = false;
                }
                return ok;
            }
            if (a is JArray aa && b is JArray ab)
            {
                if (aa.Count != ab.Count) { diffs.Add($"{path}: 배열 길이 {aa.Count} vs {ab.Count}"); return false; }
                bool ok = true;
                for (int i = 0; i < aa.Count; i++) if (!SemanticEquals(aa[i], ab[i], $"{path}[{i}]", diffs, max)) ok = false;
                return ok;
            }
            if (a is JValue va && b is JValue vb)
            {
                bool num = (va.Type is JTokenType.Integer or JTokenType.Float) && (vb.Type is JTokenType.Integer or JTokenType.Float);
                bool eq = num ? Math.Abs((double)va - (double)vb) < 1e-6 : JToken.DeepEquals(va, vb);
                if (!eq) diffs.Add($"{path}: {Short(a)} vs {Short(b)}");
                return eq;
            }
            diffs.Add($"{path}: 종류 다름 {a?.Type} vs {b?.Type}");
            return false;
        }

        static string Short(JToken t) { var s = t?.ToString(Formatting.None) ?? "null"; return s.Length > 60 ? s.Substring(0, 60) + "…" : s; }
    }
}
#endif
