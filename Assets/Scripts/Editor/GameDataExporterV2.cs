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
        public static string SourcePath => $"{GameDataJson.ImportFolder}/GameData.json";
        public static string PackFolder => $"Assets/StreamingAssets/packs/{Pack}";
        public static string OutputPath => $"{PackFolder}/data.json";
        public const string IdMapPath = "tools/id-migration-v1-v2.json";

        static readonly Dictionary<string, string> SectionOf = new()
        {
            ["Item"] = "item", ["Recipe"] = "recipe", ["Effect"] = "effect", ["Building"] = "entity", ["Monster"] = "entity",
            ["Gun"] = "gun", ["Tutorial"] = "tutorial", ["Sound"] = "sound", ["Material"] = "material",
        };

        /// <summary>임포트된 텍스처를 png 바이트로 — 원본이 LoadImage가 못 읽는 형식(tif·psd·tga)일 때. 노멀맵은 linear로 읽어 그대로 굽는다(런타임 UnpackNormal은 RG/AG 둘 다 푼다).</summary>
        static byte[] TexturePng(string assetPath, bool linear)
        {
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (src == null) throw new InvalidOperationException($"텍스처를 읽지 못했습니다: {assetPath}");
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, linear);
                tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0); tex.Apply();
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                return png;
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
        }

        /// <summary>옛 v1 id("Item:IronOre") → 팩 id("coredawn:item/iron_ore") — 에디터 도구(맵 임포터·이관)가 쓴다. 이미 팩 id면 그대로.</summary>
        public static string PackIdOf(string v1Id)
        {
            if (string.IsNullOrEmpty(v1Id) || v1Id.Contains("/")) return v1Id;
            int i = v1Id.IndexOf(':');
            if (i < 0 || !SectionOf.TryGetValue(v1Id.Substring(0, i), out var section)) return v1Id;
            var name = Regex.Replace(v1Id.Substring(i + 1), "^Recipe_", "");
            var s = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "_");
            s = Regex.Replace(s, "(?<=[A-Z])(?=[A-Z][a-z])", "_");
            return $"{Pack}:{section}/{s.ToLowerInvariant()}";
        }

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

            foreach (var sec in new[] { "items", "recipes", "effects", "buildings", "monsters", "guns", "tutorial", "sounds", "materials" })
                foreach (var e in Arr(d[sec])) NewId((string)e["id"]);

            // 아이콘(5a-4c) — 스프라이트 시트/낱장 png를 팩 textures/로 복사하고 좌표표(<파일>.json: pixelsPerUnit, frames{이름: x,y,w,h,px,py})를 쓴다.
            // v2 view.icon = {file, frame}. 같은 시트는 한 번만.
            var iconFiles = new Dictionary<string, (string file, JObject frames)>();
            JObject IconRef(JToken e, string owner)
            {
                string guid = (string)e["iconGuid"], spriteName = (string)e["icon"];
                if (string.IsNullOrEmpty(guid)) return null;
                if (!iconFiles.TryGetValue(guid, out var sheet))
                {
                    string src = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(src)) throw new InvalidOperationException($"{owner}: iconGuid {guid}의 에셋이 없습니다");
                    string ext = Path.GetExtension(src).ToLowerInvariant();
                    Directory.CreateDirectory($"{PackFolder}/textures");
                    string file = "textures/" + Snake(Path.GetFileNameWithoutExtension(src)) + ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") ? ext : ".png");
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") File.Copy(src, $"{PackFolder}/{file}", true);
                    else File.WriteAllBytes($"{PackFolder}/{file}", TexturePng(src, false));
                    var frames = new JObject(); float ppu = 100f;
                    var all = AssetDatabase.LoadAllAssetRepresentationsAtPath(src).OfType<Sprite>().ToList();
                    if (all.Count == 0) { var single = AssetDatabase.LoadAssetAtPath<Sprite>(src); if (single != null) all.Add(single); }
                    if (all.Count == 0) throw new InvalidOperationException($"{owner}: '{src}'에 스프라이트가 없습니다");
                    foreach (var sp in all)
                    {
                        frames[sp.name] = new JObject { ["x"] = sp.rect.x, ["y"] = sp.rect.y, ["w"] = sp.rect.width, ["h"] = sp.rect.height, ["px"] = sp.pivot.x, ["py"] = sp.pivot.y };
                        ppu = sp.pixelsPerUnit;
                    }
                    File.WriteAllText($"{PackFolder}/{file}.json", new JObject { ["pixelsPerUnit"] = ppu, ["frames"] = frames }.ToString(Newtonsoft.Json.Formatting.Indented) + "\n");
                    sheet = (file, frames); iconFiles[guid] = sheet;
                }
                string frame = !string.IsNullOrEmpty(spriteName) && sheet.frames[spriteName] != null ? spriteName
                             : sheet.frames.Count == 1 ? ((JProperty)sheet.frames.First).Name : null;
                if (frame == null) throw new InvalidOperationException($"{owner}: 아이콘 '{spriteName}'이 시트 '{sheet.file}'에 없습니다");
                return new JObject { ["file"] = sheet.file, ["frame"] = frame };
            }
            // 내장 연출(파티클·탄 프리팹)은 팩 파일이 아니다 — 이름만(Resources/Builtin/Effects/<이름>). guid로 이름을 확인한다
            string EffectName(JToken e, string nameKey, string guidKey, string owner)
            {
                string guid = (string)e[guidKey], name = (string)e[nameKey];
                if (string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(name)) return null;
                if (!string.IsNullOrEmpty(guid))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) throw new InvalidOperationException($"{owner}: {guidKey} {guid}의 프리팹이 없습니다");
                    if (!path.StartsWith("Assets/Resources/Builtin/Effects/")) throw new InvalidOperationException($"{owner}: {nameKey} '{path}'는 내장 연출 폴더(Resources/Builtin/Effects)에 있어야 합니다");
                    name = Path.GetFileNameWithoutExtension(path);
                }
                return name;
            }

            var materials = new JObject();
            // 팩 모델 배열 v1 [{file, materials:["Material:…"]}] → v2 [{file, materials:[팩 id]}]. 재질 존재를 검사한다
            JArray PackModels(JArray packModels, string owner)
            {
                var arr = new JArray();
                foreach (var pm in packModels)
                {
                    if (!(pm is JObject po) || string.IsNullOrEmpty((string)po["file"])) throw new InvalidOperationException($"{owner}: models 항목은 {{file, materials}} 객체여야 합니다");
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

            // 재질(5a-4c) — 셰이더는 내장, 값·텍스처는 팩. 텍스처 파일을 팩 textures/로 복사한다(png·jpg는 그대로, 그 밖은 png로 변환).
            foreach (var mat in Arr(d["materials"]))
            {
                string mid = (string)mat["id"];
                if (string.IsNullOrEmpty((string)mat["shader"])) throw new InvalidOperationException($"materials/{mid}: shader가 비었습니다");
                var v = new JObject { ["shader"] = mat["shader"] };
                var texs = new JObject();
                foreach (var t in Arr(mat["textures"]))
                {
                    string src = AssetDatabase.GUIDToAssetPath((string)t["textureGuid"]);
                    if (string.IsNullOrEmpty(src)) throw new InvalidOperationException($"materials/{mid}: 텍스처 '{t["name"]}'({t["texture"]})의 guid가 죽었습니다");
                    string ext = Path.GetExtension(src).ToLowerInvariant();
                    bool linear = (bool?)t["linear"] ?? false;
                    string file;
                    Directory.CreateDirectory($"{PackFolder}/textures");
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    {
                        file = "textures/" + Snake(Path.GetFileNameWithoutExtension(src)) + ext;
                        File.Copy(src, $"{PackFolder}/{file}", true);
                    }
                    else
                    {
                        // tif·psd·tga 등 — 임포트된 텍스처를 그대로 png로 굽는다(LoadImage가 읽는 형식만 팩에 둔다)
                        file = "textures/" + Snake(Path.GetFileNameWithoutExtension(src)) + ".png";
                        File.WriteAllBytes($"{PackFolder}/{file}", TexturePng(src, linear));
                    }
                    texs[(string)t["name"]] = new JObject { ["file"] = file, ["linear"] = linear };
                    texs[(string)t["name"]] = new JObject { ["file"] = file, ["linear"] = (bool?)t["linear"] ?? false };
                }
                if (texs.Count > 0) v["textures"] = texs;
                JObject Vec4s(JToken arr) { var o = new JObject(); foreach (var c in Arr(arr)) o[(string)c["name"]] = new JArray((float)c["r"], (float)c["g"], (float)c["b"], (float)c["a"]); return o; }
                if (Arr(mat["colors"]).Count > 0) v["colors"] = Vec4s(mat["colors"]);
                if (Arr(mat["vectors"]).Count > 0) v["vectors"] = Vec4s(mat["vectors"]);
                if (Arr(mat["floats"]).Count > 0) { var o = new JObject(); foreach (var f in Arr(mat["floats"])) o[(string)f["name"]] = (float)f["value"]; v["floats"] = o; }
                if (Arr(mat["keywords"]).Count > 0) v["keywords"] = mat["keywords"].DeepClone();
                if ((int?)mat["renderQueue"] is int rq && rq >= 0) v["renderQueue"] = rq;
                if (Arr(mat["tags"]).Count > 0) { var o = new JObject(); foreach (var t in Arr(mat["tags"])) o[(string)t["name"]] = t["value"]; v["tags"] = o; }
                var mo = new JObject();
                if (!string.IsNullOrEmpty((string)mat["displayName"])) mo["displayName"] = mat["displayName"];
                mo["view"] = v;
                materials[KeyOf(mid)] = mo;
            }
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
            var entities = new JObject(); var guns = new JObject(); var tutorial = new JObject(); var sounds = new JObject();

            // v1 항목의 view{type, sfx…} 객체를 v2 view 블록에 얹는다(평평한 model/prefab/icon 키는 View()가 이미 옮겼다). 소리 id는 팩 id로.
            void MergeView(JObject view, JToken e)
            {
                if (e["view"] is not JObject v) return;
                foreach (var p in v.Properties()) view[p.Name] = Remap(p.Value);
            }

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
                var itemView = new JObject();
                var iconRef = IconRef(it, (string)it["id"]);
                if (iconRef != null) itemView["icon"] = iconRef;
                foreach (var (nk, gk) in new[] { ("bullet", "bulletGuid"), ("muzzleFlash", "muzzleFlashGuid"), ("hitEffect", "hitEffectGuid") })
                {
                    string fx = EffectName(it, nk, gk, (string)it["id"]);
                    if (fx != null) itemView[nk] = fx;
                }
                MergeView(itemView, it);
                if (itemView.Count > 0) o["view"] = itemView;
                items[KeyOf((string)it["id"])] = o;

                // 광맥 — Ore 아이템마다 하나, entities/<item>_deposit. 맵은 칸과 자원만 적고 채굴 시간은 원광이 갖는다.
                // 매장량 없음(바닥나지 않음), 부서지지 않으므로 Health 없음. Ore↔extractInterval 짝은 여기서 검사한다(구 임포터의 몫).
                if ((string)it["type"] == "Ore")
                {
                    float interval = (float?)it["extractInterval"] ?? -1f;
                    if (!(interval > 0f)) throw new InvalidOperationException($"Ore 아이템 '{it["id"]}'에 extractInterval(>0)이 없습니다");
                    string key = KeyOf((string)it["id"]) + "_deposit";
                    if (entities[key] != null) throw new InvalidOperationException($"entities/{key} id 충돌");
                    entities[key] = new JObject
                    {
                        ["displayName"] = (string)it["displayName"] + " 광맥",
                        ["faction"] = "Neutral",
                        ["view"] = new JObject { ["type"] = "Deposit" },
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
                string name = ((string)b["id"]).Split(':', 2)[1];
                var o = Head(b);
                o["faction"] = kind == "Nest" ? "Monster" : kind == "Tree" ? "Neutral" : "Player";
                var mods = new JArray();
                var building = new JObject { ["type"] = "Building", ["size"] = b["size"]?.DeepClone(), ["category"] = b["category"] };
                if ((bool?)b["hideFromBuildMenu"] == true) building["placeable"] = false;
                if ((bool?)b["isDemolishable"] == false) building["isDemolishable"] = false;
                if ((bool?)b["isAttackable"] == false) building["isAttackable"] = false;
                if ((bool?)b["walkable"] == true) building["walkable"] = true;
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
                    case "Nest": mods.Add(new JObject { ["type"] = "Nest" }); break;   // 복구 일수는 정의 기본값(2·3) — 맵이 둥지마다 덮을 수 있다
                    case "Tower":
                    {
                        // 전달 방식(fireMode)이 모듈을 고른다 — 건물 이름으로 갈라 두면 새 타워마다 exporter를 고쳐야 한다
                        string fm = (string)b["fireMode"];
                        if (string.IsNullOrEmpty(fm)) throw new InvalidOperationException($"'{b["id"]}': 타워는 fireMode(Projectile|Hitscan|Aura|Trigger|None)가 필요합니다");
                        // 탄의 출처 — 받는 탄이 있으면 탄창(AmmoConsumer), 없으면 자기 효과(FixedAmmo). 둘 다 없으면 무엇으로 쏘는지 모른다
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
                                mods.Add(new JObject { ["type"] = "Trigger", ["radius"] = b["range"] ?? 2.0, ["once"] = true, ["cooldown"] = 1.0 });
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
                                // v1의 음수는 "생략(SO 기본값)" 신호다 — 팩에는 SO가 없으므로 그 기본값(TowerDataSO)을 그대로 적는다
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
                // 사망 드롭: 정의된 목록(둥지의 괴수핵) 또는 그릇 내용물(버퍼가 있는 건물은 기본으로 떨군다)
                if (Arr(b["drops"]).Count > 0 || inSlots > 0 || outSlots > 0)
                {
                    var loot = new JObject { ["type"] = "Loot" };
                    if (Arr(b["drops"]).Count > 0) loot["drops"] = Amounts(b["drops"]);
                    mods.Add(loot);
                }
                o["modules"] = mods;
                var view = View(b, "model", "modelGuid", "modelCurveL", "modelCurveLGuid", "modelCurveR", "modelCurveRGuid");
                var bIcon = IconRef(b, (string)b["id"]); if (bIcon != null) view["icon"] = bIcon;
                MergeView(view, b);
                if (b["models"] is JArray packModels && packModels.Count > 0)   // 팩 모델 배열이 있으면 그것이 정본 — guid 참조를 지운다
                {
                    view["model"] = PackModels(packModels, (string)b["id"]); view.Remove("modelGuid");
                }
                if (b["modelsCurveL"] is JArray curveL && curveL.Count > 0) { view["modelCurveL"] = PackModels(curveL, (string)b["id"]); view.Remove("modelCurveLGuid"); }
                if (b["modelsCurveR"] is JArray curveR && curveR.Count > 0) { view["modelCurveR"] = PackModels(curveR, (string)b["id"]); view.Remove("modelCurveRGuid"); }
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
                var mview = View(m, "model", "modelGuid");
                MergeView(mview, m);
                if (m["models"] is JArray monsterModels && monsterModels.Count > 0) { mview["model"] = PackModels(monsterModels, (string)m["id"]); mview.Remove("modelGuid"); }
                o["view"] = mview;
                entities[KeyOf((string)m["id"])] = o;
            }


            // 플레이어 — v1의 player 블록(HP·소지품 칸 수·핫바 창). SO가 없는 유일한 엔티티: 심이 이 정의로 조립한다
            if (d["player"] is JObject playerJson)
            {
                var o = new JObject { ["displayName"] = playerJson["displayName"] ?? "플레이어", ["faction"] = "Player" };
                o["modules"] = new JArray
                {
                    new JObject { ["type"] = "Health", ["maxHp"] = playerJson["maxHp"] ?? 300 },
                    new JObject { ["type"] = "Effects" },
                    new JObject { ["type"] = "Inventory", ["main"] = playerJson["main"] ?? 25, ["hotbar"] = playerJson["hotbar"] ?? 7 },
                    new JObject { ["type"] = "Crafter", ["manual"] = true, ["speed"] = 1.0, ["recipes"] = new JArray() },
                    new JObject { ["type"] = "Weapon" },   // 무기 소지자 — 총별 탄창·재장전·연사는 심이 판정
                };
                o["view"] = new JObject { ["type"] = "Player" };
                entities["player"] = o;
            }

            // 밤 웨이브 규칙 — 하나. 점수식 계수·자극 버프·명단·진입로 무리. id는 v2로 치환
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
            foreach (var g in Arr(d["guns"]))
            {
                var o = Remap(g) as JObject;
                if (o?["view"] is JObject gv && gv["models"] is JArray gm && gm.Count > 0)   // 팩 모델 — guid 참조를 지운다
                {
                    gv["model"] = PackModels(gm, (string)g["id"]); gv.Remove("models"); gv.Remove("modelGuid");
                }
                guns[KeyOf((string)g["id"])] = o;
            }
            foreach (var t in Arr(d["tutorial"])) tutorial[KeyOf((string)t["id"])] = Remap(t);
            // 소리 — 변형 클립 묶음만(표현 전용). 볼륨·공간감은 쓰는 자리(view.sfx · sfx)의 값이다.
            foreach (var snd in Arr(d["sounds"]))
            {
                var o = new JObject();
                if (!string.IsNullOrEmpty((string)snd["displayName"])) o["displayName"] = snd["displayName"];
                var clips = new JArray();
                foreach (var c in Arr(snd["clips"]))
                {
                    string guid = (string)c["clipGuid"];
                    if (string.IsNullOrEmpty(guid)) continue;
                    string src = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(src)) throw new InvalidOperationException($"sounds/{snd["id"]}: 클립 '{c["clip"]}'의 guid가 죽었습니다");
                    string ext = Path.GetExtension(src).ToLowerInvariant();
                    if (ext != ".wav" && ext != ".ogg" && ext != ".mp3") throw new InvalidOperationException($"sounds/{snd["id"]}: 클립 '{src}'는 wav/ogg/mp3가 아닙니다");
                    string file = "sounds/" + Snake(Path.GetFileNameWithoutExtension(src)) + ext;
                    Directory.CreateDirectory($"{PackFolder}/sounds");
                    File.Copy(src, $"{PackFolder}/{file}", true);
                    clips.Add(file);
                }
                if (clips.Count == 0) throw new InvalidOperationException($"sounds/{snd["id"]}: clips가 비었습니다");
                o["view"] = new JObject { ["clips"] = clips };
                sounds[KeyOf((string)snd["id"])] = o;
            }
            // 팩 view 검증 — 뷰 종류와 소리 자리(ViewSchema 표), 소리 id 존재
            void CheckView(string owner, JToken view)
            {
                if (view is not JObject v) return;
                string type = (string)v["type"];
                if (string.IsNullOrEmpty(type)) return;   // type 없는 view(아이콘만 있는 아이템 등)는 뷰 종류가 없다
                if (!CoreDawn.Data.ViewSchema.Types.TryGetValue(type, out var allowed))
                    throw new InvalidOperationException($"{owner}: 모르는 view.type '{type}' (허용: {string.Join(", ", CoreDawn.Data.ViewSchema.Types.Keys)})");
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
            // 주야 시계 — 하나. TimeManager가 읽는다
            if (d["dayCycle"] is JObject dc)
                outRoot["dayCycle"] = new JObject { ["dayDuration"] = dc["dayDuration"] ?? 360.0, ["nightDuration"] = dc["nightDuration"] ?? 10.0 };
            Directory.CreateDirectory(PackFolder);
            File.WriteAllText(OutputPath, outRoot.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
            var map = new JObject();
            foreach (var kv in idmap.OrderBy(k => k.Key, StringComparer.Ordinal)) map[kv.Key] = kv.Value;
            File.WriteAllText(IdMapPath, map.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
            AssetDatabase.ImportAsset(OutputPath);

            // 내보낸 결과를 심 로더로 곧장 검증 — 편집기에서 깨진 참조를 저장 시점에 잡는다
            var db = Sim.SimDatabase.Load(File.ReadAllText(OutputPath), Pack, strict: false);
            string report = $"[v2 export] {OutputPath}: entities {entities.Count} · items {items.Count} · recipes {recipes.Count} · effects {effects.Count} · wave {(waveRule != null ? "rule" : "none")} · id {idmap.Count}";
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
