using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    // ═══════════════════════════════════════════════════════════
    //  view 블록의 정의 — 심의 Def.View(JObject)를 뷰 계층이 읽는 꼴. 섹션마다 다르다(사용자 지적 2026-09-03:
    //  "view도 class 있는 데이터인데 왜 추론으로 하는거야" — 편집기가 힌트로 흉내 내던 것을 실제 클래스로).
    //  런타임(조립기·PackAssets·Gun)과 편집기(Raw 탭 스키마)가 같은 클래스를 쓴다 — 키가 여기와 다르면
    //  팩 로드 때 오류(SimSchema 직렬화기: 모르는 키 = 오류)다. 심은 view를 모른다(Def.View는 그대로 JObject).
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 소리를 <b>쓰는 자리</b> 하나 — <c>{sound, volume, spatial}</c>. 소리 자체(클립 묶음)는 팩 sounds 섹션(<see cref="SoundDef"/>)이고,
    /// 얼마나 크게·어디서 나는가는 쓰는 자리가 정한다(EffectSpec ↔ EffectUse와 같은 구분). view.sfx와 팩 최상위 sfx가 이 꼴이다.
    /// </summary>
    public sealed class SoundUse
    {
        [JsonProperty("sound")] public string Sound;
        [JsonProperty("volume")] public float Volume = 1f;
        [JsonProperty("spatial")] public bool Spatial = true;
    }

    /// <summary>모델 한 항목 — 팩 glb <c>{file, materials[]}</c>: materials[i]는 glb 재질 슬롯 i에 꽂을 팩 재질 id.</summary>
    public sealed class ModelRef
    {
        [JsonProperty("file")] public string File = "";
        [JsonProperty("materials")] public List<string> Materials = new List<string>();
    }

    /// <summary>부모(손·홀더·건물 루트) 기준 자세 — position, rotation(오일러), scale. 없는 값은 원점·단위.</summary>
    public sealed class PoseDef
    {
        [JsonProperty("position")] public float[] Position;
        [JsonProperty("rotation")] public float[] Rotation;
        [JsonProperty("scale")] public float Scale = 1f;

        public (Vector3 position, Quaternion rotation, float scale) ToTRS()
            => (ViewDefs.Vec3(Position, Vector3.zero), Quaternion.Euler(ViewDefs.Vec3(Rotation, Vector3.zero)), Scale);

        public static readonly (Vector3, Quaternion, float) Identity = (Vector3.zero, Quaternion.identity, 1f);
    }

    public sealed class BoxDef
    {
        [JsonProperty("center")] public float[] Center;
        [JsonProperty("size")] public float[] Size;
    }

    public sealed class CapsuleDef
    {
        [JsonProperty("center")] public float[] Center;
        [JsonProperty("radius")] public float Radius = 0.5f;
        [JsonProperty("height")] public float Height = 1f;
    }

    /// <summary>데이터로 적은 콜라이더(칸 단위, 루트 기준) — 자식 오브젝트(name)로 붙는다. box나 capsule 중 하나.</summary>
    public sealed class ColliderDef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("layer")] public string Layer;
        [JsonProperty("box")] public BoxDef Box;
        [JsonProperty("capsule")] public CapsuleDef Capsule;
    }

    /// <summary>타워 리그 노드 이름 — 블렌더 규약(YawPivot → PitchPivot → Droop → Recoil, 총구 Muzzle_*)을 바꿀 때만 적는다.</summary>
    public sealed class RigDef
    {
        [JsonProperty("yaw")] public string Yaw;
        [JsonProperty("pitch")] public string Pitch;
        [JsonProperty("droop")] public string Droop;
        [JsonProperty("recoil")] public string Recoil;
        [JsonProperty("muzzle")] public string Muzzle;
    }

    /// <summary>몬스터 상태 → glb 클립 이름. attack·hit는 여러 개 중 무작위.</summary>
    public sealed class AnimDef
    {
        [JsonProperty("idle")] public string Idle;
        [JsonProperty("walk")] public string Walk;
        [JsonProperty("run")] public string Run;
        [JsonProperty("alert")] public string Alert;
        [JsonProperty("death")] public string Death;
        [JsonProperty("attack")] public List<string> Attack = new List<string>();
        [JsonProperty("hit")] public List<string> Hit = new List<string>();
    }

    /// <summary>탄에 얹을 넉백 — 탄약이 넉백을 직접 명시하지 않았을 때만, 피해 합 × perDamage.</summary>
    public sealed class KnockbackDef
    {
        [JsonProperty("effect")] public string Effect;
        [JsonProperty("perDamage")] public float PerDamage = 0.03f;
    }

    /// <summary>아이콘 — 팩 png + 좌표표(같은 이름의 .json 사이드카)의 프레임 이름.</summary>
    public sealed class IconDef
    {
        [JsonProperty("file")] public string File = "";
        [JsonProperty("frame")] public string Frame = "";
    }

    public sealed class TextureRef
    {
        [JsonProperty("file")] public string File = "";
        [JsonProperty("linear")] public bool Linear;
    }

    /// <summary>
    /// entities의 view — 건물(BuildingAssembler)·몬스터(MonsterAssembler)·타워 리그(TowerVisualController)·벨트 커브.
    /// type은 <see cref="ViewSchema.Types"/>의 키.
    /// </summary>
    public sealed class EntityViewDef
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("model")] public List<ModelRef> Model = new List<ModelRef>();
        [JsonProperty("pose")] public PoseDef Pose;
        [JsonProperty("sfx")] public Dictionary<string, SoundUse> Sfx = new Dictionary<string, SoundUse>();
        /// <summary>루트 레이어 — 기본 Entity(광맥 몸은 Ground, 둥지는 Nest).</summary>
        [JsonProperty("layer")] public string Layer;
        /// <summary>렌더러마다 MeshCollider — false면 데이터 콜라이더(colliders)만 쓴다.</summary>
        [JsonProperty("meshCollider")] public bool MeshCollider = true;
        [JsonProperty("colliders")] public List<ColliderDef> Colliders = new List<ColliderDef>();
        /// <summary>몬스터 몸 캡슐.</summary>
        [JsonProperty("collider")] public CapsuleDef Collider;
        [JsonProperty("rig")] public RigDef Rig;
        [JsonProperty("anim")] public AnimDef Anim;
        /// <summary>몬스터 사망 연출 — MonsterVisualController.DeathStyle 이름.</summary>
        [JsonProperty("deathStyle")] public string DeathStyle;
        [JsonProperty("sinkDepth")] public float SinkDepth = 1.5f;
        [JsonProperty("deathDelay")] public float DeathDelay = 2f;
        /// <summary>항상 도는 glb 클립(벨트 모프 애니) 이름.</summary>
        [JsonProperty("loop")] public string Loop;
        // 벨트 커브 — 모양별 모델·자세·루프
        [JsonProperty("modelCurveL")] public List<ModelRef> ModelCurveL = new List<ModelRef>();
        [JsonProperty("modelCurveR")] public List<ModelRef> ModelCurveR = new List<ModelRef>();
        [JsonProperty("poseCurveL")] public PoseDef PoseCurveL;
        [JsonProperty("poseCurveR")] public PoseDef PoseCurveR;
        [JsonProperty("loopCurveL")] public string LoopCurveL;
        [JsonProperty("loopCurveR")] public string LoopCurveR;

        /// <summary>모양별 모델 목록 — 커브 모양은 커브 모델만 본다(없으면 빈 목록 → 조립기가 자리표시 상자).</summary>
        public IReadOnlyList<ModelRef> ModelsFor(BeltShape shape)
            => shape == BeltShape.CurveL ? ModelCurveL : shape == BeltShape.CurveR ? ModelCurveR : Model;

        /// <summary>모양별 자세 — 커브 자세가 없으면 pose.</summary>
        public (Vector3 position, Quaternion rotation, float scale) PoseFor(BeltShape shape)
        {
            var p = shape == BeltShape.CurveL && PoseCurveL != null ? PoseCurveL
                  : shape == BeltShape.CurveR && PoseCurveR != null ? PoseCurveR
                  : Pose;
            return p != null ? p.ToTRS() : PoseDef.Identity;
        }

        public string LoopFor(BeltShape shape)
            => shape == BeltShape.CurveL ? LoopCurveL : shape == BeltShape.CurveR ? LoopCurveR : Loop;

        /// <summary>이름의 소리 자리. 없으면 null — 그 연출은 소리 없이 지나간다(빠진 이름은 로드 때 검증됐다).</summary>
        public SoundUse SfxOf(string name) => Sfx != null && Sfx.TryGetValue(name, out var u) ? u : null;
    }

    /// <summary>guns의 view — WeaponManager가 조립하고 Gun이 소리·넉백을 읽는다. type은 "Gun".</summary>
    public sealed class GunViewDef
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("model")] public List<ModelRef> Model = new List<ModelRef>();
        [JsonProperty("pose")] public PoseDef Pose;
        [JsonProperty("sfx")] public Dictionary<string, SoundUse> Sfx = new Dictionary<string, SoundUse>();
        /// <summary>총구 앵커(모델 기준). 없으면 모델의 MuzzlePoint 노드.</summary>
        [JsonProperty("muzzle")] public float[] Muzzle;
        /// <summary>가늠자 앵커(모델 기준). 없으면 모델의 SightPos 노드.</summary>
        [JsonProperty("sight")] public float[] Sight;
        [JsonProperty("knockback")] public KnockbackDef Knockback;

        public (Vector3 position, Quaternion rotation, float scale) PoseTRS => Pose != null ? Pose.ToTRS() : PoseDef.Identity;
        public Vector3? MuzzleOffset => Muzzle != null && Muzzle.Length >= 3 ? ViewDefs.Vec3(Muzzle, Vector3.zero) : (Vector3?)null;
        public Vector3? SightOffset => Sight != null && Sight.Length >= 3 ? ViewDefs.Vec3(Sight, Vector3.zero) : (Vector3?)null;
        public SoundUse SfxOf(string name) => Sfx != null && Sfx.TryGetValue(name, out var u) ? u : null;
    }

    /// <summary>items의 view — 아이콘(PackAssets)과 탄약 연출 이름(내장 Effects: Resources/Builtin/Effects/&lt;이름&gt;).</summary>
    public sealed class ItemViewDef
    {
        [JsonProperty("icon")] public IconDef Icon;
        [JsonProperty("bullet")] public string Bullet;
        [JsonProperty("muzzleFlash")] public string MuzzleFlash;
        [JsonProperty("hitEffect")] public string HitEffect;
    }

    /// <summary>sounds의 view — 변형 클립 묶음(팩 상대 경로).</summary>
    public sealed class SoundViewDef
    {
        [JsonProperty("clips")] public List<string> Clips = new List<string>();
    }

    /// <summary>materials의 view — PackAssets.MaterialOf가 셰이더(BuiltinShaders 목록)에 꽂는 값.</summary>
    public sealed class MaterialViewDef
    {
        [JsonProperty("shader")] public string Shader;
        [JsonProperty("textures")] public Dictionary<string, TextureRef> Textures = new Dictionary<string, TextureRef>();
        [JsonProperty("colors")] public Dictionary<string, float[]> Colors = new Dictionary<string, float[]>();
        [JsonProperty("vectors")] public Dictionary<string, float[]> Vectors = new Dictionary<string, float[]>();
        [JsonProperty("floats")] public Dictionary<string, float> Floats = new Dictionary<string, float>();
        [JsonProperty("keywords")] public List<string> Keywords = new List<string>();
        [JsonProperty("tags")] public Dictionary<string, string> Tags = new Dictionary<string, string>();
        /// <summary>0이면 셰이더 기본.</summary>
        [JsonProperty("renderQueue")] public int RenderQueue;
    }

    public static class ViewDefs
    {
        public static Vector3 Vec3(float[] a, Vector3 dflt) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : dflt;
        public static Vector4 Vec4(float[] a, Vector4 dflt) => a != null && a.Length >= 4 ? new Vector4(a[0], a[1], a[2], a[3]) : dflt;
    }
}
