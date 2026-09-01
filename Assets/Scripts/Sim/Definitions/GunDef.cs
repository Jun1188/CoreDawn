using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 총 정의 — 팩 guns 섹션. 심이 읽는 값(탄창·재장전·연사 간격·펠릿·사거리·받는 탄·배율)과 뷰가 읽는 감각 값(반동·탄퍼짐·스윙·줌)이
    /// 한 정의에 있다 — 총 하나의 수치는 한 곳에 모여 있어야 밸런스를 만질 때 두 파일을 오가지 않는다. 심은 앞쪽만 본다.
    /// 명중 효과·탄도는 탄약(<see cref="AmmoModuleDef"/>)이 정하고, 총은 장전 가능한 탄종 목록과 피해 배율만 갖는다(포탑과 같은 문법).
    /// </summary>
    public sealed class GunDef : Def
    {
        [JsonProperty("isAutomatic")] public bool IsAutomatic;
        /// <summary>탄을 소비하지 않는 무기(근접) — 탄창은 늘 가득이고 재장전이 없다.</summary>
        [JsonProperty("unlimitedAmmo")] public bool UnlimitedAmmo;
        [JsonProperty("blockAim")] public bool BlockAim;
        /// <summary>전달 방식 — Projectile(탄약의 탄을 날림) | Hitscan(즉시 판정) | Aura(총구 반경 range의 전원에게 — 근접 휘두름). 뷰(ProjectileSystem)가 읽는다.</summary>
        [JsonProperty("fireMode")] public string FireMode = "Projectile";
        /// <summary>발사 간격(초) — 포탑의 fireRate(발/초)와 달리 옛 GunData 규약 그대로 "초". 낮을수록 빠른 연사.</summary>
        [JsonProperty("fireRate")] public float FireRate = 0.2f;
        [JsonProperty("range")] public float Range = 100f;          // m
        [JsonProperty("reloadTime")] public float ReloadTime = 1.5f;
        [JsonProperty("zoomMultiplier")] public float ZoomMultiplier = 1.3f;
        [JsonProperty("magSize")] public int MagSize = 30;
        /// <summary>방아쇠 한 번에 나가는 탄 수(샷건 8). 펠릿마다 탄퍼짐을 따로 받고 탄창도 그만큼 준다. 0·1 = 한 발.</summary>
        [JsonProperty("pellets")] public int Pellets = 1;
        [JsonProperty("ammoFilter")] public List<string> AmmoFilterIds = new List<string>();
        [JsonProperty("damageMultiplier")] public float DamageMultiplier = 1f;

        // ── 감각 튜닝(뷰가 읽는다) ──
        [JsonProperty("xRecoil")] public float XRecoil = 3f;
        [JsonProperty("yRecoil")] public float YRecoil = 2f;
        [JsonProperty("zRecoil")] public float ZRecoil = 1f;
        [JsonProperty("visualKickbackZ")] public float VisualKickbackZ = 1f;
        [JsonProperty("visualKickbackRot")] public float[] VisualKickbackRot;
        [JsonProperty("baseSpread")] public float BaseSpread = 0.5f;
        [JsonProperty("maxSpread")] public float MaxSpread = 5f;
        [JsonProperty("spreadIncreasePerShot")] public float SpreadIncreasePerShot = 1f;
        [JsonProperty("spreadRecoveryRate")] public float SpreadRecoveryRate = 5f;
        [JsonProperty("swingTime")] public float SwingTime = -1f;
        [JsonProperty("swingWindup")] public float SwingWindup = -1f;
        [JsonProperty("swingAlternate")] public bool SwingAlternate;
        [JsonProperty("swingRotation")] public float[] SwingRotation;
        [JsonProperty("swingPosition")] public float[] SwingPosition;

        /// <summary>장전 가능한 탄종 — 첫 항목이 기본 탄종. json은 Resolve가, 코드 조립(테스트)은 직접 채운다.</summary>
        [JsonIgnore] public List<ItemDef> AmmoFilter { get; } = new List<ItemDef>();

        [JsonIgnore] public bool IsHitscan => FireMode == "Hitscan";
        [JsonIgnore] public bool IsAura => FireMode == "Aura";
        [JsonIgnore] public int RoundsPerTrigger => Pellets > 1 ? Pellets : 1;
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
                return sum * DamageMultiplier * RoundsPerTrigger;
            }
        }

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            AmmoFilter.Clear();
            foreach (var id in AmmoFilterIds)
            {
                var item = db.ResolveItem(id, errors, Id);
                if (item == null) continue;
                if (item.Get<AmmoModuleDef>() == null) { errors.Add($"{Id}: ammoFilter '{id}'은(는) 탄약(Ammo 모듈)이 아닙니다"); continue; }
                AmmoFilter.Add(item);
            }
            if (!UnlimitedAmmo && AmmoFilter.Count == 0) errors.Add($"{Id}: 탄을 쓰는 총인데 ammoFilter가 비었습니다");
            if (FireMode != "Projectile" && FireMode != "Hitscan" && FireMode != "Aura") errors.Add($"{Id}: 총의 fireMode는 Projectile|Hitscan|Aura — '{FireMode}'");
        }
    }
}
