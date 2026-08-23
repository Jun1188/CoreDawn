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
//   - 효과 → 총 → 아이템 → 레시피 → 건물 순으로 처리한다. 탄약·총이 효과를,
//     무기 아이템이 총을, 레시피가 아이템을, 건물이 레시피와 아이템을 참조하므로 이 순서여야 한다.
//   - json에 적힌 필드만 덮는다 — 생략 필드는 에셋 값 유지 (짧은 항목이 에셋을 0으로 밀지 않게)
//   - 항목 하나가 실패해도 전체를 중단하지 않는다 — 에러 로그 + errors++ 후 그 항목만 스킵
//  주의: 아이템의 역할(탄약·무기)은 서브클래스가 아니라 모듈(서브에셋)이다 —
//        임포터는 json에 해당 필드가 있을 때 모듈을 만들어(EnsureModule) 배선한다.
//        에셋/씬 참조(bulletPrefab·enemyLayer·icon 스프라이트 외 오브젝트)는 json 밖 — 인스펙터 소관.
// ================================================================
public static class GameDataImporter
{
    internal const string ImportFolder = "Assets/Data/Import";   // GameDataEditorWindow도 이 경로를 쓴다
    const string ItemFolder     = "Assets/Data/Item";
    const string RecipeFolder   = "Assets/Data/Recipe";
    const string BuildingFolder = "Assets/Data/Buildings";
    const string PrefabFolder   = "Assets/Prefabs/Buildings";

    /// <summary>
    /// 칸 하나의 한 변(m). <b>씬의 World.cellSize와 같아야 한다</b> — 여기서 구운 프리팹의
    /// 크기와 배치 격자가 어긋나면 건물이 칸을 덜 채우거나 넘친다.
    /// </summary>
    const float CellSize = 4f;

    /// <summary>건물 높이(m). 풋프린트 콜라이더의 높이이자 큐브 폴백의 키다.</summary>
    const float BuildingHeight = 2f;
    const string ModelFolder    = "Assets/Art/Models";
    const string EffectFolder   = "Assets/Data/Effects";
    const string GunFolder      = "Assets/Data/Guns";

    // ── JSON DTO (스키마 문서는 Import 폴더의 샘플 참조) ──────────

    /// <summary>
    /// 스키마에 없는 필드를 보존하는 베이스 — GameDataEditorWindow가 Newtonsoft로
    /// 파일을 왕복 저장할 때, DTO가 모르는 필드(예: 아직 임포터가 없는 미래 건물의
    /// 값)를 지우지 않고 그대로 되쓴다. JsonUtility(임포터 경로)는 딕셔너리를
    /// 직렬화하지 못해 이 필드를 조용히 무시한다 — 임포터 동작에는 영향이 없다.
    /// </summary>
    internal class JsonDtoBase
    {
        [Newtonsoft.Json.JsonExtensionData]
        public IDictionary<string, object> unknownJson;
    }

    [Serializable] internal class Root : JsonDtoBase
    {
        public EffectDto[]   effects;
        public GunDto[]      guns;
        public ItemDto[]     items;
        public RecipeDto[]   recipes;
        public BuildingDto[] buildings;
        public WaveDto[]     waves;
    }

    /// <summary>공격 효과 한 항목 — EffectEntry의 json 형태. effect는 효과 id.</summary>
    [Serializable] internal class EffectEntryDto : JsonDtoBase { public string effect; public float value; }

    [Serializable] internal class EffectDto : JsonDtoBase
    {
        public string id;            // 필수. 예: "Effect:Damage"
        public string displayName;   // 필수
        public string description;
        public string kind;          // 생성 시 필수 — EffectKindMap 참조 (Damage/Heal/Knockback/DamageOverTime/MoveSpeed/AttackModifier/IncomingDamage)
        public float  duration;      // 지속 효과 전용. >0일 때만 덮음
        public string stacking;      // Refresh | Stack. 생략 시 유지
        public float  tickInterval;  // DamageOverTime 전용. >0일 때만 덮음
        public string[] affects;     // AttackModifier 전용 — 증폭할 효과 id들. null = 유지
        public string knockbackMode; // Knockback 전용 — Directional | Radial. 생략 시 유지
    }

    [Serializable] internal class GunDto : JsonDtoBase
    {
        public string id;            // 필수. 예: "Gun:Rifle"
        public string displayName;   // 필수
        public string description;
        public bool   isAutomatic;   // 주의: bool은 생략을 구분 못 한다 — 항상 명시할 것
        public bool   unlimitedAmmo; // 탄을 소비하지 않는 무기(근접). 위와 같은 이유로 항상 명시할 것
        public bool   blockAim;      // 조준 불가(근접). 생략 = false = 조준 가능이라 기존 총은 안전
        public string fireMode;      // Projectile | Hitscan | Aura. 생략 시 유지
        public float  fireRate, range, reloadTime;            // >0일 때만 덮음. 탄속·탄도는 탄약(items) 소유
        public float  zoomMultiplier;                         // 조준 줌 배율(FOV 절대값 아님). >0일 때만 덮음
        public int    magSize, pellets;                       // >0일 때만 덮음. pellets = 방아쇠당 탄 수(샷건 8)

        // 명중 효과는 탄약이 정의한다 — 총은 장전 가능 탄종 목록과 배율만 갖는다
        public string[] ammoFilter;              // 장전 가능 탄종 id들 — 첫 항목이 기본 탄종. null = 유지
        public float  damageMultiplier = -1f;    // 피해형 항목 배율. 음수 = 생략(유지)

        // 감각 튜닝 — 0이 정당한 값이라 음수를 "생략(유지)" 신호로 쓴다
        public float  xRecoil = -1f, yRecoil = -1f, zRecoil = -1f;
        public float  visualKickbackZ = -1f;
        public float[] visualKickbackRot;                                  // [x,y,z]. null = 유지
        public float  baseSpread = -1f, maxSpread = -1f, spreadIncreasePerShot = -1f, spreadRecoveryRate = -1f;

        // 근접 스윙 — swingTime > 0일 때만 스윙한다(총기는 생략하면 그만)
        public float   swingTime = -1f, swingWindup = -1f;
        public float[] swingRotation, swingPosition;   // [x,y,z]. null = 유지
        public bool    swingAlternate;                 // bool이라 생략 판별 불가 — 스윙 무기는 항상 명시할 것
    }

    [Serializable] internal class ItemDto : JsonDtoBase
    {
        public string id;            // 필수. 예: "Item:IronOre"
        public string displayName;   // 필수
        public string description;
        public string type;          // ItemType 이름 — 용도 축 (Ore/Ingot/Part/RepairPart/Ammo/Weapon/...)
        public string line;          // ItemLine 이름 — 계통 축 (Iron/Copper/Crystal/Beast). 생략 시 기존 값 유지
        public string icon;          // 스프라이트 이름 (선택 — 프로젝트에서 이름으로 검색)
        public EffectEntryDto[] attackEffects;  // Ammo 전용 — 1발의 명중 효과. null = 유지
        public float  damage;        // Ammo 전용 구 숏컷 — attackEffects가 없을 때만 {Damage, damage}로 변환
        public string gun;           // Weapon 전용 — 연결할 GunData id (예: "Gun:Rifle")

        // Ammo 탄도 — 탄의 물리 성질(발사기가 아니라 탄약 소유). 0이 정당한 값(직선·무폭발)이라 음수가 생략(유지) 신호다
        public float  speed = -1f, gravity = -1f, explosionRadius = -1f, lifetime = -1f;
        public int    pierce = -1;   // 추가 관통 대상 수 — 0 정당(첫 대상에서 멈춤), 음수 = 생략(유지)
    }

    [Serializable] internal class SlotDto : JsonDtoBase { public string item; public int amount; }

    [Serializable] internal class RecipeDto : JsonDtoBase
    {
        public string id;            // 필수. 예: "Recipe:Recipe_IronIngot"
        public string displayName;   // 필수
        public string description;
        public int    tier;          // 해금 코어 티어 (구 requiredCoreTier — 필드 통합)
        public float  craftTime = 2f;
        public SlotDto[] inputs;
        public SlotDto[] outputs;
    }

    [Serializable] internal class Vec2Dto : JsonDtoBase { public int x, y; }
    [Serializable] internal class PortDto : JsonDtoBase { public int x, y; public string dir; public bool isInput; }

    [Serializable] internal class BuildingDto : JsonDtoBase
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
        public float    minRange = -1f;                      // Tower — 최소 사거리(박격포 사각). 0 정당, 음수 = 생략(유지)
        public string   fireMode;                            // Tower — Projectile | Hitscan | Aura | None
        public float    muzzleHeight = -1f;                  // Tower — 0 정당, 음수 = 생략(유지)
        public bool     preferHighArc;                       // Tower — 곡사 시 고각 선택(박격포). bool은 항상 명시
        public float    turnSpeed = -1f;                     // Tower — 포탑 선회 속도(도/초). 0 정당(포탑 없음), 음수 = 생략(유지)
        public float    aimTolerance = -1f;                  // Tower — 조준 완료 허용 오차(도). 0 정당, 음수 = 생략(유지)
        public string   defaultAmmo;                         // Tower — 무공급(씬 배치) 폴백 탄 아이템 id. 생략 시 유지
        public string[] ammoFilter;                          // Tower
        public TierDto[] tiers;                              // Core
    }

    /// <summary>
    /// 코어 수리 단계. 이걸 JSON에 두는 이유 — 예전엔 에셋에만 손으로 적혀 있어서
    /// 브랜치 머지 때 조용히 덮여 사라졌다. 이제 재임포트로 항상 복구된다.
    /// </summary>
    [Serializable] internal class TierDto : JsonDtoBase
    {
        public string   name;            // → CoreTierDefinition.tierLabel
        public string   description;
        public SlotDto[] requirements;
        public string[] unlocks;
        public int      maxHpBonus;
        public bool     isFinal;
    }

    [Serializable] internal class WaveDto : JsonDtoBase
    {
        public string id;
        public string displayName;
        public string description;
        public int day;
        public int requiredCoreTier;
        public int baseAmount;
        public int maxAliveAmount;
        public float spawnInterval;
        public float monsterMaxHp;   // 0 = wave_settings.json → 프리팹 기본값 폴백
    }

    /// <summary>
    /// kind → 만들 서브클래스. BuildingDataSO가 추상이라 임포터가 무엇을 CreateInstance할지
    /// 알아야 한다 — 아이템·레시피에는 없던 개념이라 별도로 둔다.
    /// </summary>
    internal static readonly Dictionary<string, Type> KindMap = new(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>
    /// 효과 kind → 클래스 (건물 KindMap과 같은 패턴). 클래스 = 채널(코드), value = 크기라서
    /// json이 갖는 형태 필드는 duration·stacking·tickInterval·affects뿐이다.
    /// </summary>
    internal static readonly Dictionary<string, Type> EffectKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Damage"]         = typeof(DamageEffectSO),
        ["Heal"]           = typeof(HealEffectSO),
        ["Knockback"]      = typeof(KnockbackEffectSO),
        ["DamageOverTime"] = typeof(DamageOverTimeEffectSO),
        ["MoveSpeed"]      = typeof(MoveSpeedEffectSO),
        ["AttackModifier"] = typeof(AttackModifierEffectSO),
        ["IncomingDamage"] = typeof(IncomingDamageEffectSO),
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

        // 0패스: 효과 (탄약·총의 attackEffects가 참조한다).
        // affects(효과→효과 참조)는 전 파일의 효과가 다 만들어진 뒤 2차로 해석한다.
        var pendingAffects = new List<(string file, EffectDto dto, AttackModifierEffectSO asset)>();
        foreach (var (file, root) in roots)
            if (root?.effects != null)
                foreach (var dto in root.effects)
                    ImportEffect(file, dto, byId, pendingAffects, ref created, ref updated, ref errors);
        foreach (var (file, dto, asset) in pendingAffects)
            ResolveAffects(file, dto, asset, byId, ref errors);

        // 0.5패스: 총 (무기 아이템의 gun 참조가 필요하다).
        // 총의 ammo(아이템 참조)는 상호 참조라 아이템 패스 뒤 2차로 해석한다.
        foreach (var (file, root) in roots)
            if (root?.guns != null)
                foreach (var dto in root.guns)
                    ImportGun(file, dto, byId, ref created, ref updated, ref errors);

        // 1패스: 아이템 (레시피·건물이 참조할 수 있도록 먼저)
        foreach (var (file, root) in roots)
            if (root?.items != null)
                foreach (var dto in root.items)
                    ImportItem(file, dto, byId, ref created, ref updated, ref errors);

        // 1.5패스: 총 → 탄약 참조 해석 (아이템이 다 만들어진 뒤)
        foreach (var (file, root) in roots)
            if (root?.guns != null)
                foreach (var dto in root.guns)
                    if (!string.IsNullOrEmpty(dto?.id) && byId.TryGetValue(dto.id, out var g) && g is GunData gun)
                        ResolveGunAmmo(file, dto, gun, byId, ref errors);

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

        // 4패스: 웨이브
        foreach (var (file, root) in roots)
            if (root?.waves != null)
                foreach (var dto in root.waves)
                    ImportWave(file, dto, byId, ref created, ref updated, ref errors);

        AssetDatabase.SaveAssets();
        BuildingDatabaseScanner.RebuildAll();   // 아이템·건물 DB 재수집

        Debug.Log($"[GameDataImporter] 완료 — 생성 {created}, 갱신 {updated}, 오류 {errors} (파일 {files.Length}개)");
    }

    // ── 효과 ─────────────────────────────────────────────────

    static void ImportEffect(string file, EffectDto dto, Dictionary<string, GameDataSO> byId,
        List<(string, EffectDto, AttackModifierEffectSO)> pendingAffects,
        ref int created, ref int updated, ref int errors)
    {
        if (!ValidateKey(file, "effects", dto?.id, dto?.displayName, ref errors)) return;

        var existing = Find<EffectSO>(byId, dto.id, file, ref errors);
        if (existing == null && byId.ContainsKey(dto.id)) return;   // id가 다른 타입과 충돌

        bool isNew = existing == null;
        EffectSO fx = existing;
        if (isNew)
        {
            if (string.IsNullOrEmpty(dto.kind) || !EffectKindMap.TryGetValue(dto.kind, out var kind))
            {
                Debug.LogError($"[GameDataImporter] {file} effects '{dto.id}': 알 수 없는 kind '{dto.kind}' — " +
                               $"가능: {string.Join("/", EffectKindMap.Keys)}");
                errors++;
                return;
            }
            fx = (EffectSO)CreateAsset(kind, dto.id, EffectFolder, byId);
        }
        else if (!string.IsNullOrEmpty(dto.kind) &&
                 EffectKindMap.TryGetValue(dto.kind, out var wanted) && fx.GetType() != wanted)
        {
            // 타입 교체는 참조(탄약·총의 entry, 중첩 키)를 전부 끊으므로 자동으로 하지 않는다
            Debug.LogError($"[GameDataImporter] {file} effects '{dto.id}': kind '{dto.kind}'가 기존 타입 " +
                           $"{fx.GetType().Name}과 다릅니다 — 수동으로 정리하세요 (기존 유지)");
            errors++;
            return;
        }

        fx.displayName = dto.displayName;
        fx.description = dto.description ?? "";

        if (fx is DurationEffectSO dur)
        {
            if (dto.duration > 0f) dur.duration = dto.duration;
            if (!string.IsNullOrEmpty(dto.stacking))
            {
                if (Enum.TryParse(dto.stacking, true, out EffectStacking st)) dur.stacking = st;
                else { Debug.LogError($"[GameDataImporter] {file} effects '{dto.id}': 알 수 없는 stacking '{dto.stacking}'"); errors++; }
            }
        }
        if (fx is DamageOverTimeEffectSO dot && dto.tickInterval > 0f) dot.tickInterval = dto.tickInterval;

        if (!string.IsNullOrEmpty(dto.knockbackMode))
        {
            if (fx is not KnockbackEffectSO kb)
            { Debug.LogError($"[GameDataImporter] {file} effects '{dto.id}': knockbackMode는 Knockback 전용입니다"); errors++; }
            else if (Enum.TryParse(dto.knockbackMode, true, out KnockbackMode km)) kb.mode = km;
            else { Debug.LogError($"[GameDataImporter] {file} effects '{dto.id}': 알 수 없는 knockbackMode '{dto.knockbackMode}' — 가능: Directional/Radial"); errors++; }
        }

        // affects는 전 파일의 효과 임포트가 끝난 뒤 해석 (앞 항목이 뒤 항목을 참조할 수 있게)
        if (dto.affects != null)
        {
            if (fx is AttackModifierEffectSO buff) pendingAffects.Add((file, dto, buff));
            else { Debug.LogError($"[GameDataImporter] {file} effects '{dto.id}': affects는 AttackModifier 전용입니다"); errors++; }
        }

        EditorUtility.SetDirty(fx);
        if (isNew) created++; else updated++;
    }

    static void ResolveAffects(string file, EffectDto dto, AttackModifierEffectSO buff,
        Dictionary<string, GameDataSO> byId, ref int errors)
    {
        var list = new List<EffectSO>(dto.affects.Length);
        foreach (var id in dto.affects)
        {
            if (byId.TryGetValue(id ?? "", out var so) && so is EffectSO fx) list.Add(fx);
            else
            {
                Debug.LogError($"[GameDataImporter] {file} effects '{dto.id}': affects의 효과 id '{id}' 를 찾을 수 없습니다 — 스킵");
                errors++;
                return;
            }
        }
        buff.affects = list.ToArray();
        EditorUtility.SetDirty(buff);
    }

    /// <summary>attackEffects 항목 배열 해석 — entries가 null이면 "json에 없음"(기존 유지)이다.</summary>
    static bool TryResolveEffectEntries(string file, string section, string ownerId, EffectEntryDto[] dtos,
        Dictionary<string, GameDataSO> byId, out EffectEntry[] entries, ref int errors)
    {
        entries = null;
        if (dtos == null) return true;

        var list = new List<EffectEntry>(dtos.Length);
        foreach (var e in dtos)
        {
            if (byId.TryGetValue(e.effect ?? "", out var so) && so is EffectSO fx)
            {
                list.Add(new EffectEntry(fx, e.value));
            }
            else
            {
                Debug.LogError($"[GameDataImporter] {file} {section} '{ownerId}': 효과 id '{e.effect}' 를 찾을 수 없습니다 — 스킵");
                errors++;
                return false;
            }
        }
        entries = list.ToArray();
        return true;
    }

    // ── 총 ───────────────────────────────────────────────────

    static void ImportGun(string file, GunDto dto, Dictionary<string, GameDataSO> byId,
        ref int created, ref int updated, ref int errors)
    {
        if (!ValidateKey(file, "guns", dto?.id, dto?.displayName, ref errors)) return;

        var existing = Find<GunData>(byId, dto.id, file, ref errors);
        if (existing == null && byId.ContainsKey(dto.id)) return;   // id가 다른 타입과 충돌

        bool isNew = existing == null;
        var gun = existing != null ? existing
            : (GunData)CreateAsset(typeof(GunData), dto.id, GunFolder, byId);

        gun.displayName = dto.displayName;
        gun.description = dto.description ?? "";
        gun.isAutomatic = dto.isAutomatic;   // bool은 생략 판별 불가 — json이 항상 명시한다 (DTO 주석 참조)
        gun.unlimitedAmmo = dto.unlimitedAmmo;
        gun.blockAim = dto.blockAim;
        gun.swingAlternate = dto.swingAlternate;

        if (!string.IsNullOrEmpty(dto.fireMode))
        {
            if (Enum.TryParse(dto.fireMode, true, out FireMode fm)) gun.fireMode = fm;
            else { Debug.LogError($"[GameDataImporter] {file} guns '{dto.id}': 알 수 없는 fireMode '{dto.fireMode}'"); errors++; }
        }

        if (dto.fireRate    > 0f) gun.fireRate    = dto.fireRate;
        if (dto.range       > 0f) gun.range       = dto.range;
        if (dto.magSize     > 0)  gun.magSize     = dto.magSize;
        if (dto.pellets     > 0)  gun.pellets     = dto.pellets;
        if (dto.reloadTime  > 0f) gun.reloadTime  = dto.reloadTime;
        if (dto.zoomMultiplier > 0f) gun.zoomMultiplier = dto.zoomMultiplier;

        // 감각 튜닝 — 0이 정당한 값이라 음수가 "생략(유지)" 신호다
        if (dto.xRecoil >= 0f) gun.xRecoil = dto.xRecoil;
        if (dto.yRecoil >= 0f) gun.yRecoil = dto.yRecoil;
        if (dto.zRecoil >= 0f) gun.zRecoil = dto.zRecoil;
        if (dto.visualKickbackZ >= 0f) gun.visualKickbackZ = dto.visualKickbackZ;
        if (dto.visualKickbackRot is { Length: 3 })
            gun.visualKickbackRot = new Vector3(dto.visualKickbackRot[0], dto.visualKickbackRot[1], dto.visualKickbackRot[2]);
        if (dto.baseSpread            >= 0f) gun.baseSpread            = dto.baseSpread;
        if (dto.maxSpread             >= 0f) gun.maxSpread             = dto.maxSpread;
        if (dto.spreadIncreasePerShot >= 0f) gun.spreadIncreasePerShot = dto.spreadIncreasePerShot;
        if (dto.spreadRecoveryRate    >= 0f) gun.spreadRecoveryRate    = dto.spreadRecoveryRate;

        if (dto.swingTime   >= 0f) gun.swingTime   = dto.swingTime;
        if (dto.swingWindup >= 0f) gun.swingWindup = dto.swingWindup;
        if (dto.swingRotation != null && dto.swingRotation.Length == 3)
            gun.swingRotation = new Vector3(dto.swingRotation[0], dto.swingRotation[1], dto.swingRotation[2]);
        if (dto.swingPosition != null && dto.swingPosition.Length == 3)
            gun.swingPosition = new Vector3(dto.swingPosition[0], dto.swingPosition[1], dto.swingPosition[2]);

        if (dto.damageMultiplier >= 0f) gun.damageMultiplier = dto.damageMultiplier;
        // ammo(아이템 참조)는 아이템 패스가 끝난 뒤 2차로 해석한다 — ResolveGunAmmo

        EditorUtility.SetDirty(gun);
        if (isNew) created++; else updated++;
    }

    /// <summary>총의 탄종 목록 해석 — 아이템 패스 뒤에 호출된다 (총↔아이템 상호 참조 해소).
    /// 첫 항목이 기본 탄종이다 — 별도 ammo 필드는 중복이라 폐지했다.</summary>
    static void ResolveGunAmmo(string file, GunDto dto, GunData gun,
        Dictionary<string, GameDataSO> byId, ref int errors)
    {
        if (dto.ammoFilter == null) return;   // 생략 = 유지

        gun.ammoFilter = ResolveItems(file, dto.id, dto.ammoFilter, byId, ref errors);

        if (gun.DefaultAmmo == null)
            Debug.LogWarning($"[GameDataImporter] {file} guns '{dto.id}': ammoFilter가 비어 기본 탄종이 없습니다 — 발사 불가");
        else if (gun.AmmoModule == null)
            Debug.LogWarning($"[GameDataImporter] {file} guns '{dto.id}': 기본 탄종 '{gun.DefaultAmmo.Id}'에 " +
                             "AmmoModule이 없습니다 — 발사해도 효과가 없습니다 (attackEffects 확인)");
        EditorUtility.SetDirty(gun);
    }

    // ── 아이템 ────────────────────────────────────────────────

    /// <summary>
    /// 아이템의 역할 모듈을 보장한다 — 없으면 만들어 아이템 에셋의 서브에셋으로 붙인다
    /// (파일 하나 = 아이템 + 그 모듈들. 별도 에셋 파일이 늘지 않는다).
    /// </summary>
    static T EnsureModule<T>(ItemDataSO item) where T : ItemModuleSO
    {
        var module = item.GetModule<T>();
        if (module != null) return module;

        module = ScriptableObject.CreateInstance<T>();
        module.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(module, item);
        item.EditorModules.Add(module);
        EditorUtility.SetDirty(item);
        return module;
    }

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

        // 아이템은 전부 평평한 ItemDataSO — 탄약·무기 같은 역할은 모듈(서브에셋)로 조합한다.
        // 그래서 구 방식의 "타입 승격을 위한 같은 id 재생성"이 필요 없다.
        var existing = Find<ItemDataSO>(byId, dto.id, file, ref errors);
        if (existing == null && byId.ContainsKey(dto.id)) return;   // id가 다른 타입과 충돌

        bool isNew = existing == null;
        var item = existing != null ? existing
            : (ItemDataSO)CreateAsset(typeof(ItemDataSO), dto.id, ItemFolder, byId);

        item.displayName = dto.displayName;
        item.description = dto.description ?? "";
        if (hasType) item.type = type;

        if (!string.IsNullOrEmpty(dto.line))
        {
            if (Enum.TryParse(dto.line, true, out ItemLine l)) item.line = l;
            else { Debug.LogError($"[GameDataImporter] {file} items '{dto.id}': 알 수 없는 line '{dto.line}'"); errors++; }
        }

        // 탄약 모듈 — attackEffects(정식)가 있으면 그대로, 없으면 구 damage 숏컷을 변환.
        // json에 둘 다 없으면 기존 모듈은 건드리지 않는다 (적힌 필드만 덮기).
        if (dto.attackEffects != null)
        {
            if (TryResolveEffectEntries(file, "items", dto.id, dto.attackEffects, byId, out var ammoEntries, ref errors)
                && ammoEntries != null)
                EnsureModule<AmmoModuleSO>(item).attackEffects = ammoEntries;
        }
        else if (dto.damage > 0f)
        {
            var damageEffect = byId.TryGetValue("Effect:Damage", out var dmgSo) ? dmgSo as DamageEffectSO : null;
            if (damageEffect == null)
            {
                Debug.LogError($"[GameDataImporter] {file} items '{dto.id}': " +
                               "id 'Effect:Damage' 효과가 없어 탄약 피해를 배선하지 못했습니다 (effects 섹션 확인).");
                errors++;
            }
            else
            {
                // 기존 피해 항목이 있으면 값만 갱신, 없으면 맨 앞에 추가 — 수동 배선한 부가 효과는 보존
                var ammo = EnsureModule<AmmoModuleSO>(item);
                var list = new List<EffectEntry>(ammo.attackEffects ?? Array.Empty<EffectEntry>());
                int idx = list.FindIndex(e => e.effect is DamageEffectSO);
                if (idx >= 0) list[idx] = new EffectEntry(list[idx].effect, dto.damage);
                else list.Insert(0, new EffectEntry(damageEffect, dto.damage));
                ammo.attackEffects = list.ToArray();
            }
        }

        // 탄도 — 탄의 물리 성질(발사기가 아니라 탄약 소유). 0이 정당한 값이라 음수가 생략 신호다.
        if (dto.speed >= 0f || dto.gravity >= 0f || dto.explosionRadius >= 0f || dto.lifetime >= 0f || dto.pierce >= 0)
        {
            var ammo = EnsureModule<AmmoModuleSO>(item);
            if (dto.speed           >= 0f) ammo.speed           = dto.speed;
            if (dto.gravity         >= 0f) ammo.gravity         = dto.gravity;
            if (dto.explosionRadius >= 0f) ammo.explosionRadius = dto.explosionRadius;
            if (dto.lifetime        >= 0f) ammo.lifetime        = dto.lifetime;
            if (dto.pierce          >= 0)  ammo.pierce          = dto.pierce;
        }

        // 무기 모듈 — 아이템 ↔ 총 데이터 연결
        if (!string.IsNullOrEmpty(dto.gun))
        {
            if (byId.TryGetValue(dto.gun, out var g) && g is GunData gunData)
                EnsureModule<WeaponModuleSO>(item).gun = gunData;
            else
            {
                Debug.LogError($"[GameDataImporter] {file} items '{dto.id}': 총 id '{dto.gun}' 을 찾을 수 없습니다 (guns 섹션 확인)");
                errors++;
            }
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
        recipe.craftTime        = dto.craftTime;
        recipe.inputs           = inputs;
        recipe.outputs          = outputs;

        EditorUtility.SetDirty(recipe);
        if (isNew) created++; else updated++;
    }

    // ── 웨이브 ────────────────────────────────────────────────

    static void ImportWave(string file, WaveDto dto, Dictionary<string, GameDataSO> byId,
        ref int created, ref int updated, ref int errors)
    {
        if (!ValidateKey(file, "waves", dto?.id, dto?.displayName, ref errors)) return;

        var existing = Find<WaveDataSO>(byId, dto.id, file, ref errors);
        if (existing == null && byId.ContainsKey(dto.id)) return;

        bool isNew = existing == null;
        var wave = existing != null ? existing
            : (WaveDataSO)CreateAsset(typeof(WaveDataSO), dto.id, "Assets/Data/Wave", byId);

        wave.displayName      = dto.displayName;
        wave.description      = dto.description ?? "";
        wave.day              = dto.day;
        wave.requiredCoreTier = dto.requiredCoreTier;
        wave.baseAmount       = dto.baseAmount;
        wave.maxAliveAmount   = dto.maxAliveAmount;
        wave.spawnInterval    = dto.spawnInterval;
        wave.monsterMaxHp     = dto.monsterMaxHp;

        EditorUtility.SetDirty(wave);
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
                if (dto.damageMultiplier > 0f) tower.damageMultiplier = dto.damageMultiplier;
                if (dto.range > 0f) tower.range = dto.range;
                if (dto.minRange >= 0f) tower.minRange = dto.minRange;   // 0 정당 — 음수가 생략 신호
                if (dto.fireRate > 0f) tower.fireRate = dto.fireRate;
                if (!string.IsNullOrEmpty(dto.fireMode))
                {
                    if (Enum.TryParse(dto.fireMode, true, out FireMode fm)) tower.fireMode = fm;
                    else { Debug.LogError($"[GameDataImporter] {file} buildings '{dto.id}': 알 수 없는 fireMode '{dto.fireMode}'"); errors++; }
                }
                if (dto.muzzleHeight >= 0f) tower.muzzleHeight = dto.muzzleHeight;   // 0 정당 — 음수가 생략 신호
                tower.preferHighArc = dto.preferHighArc;   // bool은 생략 판별 불가 — json이 항상 명시한다
                if (dto.turnSpeed >= 0f) tower.turnSpeed = dto.turnSpeed;         // 0 정당(포탑 없는 타워)
                if (dto.aimTolerance >= 0f) tower.aimTolerance = dto.aimTolerance;
                if (!string.IsNullOrEmpty(dto.defaultAmmo))
                {
                    if (byId.TryGetValue(dto.defaultAmmo, out var da) && da is ItemDataSO defAmmo)
                        tower.defaultAmmo = defAmmo;
                    else
                    {
                        Debug.LogError($"[GameDataImporter] {file} buildings '{dto.id}': defaultAmmo id '{dto.defaultAmmo}' 를 찾을 수 없습니다");
                        errors++;
                    }
                }
                tower.ammoFilter = ResolveItems(file, dto.id, dto.ammoFilter, byId, ref errors);
                break;

            case CoreDataSO core:
                // tiers 가 통째로 빠진 JSON이면 기존 값을 지우지 않는다 —
                // 한 필드 누락으로 게임 진행 전체가 사라지는 편이 더 나쁘다
                if (dto.tiers != null) core.tiers = ResolveTiers(file, dto, byId, ref errors);
                break;
        }
    }

    /// <summary>
    /// 코어 수리 단계 해석. 요구 아이템을 하나라도 못 찾으면 그 단계는 넣지 않는다 —
    /// 요구가 반쯤 빠진 단계는 그냥 통과해 버려서, 조용히 진행도를 깨뜨린다.
    /// </summary>
    static CoreTierDefinition[] ResolveTiers(string file, BuildingDto dto,
        Dictionary<string, GameDataSO> byId, ref int errors)
    {
        var list = new List<CoreTierDefinition>(dto.tiers.Length);

        for (int i = 0; i < dto.tiers.Length; i++)
        {
            var t = dto.tiers[i];
            if (t == null) continue;

            string where = $"{dto.id} tiers[{i}]" + (string.IsNullOrEmpty(t.name) ? "" : $" '{t.name}'");

            if (!TryResolveSlots(file, "buildings", where, t.requirements, byId, out var reqs, ref errors))
            {
                Debug.LogError($"[GameDataImporter] {file} {where}: 요구 아이템을 해석하지 못해 이 단계를 건너뜁니다.");
                continue;
            }
            if (reqs == null || reqs.Length == 0)
            {
                Debug.LogError($"[GameDataImporter] {file} {where}: 요구가 비어 있습니다 — 즉시 통과하는 단계가 되므로 제외합니다.");
                errors++;
                continue;
            }

            var reqArr = new CoreTierRequirement[reqs.Length];
            for (int k = 0; k < reqs.Length; k++)
                reqArr[k] = new CoreTierRequirement { item = reqs[k].item, amount = reqs[k].amount };

            list.Add(new CoreTierDefinition
            {
                tierLabel    = t.name,
                description  = t.description,
                requirements = reqArr,
                unlocks      = t.unlocks ?? Array.Empty<string>(),
                maxHpBonus   = t.maxHpBonus,
                isFinal      = t.isFinal,
            });
        }

        return list.ToArray();
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
                if (EnsureContract(contents, so, isTower, dto.model))
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
        EnsureContract(root, so, isTower, dto.model);

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
    /// <summary>
    /// 건물 프리팹을 Entity 레이어로 올린다. 이 레이어가 아니면 조준·상호작용이 통째로 죽는다.
    /// 레이어가 프로젝트에 없으면(설정 누락) 경고만 남기고 넘어간다 — 임포트 전체를 세울 일은 아니다.
    /// </summary>
    static bool EnsureEntityLayer(GameObject root, BuildingDataSO so)
    {
        int entityLayer = LayerMask.NameToLayer("Entity");
        if (entityLayer < 0)
        {
            Debug.LogWarning($"[GameDataImporter] 'Entity' 레이어가 없습니다 — '{so.name}' 레이어를 건너뜁니다. " +
                             "Project Settings > Tags and Layers 를 확인하세요.");
            return false;
        }

        bool changed = false;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            // Default(0)인 것만 옮긴다 — 일부러 다른 레이어를 준 자식은 그 의도를 남긴다
            if (t.gameObject.layer != 0) continue;
            t.gameObject.layer = entityLayer;
            changed = true;
        }
        return changed;
    }

    static bool EnsureContract(GameObject root, BuildingDataSO so, bool isTower, string modelFile)
    {
        bool changed = false;

        // 0-a) 모델 자식 동기화 — 레이어보다 먼저: 새로 붙은 모델 자식도 아래에서 레이어를 받는다.
        changed |= EnsureModel(root, so, modelFile);

        // 0) Entity 레이어 — 플레이어의 상호작용 레이캐스트가 이 마스크로 쏜다.
        //    Default로 두면 조준해도 프롬프트가 안 뜨고 E가 먹지 않는다 (코어 열기·보관함·필터 전부).
        //    콜라이더가 자식에 있는 모델 프리팹도 있으므로 Default인 자식까지 함께 옮긴다 —
        //    일부러 다른 레이어를 준 자식(장애물 등)은 건드리지 않는다.
        changed |= EnsureEntityLayer(root, so);
        changed |= EnsureRootScale(root);

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

        // 1-b) 코어 표식 — 이게 꺼져 있으면 코어를 아무도 못 찾는다.
        //      내구도 UI가 비고, 플로우필드의 최종 목표도, 코어 파괴 = 게임오버 판정도 죽는다.
        if (entity is BuildingEntity be)
        {
            var coreObj = new SerializedObject(be);
            var isCore = coreObj.FindProperty("isCore");
            bool wantCore = so is CoreDataSO;
            if (isCore != null && isCore.boolValue != wantCore)
            {
                isCore.boolValue = wantCore;
                coreObj.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
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

        // 3) 콜라이더 — 매 임포트마다 그리는 메시 그대로 덮어쓴다(팀 결정: 예외·불변 규칙 없음).
        //    루트 상자(풋프린트·AABB)는 모델과의 괴리로 보이지 않는 벽을 만들던 원인이라 제거하고,
        //    렌더 메시마다 MeshCollider를 붙인다 — 충돌이 곧 그림이다. 타워의 포탑 회전 같은
        //    트랜스폼 애니메이션은 콜라이더가 오브젝트를 따라 움직이므로 그대로 유효하다.
        changed |= EnsureColliders(root);

        return changed;
    }

    /// <summary>
    /// 렌더 메시마다 논컨벡스 MeshCollider를 강제한다 — 총알·조준이 그림에 없는 빈 공간에
    /// 걸리지 않는다. 루트의 BoxCollider(구 체제)는 제거. 이미 맞는 상태면 아무것도 안 바꿔
    /// 재임포트가 프리팹을 더럽히지 않는다. 스킨드 메시는 바인드 포즈 기준(현재 건물엔 없음).
    /// </summary>
    static bool EnsureColliders(GameObject root)
    {
        bool changed = false;

        foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
        {
            // 중첩 프리팹 인스턴스 소속(모델 에셋 내부)은 여기서 못 지운다 — 그 모델 에셋에서 지울 것
            if (PrefabUtility.IsPartOfPrefabInstance(box)) continue;
            UnityEngine.Object.DestroyImmediate(box);
            changed = true;
        }

        int entityLayer = LayerMask.NameToLayer("Entity");

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

            Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                      : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
            if (mesh == null) continue;

            var mc = r.GetComponent<MeshCollider>();
            if (mc == null) { mc = r.gameObject.AddComponent<MeshCollider>(); changed = true; }
            if (mc.sharedMesh != mesh) { mc.sharedMesh = mesh; changed = true; }
            if (mc.convex) { mc.convex = false; changed = true; }

            // 콜라이더를 얹은 오브젝트는 반드시 Entity 레이어 — 조준·상호작용·몬스터 감지가
            // 전부 이 레이어로 본다. 그림 전용이던 시절의 레이어가 남아 있으면(타워가 Ground였다)
            // 충돌체가 엉뚱한 레이어로 나가 타워 상호작용·감지가 통째로 죽는다.
            if (entityLayer >= 0 && r.gameObject.layer != entityLayer)
            {
                r.gameObject.layer = entityLayer;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>벨트 커브 전용 — 본체와 같은 방식이되 컴포넌트는 붙이지 않는다(메시 교체용).</summary>
    static GameObject EnsureCurvePrefab(BuildingDataSO so, string modelFile, string suffix)
    {
        string path = $"{PrefabFolder}/{so.name}{suffix}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            // 이미 있으면 모델 지정이 비어 있어도 계약은 계속 맞춘다 — 커브는 본체와 달리
            // EnsureContract를 거치지 않아 여기서 손보지 않으면 칸 크기·콜라이더가 옛날에 머문다
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool dirty = EnsureRootScale(contents);
                dirty |= EnsureColliders(contents);
                if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return existing;
        }

        if (string.IsNullOrEmpty(modelFile)) return null;

        var root = MakeBody(modelFile, so.size, so.name + suffix);
        root.name = so.name + suffix;
        EnsureColliders(root);
        EnsureRootScale(root);

        Directory.CreateDirectory(PrefabFolder);
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// 모델이 있으면 순수 루트 아래 "Model" 자식 인스턴스, 없으면 풋프린트 크기 큐브("Mesh").
    /// 모델을 루트로 쓰면 프리팹이 모델의 변형(variant)이 되어 나중에 모델을 갈아끼울 수 없다 —
    /// 자식이면 EnsureModel이 JSON 변경을 따라 교체한다.
    /// 아트가 늦어도 배치·연결·시뮬레이션은 먼저 굴러가야 하므로 임포트를 막지 않는다.
    /// </summary>
    static GameObject MakeBody(string modelFile, Vector2Int size, string logName)
    {
        // 루트는 스케일 1의 순수 GO — 큐브·모델을 그대로 루트로 쓰면 그 스케일이
        // 루트 콜라이더 크기까지 곱해져 충돌이 부풀거나, 모델 변형이 돼 교체가 막힌다.
        var root = new GameObject("Body");

        var model = string.IsNullOrEmpty(modelFile) ? null
            : FindAsset<GameObject>(Path.GetFileNameWithoutExtension(modelFile), ModelFolder);
        if (model != null)
        {
            AttachModelChild(root, model);
            return root;
        }

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Mesh";
        cube.transform.SetParent(root.transform, false);
        cube.transform.localScale = new Vector3(size.x * 0.9f, BuildingHeight / CellSize, size.y * 0.9f);
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<BoxCollider>());   // EnsureColliders가 메시 기준으로 다시 붙인다

        if (!string.IsNullOrEmpty(modelFile))
            Debug.LogWarning($"[GameDataImporter] '{logName}': 모델 '{modelFile}' 을 찾지 못해 큐브로 만들었습니다");
        return root;
    }

    static void AttachModelChild(GameObject root, GameObject model)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = "Model";
        inst.transform.SetParent(root.transform, false);
    }

    /// <summary>
    /// "Model" 자식을 JSON(dto.model)과 일치시킨다 — 모델 파일명을 바꾸면 재임포트가 교체한다.
    /// 큐브 플레이스홀더("Mesh")도 모델이 도착하는 순간 대체된다.
    /// 루트 자체가 모델 인스턴스인 구 구조(모델 변형으로 저장된 프리팹)는 교체가 불가능하다 —
    /// 경고만 남긴다: 프리팹을 지우고 재임포트하면 새 구조로 다시 만들어진다.
    /// 모델이 실제로 바뀐 순간에는 루트 콜라이더를 새 AABB로 재적합한다 — 이전 적합은 이전 모델의 것.
    /// </summary>
    static bool EnsureModel(GameObject root, BuildingDataSO so, string modelFile)
    {
        var wanted = string.IsNullOrEmpty(modelFile) ? null
            : FindAsset<GameObject>(Path.GetFileNameWithoutExtension(modelFile), ModelFolder);
        if (wanted == null) return false;   // 모델 미지정·미발견 — 현 상태 유지

        var current = root.transform.Find("Model");
        if (current == null && !OnlyPlaceholderVisuals(root))
        {
            // "Model" 자식 없이 이미 그림이 있는 프리팹 — 구 구조(모델이 루트에 흡수됐거나
            // 손으로 만든 것). 여기에 모델을 얹으면 이중으로 겹치므로 절대 손대지 않는다.
            // 지금 모델과 같은 에셋에서 나온 렌더러가 하나도 없으면 파일명이 바뀐 것 — 안내만.
            if (!AnyRendererFrom(root, wanted))
                Debug.LogWarning($"[GameDataImporter] '{so.name}': 모델이 '{modelFile}' 로 바뀌었지만 " +
                                 "프리팹이 구 구조(모델=루트)라 자동 교체할 수 없습니다 — " +
                                 "프리팹을 지우고 재임포트하면 새 구조로 다시 만들어집니다.");
            return false;
        }

        if (current != null &&
            PrefabUtility.GetCorrespondingObjectFromOriginalSource(current.gameObject) == wanted)
            return false;   // 이미 원하는 모델

        if (current != null) UnityEngine.Object.DestroyImmediate(current.gameObject);
        var placeholder = root.transform.Find("Mesh");   // 큐브 플레이스홀더 — 모델이 왔으니 은퇴
        if (placeholder != null) UnityEngine.Object.DestroyImmediate(placeholder.gameObject);

        AttachModelChild(root, wanted);
        // 콜라이더는 EnsureContract의 EnsureColliders가 새 메시 기준으로 다시 만든다

        Debug.Log($"[GameDataImporter] '{so.name}': 모델을 '{modelFile}' 로 교체했습니다.");
        return true;
    }

    /// <summary>그림이 큐브 플레이스홀더("Mesh")뿐인가 — 그때만 모델을 안전하게 얹을 수 있다.</summary>
    static bool OnlyPlaceholderVisuals(GameObject root)
    {
        var mesh = root.transform.Find("Mesh");
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (mesh == null || r.transform != mesh) return false;
        return true;   // 렌더러가 없거나 전부 플레이스홀더
    }

    /// <summary>루트 아래에 이 모델 에셋에서 나온 렌더러가 있는가 — 구 구조가 최신인지 판별용.</summary>
    static bool AnyRendererFrom(GameObject root, GameObject modelAsset)
    {
        string wantedPath = AssetDatabase.GetAssetPath(modelAsset);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(r.gameObject);
            if (src != null && AssetDatabase.GetAssetPath(src) == wantedPath) return true;
        }
        return false;
    }

    /// <summary>
    /// 프리팹 루트의 스케일을 <b>칸 크기</b>에 맞춘다.
    ///
    /// 모델과 풋프린트는 "1칸 = 1"이라는 로컬 단위로 저작돼 있다. 칸이 몇 미터인지는
    /// 월드가 정하므로(<see cref="World"/>.cellSize), 그 배율을 루트가 한 번에 받는다 —
    /// 모델도 콜라이더도 함께 곱해져 서로 어긋날 수가 없다.
    /// 콜라이더를 월드 미터로 따로 계산하면 둘이 각자 놀다가 조준·철거가 빗나간다.
    /// </summary>
    static bool EnsureRootScale(GameObject root)
    {
        var want = Vector3.one * CellSize;
        if ((root.transform.localScale - want).sqrMagnitude < 0.0001f) return false;

        root.transform.localScale = want;
        return true;
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
