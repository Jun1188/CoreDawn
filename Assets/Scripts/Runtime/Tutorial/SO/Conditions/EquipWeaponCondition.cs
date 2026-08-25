using UnityEngine;

[TutorialConditionMenu("전투/무기 장착 상태")]
public class EquipWeaponCondition : TutorialConditionSO
{
    public override bool Evaluate(TutorialObserver w, int baseline) => w.WeaponEquipped;

    public override string Summary => "무기 장착";
}
