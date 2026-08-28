using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Tutorial
{
    [TutorialConditionMenu("아이템/분류별 손 제작")]
    public class CraftItemTypeCondition : CumulativeConditionSO
    {
        [Tooltip("이 분류의 물건을 손으로 만들면 센다. 갖고 있는지가 아니라 만들었는지를 묻는다.")]
        public ItemType itemType = ItemType.Weapon;

        protected override int Counter(TutorialObserver w) => w.CraftedOfType(itemType);

        protected override string Verb => $"{itemType} 손 제작";
    }
}
