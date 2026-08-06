using UnityEngine;

/// <summary>
/// 효과 정의의 베이스 — 명중한 공격이 대상에게 "무슨 일을 일으키는가"의 단위.
/// 로직은 하위 클래스(C#)가, 수치는 SO 에셋이 갖는다 (AmmoItemSO 등과 같은 패턴).
///
/// 무기(GunData)·포탑(TowerDataSO)·근접(CombatComponent)이 EffectSO 배열을 들고 있다가
/// 명중 시 Entity.ApplyEffects로 전달한다. 배열을 비우면 Power만큼의 순수 피해로 동작하므로
/// 기존 프리팹·데이터에 에셋을 일일이 꽂지 않아도 예전과 같다.
///
/// 즉시 효과(피해·회복)는 Apply에서 끝나고,
/// 지속 효과는 <see cref="DurationEffectSO"/>를 상속해 대상의 EffectController에 등록된다.
/// </summary>
public abstract class EffectSO : ScriptableObject
{
    public abstract void Apply(Entity target, in EffectContext ctx);
}
