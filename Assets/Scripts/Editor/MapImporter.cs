using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Worlds;
using CoreDawn.Data;
using CoreDawn.Managers;
using CoreDawn.Sim;

namespace CoreDawn.EditorTools
{
    /// <summary>
    /// MapData.json → MapDataSO 임포터.
    ///
    /// GameDataImporter와 같은 규율을 따른다:
    ///   · json이 정본 — 에셋을 손으로 고치지 말고 json을 고쳐 다시 임포트한다
    ///   · id("Map:이름")로 기존 에셋을 찾아 <b>제자리 갱신</b> — 참조(씬·프리팹)가 끊기지 않는다
    ///   · 파싱 실패는 에러다 — 조용한 기본값 폴백은 만들지 않는다 (잘못된 맵으로 게임이 돌면 더 나쁘다)
    ///
    /// 파일을 나눈 이유: 맵은 여러 개고 한 장이 수십~수백 KB라, 규칙 데이터(GameData.json)와
    /// 수명 주기가 다르다. 맵 편집이 규칙 파일의 diff를 뒤덮지 않는다.
    /// </summary>
    public static class MapImporter
    {
        internal const string JsonPath = "Assets/Data/Import/MapData.json";   // GameDataEditorWindow도 이 경로를 쓴다
        const string MapFolder = "Assets/Data/Maps";

        // ── DTO (JsonUtility가 채운다) ──────────────────────────────

        [Serializable] internal class Root : GameDataJson.JsonDtoBase { public MapDto[] maps; }

        [Serializable] internal class MapDto : GameDataJson.JsonDtoBase
        {
            public string id;            // 필수. 예: "Map:Plains01"
            public string displayName;   // 필수
            public string description;
            public int width, height;
            public float cellSize;        // 칸 한 변(m). 필수(>0)
            public CellDto core;
            public string[] tiles;       // 행마다 한 줄, 한 글자가 한 칸
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
            public string item;          // GameData의 아이템 id — 수치(재생·상한·난이도)는 팩의 광맥 정의가 갖는다
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
            public string defender;      // 낮 방어 몬스터 종류(GameData 몬스터 id). 생략 = 스포너 기본
        }

        /// <summary>스폰 자리 — boss는 이 자리에 서는 보스의 몬스터 id("Monster:Boss"), 없으면 보스 없음(자리는 웨이브 출구).</summary>
        [Serializable] internal class SpawnDto : GameDataJson.JsonDtoBase { public int x, y; public string boss; }

        // ── 실행 ────────────────────────────────────────────────────

        [MenuItem("Tools/Factory/Import Map Data (JSON)")]
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

            // 기존 맵 에셋 색인(제자리 갱신 — 참조 보존) + 광맥 자원 검증용 팩
            var byId = new Dictionary<string, MapDataSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:MapDataSO"))
            {
                var so = AssetDatabase.LoadAssetAtPath<MapDataSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so != null && !string.IsNullOrEmpty(so.Id)) byId[so.Id] = so;
            }
            SimDatabase pack;
            try { pack = SimDatabase.Load(File.ReadAllText(PackLoader.PathOf(PackLoader.DefaultPack)), PackLoader.DefaultPack); }
            catch (Exception e) { Debug.LogError("[MapImporter] 팩 data.json을 읽지 못해 광맥 자원을 검증할 수 없습니다 — GameData 편집기에서 먼저 저장하세요. " + e.Message); return; }

            int created = 0, updated = 0, errors = 0;
            foreach (var dto in root.maps)
                ImportMap(dto, byId, pack, ref created, ref updated, ref errors);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 열려 있는 씬이 이 맵을 쓰고 있으면 배치물을 다시 세운다 — 배치의 정본은 맵이므로
            // 맵이 바뀌는 순간이 곧 씬이 따라가야 할 순간이다. 별도 버튼을 두면 누르는 것을 잊는다.
            foreach (var dto in root.maps)
            {
                if (dto == null || string.IsNullOrEmpty(dto.id)) continue;
                if (byId.TryGetValue(dto.id, out var imported))
                    WorldPlaceableBaker.BakeIfOpen(imported);
            }

            string msg = $"[MapImporter] 맵 {created}개 생성, {updated}개 갱신";
            if (errors > 0) Debug.LogError($"{msg} — 오류 {errors}건 (위 로그 확인)");
            else Debug.Log(msg);
        }

        static void ImportMap(MapDto dto, Dictionary<string, MapDataSO> byId, SimDatabase pack,
            ref int created, ref int updated, ref int errors)
        {
            if (dto == null || string.IsNullOrEmpty(dto.id) || string.IsNullOrEmpty(dto.displayName))
            {
                Debug.LogError($"[MapImporter] id 또는 displayName이 비어 있는 맵이 있습니다.");
                errors++;
                return;
            }
            if (dto.width <= 0 || dto.height <= 0)
            {
                Debug.LogError($"[MapImporter] '{dto.id}': width/height가 유효하지 않습니다 ({dto.width}×{dto.height}).");
                errors++;
                return;
            }

            // 기존 에셋 제자리 갱신 (참조 보존)
            MapDataSO map = byId.TryGetValue(dto.id, out var found) ? found : null;
            bool isNew = map == null;
            if (isNew)
            {
                if (!AssetDatabase.IsValidFolder(MapFolder))
                    AssetDatabase.CreateFolder("Assets/Data", "Maps");

                map = ScriptableObject.CreateInstance<MapDataSO>();
                var so = new SerializedObject(map);
                so.FindProperty("id").stringValue = dto.id;   // id는 private — 직렬화로만 설정한다
                so.ApplyModifiedPropertiesWithoutUndo();

                string file = $"{MapFolder}/{dto.id.Replace(':', '_')}.asset";
                AssetDatabase.CreateAsset(map, AssetDatabase.GenerateUniqueAssetPath(file));
                byId[dto.id] = map;
            }

            map.displayName = dto.displayName;
            map.description = dto.description ?? "";
            map.width = dto.width;
            map.height = dto.height;
            if (!(dto.cellSize > 0f))
            {
                Debug.LogError($"[MapImporter] '{dto.id}': cellSize(칸 한 변, m)가 없거나 0 이하입니다 — 공장·배치·길찾기가 이 값으로 격자를 잡습니다.");
                errors++;
            }
            else map.cellSize = dto.cellSize;
            map.core = dto.core != null ? new Vector2Int(dto.core.x, dto.core.y) : Vector2Int.zero;
            map.EditorSetTiles(BakeTiles(dto, ref errors));
            map.nodes = ResolveNodes(dto, pack, ref errors);
            map.nests = ResolveNests(dto, pack, ref errors);
            map.nightSpawnPoints = ResolveNightSpawns(dto);
            map.startItems = ResolveStartItems(dto, pack, ref errors);
            map.trees = ResolveCells(dto.trees);

            EditorUtility.SetDirty(map);
            if (isNew) created++; else updated++;
        }

        /// <summary>
        /// 문자열 타일을 바이트 격자로 굽는다 — 런타임은 매 칸을 조회하므로 문자열로 두지 않는다.
        /// 줄이 모자라거나 짧으면 지면(0)으로 채운다(명세). 알 수 없는 글자는 에러로 알리고 지면 처리.
        /// </summary>
        static byte[] BakeTiles(MapDto dto, ref int errors)
        {
            var baked = new byte[dto.width * dto.height];   // 기본값 0 = 지면
            if (dto.tiles == null) return baked;

            int rows = Mathf.Min(dto.tiles.Length, dto.height);
            for (int y = 0; y < rows; y++)
            {
                string row = dto.tiles[y];
                if (string.IsNullOrEmpty(row)) continue;

                int cols = Mathf.Min(row.Length, dto.width);
                for (int x = 0; x < cols; x++)
                {
                    char c = row[x];
                    if (c < '0' || c > '2')
                    {
                        Debug.LogError($"[MapImporter] '{dto.id}' ({x},{y}): 알 수 없는 타일 '{c}' — 지면으로 처리합니다. " +
                                       "0=지면 1=강 2=절벽");
                        errors++;
                        continue;
                    }
                    baked[y * dto.width + x] = (byte)(c - '0');
                }
            }
            return baked;
        }

        static StartItemSpec[] ResolveStartItems(MapDto dto, SimDatabase pack, ref int errors)
        {
            if (dto.startItems == null) return Array.Empty<StartItemSpec>();
            var list = new List<StartItemSpec>(dto.startItems.Length);
            foreach (var si in dto.startItems)
            {
                if (si == null) continue;
                string itemId = string.IsNullOrEmpty(si.item) ? null : GameDataExporterV2.PackIdOf(si.item);
                if (itemId == null || pack.Item(itemId) == null) { Debug.LogError($"[MapImporter] '{dto.id}' startItems: 아이템 '{si.item}'({itemId})이 팩에 없습니다."); errors++; continue; }
                if (si.amount <= 0) { Debug.LogError($"[MapImporter] '{dto.id}' startItems: '{si.item}'의 amount가 0 이하입니다."); errors++; continue; }
                list.Add(new StartItemSpec { itemId = itemId, amount = si.amount });
            }
            return list.ToArray();
        }

        static ResourceNodeSpec[] ResolveNodes(MapDto dto, SimDatabase pack, ref int errors)
        {
            if (dto.nodes == null) return Array.Empty<ResourceNodeSpec>();

            var list = new List<ResourceNodeSpec>(dto.nodes.Length);
            foreach (var n in dto.nodes)
            {
                if (n == null) continue;

                // 맵 json은 편집 형식(v1) id("Item:CopperOre")를 적는다 — 에셋에는 팩 id로 굳힌다(런타임이 읽는 형식)
                string itemId = string.IsNullOrEmpty(n.item) ? null : GameDataExporterV2.PackIdOf(n.item);
                if (itemId == null || pack.Item(itemId) == null)
                {
                    Debug.LogError($"[MapImporter] '{dto.id}' 광맥({n.x},{n.y}): 아이템 '{n.item}'({itemId})이 팩에 없습니다.");
                    errors++;
                    continue;   // 캘 것이 없는 광맥은 넣지 않는다
                }

                list.Add(new ResourceNodeSpec
                {
                    itemId = itemId,
                    cell = new Vector2Int(n.x, n.y),
                });
            }
            return list.ToArray();
        }

        /// <summary>밤 웨이브 진입로. 맵 밖이나 통행 불가 칸은 버린다 — 거기서 나오면 즉시 갇힌다.</summary>
        static Vector2Int[] ResolveNightSpawns(MapDto dto) => ResolveCells(dto.nightSpawnPoints);

        /// <summary>칸 목록을 그대로 옮긴다. 빠진 항목(null)만 걸러낸다.</summary>
        static Vector2Int[] ResolveCells(CellDto[] cells)
        {
            if (cells == null) return Array.Empty<Vector2Int>();

            var list = new List<Vector2Int>(cells.Length);
            foreach (var p in cells)
            {
                if (p == null) continue;
                list.Add(new Vector2Int(p.x, p.y));
            }
            return list.ToArray();
        }

        /// <summary>몬스터 id(v1 "Monster:Boss") → 팩 id. 비면 null, 팩에 없으면 오류(그 자리는 보스 없이).</summary>
        static string ResolveMonster(string v1Id, SimDatabase pack, string where, ref int errors)
        {
            if (string.IsNullOrEmpty(v1Id)) return null;
            string id = GameDataExporterV2.PackIdOf(v1Id);
            if (pack.Entity(id) == null)
            {
                Debug.LogError($"[MapImporter] {where}: 몬스터 '{v1Id}'({id})가 팩에 없습니다.");
                errors++;
                return null;
            }
            return id;
        }

        static NestSpec[] ResolveNests(MapDto dto, SimDatabase pack, ref int errors)
        {
            if (dto.nests == null) return Array.Empty<NestSpec>();

            var list = new List<NestSpec>(dto.nests.Length);
            foreach (var nest in dto.nests)
            {
                if (nest == null) continue;

                var points = new List<SpawnPointSpec>();
                if (nest.spawnPoints != null)
                    foreach (var p in nest.spawnPoints)
                        if (p != null) points.Add(new SpawnPointSpec { offset = new Vector2Int(p.x, p.y),
                            boss = ResolveMonster(p.boss, pack, $"'{dto.id}' 둥지({nest.x},{nest.y}) 자리({p.x},{p.y})", ref errors) });

                if (nest.triggerRange > nest.warningRange)
                    Debug.LogWarning($"[MapImporter] '{dto.id}' 둥지({nest.x},{nest.y}): triggerRange가 warningRange보다 큽니다 — " +
                                     "경고 없이 습격당합니다.");

                // 교전 구역은 안쪽부터 바깥으로 min ≤ max ≤ chase ≤ leash 여야 뜻이 통한다.
                // 어긋나면 "쫓아오다 말고 되돌아가는" 식으로 조용히 이상해지므로 여기서 잡는다.
                if (nest.engageMinRange > 0f || nest.engageMaxRange > 0f)
                {
                    if (nest.engageMaxRange < nest.engageMinRange)
                        Debug.LogWarning($"[MapImporter] '{dto.id}' 둥지({nest.x},{nest.y}): engageMaxRange가 engageMinRange보다 작습니다.");
                    if (nest.chaseRange > 0f && nest.chaseRange < nest.engageMaxRange)
                        Debug.LogWarning($"[MapImporter] '{dto.id}' 둥지({nest.x},{nest.y}): chaseRange가 engageMaxRange보다 작습니다 — " +
                                         "교전하자마자 추적을 포기합니다.");
                    if (nest.leashRange > 0f && nest.leashRange < nest.chaseRange)
                        Debug.LogWarning($"[MapImporter] '{dto.id}' 둥지({nest.x},{nest.y}): leashRange가 chaseRange보다 작습니다.");
                }

                list.Add(new NestSpec
                {
                    cell = new Vector2Int(nest.x, nest.y),
                    warningRange = nest.warningRange,
                    triggerRange = nest.triggerRange,
                    defenseSpawnAmount = nest.defenseSpawnAmount,
                    defenseSpawnCooldown = nest.defenseSpawnCooldown,
                    spawnPoints = points.ToArray(),
                    defender = ResolveMonster(nest.defender, pack, $"'{dto.id}' 둥지({nest.x},{nest.y}) 방어자", ref errors),

                    engageMinRange = nest.engageMinRange,
                    engageMaxRange = nest.engageMaxRange,
                    chaseRange = nest.chaseRange,
                    leashRange = nest.leashRange,
                    engageDayOnly = nest.engageDayOnly,

                });
            }
            return list.ToArray();
        }
    }
}
