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
    /// <summary>
    /// 편집 형식(v1 GameData.json) → 게임·모드 형식(v2 팩 data.json) 내보내기. tools/migrate-gamedata-v1-to-v2.py의 C# 판 —
    /// 팀원 환경에 python이 없어 에디터 안에서 돈다. 저장 버튼이 SO 임포트와 함께 호출한다(임포터를 둘로 쪼갠 것의 심 쪽 절반).
    ///
    /// v2 규칙: id는 저장하지 않고 키에서 파생(pack:section/key, 소문자 snake), 엔티티는 kind 대신 modules[],
    /// 뷰 참조(모델·프리팹·아이콘)는 view 섹션으로, guns·tutorial은 원본 그대로(id만 치환).
    /// </summary>
    public static class GameDataExporterV2
    {
        public const string Pack = "coredawn";
        public static string SourcePath => $"{GameDataImporter.ImportFolder}/GameData.json";
        public static string PackFolder => $"Assets/StreamingAssets/packs/{Pack}";
        public static string OutputPath => $"{PackFolder}/data.json";
        public const string IdMapPath = "tools/id-migration-v1-v2.json";

        static readonly Dictionary<string, string> SectionOf = new()
        {
            ["Item"] = "item", ["Recipe"] = "recipe", ["Effect"] = "effect", ["Building"] = "entity", ["Monster"] = "entity",
            ["Wave"] = "wave", ["Gun"] = "gun", ["Tutorial"] = "tutorial",
        };

        [MenuItem("Tools/Factory/Export pack data.json (v2)")]
        public static void ExportMenu()
        {
            var report = Export();
            Debug.Log(report);
        }

        public static string Export()
        {
            var d = JObject.Parse(File.ReadAllText(SourcePath));
            var idmap = new Dictionary<string, string>();

            string Snake(string name)
            {
                name = Regex.Replace(name, "^Recipe_", "");
                var s = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "_");
                s = Regex.Replace(s, "(?<=[A-Z])(?=[A-Z][a-z])", "_");
                return s.ToLowerInvariant();
            }
            string NewId(string old)
            {
                if (string.IsNullOrEmpty(old)) return old;
                if (idmap.TryGetValue(old, out var n)) return n;
                int i = old.IndexOf(':');
                if (i < 0 || !SectionOf.TryGetValue(old.Substring(0, i), out var section)) return old;
                n = $"{Pack}:{section}/{Snake(old.Substring(i + 1))}";
                idmap[old] = n;
                return n;
            }
            string KeyOf(string old) => NewId(old).Split('/', 2)[1];
            JArray Arr(JToken t) => t as JArray ?? new JArray();

            foreach (var sec in new[] { "items", "recipes", "effects", "buildings", "monsters", "waves", "guns", "tutorial" })
                foreach (var e in Arr(d[sec])) NewId((string)e["id"]);
            var dup = idmap.Values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dup.Count > 0) throw new InvalidOperationException("id 충돌: " + string.Join(", ", dup));

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
            JObject View(JToken e, params string[] keys)
            {
                var v = new JObject();
                foreach (var k in keys) if (e[k] != null && !string.IsNullOrEmpty((string)e[k])) v[k] = e[k];
                return v;
            }

            var items = new JObject(); var recipes = new JObject(); var effects = new JObject();
            var entities = new JObject(); var waves = new JObject(); var guns = new JObject(); var tutorial = new JObject();

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
                o["modules"] = mods;
                o["view"] = new JObject { ["icon"] = it["icon"], ["iconGuid"] = it["iconGuid"] };
                items[KeyOf((string)it["id"])] = o;
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
                string name = ((string)b["id"]).Split(':', 2)[1];
                var o = Head(b);
                o["faction"] = kind == "Nest" ? "Monster" : kind == "Tree" ? "Neutral" : "Player";
                var mods = new JArray();
                var building = new JObject { ["type"] = "Building", ["size"] = b["size"]?.DeepClone(), ["category"] = b["category"] };
                if ((bool?)b["hideFromBuildMenu"] == true) building["placeable"] = false;
                if ((bool?)b["isDemolishable"] == false) building["isDemolishable"] = false;
                if ((bool?)b["isAttackable"] == false) building["isAttackable"] = false;
                foreach (var k in new[] { "requiredCoreTier", "threatSeedCost", "menuOrder" })
                    if (((int?)b[k] ?? 0) != 0) building[k] = b[k];
                if (Arr(b["buildCost"]).Count > 0) building["cost"] = Amounts(b["buildCost"]);
                mods.Add(building);
                mods.Add(new JObject { ["type"] = "Health", ["maxHp"] = b["maxHp"] });
                mods.Add(new JObject { ["type"] = "Effects" });
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
                        // 보호막 값은 v1에 아직 없다(옛 에셋 값이 정의의 기본값) — v1에 적히면 그대로 실린다
                        foreach (var k in new[] { "burnSurplusIntoShield", "shieldPerItem", "shieldValueByType", "baseMaxShield" })
                            if (b[k] != null) coreMod[k] = b[k].DeepClone();
                        mods.Add(coreMod);
                        break;
                    }
                    case "Nest": mods.Add(new JObject { ["type"] = "NestSpawner" }); break;
                    case "Tower":
                        if (name == "Fence") mods.Add(new JObject { ["type"] = "Blocker" });
                        else if (name == "Mine")
                        {
                            mods.Add(new JObject { ["type"] = "Trigger", ["radius"] = b["range"] ?? 2.0, ["once"] = true, ["effects"] = new JArray() });
                            mods.Add(Ammo());
                        }
                        else if (name == "SlowFieldTower")
                        {
                            float fr = (float?)b["fireRate"] ?? 0f;
                            mods.Add(new JObject { ["type"] = "AuraEmitter", ["radius"] = b["range"] ?? 5.0, ["interval"] = fr > 0f ? 1.0 / fr : 1.0, ["effects"] = new JArray() });
                            mods.Add(Ammo());
                        }
                        else
                        {
                            mods.Add(new JObject { ["type"] = "TowerBrain", ["range"] = b["range"] ?? 8.0, ["minRange"] = b["minRange"] ?? 0.0, ["fireRate"] = b["fireRate"] ?? 1.0,
                                                   ["turnSpeed"] = b["turnSpeed"] ?? 180.0, ["aimTolerance"] = b["aimTolerance"] ?? 5.0,
                                                   ["preferHighArc"] = b["preferHighArc"] ?? false, ["muzzleHeight"] = b["muzzleHeight"] ?? 1.0 });
                            mods.Add(Ammo());
                        }
                        break;
                    case "DronePort":
                        mods.Add(new JObject { ["type"] = "DronePort", ["carryCapacity"] = b["carryCapacity"] ?? 10, ["droneRange"] = b["droneRange"] ?? 20.0, ["travelSpeed"] = b["travelSpeed"] ?? 5.0 });
                        break;
                    case "Tree": case "Storage": break;
                    default: throw new InvalidOperationException("unknown building kind " + kind);
                }
                o["modules"] = mods;
                var view = View(b, "model", "modelGuid", "modelCurveL", "modelCurveLGuid", "modelCurveR", "modelCurveRGuid");
                if (view.Count > 0) o["view"] = view;
                entities[KeyOf((string)b["id"])] = o;
            }

            foreach (var m in Arr(d["monsters"]))
            {
                var o = Head(m);
                o["faction"] = "Monster";
                var brain = new JObject { ["type"] = "MonsterBrain" };
                foreach (var k in new[] { "maxPatience", "patienceRadius", "outsidePatienceDrain", "rangedPokePatienceDrain", "patienceRecoverRate", "absoluteLeashMultiplier", "returnRegenPerSecond", "returnTimeout" })
                    if (m[k] != null) brain[k] = m[k];
                o["modules"] = new JArray
                {
                    new JObject { ["type"] = "Health", ["maxHp"] = m["maxHp"] },
                    new JObject { ["type"] = "Effects" },
                    new JObject { ["type"] = "Movement", ["moveSpeed"] = m["moveSpeed"], ["rotateSpeed"] = m["rotateSpeed"], ["crowdRadius"] = m["crowdRadius"],
                                  ["knockbackDamping"] = m["knockbackDamping"], ["stickToGround"] = m["stickToGround"] },
                    new JObject { ["type"] = "Attack", ["range"] = m["attackRange"], ["cooldown"] = m["attackCooldown"], ["effects"] = Uses(m["attackEffects"]) },
                    brain,
                };
                o["view"] = new JObject { ["prefab"] = m["prefab"], ["prefabGuid"] = m["prefabGuid"] };
                entities[KeyOf((string)m["id"])] = o;
            }

            // 플레이어 — v1의 player 블록(HP·가방·핫바). SO가 없는 유일한 엔티티: 심이 이 정의로 조립한다
            if (d["player"] is JObject playerJson)
            {
                var o = new JObject { ["displayName"] = playerJson["displayName"] ?? "플레이어", ["faction"] = "Player" };
                o["modules"] = new JArray
                {
                    new JObject { ["type"] = "Health", ["maxHp"] = playerJson["maxHp"] ?? 300 },
                    new JObject { ["type"] = "Effects" },
                    new JObject { ["type"] = "Inventory", ["main"] = playerJson["main"] ?? 18, ["hotbar"] = playerJson["hotbar"] ?? 7 },
                    new JObject { ["type"] = "Crafter", ["manual"] = true, ["speed"] = 1.0, ["recipes"] = new JArray() },
                };
                entities["player"] = o;
            }

            foreach (var w in Arr(d["waves"]))
            {
                var o = Head(w);
                o["day"] = w["day"]; o["requiredCoreTier"] = w["requiredCoreTier"]; o["baseAmount"] = w["baseAmount"]; o["maxAliveAmount"] = w["maxAliveAmount"];
                o["spawnInterval"] = w["spawnInterval"]; o["monster"] = NewId((string)w["monster"]); o["buffs"] = Uses(w["buffs"]);
                waves[KeyOf((string)w["id"])] = o;
            }

            JToken Remap(JToken x)
            {
                switch (x)
                {
                    case JValue v when v.Type == JTokenType.String && idmap.TryGetValue((string)v, out var mapped): return mapped;
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
            foreach (var g in Arr(d["guns"])) guns[KeyOf((string)g["id"])] = Remap(g);
            foreach (var t in Arr(d["tutorial"])) tutorial[KeyOf((string)t["id"])] = Remap(t);

            var outRoot = new JObject
            {
                ["format"] = 2, ["pack"] = Pack, ["items"] = items, ["recipes"] = recipes, ["effects"] = effects,
                ["entities"] = entities, ["waves"] = waves, ["guns"] = guns, ["tutorial"] = tutorial,
            };
            Directory.CreateDirectory(PackFolder);
            File.WriteAllText(OutputPath, outRoot.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
            var map = new JObject();
            foreach (var kv in idmap.OrderBy(k => k.Key, StringComparer.Ordinal)) map[kv.Key] = kv.Value;
            File.WriteAllText(IdMapPath, map.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
            AssetDatabase.ImportAsset(OutputPath);

            // 내보낸 결과를 심 로더로 곧장 검증 — 편집기에서 깨진 참조를 저장 시점에 잡는다
            var db = Sim.SimDatabase.Load(File.ReadAllText(OutputPath), Pack, strict: false);
            string report = $"[v2 export] {OutputPath}: entities {entities.Count} · items {items.Count} · recipes {recipes.Count} · effects {effects.Count} · waves {waves.Count} · id {idmap.Count}";
            if (db.Errors.Count > 0)
            {
                report += $"\n  로드 오류 {db.Errors.Count}건:\n  " + string.Join("\n  ", db.Errors);
                Debug.LogError(report);
            }
            return report;
        }
    }
}
#endif
