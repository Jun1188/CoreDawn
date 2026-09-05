using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 총 정의 — 팩 guns 섹션. 수치를 <b>용도별 묶음</b>으로 둔다(사용자 지시 2026-09-03 "필드들을 용도별로 묶어줘"):
    ///   fire(발사) · ammo(탄) · aim(조준) · recoil(반동·킥백) · spread(탄퍼짐) · swing(근접 휘두름).
    /// 심은 fire·ammo만 읽고 나머지는 뷰(WeaponManager·Gun)가 읽는다 — 한 정의에 두는 이유는 총 하나의 수치가 한 곳에 모여야
    /// 밸런스를 만질 때 두 파일을 오가지 않기 때문이다. 묶음이 json에 없으면 기본값(반동·탄퍼짐 0, 스윙 없음)이라
    /// 근접 무기는 필요 없는 묶음을 아예 적지 않는다(내보내기도 기본값과 같은 키는 적지 않는다).
    /// 명중 효과·탄도는 탄약(<see cref="AmmoModuleDef"/>)이 정하고, 총은 장전 가능한 탄종 목록과 피해 배율만 갖는다(포탑과 같은 문법).
    /// </summary>
    public sealed class GunDef : Def
    {
        [JsonProperty("fire")] public GunFire Fire = new GunFire();
        [JsonProperty("ammo")] public GunAmmo Ammo = new GunAmmo();
        [JsonProperty("aim")] public GunAim Aim = new GunAim();
        [JsonProperty("recoil")] public GunRecoil Recoil = new GunRecoil();
        [JsonProperty("spread")] public GunSpread Spread = new GunSpread();
        [JsonProperty("swing")] public GunSwing Swing = new GunSwing();

        /// <summary>장전 가능한 탄종 — 첫 항목이 기본 탄종. json은 Resolve가, 코드 조립(테스트)은 직접 채운다.</summary>
        [JsonIgnore] public List<ItemDef> AmmoFilter { get; } = new List<ItemDef>();

        [JsonIgnore] public bool IsHitscan => Fire.Mode == "Hitscan";
        [JsonIgnore] public bool IsAura => Fire.Mode == "Aura";
        [JsonIgnore] public int RoundsPerTrigger => Fire.Pellets > 1 ? Fire.Pellets : 1;
        [JsonIgnore] public ItemDef DefaultAmmo => AmmoFilter.Count > 0 ? AmmoFilter[0] : null;

        /// <summary>방아쇠 한 번의 피해 총량(기본 탄의 즉발 피해 합 × 배율 × 펠릿 수) — 반동 연출 크기·툴팁 표기용. 전투 계산엔 쓰지 않는다.</summary>
        [JsonIgnore] public float BaseDamage
        {
            get
            {
                var ammo = DefaultAmmo?.Get<AmmoModuleDef>();
                float sum = 0f;
                if (ammo != null)
                    foreach (var e in ammo.Effects)
                        if (e.Spec != null && e.Spec.Kind == EffectKind.Damage) sum += e.Value;
                return sum * Fire.DamageMultiplier * RoundsPerTrigger;
            }
        }

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            // "fire": null 같은 명시적 null도 기본 묶음으로 — 소비자는 묶음이 항상 있다고 본다
            Fire ??= new GunFire(); Ammo ??= new GunAmmo(); Aim ??= new GunAim();
            Recoil ??= new GunRecoil(); Spread ??= new GunSpread(); Swing ??= new GunSwing();

            AmmoFilter.Clear();
            foreach (var id in Ammo.Filter)
            {
                var item = db.ResolveItem(id, errors, Id);
                if (item == null) continue;
                if (item.Get<AmmoModuleDef>() == null) { errors.Add($"{Id}: ammo.filter '{id}'은(는) 탄약(Ammo 모듈)이 아닙니다"); continue; }
                AmmoFilter.Add(item);
            }
            if (!Ammo.Unlimited && AmmoFilter.Count == 0) errors.Add($"{Id}: 탄을 쓰는 총인데 ammo.filter가 비었습니다");
            if (Fire.Mode != "Projectile" && Fire.Mode != "Hitscan" && Fire.Mode != "Aura") errors.Add($"{Id}: fire.mode는 Projectile|Hitscan|Aura — '{Fire.Mode}'");
        }
    }

    /// <summary>발사 — 심(간격·사거리·펠릿·배율)과 뷰(연사 입력)가 같이 본다.</summary>
    public sealed class GunFire
    {
        /// <summary>전달 방식 — Projectile(탄약의 탄을 날림) | Hitscan(즉시 판정) | Aura(총구 반경 range의 전원에게 — 근접 휘두름). 뷰(ProjectileSystem)가 읽는다.</summary>
        [JsonProperty("mode")] public string Mode = "Projectile";
        /// <summary>발사 간격(초) — 포탑의 fireRate(발/초)와 반대 단위라 이름을 interval로 했다. 낮을수록 빠른 연사.</summary>
        [JsonProperty("interval")] public float Interval = 0.2f;
        [JsonProperty("range")] public float Range = 100f;   // m
        /// <summary>방아쇠 한 번에 나가는 탄 수(샷건 8). 펠릿마다 탄퍼짐을 따로 받고 탄창도 그만큼 준다. 1 = 한 발.</summary>
        [JsonProperty("pellets")] public int Pellets = 1;
        /// <summary>눌고 있으면 연사 — 뷰(WeaponController)가 읽는다.</summary>
        [JsonProperty("automatic")] public bool Automatic;
        [JsonProperty("damageMultiplier")] public float DamageMultiplier = 1f;
    }

    /// <summary>탄 — 탄창·재장전·받는 탄종. 심이 읽는다.</summary>
    public sealed class GunAmmo
    {
        /// <summary>탄을 소비하지 않는 무기(근접) — 탄창은 늘 가득이고 재장전이 없다. magSize·reloadTime은 무의미.</summary>
        [JsonProperty("unlimited")] public bool Unlimited;
        [JsonProperty("magSize")] public int MagSize = 30;
        [JsonProperty("reloadTime")] public float ReloadTime = 1.5f;
        /// <summary>장전 가능한 탄종 id — 첫 항목이 기본 탄종.</summary>
        [JsonProperty("filter")] public List<string> Filter = new List<string>();
    }

    /// <summary>조준 — 뷰가 읽는다.</summary>
    public sealed class GunAim
    {
        /// <summary>조준(ADS) 불가 — 근접·투척. zoom은 무의미.</summary>
        [JsonProperty("block")] public bool Block;
        [JsonProperty("zoom")] public float Zoom = 1.3f;
    }

    /// <summary>반동(카메라)·킥백(무기 모델) — 뷰가 읽는다. 묶음이 없으면 반동 없음.</summary>
    public sealed class GunRecoil
    {
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("z")] public float Z;
        [JsonProperty("kickbackZ")] public float KickbackZ;
        [JsonProperty("kickbackRot")] public float[] KickbackRot;
    }

    /// <summary>탄퍼짐(도) — 뷰가 읽는다. 묶음이 없으면 탄퍼짐 없음.</summary>
    public sealed class GunSpread
    {
        [JsonProperty("base")] public float Base;
        [JsonProperty("max")] public float Max;
        [JsonProperty("perShot")] public float PerShot;
        [JsonProperty("recovery")] public float Recovery;
    }

    /// <summary>근접 휘두름 — 뷰가 읽는다. time ≤ 0(묶음 없음)이면 휘두르지 않고 킥백을 쓴다.</summary>
    public sealed class GunSwing
    {
        [JsonProperty("time")] public float Time = -1f;
        [JsonProperty("windup")] public float Windup = -1f;
        [JsonProperty("alternate")] public bool Alternate;
        [JsonProperty("rotation")] public float[] Rotation;
        [JsonProperty("position")] public float[] Position;
    }
}
