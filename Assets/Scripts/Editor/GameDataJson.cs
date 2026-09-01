#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace CoreDawn.EditorTools
{
    // ================================================================
    //  GameDataJson — 편집 형식(v1) GameData.json의 DTO.
    //
    //  정본은 하나(Assets/Data/Import/GameData.json)이고 GameData 편집기(GameDataEditorWindow)가 이 DTO로
    //  읽고 쓴다. 저장할 때 GameDataExporterV2가 v2 팩(StreamingAssets/packs/<pack>/data.json)을 내고,
    //  ViewCatalogBaker가 뷰 카탈로그를 굽는다. 런타임은 v2 팩만 읽는다 — SO 에셋은 5a-3e에서 퇴역했다.
    //  (구 GameDataImporter의 SO 생성부는 삭제. v2 직접 편집은 5a-3e-2에서.)
    // ================================================================
    public static class GameDataJson
    {
        internal const string ImportFolder = "Assets/Data/Import";   // GameDataEditorWindow도 이 경로를 쓴다

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
            public SoundDto[] sounds;                    // 소리 — 변형 클립 묶음
            public MaterialDto[] materials;              // 재질 — 셰이더 이름 + 값·텍스처(5a-4c). 셰이더는 내장, 값은 팩
            public Dictionary<string, SfxUseDto> sfx;   // 공용 소리 자리(ui_click·construct·mine…) — 구 CommonSFX
            public EffectDto[]   effects;
            public GunDto[]      guns;
            public ItemDto[]     items;
            public RecipeDto[]   recipes;
            public BuildingDto[] buildings;
            public MonsterDto[]  monsters;
            public WaveRuleDto   wave;      // 밤 웨이브 규칙(점수식) — SO 없음, 팩 wave 블록
            public DayCycleDto   dayCycle;  // 주야 시계(낮·밤 길이) — 팩 dayCycle 블록
            public TutorialStepDto[] tutorial;
            public PlayerDto         player;    // 팩의 entities/player — SO 없음, 심이 정의로 조립한다
        }


        /// <summary>플레이어 — HP·소지품 칸 수(main, 핫바 포함)·핫바 창 크기. v2 exporter가 Health·Effects·Inventory·Crafter(manual) 모듈로 낸다.</summary>
        [Serializable] internal class PlayerDto : JsonDtoBase
        {
            public string displayName;
            public float  maxHp = 300f;
            public int    main = 25;   // 핫바 포함 전체
            public int    hotbar = 7;  // main의 앞 몇 칸이 핫바인가
        }

        /// <summary>
        /// 튜토리얼 완료 조건 한 개 — 팩 tutorial 섹션 conditions[]의 json 형태(런타임 TutorialConditionDef와 같은 키).
        /// type은 조건 클래스 이름 — "Condition" 접미는 생략해도 된다 (MineResource / MineResourceCondition).
        /// 나머지 필드는 그 종류가 실제로 읽는 것만 의미가 있다.
        /// </summary>
        [Serializable] internal class TutorialConditionDto : JsonDtoBase
        {
            public string type;
            public int    count = 1;      // 누적형·소지형: 몇 번/몇 개
            public float  seconds = 2f;   // MoveAndLook: 이동 누적 초
            public int    tier = 1;       // CoreTier: 목표 티어
            public string itemType;       // ItemType 이름. 생략 시 클래스 기본값 유지
            public string item;           // 아이템 id (AcquireItem 전용)
        }

        [Serializable] internal class TutorialStepDto : JsonDtoBase
        {
            public string id;            // 필수. 예: "Tutorial:Mine" — 세이브 키라 바꾸면 안 된다
            public string displayName;   // 필수
            public string description;
            public int    order;         // 작을수록 먼저
            public string tag = "GUIDE"; // 카드 배지
            public string body;          // 카드 본문
            public string[] keyHints;    // 키캡
            public float  minSeconds = 2.5f;
            public bool   requireInOrder;
            public TutorialConditionDto[] conditions;   // 전부 충족해야 끝난다. 비면 영영 안 끝남(저작 중)
        }

        /// <summary>공격 효과 한 항목 — EffectEntry의 json 형태. effect는 효과 id.</summary>
        [Serializable] internal class EffectEntryDto : JsonDtoBase { public string effect; public float value; }

        /// <summary>소리를 쓰는 자리 — sound(소리 id) + 볼륨 + 공간감. EffectEntry가 효과 + 값이듯 소리 + 재생 값.</summary>
        [Serializable] internal class SfxUseDto : JsonDtoBase { public string sound; public float volume = 1f; public bool spatial = true; }
        /// <summary>정의의 표현 블록(v1) — 뷰 종류(ViewSchema 표의 키)와 소리 자리. 모델·프리팹·아이콘은 각 DTO의 평평한 필드로 남아 있다(exporter가 합친다).</summary>
        [Serializable] internal class ViewDto : JsonDtoBase { public string type; public Dictionary<string, SfxUseDto> sfx; }
        /// <summary>소리 한 종 — 변형 클립 묶음(재생 때 무작위). id 관례 "Sound:이름".</summary>
        [Serializable] internal class SoundDto : JsonDtoBase { public string id; public string displayName; public ClipDto[] clips; }
        [Serializable] internal class ClipDto : JsonDtoBase { public string clip; public string clipGuid; }

        // 재질(5a-4c) — PackMaterialHarvester가 거두고 v2 내보내기가 textures를 팩 png로 복사한다. 값은 셰이더 기본값과 다른 것만
        [Serializable] internal class MaterialDto : JsonDtoBase
        {
            public string id;              // "Material:TreeBark"
            public string displayName;
            public string shader;          // 내장 셰이더 이름("CoreDawn/Vegetation Lit", "Universal Render Pipeline/Lit")
            public TextureRefDto[] textures;
            public ColorDto[] colors;
            public ColorDto[] vectors;
            public FloatDto[] floats;
            public string[] keywords;
            public int renderQueue = -1;   // -1 = 셰이더 기본
            public TagDto[] tags;          // 태그 오버라이드(RenderType 등)
        }
        [Serializable] internal class TextureRefDto : JsonDtoBase { public string name; public string texture; public string textureGuid; public bool linear; }
        [Serializable] internal class ColorDto : JsonDtoBase { public string name; public float r, g, b, a; }
        [Serializable] internal class FloatDto : JsonDtoBase { public string name; public float value; }
        [Serializable] internal class TagDto : JsonDtoBase { public string name; public string value; }
        [Serializable] internal class ModelDto : JsonDtoBase { public string file; public string[] materials; }   // 팩 모델 + 슬롯별 재질 id

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
            public ViewDto view;   // 뷰 종류 + 소리 자리
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
            public int    maxStack;      // 한 슬롯 최대 개수. 0 = 생략(기존 값 유지) — 무기·설치물은 1
            public bool   hideFromMenu;  // 아이템 고르는 목록(분배기 필터 등)에서 숨김 — 내부 탄약용
            public string icon;          // 스프라이트 이름 — 아틀라스 안에서 어느 스프라이트인지 고르는 열쇠이기도 하다
            public string iconGuid;      // 스프라이트를 담은 에셋의 guid — 이쪽이 파일을 특정한다
            // Ammo 전용 뷰 참조 — 탄 외형·연출 프리팹, 이름 + guid 짝(model/modelGuid 규약).
            // 값의 출처는 구 SO 배선에서 1회 유틸이 채움(5a-3a) — 뷰 카탈로그가 이 guid를 굽는다
            public string bullet, bulletGuid;             // 탄 외형(Bullet 컴포넌트 필수)
            public string muzzleFlash, muzzleFlashGuid;   // 총구 화염
            public string hitEffect, hitEffectGuid;       // 착탄/폭발 이펙트
            public EffectEntryDto[] attackEffects;  // Ammo 전용 — 1발의 명중 효과. null = 유지
            public float  damage;        // Ammo 전용 구 숏컷 — attackEffects가 없을 때만 {Damage, damage}로 변환
            public string gun;           // Weapon 전용 — 연결할 GunData id (예: "Gun:Rifle")
            // Ore 전용 — 이 원광 1개를 캐는 초. 손 채굴은 그대로, 채굴기는 ÷ speedMultiplier.
            // v2 exporter가 Ore 아이템마다 광맥 엔티티(entities/<item>_deposit)를 여기서 만든다 — 광맥은 따로 적지 않는다.
            public float  extractInterval = -1f;   // 음수 = 생략. Ore는 필수(>0), 다른 타입에 있으면 오류

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
            public ViewDto view;   // 뷰 종류 + 소리 자리
            public string id;            // 필수. 예: "Building:Assembler"
            public string kind;          // 필수 — 어느 서브클래스로 만들지 (KindMap 참조)
            public string displayName;   // 필수
            public string description;
            public string category;      // BuildingCategory 이름
            public ModelDto[] models;     // 팩 모델 배열 {file: "models/x.glb", materials: ["Material:…"(슬롯 순)]} — [0]이 기본, 나머지는 변형. 있으면 model/modelGuid 대신 이것이 v2 view.model이 된다(5a-4c)
            public string model;          // 모델 파일명 — 사람이 읽는 표시이자 guid가 죽었을 때의 폴백
            public string modelGuid;      // 모델 에셋 guid — 이쪽이 진실. 이름은 프로젝트에 둘 있으면 어느 쪽이 걸릴지 정해지지 않는다
            public string icon;           // 빌드 메뉴 아이콘 — 스프라이트 이름(아이템 icon과 같은 규약)
            public string iconGuid;       // 스프라이트를 담은 에셋의 guid
            // 배치 프리팹 참조 — 임포터가 model에서 굽는 산출물(둥지·나무는 손 프리팹)의 주소.
            // 뷰 카탈로그가 이 guid를 굽는다. 값은 1회 유틸이 SO에서 채우고, 임포터가 프리팹을 다시 구워도 guid는 유지된다(경로 불변)
            public Vec2Dto size;
            public PortDto[] ports;
            public int   inputSlots, outputSlots, bufferStackCap, requiredCoreTier, maxHp;
            public int   menuOrder;      // 같은 티어 안 표시 순서 (공정 단계). 건설 메뉴 정렬용
            public int   threatSeedCost = -1;   // 몬스터 위협도 시드(월드 칸=10). 코어 0 · 포탑 10 · 일반 80. 음수 = 생략(유지)
            public bool  hideFromBuildMenu;
            // SO 기본값과 같은 초기값을 준다 — json에서 빠진 건물이 철거 불가로 죽지 않게.
            // JsonUtility는 없는 필드를 0으로 밀지 않고 생성자 초기값을 남긴다
            // (minRange = -1f 와 같은 규약). bool? 는 JsonUtility가 아예 읽지 못한다.
            public bool isDemolishable = true, isAttackable = false;
            public bool walkable;                 // 밟고 지나갈 수 있는 건물(지뢰) — 길찾기가 땅으로 본다
            public SlotDto[] buildCost;

            // 종류별 전용 필드 — 해당 kind가 아니면 무시된다
            public float    speedMultiplier;      // Miner
            public float    speedTilesPerSec;     // Belt
            public string   modelCurveL, modelCurveR;
            public string   modelCurveLGuid, modelCurveRGuid;
            public string[] availableRecipes;     // Assembler
            public float    damageMultiplier, range, fireRate;   // Tower
            public float    minRange = -1f;                      // Tower — 최소 사거리(박격포 사각). 0 정당, 음수 = 생략(유지)
            public string   fireMode;                            // Tower — Projectile | Hitscan | Aura | None
            public float    muzzleHeight = -1f;                  // Tower — 0 정당, 음수 = 생략(유지)
            public bool     preferHighArc;                       // Tower — 곡사 시 고각 선택(박격포). bool은 항상 명시
            public float    turnSpeed = -1f;                     // Tower — 포탑 선회 속도(도/초). 0 정당(포탑 없음), 음수 = 생략(유지)
            public float    aimTolerance = -1f;                  // Tower — 조준 완료 허용 오차(도). 0 정당, 음수 = 생략(유지)
            public string   defaultAmmo;                         // Tower — 무공급(씬 배치) 폴백 탄 아이템 id. 생략 시 유지
            public string[] ammoFilter;                          // Tower — 탄창으로 쏘는 건물의 받는 탄
            public EffectEntryDto[] attackEffects;               // Tower — 탄창 없이 자기 효과로 쏘는 건물(지뢰·연료 없는 오라)의 명중 효과 → 팩 FixedAmmo
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

        /// <summary>몬스터 종류 — MonsterDataSO. 프리팹은 에셋 참조라 guid로 적는다(아이템 아이콘과 같은 규약).</summary>
        [Serializable] internal class MonsterDto : JsonDtoBase
        {
            public ViewDto view;   // 뷰 종류 + 소리 자리
            public string id;            // 필수. 예: "Monster:Basic"
            public string displayName;   // 필수
            public string description;
            public string model;         // 모델 프리팹 이름(Art/Models/Monsters — 리그·Animator·머티리얼을 안에 든 모델) — 사람이 읽는 용도
            public string modelGuid;     // 모델 에셋 guid — 이쪽이 파일을 특정한다
            public float  maxHp;
            public float  moveSpeed, rotateSpeed, crowdRadius, knockbackDamping;
            public bool   stickToGround = true;   // 주의: bool은 생략을 구분 못 한다 — 항상 명시할 것
            public float  attackRange, attackCooldown;
            public EffectEntryDto[] attackEffects;
            public float  maxPatience, patienceRadius, outsidePatienceDrain, rangedPokePatienceDrain,
                          patienceRecoverRate, absoluteLeashMultiplier, returnRegenPerSecond, returnTimeout;
        }

        /// <summary>주야 시계 — 낮 길이·밤(달이 뜨고 지는) 길이. TimeManager가 팩에서 읽는다.</summary>
        [Serializable] internal class DayCycleDto : JsonDtoBase { public float dayDuration = 360f, nightDuration = 10f; }

        /// <summary>밤 웨이브 규칙 — 점수식·명단·자극 버프·진입로 무리. 일차별 표(옛 웨이브 SO)는 없다. SO를 만들지 않고 v2 exporter가 wave 블록으로 낸다.</summary>
        [Serializable] internal class WaveRuleDto : JsonDtoBase
        {
            public float basePoints, dayPoints = 40f, gatePoints = 80f;     // score = (base + day×dayPoints + gate×gatePoints) × 총량(살아 있는 몫 + 강화분)
            public float stimulusAmplitude = 2f, stimulusExponent = 4f, stimulusLinear = 0.1f;   // 강화분 h(r) = A·r^p + b·r, r = 파괴/전체
            public StimulusBuffDto[] stimulusBuffs;
            public int   nestsPerNightMin = 1, nestsPerNightMax;             // max 0 = 전부
            public float targetNightLength = 60f; public int burstsPerNight = 4;   // 간격 = 길이 ÷ 수
            public float burstSpread = 2f;
            public RosterDto[] roster;
            public TrickleDto trickle;
        }
        [Serializable] internal class StimulusBuffDto : JsonDtoBase { public string effect; public float baseValue = 1f, perStimulus, min = 0.05f, max = 10f; }
        [Serializable] internal class RosterDto : JsonDtoBase { public string monster; public float cost = 10f, weight = 1f; public int minDay = 1, minGate; }
        [Serializable] internal class TrickleDto : JsonDtoBase { public string monster; public int group = 3; public float interval = 20f, untilKilledFraction = 0.9f; }
    }
}
#endif
