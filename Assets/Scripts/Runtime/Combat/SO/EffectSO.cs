using UnityEngine;

/// <summary>
/// 효과 정의의 베이스 — 명중한 공격이 대상에게 "무슨 일을 일으키는가"의 단위.
/// 정의(에셋)는 행동과 형태만 갖고, 크기는 시전측 EffectEntry.value가
/// EffectContext.Value로 흘러 들어온다.
///
/// 무기(GunData)·탄약(AmmoModuleSO)·근접(CombatComponent)·오라(TowerDataSO)가
/// EffectEntry 목록을 들고 있다가 명중 시 Entity.ApplyEffects로 전달한다.
///
/// 즉시 효과(피해·회복·넉백)는 Apply에서 끝나고,
/// 지속 효과는 <see cref="DurationEffectSO"/>를 상속해 대상의 EffectController에 등록된다.
///
/// GameDataSO 상속인 이유: 효과도 json(GameData)이 소유하는 데이터다 — 임포터가
/// id("Effect:이름")로 찾아 갱신하고, 탄약·총의 attackEffects가 id로 참조한다.
/// </summary>
public abstract class EffectSO : GameDataSO
{
    public abstract void Apply(Entity target, in EffectContext ctx);
}
