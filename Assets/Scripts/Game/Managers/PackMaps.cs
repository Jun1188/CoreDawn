using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 팩 맵 로더 — <c>packs/&lt;pack&gt;/maps/&lt;이름&gt;.json</c> → <see cref="MapDef"/>.
    /// 경로를 아는 유일한 곳(PackLoader와 같은 성격). 동기 로드(맵 하나 ~수십 KB)·캐시.
    /// json의 id는 전부 <b>팩 id로 이미 풀린 값</b>이다(내보내기가 v1을 풀어서 쓴다) — 여기서는 검증만, 폴백 없음.
    /// </summary>
    public static class PackMaps
    {
        static readonly Dictionary<string, MapDef> cache = new Dictionary<string, MapDef>(StringComparer.Ordinal);

        /// <summary>Clear마다 오른다 — 실패를 캐시한 쪽(World.Map)이 "다시 읽어야 하나"를 이 값으로 판단한다.</summary>
        public static int Version { get; private set; }

        public static string PathOf(string pack, string mapId)
        {
            string name = mapId.Substring(mapId.LastIndexOf('/') + 1);
            return Path.Combine(Application.streamingAssetsPath, "packs", pack, "maps", name + ".json");
        }

        /// <summary>맵을 읽는다(캐시). 없거나 깨졌으면 오류 로그 + null — 호출자가 소리 낸다.</summary>
        public static MapDef Of(string pack, string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) { Debug.LogError("[PackMaps] 맵 id가 비어 있습니다."); return null; }
            string key = pack + "|" + mapId;
            if (cache.TryGetValue(key, out var cached)) return cached;

            string path = PathOf(pack, mapId);
            if (!File.Exists(path)) { Debug.LogError($"[PackMaps] 맵 파일이 없습니다: {path} (id {mapId})"); return null; }

            MapDef map;
            try { map = Parse(JObject.Parse(File.ReadAllText(path)), mapId); }
            catch (Exception e) { Debug.LogError($"[PackMaps] '{mapId}' 파싱 실패: {e.Message}"); return null; }
            if (map != null) cache[key] = map;
            return map;
        }

        public static void Clear() { cache.Clear(); Version++; }

        static MapDef Parse(JObject j, string expectedId)
        {
            var m = new MapDef
            {
                Id = (string)j["id"],
                displayName = (string)j["displayName"] ?? "",
                description = (string)j["description"] ?? "",
                width = (int?)j["width"] ?? 0,
                height = (int?)j["height"] ?? 0,
                cellSize = (float?)j["cellSize"] ?? 0f,
                core = Cell(j["core"]),
            };
            if (m.Id != expectedId) { Debug.LogError($"[PackMaps] 파일의 id '{m.Id}'가 요청한 '{expectedId}'와 다릅니다."); return null; }
            if (m.width <= 0 || m.height <= 0 || !(m.cellSize > 0f)) { Debug.LogError($"[PackMaps] '{m.Id}': width/height/cellSize가 유효하지 않습니다."); return null; }

            m.tiles = new byte[m.width * m.height];   // 기본 0 = 지면
            if (j["tiles"] is JArray rows)
                for (int y = 0; y < rows.Count && y < m.height; y++)
                {
                    string row = (string)rows[y] ?? "";
                    for (int x = 0; x < row.Length && x < m.width; x++)
                    {
                        char c = row[x];
                        if (c < '0' || c > '2') { Debug.LogError($"[PackMaps] '{m.Id}' ({x},{y}): 알 수 없는 타일 '{c}' (0=지면 1=강 2=절벽)"); continue; }
                        m.tiles[y * m.width + x] = (byte)(c - '0');
                    }
                }

            if (j["nodes"] is JArray nodes)
            {
                var list = new List<ResourceNodeSpec>(nodes.Count);
                foreach (var n in nodes)
                    list.Add(new ResourceNodeSpec { itemId = (string)n["item"], cell = Cell(n) });
                m.nodes = list.ToArray();
            }
            if (j["nests"] is JArray nests)
            {
                var list = new List<NestSpec>(nests.Count);
                foreach (var n in nests)
                {
                    var spec = new NestSpec
                    {
                        cell = Cell(n),
                        warningRange = (float?)n["warningRange"] ?? 0f,
                        triggerRange = (float?)n["triggerRange"] ?? 0f,
                        defenseSpawnAmount = (int?)n["defenseSpawnAmount"] ?? 0,
                        defenseSpawnCooldown = (float?)n["defenseSpawnCooldown"] ?? 0f,
                        engageMinRange = (float?)n["engageMinRange"] ?? 0f,
                        engageMaxRange = (float?)n["engageMaxRange"] ?? 0f,
                        chaseRange = (float?)n["chaseRange"] ?? 0f,
                        leashRange = (float?)n["leashRange"] ?? 0f,
                        engageDayOnly = (bool?)n["engageDayOnly"] ?? false,
                        defender = (string)n["defender"],
                        spawnPoints = Array.Empty<SpawnPointSpec>(),
                    };
                    if (n["spawnPoints"] is JArray sps)
                    {
                        var pts = new List<SpawnPointSpec>(sps.Count);
                        foreach (var p in sps) pts.Add(new SpawnPointSpec { offset = Cell(p), boss = (string)p["boss"] });
                        spec.spawnPoints = pts.ToArray();
                    }
                    list.Add(spec);
                }
                m.nests = list.ToArray();
            }
            m.nightSpawnPoints = Cells(j["nightSpawnPoints"]);
            m.trees = Cells(j["trees"]);
            if (j["startItems"] is JArray sis)
            {
                var list = new List<StartItemSpec>(sis.Count);
                foreach (var s in sis) list.Add(new StartItemSpec { itemId = (string)s["item"], amount = (int?)s["amount"] ?? 0 });
                m.startItems = list.ToArray();
            }
            return m;
        }

        static Vector2Int Cell(JToken t) => t == null ? Vector2Int.zero : new Vector2Int((int?)t["x"] ?? 0, (int?)t["y"] ?? 0);

        static Vector2Int[] Cells(JToken t)
        {
            if (!(t is JArray arr)) return Array.Empty<Vector2Int>();
            var list = new List<Vector2Int>(arr.Count);
            foreach (var c in arr) list.Add(Cell(c));
            return list.ToArray();
        }
    }
}
