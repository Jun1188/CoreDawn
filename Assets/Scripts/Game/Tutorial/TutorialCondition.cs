using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Tutorial
{
    /// <summary>편집기(GameData 튜토리얼 탭)의 "조건 추가 ▾" 메뉴에 보일 이름. 없으면 클래스 이름이 그대로 뜬다.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TutorialConditionMenuAttribute : Attribute
    {
        public readonly string Label;
        public TutorialConditionMenuAttribute(string label) => Label = label;
    }

    /// <summary>
    /// 튜토리얼 완료 조건 모듈 — 팩 tutorial 섹션의 <see cref="TutorialConditionDef"/>(값)를 받아 판정한다(plain C#).
    ///
    /// 스텝은 조건 목록을 갖고, 전부 충족해야 끝난다. 디자이너는 편집기에서 조건 종류를 고르고 값을 적는다 —
    /// 조건 종류를 더하는 것만 프로그래머의 일이다(클래스 하나 + <see cref="TutorialConditions"/> 표 한 줄).
    /// 조건의 파라미터와 판정 코드가 <b>같은 클래스</b>에 있어 무의미한 파라미터 조합을 저작할 수 없다.
    /// public 필드 이름(count·seconds·tier·itemType·item)은 편집기가 반사로 읽어 그린다 — 바꾸면 편집기 스위치도 같이.
    /// </summary>
    public abstract class TutorialCondition
    {
        /// <summary>팩 값으로 자신을 채운다 — 종류마다 읽는 필드가 다르다.</summary>
        public virtual void Configure(TutorialConditionDef def) { }

        /// <summary>
        /// 충족했는가. <paramref name="baseline"/>은 이 스텝이 화면에 뜬 순간의 <see cref="CounterOf"/> 값이고,
        /// 아직 뜬 적 없는 스텝은 0이다 — 절대형 조건은 무시하면 된다.
        /// </summary>
        public abstract bool Evaluate(TutorialObserver world, int baseline);

        /// <summary>누적형 조건의 기준점으로 쓸 현재 값. 절대형은 0 (기준점이 무의미하다).</summary>
        public virtual int CounterOf(TutorialObserver world) => 0;

        /// <summary>한 줄 요약 — 디버그·편집기 표시용.</summary>
        public abstract string Summary { get; }
    }

    /// <summary>
    /// 누적형 조건의 공통 뼈대 — "이 안내가 뜬 뒤로 <see cref="count"/>번 더".
    /// 서브클래스는 단조 증가 카운터 하나(<see cref="Counter"/>)만 정의한다. 기준점을 빼는 판정식은
    /// 여기 한 곳에 봉인돼 있어(sealed) 카운터와 판정이 어긋날 방법이 없다.
    /// </summary>
    public abstract class CumulativeCondition : TutorialCondition
    {
        /// <summary>이 안내가 뜬 뒤로 몇 번 더 해야 하는가.</summary>
        public int count = 1;

        public override void Configure(TutorialConditionDef def) => count = Mathf.Max(1, def.Count);

        protected abstract int Counter(TutorialObserver world);
        protected abstract string Verb { get; }

        public sealed override int CounterOf(TutorialObserver world) => Counter(world);
        public sealed override bool Evaluate(TutorialObserver world, int baseline) => Counter(world) - baseline >= Mathf.Max(1, count);
        public override string Summary => $"{Verb} ×{Mathf.Max(1, count)}";
    }

    /// <summary>
    /// 조건 종류 표 — 팩의 "type" 문자열 → 조건 클래스(SimSchema와 같은 명시 표; 런타임에 리플렉션 없음).
    /// "Condition" 접미는 있어도 없어도 된다(MineResource / MineResourceCondition).
    /// </summary>
    public static class TutorialConditions
    {
        static readonly Dictionary<string, Func<TutorialCondition>> Table = new Dictionary<string, Func<TutorialCondition>>(StringComparer.Ordinal)
        {
            ["AcquireItem"] = () => new AcquireItemCondition(),
            ["AcquireItemType"] = () => new AcquireItemTypeCondition(),
            ["CoreTier"] = () => new CoreTierCondition(),
            ["CraftItemType"] = () => new CraftItemTypeCondition(),
            ["CycleBeltShape"] = () => new CycleBeltShapeCondition(),
            ["DemolishBuilding"] = () => new DemolishBuildingCondition(),
            ["EnterBuildMode"] = () => new EnterBuildModeCondition(),
            ["EquipWeapon"] = () => new EquipWeaponCondition(),
            ["Jump"] = () => new JumpCondition(),
            ["MineResource"] = () => new MineResourceCondition(),
            ["MoveAndLook"] = () => new MoveAndLookCondition(),
            ["NightReached"] = () => new NightReachedCondition(),
            ["OpenInventory"] = () => new OpenInventoryCondition(),
            ["PlaceBelt"] = () => new PlaceBeltCondition(),
            ["PlaceBuilding"] = () => new PlaceBuildingCondition(),
            ["Slide"] = () => new SlideCondition(),
            ["Sprint"] = () => new SprintCondition(),
            ["SurviveNight"] = () => new SurviveNightCondition(),
            ["SwitchHotbarSlot"] = () => new SwitchHotbarSlotCondition(),
        };

        public static IEnumerable<string> Kinds => Table.Keys;

        /// <summary>정의에서 조건을 만든다. 모르는 type은 null — 호출부가 소리 내어 알린다.</summary>
        public static TutorialCondition Create(TutorialConditionDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Type)) return null;
            string key = def.Type.EndsWith("Condition", StringComparison.Ordinal) ? def.Type.Substring(0, def.Type.Length - 9) : def.Type;
            if (!Table.TryGetValue(key, out var make)) return null;
            var c = make();
            c.Configure(def);
            return c;
        }
    }
}
