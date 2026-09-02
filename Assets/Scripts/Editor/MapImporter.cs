using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// MapData.json(v1 저작) → 팩 <c>maps/&lt;이름&gt;.json</c> 내보내기 (5a-4d — MapDataSO 굽기 퇴역).
    ///
    /// GameDataExporterV2와 같은 규율을 따른다:
    ///   · json이 정본 — 파일을 손으로 고치지 말고 v1을 고쳐 다시 내보낸다
    ///   · v1 id("Item:CopperOre"·"Monster:Boss")는 팩 id로 풀어서 쓴다 — 런타임(PackMaps)은 해석하지 않는다
    ///   · 파싱·해석 실패는 에러다 — 조용한 기본값 폴백은 만들지 않는다
    ///
    /// 클래스 이름·<see cref="ImportAll"/>은 유지한다 — GdMapTab(맵 편집 탭)이 이 DTO로 편집하고 저장 시 부른다.
    /// 파일을 GameData.json과 나눈 이유: 맵은 여러 개고 한 장이 수십 KB라 수명 주기가 다르다.
    /// </summary>
    public static class MapImporter
    {
        internal const string JsonPath = "Assets/Data/Import/MapData.json";   // GameDataEditorWindow도 이 경로를 쓴다
        const string OutFolder = "Assets/StreamingAssets/packs/coredawn/maps";

        // ── DTO (v1 편집 형식 — GdMapTab이 공유) ──────────────────────

        [Serializable] internal class Root : GameDataJson.JsonDtoBase { public MapDto[] maps; }

        [Serializable] internal class MapDto : GameDataJson.JsonDtoBase
        {
            public string id;            // v1 "Map:이름" — 내보낼 때 팩 id(coredawn:map/이름)로 풀린다
            public string displayName;   // 필수
            public string description;
            public int width, height;
            public float cellSize;        // 칸 한 변(m). 필수(>0)
            public CellDto core;
            public string[] tiles;       // 행마다 한 줄, 한 글자가 한 칸 (0=지면 1=강 2=절벽)
            public NodeDto[] nodes;
            public NestDto[] nests;

            /// <summary>밤 웨이브 진입로 — 둥지의 스폰 지점과 별개다(낮의 보스 자리가 밤의 대문이 되면 안 된다).</summary>
            public CellDto[] nightSpawnPoints;

            /// <summary>나무가 선 칸들 — 맵 에디터의 나무 도구가 찍고, 런타임이 그 자리에 세운다.</summary>
            public CellDto[] trees;

            /// <summary>시작 잔해 — 코어 주변에 흩뿌릴 아이템(v1 id)과 개수.</summary>
            public StartItemDto[] startItems;
        }
        [Serializable] internal class StartItemDto : GameDataJson.JsonDtoBase { public string item; public int amount; }

        [Serializable] internal class CellDto : GameDataJson.JsonDtoBase { public int x, y; }

        [Serializable] internal class NodeDto : GameDataJson.JsonDtoBase
        {
            public string item;          // 아이템 id — 수치(재생·상한·난이도)는 팩의 광맥 정의가 갖는다
            public int x, y;             // 광맥은 한 칸짜리
        }

        [Serializable] internal class NestDto : GameDataJson.JsonDtoBase
        {
            public int x, y;
            public float warningRange, triggerRange;
            public int defenseSpawnAmount;
            public float defenseSpawnCooldown;
            public SpawnDto[] spawnPoints;

            // 교전 규칙 — 생략하면 0이라 "교전 구역 없음"(둥지 기본 동작)이 된다
            public float engageMinRange, engageMaxRange, chaseRange, leashRange;
            public bool engageDayOnly;
            public string defender;      // 낮 방어 몬스터 종류. 생략 = 스포너 기본
        }

        /// <summary>스폰 자리 — boss는 이 자리에 서는 보스의 몬스터 id, 없으면 보스 없음(자리는 웨이브 출구).</summary>
        [Serializable] internal class SpawnDto : GameDataJson.JsonDtoBase { public int x, y; public string boss; }

        // ── 실행 ────────────────────────────────────────────────────

        [MenuItem("Tools/CoreDawn/Export maps to pack (5a-4d)")]
        public static void ImportAll()
        {
            if (!File.Exists(JsonPath))
            {
                Debug.LogError($"[MapImporter] {JsonPath} 가 없습니다.");
                return;
            }

            var root = JsonUtility.FromJson<Root>(File.ReadAllText(JsonPath));
            if (root?.maps == null)
            {
                Debug.LogError($"[MapImporter] {JsonPath} 파싱 실패 — maps 배열을 찾지 못했습니다.");
                return;
            }

            SimDatabase pack;
            try { pack = SimDatabase.Load(File.ReadAllText(PackLoader.PathOf(PackLoader.DefaultPack)), PackLoader.DefaultPack); }
            catch (Exception e) { Debug.LogError("[MapImporter] 팩 data.json을 읽지 못해 맵의 id를 검증할 수 없습니다 — GameData 편집기에서 먼저 저장하세요. " + e.Message); return; }

            Directory.CreateDirectory(OutFolder);

            int written = 0, errors = 0;
            foreach (var dto in root.maps)
                if (ExportMap(dto, pack, ref errors)) written++;

            AssetDatabase.Refresh();
            PackMaps.Clear();                  // 다음 조회가 새 파일을 읽게
            WorldPreviewDrawer.Invalidate();   // 미리보기가 맵을 다시 읽게만 한다 — 씬에 굳히지 않는다

            string msg = $"[MapImporter] 맵 {written}개 → {OutFolder}";
            if (errors > 0) Debug.LogError($"{msg} — 오류 {errors}건 (위 로그 확인)");
            else Debug.Log(msg);
        }

        static bool ExportMap(MapDto dto, SimDatabase pack, ref int errors)
        {
            if (dto == null || string.IsNullOrEmpty(dto.id) || string.IsNullOrEmpty(dto.displayName))
            {
                Debug.LogError("[MapImporter] id 또는 displayName이 비어 있는 맵이 있습니다.");
                errors++;
                return false;
            }
            if (dto.width <= 0 || dto.height <= 0)
            {
                Debug.LogError($"[MapImporter] '{dto.id}': width/height가 유효하지 않습니다 ({dto.width}×{dto.height}).");
                errors++;
                return false;
            }
            if (!(dto.cellSize > 0f))
            {
                Debug.LogError($"[MapImporter] '{dto.id}': cellSize(칸 한 변, m)가 없거나 0 이하입니다 — 공장·배치·길찾기가 이 값으로 격자를 잡습니다.");
                errors++;
                return false;
            }

            string v1Id = dto.id;
            string packId = GameDataExporterV2.PackIdOf(v1Id);
            if (packId == v1Id)
            {
                Debug.LogError($"[MapImporter] '{v1Id}': 맵 id는 \"Map:이름\" 형식이어야 합니다.");
                errors++;
                return false;
            }

            // 팩 파일은 런타임이 읽는 형식이다 — id를 전부 팩 id로 풀어서 쓴다(PackMaps는 해석하지 않는다)
            dto.id = packId;
            dto.description ??= "";
            ValidateTiles(dto, v1Id, ref errors);
            dto.nodes = ResolveNodes(dto, v1Id, pack, ref errors);
            dto.nests = ResolveNests(dto, v1Id, pack, ref errors);
            dto.nightSpawnPoints = Compact(dto.nightSpawnPoints);
            dto.trees = Compact(dto.trees);
            dto.startItems = ResolveStartItems(dto, v1Id, pack, ref errors);

            string file = Path.Combine(OutFolder, packId.Substring(packId.LastIndexOf('/') + 1) + ".json");
            File.WriteAllText(file, JsonConvert.SerializeObject(dto, Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            return true;
        }

        /// <summary>타일 행을 검증만 한다(형식은 그대로 문자열 행 — 런타임 PackMaps가 바이트로 굽는다). 알 수 없는 글자는 지면으로 고친다.</summary>
        static void ValidateTiles(MapDto dto, string v1Id, ref int errors)
        {
            if (dto.tiles == null) return;
            for (int y = 0; y < dto.tiles.Length; y++)
            {
                string row = dto.tiles[y] ?? "";
                char[] fixedRow = null;
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x] >= '0' && row[x] <= '2') continue;
                    Debug.LogError($"[MapImporter] '{v1Id}' ({x},{y}): 알 수 없는 타일 '{row[x]}' — 지면으로 처리합니다. 0=지면 1=강 2=절벽");
                    errors++;
                    fixedRow ??= row.ToCharArray();
                    fixedRow[x] = '0';
                }
                if (fixedRow != null) dto.tiles[y] = new string(fixedRow);
            }
        }

        static StartItemDto[] ResolveStartItems(MapDto dto, string v1Id, SimDatabase pack, ref int errors)
        {
            if (dto.startItems == null) return Array.Empty<StartItemDto>();
            var list = new List<StartItemDto>(dto.startItems.Length);
            foreach (var si in dto.startItems)
            {
                if (si == null) continue;
                string itemId = string.IsNullOrEmpty(si.item) ? null : GameDataExporterV2.PackIdOf(si.item);
                if (itemId == null || pack.Item(itemId) == null) { Debug.LogError($"[MapImporter] '{v1Id}' startItems: 아이템 '{si.item}'({itemId})이 팩에 없습니다."); errors++; continue; }
                if (si.amount <= 0) { Debug.LogError($"[MapImporter] '{v1Id}' startItems: '{si.item}'의 amount가 0 이하입니다."); errors++; continue; }
                si.item = itemId;
                list.Add(si);
            }
            return list.ToArray();
        }

        static NodeDto[] ResolveNodes(MapDto dto, string v1Id, SimDatabase pack, ref int errors)
        {
            if (dto.nodes == null) return Array.Empty<NodeDto>();
            var list = new List<NodeDto>(dto.nodes.Length);
            foreach (var n in dto.nodes)
            {
                if (n == null) continue;
                string itemId = string.IsNullOrEmpty(n.item) ? null : GameDataExporterV2.PackIdOf(n.item);
                if (itemId == null || pack.Item(itemId) == null)
                {
                    Debug.LogError($"[MapImporter] '{v1Id}' 광맥({n.x},{n.y}): 아이템 '{n.item}'({itemId})이 팩에 없습니다.");
                    errors++;
                    continue;   // 캘 것이 없는 광맥은 넣지 않는다
                }
                n.item = itemId;
                list.Add(n);
            }
            return list.ToArray();
        }

        static CellDto[] Compact(CellDto[] cells)
        {
            if (cells == null) return Array.Empty<CellDto>();
            var list = new List<CellDto>(cells.Length);
            foreach (var p in cells) if (p != null) list.Add(p);
            return list.ToArray();
        }

        /// <summary>몬스터 id(v1 "Monster:Boss") → 팩 id. 비면 null, 팩에 없으면 오류(그 자리는 보스 없이).</summary>
        static string ResolveMonster(string v1MonsterId, SimDatabase pack, string where, ref int errors)
        {
            if (string.IsNullOrEmpty(v1MonsterId)) return null;
            string id = GameDataExporterV2.PackIdOf(v1MonsterId);
            if (pack.Entity(id) == null)
            {
                Debug.LogError($"[MapImporter] {where}: 몬스터 '{v1MonsterId}'({id})가 팩에 없습니다.");
                errors++;
                return null;
            }
            return id;
        }

        static NestDto[] ResolveNests(MapDto dto, string v1Id, SimDatabase pack, ref int errors)
        {
            if (dto.nests == null) return Array.Empty<NestDto>();
            var list = new List<NestDto>(dto.nests.Length);
            foreach (var nest in dto.nests)
            {
                if (nest == null) continue;

                if (nest.spawnPoints != null)
                {
                    var points = new List<SpawnDto>(nest.spawnPoints.Length);
                    foreach (var p in nest.spawnPoints)
                    {
                        if (p == null) continue;
                        p.boss = ResolveMonster(p.boss, pack, $"'{v1Id}' 둥지({nest.x},{nest.y}) 자리({p.x},{p.y})", ref errors);
                        points.Add(p);
                    }
                    nest.spawnPoints = points.ToArray();
                }

                nest.defender = ResolveMonster(nest.defender, pack, $"'{v1Id}' 둥지({nest.x},{nest.y}) 방어자", ref errors);

                if (nest.triggerRange > nest.warningRange)
                    Debug.LogWarning($"[MapImporter] '{v1Id}' 둥지({nest.x},{nest.y}): triggerRange가 warningRange보다 큽니다 — 경고 없이 습격당합니다.");

                // 교전 구역은 안쪽부터 바깥으로 min ≤ max ≤ chase ≤ leash 여야 뜻이 통한다.
                // 어긋나면 "쫓아오다 말고 되돌아가는" 식으로 조용히 이상해지므로 여기서 잡는다.
                if (nest.engageMinRange > 0f || nest.engageMaxRange > 0f)
                {
                    if (nest.engageMaxRange < nest.engageMinRange)
                        Debug.LogWarning($"[MapImporter] '{v1Id}' 둥지({nest.x},{nest.y}): engageMaxRange가 engageMinRange보다 작습니다.");
                    if (nest.chaseRange > 0f && nest.chaseRange < nest.engageMaxRange)
                        Debug.LogWarning($"[MapImporter] '{v1Id}' 둥지({nest.x},{nest.y}): chaseRange가 engageMaxRange보다 작습니다 — 교전하자마자 추적을 포기합니다.");
                    if (nest.leashRange > 0f && nest.leashRange < nest.chaseRange)
                        Debug.LogWarning($"[MapImporter] '{v1Id}' 둥지({nest.x},{nest.y}): leashRange가 chaseRange보다 작습니다.");
                }

                list.Add(nest);
            }
            return list.ToArray();
        }
    }
}
