using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.FPS;
using CoreDawn.Sound;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// 5a-4a 일회성 — 프리팹·SoundManager에 흩어져 있던 소리 배선을 v1 GameData.json으로 거둔다:
    /// <c>sounds</c>(id → 변형 클립 묶음), 최상위 <c>sfx</c>(공용 자리 — 구 CommonSFX), 총·건물·몬스터의 <c>view{type, sfx}</c>.
    /// 출처: Player.prefab의 Gun(fireSound·reloadSound·볼륨), 건물 프리팹의 TowerVisualController(fireClips·destroyClips·starvedClip·볼륨),
    /// Resources/SoundManager.prefab의 commonSoundList. 같은 클립을 여러 자리가 써도 자리마다 제 이름의 소리로 둔다(이름이 곧 용도 — 합치면 "경고음 = 타워 굶음"처럼 뜻이 섞인다). 코드가 소리를 팩에서 읽게 되면 삭제한다.
    /// </summary>
    public static class SoundHarvester
    {
        const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
        const string SoundManagerPrefab = "Assets/Resources/SoundManager.prefab";
        const string BuildingPrefabs = "Assets/Prefabs/Buildings";

        [MenuItem("Tools/Factory/Harvest sounds into GameData.json (5a-4a)")]
        public static void Run()
        {
            string jsonPath = $"{GameDataJson.ImportFolder}/GameData.json";
            var root = JObject.Parse(File.ReadAllText(jsonPath));
            var sounds = new JArray();
            var byClipSet = new Dictionary<string, string>();   // 등록된 소리 id
            var report = new System.Text.StringBuilder();

            string Register(string wantedId, IEnumerable<AudioClip> clips)
            {
                var list = clips.Where(c => c != null).Distinct().OrderBy(c => c.name).ToList();
                if (list.Count == 0) return null;
                if (byClipSet.TryGetValue(wantedId, out var existing)) return existing;   // 같은 id는 한 번만(파괴음처럼 여러 타워가 공유)
                var arr = new JArray();
                foreach (var c in list)
                {
                    string path = AssetDatabase.GetAssetPath(c);
                    arr.Add(new JObject { ["clip"] = c.name, ["clipGuid"] = AssetDatabase.AssetPathToGUID(path) });
                }
                sounds.Add(new JObject { ["id"] = wantedId, ["clips"] = arr });
                byClipSet[wantedId] = wantedId;
                report.AppendLine($"  {wantedId}: {list.Count}개 클립");
                return wantedId;
            }
            JObject Use(string soundId, float volume, bool spatial)
                => soundId == null ? null : new JObject { ["sound"] = soundId, ["volume"] = volume, ["spatial"] = spatial };
            string Pascal(string s)
            {
                var sb = new System.Text.StringBuilder(); bool up = true;
                foreach (char ch in s) { if (ch == '_' || ch == ' ' || ch == '-') { up = true; continue; } sb.Append(up ? char.ToUpperInvariant(ch) : ch); up = false; }
                return sb.ToString();
            }

            // ── 총 — Player.prefab의 Gun 컴포넌트 ──
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            var gunsById = new Dictionary<string, Gun>();
            foreach (var g in player.GetComponentsInChildren<Gun>(true)) gunsById[g.gunId] = g;
            foreach (var g in root["guns"] as JArray ?? new JArray())
            {
                string v1Id = (string)g["id"];
                string packId = GameDataExporterV2.PackIdOf(v1Id);
                var view = new JObject { ["type"] = "Gun" };
                var sfx = new JObject();
                if (gunsById.TryGetValue(packId, out var gun))
                {
                    string bare = v1Id.Substring(v1Id.IndexOf(':') + 1);
                    var fire = Use(Register("Sound:" + bare + "Fire", new[] { gun.fireSound }), gun.fireVolume, true);
                    var reload = Use(Register("Sound:" + bare + "Reload", new[] { gun.reloadSound }), gun.reloadVolume, true);
                    if (fire != null) sfx["fire"] = fire;
                    if (reload != null) sfx["reload"] = reload;
                }
                else report.AppendLine($"  (총 {v1Id}: Player.prefab에 Gun 없음 — 소리 없이)");
                if (sfx.Count > 0) view["sfx"] = sfx;
                g["view"] = view;
            }

            // ── 건물 — 프리팹의 TowerVisualController ──
            foreach (var b in root["buildings"] as JArray ?? new JArray())
            {
                string v1Id = (string)b["id"];
                string bare = v1Id.Substring(v1Id.IndexOf(':') + 1);
                string kind = (string)b["kind"];
                string type = kind == "Tower" ? "Tower" : kind == "Nest" ? "Nest" : "Building";
                var view = new JObject { ["type"] = type };
                var sfx = new JObject();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BuildingPrefabs}/{bare}.prefab");
                var visual = prefab != null ? prefab.GetComponentInChildren<TowerVisualController>(true) : null;
                if (visual != null)
                {
                    var so = new SerializedObject(visual);
                    var fireClips = Clips(so.FindProperty("fireClips"));
                    var destroyClips = Clips(so.FindProperty("destroyClips"));
                    var starved = so.FindProperty("starvedClip").objectReferenceValue as AudioClip;
                    var fire = Use(Register("Sound:" + bare + "Fire", fireClips), so.FindProperty("fireVolume").floatValue, true);
                    var destroy = Use(Register("Sound:TowerDestroy", destroyClips), so.FindProperty("destroyVolume").floatValue, true);
                    var starvedUse = Use(Register("Sound:TowerStarved", new[] { starved }), so.FindProperty("starvedVolume").floatValue, true);
                    if (fire != null) sfx["fire"] = fire;
                    if (destroy != null) sfx["destroy"] = destroy;
                    if (starvedUse != null) sfx["starved"] = starvedUse;
                }
                if (sfx.Count > 0) view["sfx"] = sfx;
                b["view"] = view;
            }

            // ── 몬스터 ──
            foreach (var m in root["monsters"] as JArray ?? new JArray())
                m["view"] = new JObject { ["type"] = "Monster" };

            // ── 공용 — SoundManager.prefab의 commonSoundList (구 CommonSFX enum 순서: Click, Hover, Construct, Destroy, Warning, LevelUp, ItemPickup, Mine) ──
            var names = new[] { "ui_click", "ui_hover", "construct", "destroy", "warning", "level_up", "item_pickup", "mine" };
            var sm = AssetDatabase.LoadAssetAtPath<GameObject>(SoundManagerPrefab);
            var common = new JObject();
            if (sm != null)
            {
                var so = new SerializedObject(sm.GetComponent<SoundManager>());
                var list = so.FindProperty("commonSoundList");
                for (int i = 0; i < list.arraySize; i++)
                {
                    var el = list.GetArrayElementAtIndex(i);
                    int type = el.FindPropertyRelative("sfxType").enumValueIndex;
                    var clip = el.FindPropertyRelative("clip").objectReferenceValue as AudioClip;
                    if (type < 0 || type >= names.Length || clip == null) continue;
                    string id = Register("Sound:" + Pascal(names[type]), new[] { clip });
                    common[names[type]] = Use(id, 1f, false);
                }
            }
            root["sfx"] = common;
            root["sounds"] = sounds;

            File.WriteAllText(jsonPath, root.ToString(Newtonsoft.Json.Formatting.Indented) + "\n");
            AssetDatabase.ImportAsset(jsonPath);
            Debug.Log($"[SoundHarvester] sounds {sounds.Count}종, 공용 sfx {common.Count}자리 → {jsonPath}\n{report}");
        }

        static List<AudioClip> Clips(SerializedProperty arr)
        {
            var list = new List<AudioClip>();
            if (arr == null) return list;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue is AudioClip c) list.Add(c);
            return list;
        }
    }
}
