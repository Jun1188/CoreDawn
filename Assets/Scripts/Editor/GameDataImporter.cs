#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ================================================================
//  GameDataImporter — 외부 툴(스프레드시트 등)에서 뽑은 JSON으로
//  GameDataSO 에셋을 일괄 생성/갱신하는 통합 임포터.
//
//  대상: Assets/Data/Import/*.json (여러 파일 가능 — 파일마다 섹션 일부만 있어도 됨)
//  실행: Tools/Factory/Import Game Data (JSON)
//
//  원칙:
//   - id("분류:이름")가 기본 키 — 있으면 갱신, 없으면 생성 (멱등 재임포트, guid 보존)
//   - 아이템 → 레시피 → 건물 순으로 처리한다. 레시피가 아이템을,
//     건물이 레시피와 아이템을 참조하므로 이 순서여야 한다.
//   - 항목 하나가 실패해도 전체를 중단하지 않는다 — 에러 로그 + errors++ 후 그 항목만 스킵
//  주의: 임포터는 base(GameDataSO)+해당 타입 필드만 만진다. 기존 에셋이 서브클래스
//        (WeaponItemSO 등)면 공통 필드만 갱신되고 서브클래스 필드는 보존된다.
// ================================================================
public static class GameDataImporter
{
    const string ImportFolder   = "Assets/Data/Import";
    const string ItemFolder     = "Assets/Data/Item";
    const string RecipeFolder   = "Assets/Data/Recipe";
    const string BuildingFolder = "Assets/Data/Buildings";
    const string PrefabFolder   = "Assets/Prefabs/Buildings";
    const string ModelFolder    = "Assets/Models";

    // ── JSON DTO (스키마 문서는 Import 폴더의 샘플 참조) ──────────

    [Serializable] class Root
    {
        public ItemDto[]     items;
        public RecipeDto[]   recipes;
        public BuildingDto[] buildings;
    }

    [Serializable] class ItemDto
    {
        public string id;            // 필수. 예: "Item:IronOre"
        public string displayName;   // 필수
        public string description;
        public string type;          // ItemType 이름 — 용도 축 (Ore/Ingot/Part/RepairPart/Ammo/...)
        public string line;          // ItemLine 이름 — 계통 축 (Iron/Copper/Crystal/Beast). 생략 시 기존 값 유지
        public string icon;          // 스프라이트 이름 (선택 — 프로젝트에서 이름으로 검색)
        public float  damage;        // Ammo 전용 — 1발의 기본 피해
    }

    [Serializable] class SlotDto { public string item; public int amount; }

    [Serializable] class RecipeDto
    {
        public string id;            // 필수. 예: "Recipe:Recipe_IronIngot"
        public string displayName;   // 필수
        public string description;
        public int    tier;
        public int    requiredCoreTier;
        public float  craftTime = 2f;
        public SlotDto[] inputs;
        public SlotDto[] outputs;
    }

    [Serializable] class Vec2Dto { public int x, y; }
    [Serializable] class PortDto { public int x, y; public string dir; public bool isInput; }

    [Serializable] class BuildingDto
    {
        public string id;            // 필수. 예: "Building:Assembler"
        public string kind;          // 필수 — 어느 서브클래스로 만들지 (KindMap 참조)
        public string displayName;   // 필수
        public string description;
        public string category;      // BuildingCategory 이름
        public string model;          // 모델 파일명 (선택). 비면 풋프린트 크기 큐브로 대체
        public Vec2Dto size;
        public PortDto[] ports;
        public int   inputSlots, outputSlots, bufferStackCap, requiredCoreTier, maxHp;
        public bool  hideFromBuildMenu;
        public SlotDto[] buildCost;

        // 종류별 전용 필드 — 해당 kind가 아니면 무시된다
        public float    speedMultiplier;      // Miner
        public float    speedTilesPerSec;     // Belt
        public string   modelCurveL, modelCurveR;
        public string[] availableRecipes;     // Assembler
        public float    damageMultiplier, range, fireRate;   // Tower
        public string[] ammoFilter;                          // Tower
    }

    /// <summary>
    /// kind → 만들 서브클래스. BuildingDataSO가 추상이라 임포터가 무엇을 CreateInstance할지
    /// 알아야 한다 — 아이템·레시피에는 없던 개념이라 별도로 둔다.
    /// </summary>
    static readonly Dictionary<string, Type> KindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Miner"]     = typeof(MinerDataSO),
        ["Belt"]      = typeof(BeltDataSO),
        ["Assembler"] = typeof(AssemblerDataSO),
        ["Splitter"]  = typeof(SplitterDataSO),
        ["Merger"]    = typeof(MergerDataSO),
        ["Storage"]   = typeof(StorageDataSO),
        ["Core"]      = typeof(CoreDataSO),
        ["Tower"]     = typeof(TowerDataSO),
    };

    // ── 진입점 ────────────────────────────────────────────────

    [MenuItem("Tools/Factory/Import Game Data (JSON)")]
    public static void ImportAll()
    {
        if (!Directory.Exists(ImportFolder))
        {
            Debug.LogWarning($"[GameDataImporter] {ImportFolder} 폴더가 없습니다.");
            return;
        }

        // id → 기존 에셋 (모든 GameDataSO — 중복 id 방지와 갱신 대상 탐색 겸용)
        var byId = new Dictionary<string, GameDataSO>(StringComparer.Ordinal);
        foreach (var guid in AssetDatabase.FindAssets("t:GameDataSO"))
        {
            var so = AssetDatabase.LoadAssetAtPath<GameDataSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (so != null && !string.IsNullOrEmpty(so.Id)) byId[so.Id] = so;
        }

        int created = 0, updated = 0, errors = 0;

        var files = Directory.GetFiles(ImportFolder, "*.json", SearchOption.TopDirectoryOnly);
        var roots = new List<(string file, Root root)>();
        foreach (var f in files)
        {
            try { roots.Add((Path.GetFileName(f), JsonUtility.FromJson<Root>(File.ReadAllText(f)))); }
            catch (Exception e) { Debug.LogError($"[GameDataImporter] {f} 파싱 실패: {e.Message}"); errors++; }
        }

        // 1패스: 아이템 (레시피·건물이 참조할 수 있도록 먼저)
        foreach (var (file, root) in roots)
            if (root?.items != null)
                foreach (var dto in root.items)
                    ImportItem(file, dto, byId, ref created, ref updated, ref errors);

        // 2패스: 레시피 (건물의 availableRecipes가 참조한다)
        foreach (var (file, root) in roots)
            if (root?.recipes != null)
                foreach (var dto in root.recipes)
                    ImportRecipe(file, dto, byId, ref created, ref updated, ref errors);

        // 3패스: 건물
        foreach (var (file, root) in roots)
            if (root?.buildings != null)
                foreach (var dto in root.buildings)
                    ImportBuilding(file, dto, byId, ref created, ref updated, ref errors);

        AssetDatabase.SaveAssets();
        BuildingDatabaseScanner.RebuildAll();   // 아이템·건물 DB 재수집

        Debug.Log($"[GameDataImporter] 완료 — 생성 {created}, 갱신 {updated}, 오류 {errors} (파일 {files.Length}개)");
    }

    // ── 아이템 ────────────────────────────────────────────────

    static void ImportItem(string file, ItemDto dto, Dictionary<string, GameDataSO> byId,
        ref int created, ref int updated, ref int errors)
    {
        if (!ValidateKey(file, "items", dto?.id, dto?.displayName, ref errors)) return;

        ItemType type = default;
        bool hasType = !string.IsNullOrEmpty(dto.type);
        if (hasType && !Enum.TryParse(dto.type, true, out type))
        {
            Debug.LogError($"[GameDataImporter] {file} items '{dto.id}': 알 수 없는 type '{dto.type}'");
            errors++;
            return;
        }

        // 탄약은 피해량을 갖는 전용 클래스가 필요하다. 기존 에셋이 평범한 ItemDataSO면
        // 필드가 없어 갱신만으로는 못 바꾸므로 같은 id로 다시 만든다.
        // (레시피·건물은 뒤 패스에서 id로 다시 해석되므로 참조가 스스로 복구된다)
        Type wanted = hasType && type == ItemType.Ammo ? typeof(AmmoItemSO) : typeof(ItemDataSO);

        var existing = Find<ItemDataSO>(byId, dto.id, file, ref errors);
        if (existing == null && byId.ContainsKey(dto.id)) return;   // id가 다른 타입과 충돌

        // 서브클래스(WeaponItemSO 등)는 그대로 둔다 — 요구 타입을 이미 만족하거나 더 구체적이다
        if (existing != null && wanted == typeof(AmmoItemSO) && existing is not AmmoItemSO)
        {
            Debug.LogWarning($"[GameDataImporter] {file} items '{dto.id}': " +
                             $"{existing.GetType().Name} → AmmoItemSO 로 다시 만듭니다 (피해량 필드 필요).");
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(existing));
            byId.Remove(dto.id);
            existing = null;
        }

        bool isNew = existing == null;
        var item = existing != null ? existing
            : (ItemDataSO)CreateAsset(wanted, dto.id, ItemFolder, byId);

        item.displayName = dto.displayName;
        item.description = dto.description ?? "";
        if (hasType) item.type = type;

        if (!string.IsNullOrEmpty(dto.line))
        {
            if (Enum.TryParse(dto.line, true, out ItemLine l)) item.line = l;
            else { Debug.LogError($"[GameDataImporter] {file} items '{dto.id}': 알 수 없는 line '{dto.line}'"); errors++; }
        }

        if (item is AmmoItemSO ammo && dto.damage > 0f) ammo.damage = dto.damage;

        if (!string.IsNullOrEmpty(dto.icon))
        {
            var sprite = FindSprite(dto.icon);
            if (sprite != null) item.icon = sprite;
            else Debug.LogWarning($"[GameDataImporter] {file} items '{dto.id}': 스프라이트 '{dto.icon}' 을 찾지 못했습니다 (기존 아이콘 유지)");
        }

        EditorUtility.SetDirty(item);
        if (isNew) created++; else updated++;
    }

    // ── 레시피 ────────────────────────────────────────────────

    static void ImportRecipe(string file, RecipeDto dto, Dictionary<string, GameDataSO> byId,
        ref int created, ref int updated, ref int errors)
    {
        if (!ValidateKey(file, "recipes", dto?.id, dto?.displayName, ref errors)) return;

        // 재료/결과 아이템 해석 — 하나라도 못 찾으면 레시피 전체 스킵 (반쪽 데이터 방지)
        if (!TryResolveSlots(file, "recipes", dto.id, dto.inputs, byId, out var inputs, ref errors)) return;
        if (!TryResolveSlots(file, "recipes", dto.id, dto.outputs, byId, out var outputs, ref errors)) return;

        var existing = Find<RecipeDataSO>(byId, dto.id, file, ref errors);
        if (existing == null && byId.ContainsKey(dto.id)) return;

        bool isNew = existing == null;
        var recipe = existing != null ? existing
            : (RecipeDataSO)CreateAsset(typeof(RecipeDataSO), dto.id, RecipeFolder, byId);

        recipe.displayName      = dto.displayName;
        recipe.description      = dto.description ?? "";
        recipe.tier             = dto.tier;
        recipe.requiredCoreTier = dto.requiredCoreTier;
        recipe.craftTime        = dto.craftTime;
        recipe.inputs           = inputs;
        recipe.outputs          = outputs;

        EditorUtility.SetDirty(recipe);
        if (isNew) created++; else updated++;
    }

    // ── 건물 ──────────────────────────────────────────────────

    static void ImportBuilding(string file, BuildingDto dto, Dictionary<string, GameDataSO> byId,
        ref int created, ref int updated, ref int errors)
    {
        if (!ValidateKey(file, "buildings", dto?.id, dto?.displayName, ref errors)) return;

        if (string.IsNullOrEmpty(dto.kind) || !KindMap.TryGetValue(dto.kind, out var wanted))
        {
            Debug.LogError($"[GameDataImporter] {file} buildings '{dto.id}': 알 수 없는 kind '{dto.kind}'");
            errors++;
            return;
        }

        var existing = Find<BuildingDataSO>(byId, dto.id, file, ref errors);
        if (existing == null && byId.ContainsKey(dto.id)) return;

        // kind가 바뀌면 클래스가 달라 갱신으로 못 옮긴다 — 같은 id로 다시 만든다
        if (existing != null && existing.GetType() != wanted)
        {
            Debug.LogWarning($"[GameDataImporter] {file} buildings '{dto.id}': " +
                             $"{existing.GetType().Name} → {wanted.Name} 로 다시 만듭니다 (kind 변경).");
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(existing));
            byId.Remove(dto.id);
            existing = null;
        }

        bool isNew = existing == null;
        var so = existing != null ? existing
            : (BuildingDataSO)CreateAsset(wanted, dto.id, BuildingFolder, byId);

        so.displayName = dto.displayName;
        so.description = dto.description ?? "";

        if (!string.IsNullOrEmpty(dto.category))
        {
            if (Enum.TryParse(dto.category, true, out BuildingCategory cat)) so.category = cat;
            else { Debug.LogError($"[GameDataImporter] {file} buildings '{dto.id}': 알 수 없는 category '{dto.category}'"); errors++; }
        }

        if (dto.size != null) so.size = new Vector2Int(Mathf.Max(1, dto.size.x), Mathf.Max(1, dto.size.y));
        so.ports = BuildPorts(file, dto, ref errors);

        so.inputSlots        = dto.inputSlots;
        so.outputSlots       = dto.outputSlots;
        so.bufferStackCap    = dto.bufferStackCap;
        so.requiredCoreTier  = dto.requiredCoreTier;
        so.hideFromBuildMenu = dto.hideFromBuildMenu;
        if (dto.maxHp > 0) so.maxHp = dto.maxHp;

        // 비용은 레시피 슬롯과 같은 해석기를 쓴다 — 못 찾은 아이템이 있으면 비용만 비우고 건물은 살린다
        if (TryResolveSlots(file, "buildings", dto.id, dto.buildCost, byId, out var cost, ref errors))
            so.buildCost = cost;

        ApplyKindFields(file, dto, so, byId, ref errors);

        so.prefab = EnsurePrefab(so, dto);
        if (so is BeltDataSO belt)
        {
            belt.curveLPrefab = EnsureCurvePrefab(so, dto.modelCurveL, "LCurve") ?? belt.curveLPrefab;
            belt.curveRPrefab = EnsureCurvePrefab(so, dto.modelCurveR, "RCurve") ?? belt.curveRPrefab;
        }

        EditorUtility.SetDirty(so);
        if (isNew) created++; else updated++;
    }

    /// <summary>종류별 전용 필드. 해당 kind가 아니면 아무것도 하지 않는다.</summary>
    static void ApplyKindFields(string file, BuildingDto dto, BuildingDataSO so,
        Dictionary<string, GameDataSO> byId, ref int errors)
    {
        switch (so)
        {
            case MinerDataSO miner:
                if (dto.speedMultiplier > 0f) miner.speedMultiplier = dto.speedMultiplier;
                break;

            case BeltDataSO belt:
                if (dto.speedTilesPerSec > 0f) belt.speedTilesPerSec = dto.speedTilesPerSec;
                break;

            case AssemblerDataSO asm:
                asm.availableRecipes = ResolveRecipes(file, dto, byId, ref errors);
                break;

            case TowerDataSO tower:
                tower.damageMultiplier = dto.damageMultiplier;
                if (dto.range > 0f) tower.range = dto.range;
                if (dto.fireRate > 0f) tower.fireRate = dto.fireRate;
                tower.ammoFilter = ResolveItems(file, dto.id, dto.ammoFilter, byId, ref errors);
                break;
        }
    }

    static RecipeDataSO[] ResolveRecipes(string file, BuildingDto dto,
        Dictionary<string, GameDataSO> byId, ref int errors)
    {
        if (dto.availableRecipes == null) return Array.Empty<RecipeDataSO>();

        var list = new List<RecipeDataSO>(dto.availableRecipes.Length);
        foreach (var rid in dto.availableRecipes)
        {
            if (byId.TryGetValue(rid ?? "", out var so) && so is RecipeDataSO r) list.Add(r);
            else
            {
                // 레시피 하나가 빠져도 건물은 살린다 — 나머지 레시피는 쓸 수 있다
                Debug.LogError($"[GameDataImporter] {file} buildings '{dto.id}': 레시피 id '{rid}' 를 찾을 수 없습니다 — 이 레시피만 제외");
                errors++;
            }
        }
        return list.ToArray();
    }

    static ItemDataSO[] ResolveItems(string file, string ownerId, string[] ids,
        Dictionary<string, GameDataSO> byId, ref int errors)
    {
        if (ids == null) return Array.Empty<ItemDataSO>();

        var list = new List<ItemDataSO>(ids.Length);
        foreach (var iid in ids)
        {
            if (byId.TryGetValue(iid ?? "", out var so) && so is ItemDataSO item) list.Add(item);
            else
            {
                Debug.LogError($"[GameDataImporter] {file} buildings '{ownerId}': 아이템 id '{iid}' 를 찾을 수 없습니다 — 이 항목만 제외");
                errors++;
            }
        }
        return list.ToArray();
    }

    static PortDefinition[] BuildPorts(string file, BuildingDto dto, ref int errors)
    {
        if (dto.ports == null) return Array.Empty<PortDefinition>();

        var list = new List<PortDefinition>(dto.ports.Length);
        foreach (var p in dto.ports)
        {
            if (!Enum.TryParse(p.dir, true, out Direction d))
            {
                Debug.LogError($"[GameDataImporter] {file} buildings '{dto.id}': 알 수 없는 포트 방향 '{p.dir}' — 이 포트만 제외");
                errors++;
                continue;
            }
            list.Add(new PortDefinition
            {
                LocalOffset = new Vector2Int(p.x, p.y),
                Direction   = d,
                IsInput     = p.isInput,
            });
        }
        return list.ToArray();
    }

    // ── 프리팹 ────────────────────────────────────────────────

    /// <summary>
    /// 건물 프리팹을 보장한다. JSON에는 모델 파일명만 넣고 프리팹은 여기서 만든다 —
    /// 프리팹 이름을 손으로 적으면 오타 하나로 조용한 null 참조가 되고, 건물이 늘어날수록
    /// 이름 관리 부담만 커진다.
    ///
    /// 이미 있으면 <b>다시 만들지 않고 빠진 것만 채운다</b>. 프리팹에는 모델·이펙트·자식
    /// 오브젝트를 손으로 붙였을 수 있어 통째로 갈아엎으면 그게 날아간다. 대신 배치가
    /// 의존하는 계약(Entity · 풋프린트 콜라이더 · maxHp)은 매 임포트마다 맞춘다 —
    /// 그래야 JSON의 수치가 실제로 반영된다.
    /// 모델 자체를 바꾸려면 프리팹을 지우고 재임포트한다.
    /// </summary>
    static GameObject EnsurePrefab(BuildingDataSO so, BuildingDto dto)
    {
        string path = $"{PrefabFolder}/{so.name}.prefab";
        bool isTower = string.Equals(dto.kind, "Tower", StringComparison.OrdinalIgnoreCase);

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (EnsureContract(contents, so, isTower))
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    Debug.Log($"[GameDataImporter] '{so.name}' 기존 프리팹에 빠진 항목을 채웠습니다 (모델은 유지).");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        GameObject root = MakeBody(dto.model, so.size, so.name);
        root.name = so.name;
        EnsureContract(root, so, isTower);

        Directory.CreateDirectory(PrefabFolder);
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// 배치·전투가 기대하는 최소 구성을 루트에 맞춘다. 이미 맞으면 아무것도 하지 않는다.
    /// 자식 오브젝트는 건드리지 않는다.
    /// </summary>
    /// <returns>무언가 바꿨으면 true.</returns>
    static bool EnsureContract(GameObject root, BuildingDataSO so, bool isTower)
    {
        bool changed = false;

        // 1) Entity — 포탑만 사격 로직을 가진 BattleTower가 필요하다
        var entity = root.GetComponent<Entity>();
        if (entity == null)
        {
            entity = isTower ? root.AddComponent<BattleTower>() : root.AddComponent<BuildingEntity>();
            changed = true;
        }
        else if (isTower && entity is not BattleTower)
        {
            // 컴포넌트 교체는 참조·직렬화를 잃으므로 자동으로 하지 않는다 — 사람이 판단할 문제
            Debug.LogWarning($"[GameDataImporter] '{so.name}': 포탑인데 루트가 {entity.GetType().Name} 입니다. " +
                             "BattleTower로 직접 교체하거나 프리팹을 지우고 재임포트하세요.");
        }

        // 2) 최대 체력 — HealthComponent의 필드는 private이라 직렬화 경로로 넣는다
        if (entity != null && so.maxHp > 0)
        {
            var sobj = new SerializedObject(entity);
            var hp = sobj.FindProperty("health.maxHealth");
            if (hp != null && !Mathf.Approximately(hp.floatValue, so.maxHp))
            {
                hp.floatValue = so.maxHp;
                sobj.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
        }

        // 3) 풋프린트 콜라이더 — 프리팹과 점유 칸이 어긋나는 건 배치 버그의 단골이다.
        //    모델이 자기 콜라이더를 자식에 갖고 있어도 루트에는 풋프린트 기준이 하나 있어야 한다.
        var want = new Vector3(so.size.x, 1f, so.size.y);
        var center = new Vector3((so.size.x - 1) * 0.5f, 0.5f, (so.size.y - 1) * 0.5f);

        var col = root.GetComponent<BoxCollider>();
        if (col == null)
        {
            AddFootprintCollider(root, so.size);
            changed = true;
        }
        else if ((col.size - want).sqrMagnitude > 0.0001f || (col.center - center).sqrMagnitude > 0.0001f)
        {
            col.size = want;
            col.center = center;
            changed = true;
        }

        return changed;
    }

    /// <summary>벨트 커브 전용 — 본체와 같은 방식이되 컴포넌트는 붙이지 않는다(메시 교체용).</summary>
    static GameObject EnsureCurvePrefab(BuildingDataSO so, string modelFile, string suffix)
    {
        if (string.IsNullOrEmpty(modelFile)) return null;

        string path = $"{PrefabFolder}/{so.name}{suffix}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var root = MakeBody(modelFile, so.size, so.name + suffix);
        root.name = so.name + suffix;
        AddFootprintCollider(root, so.size);

        Directory.CreateDirectory(PrefabFolder);
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// 모델이 있으면 그 인스턴스, 없으면 풋프린트 크기 큐브.
    /// 아트가 늦어도 배치·연결·시뮬레이션은 먼저 굴러가야 하므로 임포트를 막지 않는다.
    /// </summary>
    static GameObject MakeBody(string modelFile, Vector2Int size, string logName)
    {
        var model = string.IsNullOrEmpty(modelFile) ? null
            : FindAsset<GameObject>(Path.GetFileNameWithoutExtension(modelFile), ModelFolder);

        if (model != null) return (GameObject)PrefabUtility.InstantiatePrefab(model);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = new Vector3(size.x * 0.9f, 0.6f, size.y * 0.9f);
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<BoxCollider>());   // 아래에서 풋프린트 기준으로 다시 붙인다

        if (!string.IsNullOrEmpty(modelFile))
            Debug.LogWarning($"[GameDataImporter] '{logName}': 모델 '{modelFile}' 을 찾지 못해 큐브로 만들었습니다");
        return cube;
    }

    /// <summary>충돌 크기는 size에서 계산한다 — 프리팹과 풋프린트가 어긋나는 건 배치 버그의 단골이다.</summary>
    static void AddFootprintCollider(GameObject root, Vector2Int size)
    {
        var col = root.AddComponent<BoxCollider>();
        col.size   = new Vector3(size.x, 1f, size.y);
        col.center = new Vector3((size.x - 1) * 0.5f, 0.5f, (size.y - 1) * 0.5f);
    }

    static T FindAsset<T>(string name, string folder) where T : UnityEngine.Object
    {
        if (!Directory.Exists(folder)) return null;
        foreach (var guid in AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<T>(path);
        }
        return null;
    }

    // ── 공통 헬퍼 ─────────────────────────────────────────────

    static bool ValidateKey(string file, string section, string id, string displayName, ref int errors)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(displayName))
        {
            Debug.LogError($"[GameDataImporter] {file} {section}: id/displayName은 필수입니다 (id='{id}')");
            errors++;
            return false;
        }
        return true;
    }

    /// <summary>id로 기존 에셋 탐색. 있는데 타입이 다르면 오류(null 반환 + 카운트).</summary>
    static T Find<T>(Dictionary<string, GameDataSO> byId, string id, string file, ref int errors) where T : GameDataSO
    {
        if (!byId.TryGetValue(id, out var so)) return null;
        if (so is T typed) return typed;
        Debug.LogError($"[GameDataImporter] {file}: id '{id}'가 다른 타입({so.GetType().Name}) 에셋과 충돌합니다");
        errors++;
        return null;
    }

    static GameDataSO CreateAsset(Type type, string id, string folder, Dictionary<string, GameDataSO> byId)
    {
        Directory.CreateDirectory(folder);

        var asset = (GameDataSO)ScriptableObject.CreateInstance(type);

        // private [SerializeField] id 세팅
        var so = new SerializedObject(asset);
        so.FindProperty("id").stringValue = id;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 파일명 = id의 콜론 뒤 이름
        int colon = id.LastIndexOf(':');
        string name = colon >= 0 ? id[(colon + 1)..] : id;
        AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset"));

        byId[id] = asset;   // 같은 임포트 안에서 뒤따르는 참조 해석 가능
        return asset;
    }

    static bool TryResolveSlots(string file, string section, string ownerId, SlotDto[] dtos,
        Dictionary<string, GameDataSO> byId, out RecipeDataSO.Slot[] slots, ref int errors)
    {
        slots = Array.Empty<RecipeDataSO.Slot>();
        if (dtos == null || dtos.Length == 0) return true;

        var list = new List<RecipeDataSO.Slot>(dtos.Length);
        foreach (var s in dtos)
        {
            if (byId.TryGetValue(s.item ?? "", out var so) && so is ItemDataSO item)
            {
                list.Add(new RecipeDataSO.Slot { item = item, amount = s.amount });
            }
            else
            {
                Debug.LogError($"[GameDataImporter] {file} {section} '{ownerId}': 아이템 id '{s.item}' 을 찾을 수 없습니다 — 스킵");
                errors++;
                return false;
            }
        }
        slots = list.ToArray();
        return true;
    }

    /// <summary>이름으로 스프라이트 검색 (서브에셋 포함, 이름 정확 일치 우선).</summary>
    static Sprite FindSprite(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets($"{name} t:Sprite"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub is Sprite sprite && sprite.name == name)
                    return sprite;
        }
        return null;
    }
}
#endif
