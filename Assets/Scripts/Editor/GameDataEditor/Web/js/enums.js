// ═══════════════════════════════════════════════════════════
//  enum · 상수 — C# 쪽 정의와 짝을 이룬다.
//  임포터(GameDataImporter.cs)에 값이 추가되면 여기도 같이 고쳐야 한다.
// ═══════════════════════════════════════════════════════════
window.EdEnum = {
  ITEM_TYPES: [
  { v:"Ore",        ko:"원광",      desc:"채굴로만 얻는 원자재" },
  { v:"Ingot",      ko:"소재",      desc:"제련 산출물" },
  { v:"Part",       ko:"부품",      desc:"제작·조립 중간재" },
  { v:"RepairPart", ko:"수리 부품", desc:"게이트 납품 전용" },
  { v:"Ammo",       ko:"탄약",      desc:"무기·포탑 소모품" },
  { v:"Weapon",     ko:"무기",      desc:"손에 드는 것" },
  { v:"Armor",      ko:"방어구",    desc:"착용 장비 (부위 구분 없음)" },
  { v:"Salvage",    ko:"회수물",    desc:"수집으로만 얻고 제작 불가" },
],

  LINE_COLOR: { Iron:"#E8A54B", Copper:"#4FD8E0", Crystal:"#B48CFF", Beast:"#FF5D73", None:"#8FA3C0" },

  CATEGORIES: [
  { v:"Production", ko:"생산" }, { v:"Logistics", ko:"물류" },
  { v:"Storage",    ko:"저장" }, { v:"Defense",   ko:"방어" },
],

  KINDS: [
  { v:"Miner",     ko:"채굴기",   cat:"Production", extra:["speedMultiplier"] },
  { v:"Assembler", ko:"조립기",   cat:"Production", extra:["availableRecipes"] },
  { v:"Belt",      ko:"벨트",     cat:"Logistics",  extra:["speedTilesPerSec","curveLPrefab","curveRPrefab"] },
  { v:"Splitter",  ko:"분배기",   cat:"Logistics",  extra:[] },
  { v:"Merger",    ko:"합류기",   cat:"Logistics",  extra:[] },
  { v:"Storage",   ko:"보관소",   cat:"Storage",    extra:[] },
  { v:"DronePort", ko:"드론 스테이션", cat:"Logistics", extra:["droneRange","carryCapacity","travelSpeed"] },
  { v:"Core",      ko:"코어",     cat:"Production", extra:["tiers"] },
  { v:"Tower",     ko:"방어 타워", cat:"Defense",    extra:["damageMultiplier","range","fireRate","ammoFilter"] },
],

  DIRS: ["North","East","South","West"],

  DVEC: { North:[0,1], East:[1,0], South:[0,-1], West:[-1,0] },

  TIER_MARK: ["⓪","①","②","③","④","⑤"],

  EFFECT_KINDS: [
  { v:"Damage",         ko:"피해",        dur:false, desc:"value 만큼 체력을 깎는다" },
  { v:"Heal",           ko:"회복",        dur:false, desc:"value 만큼 체력을 채운다" },
  { v:"Knockback",      ko:"넉백",        dur:false, desc:"value = 밀어내는 거리" },
  { v:"DamageOverTime", ko:"지속 피해",    dur:true,  tick:true, desc:"tickInterval 마다 value 피해" },
  { v:"MoveSpeed",      ko:"이동속도",     dur:true,  desc:"value = 배율 (0.5 = 절반)" },
  { v:"AttackModifier", ko:"공격 증폭",    dur:true,  affects:true, desc:"affects 의 효과를 value 배" },
  { v:"IncomingDamage", ko:"받는 피해",    dur:true,  desc:"value = 받는 피해 배율" },
],

  FIRE_MODES: ["Projectile", "Hitscan", "Aura"],

  STACKING: ["Refresh", "Stack"],

};
