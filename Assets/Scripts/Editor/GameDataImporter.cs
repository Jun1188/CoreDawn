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
//   - 아이템 → 레시피 순으로 처리해 레시피가 새 아이템을 참조 가능
//   - 에셋 참조가 필요한 타입(건물 프리팹·무기 gunData)은 JSON 대상이 아님 —
//     그런 타입은 인스펙터 저작 유지. 새 JSON 타입 추가 = DTO + Import 함수 한 쌍
//  주의: 임포터는 base(GameDataSO)+해당 타입 필드만 만진다. 기존 에셋이 서브클래스
//        (WeaponItemSO 등)면 공통 필드만 갱신되고 서브클래스 필드는 보존된다.
// ================================================================
public static class GameDataImporter
{
    const string ImportFolder = "Assets/Data/Import";
    const string ItemFolder   = "Assets/Data/Item";
    const string RecipeFolder = "Assets/Data/Recipe";

    // ── JSON DTO (스키마 문서는 Import 폴더의 샘플 참조) ──────────

    [Serializable] class Root
    {
        public ItemDto[]   items;
        public RecipeDto[] recipes;
    }

    [Serializable] class ItemDto
    {
        public string id;            // 필수. 예: "Item:IronOre"
        public string displayName;   // 필수
        public string description;
        public string type;          // ItemType 이름 (Ore/Ingot/Component/Fuel/Misc/...)
        public string icon;          // 스프라이트 이름 (선택 — 프로젝트에서 이름으로 검색)
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

        // 1패스: 아이템 (레시피가 참조할 수 있도록 먼저)
        foreach (var (file, root) in roots)
            if (root?.items != null)
                foreach (var dto in root.items)
                    ImportItem(file, dto, byId, ref created, ref updated, ref errors);

        // 2패스: 레시피
        foreach (var (file, root) in roots)
            if (root?.recipes != null)
                foreach (var dto in root.recipes)
                    ImportRecipe(file, dto, byId, ref created, ref updated, ref errors);

        AssetDatabase.SaveAssets();
        BuildingDatabaseScanner.RebuildAll();   // 아이템 DB 재수집

        Debug.Log($"[GameDataImporter] 완료 — 생성 {created}, 갱신 {updated}, 오류 {errors} (파일 {files.Length}개)");
    }

    // ── 타입별 매핑 ───────────────────────────────────────────

    static void ImportItem(string file, ItemDto dto, Dictionary<string, GameDataSO> byId,
        ref int created, ref int updated, ref int errors)
    {
        if (!ValidateKey(file, "items", dto?.id, dto?.displayName, ref errors)) return;

        var existing = Find<ItemDataSO>(byId, dto.id, file, ref errors);
        if (existing == null && byId.ContainsKey(dto.id)) return;   // id가 다른 타입과 충돌 — Find가 로그함

        bool isNew = existing == null;
        var item = existing != null ? existing
            : CreateAsset<ItemDataSO>(dto.id, ItemFolder, byId);

        item.displayName = dto.displayName;
        item.description = dto.description ?? "";

        if (!string.IsNullOrEmpty(dto.type))
        {
            if (Enum.TryParse(dto.type, true, out ItemType t)) item.type = t;
            else { Debug.LogError($"[GameDataImporter] {file} items '{dto.id}': 알 수 없는 type '{dto.type}'"); errors++; }
        }

        if (!string.IsNullOrEmpty(dto.icon))
        {
            var sprite = FindSprite(dto.icon);
            if (sprite != null) item.icon = sprite;
            else Debug.LogWarning($"[GameDataImporter] {file} items '{dto.id}': 스프라이트 '{dto.icon}' 을 찾지 못했습니다 (기존 아이콘 유지)");
        }

        EditorUtility.SetDirty(item);
        if (isNew) created++; else updated++;
    }

    static void ImportRecipe(string file, RecipeDto dto, Dictionary<string, GameDataSO> byId,
        ref int created, ref int updated, ref int errors)
    {
        if (!ValidateKey(file, "recipes", dto?.id, dto?.displayName, ref errors)) return;

        // 재료/결과 아이템 해석 — 하나라도 못 찾으면 레시피 전체 스킵 (반쪽 데이터 방지)
        if (!TryResolveSlots(file, dto.id, dto.inputs, byId, out var inputs, ref errors)) return;
        if (!TryResolveSlots(file, dto.id, dto.outputs, byId, out var outputs, ref errors)) return;

        var existing = Find<RecipeDataSO>(byId, dto.id, file, ref errors);
        if (existing == null && byId.ContainsKey(dto.id)) return;   // id가 다른 타입과 충돌 — Find가 로그함

        bool isNew = existing == null;
        var recipe = existing != null ? existing
            : CreateAsset<RecipeDataSO>(dto.id, RecipeFolder, byId);

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

    static T CreateAsset<T>(string id, string folder, Dictionary<string, GameDataSO> byId) where T : GameDataSO
    {
        Directory.CreateDirectory(folder);

        var asset = ScriptableObject.CreateInstance<T>();

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

    static bool TryResolveSlots(string file, string recipeId, SlotDto[] dtos,
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
                Debug.LogError($"[GameDataImporter] {file} recipes '{recipeId}': 아이템 id '{s.item}' 을 찾을 수 없습니다 — 레시피 스킵");
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
