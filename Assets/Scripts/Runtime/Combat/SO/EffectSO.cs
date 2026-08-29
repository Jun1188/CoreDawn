using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 효과 정의 에셋 — 명중한 공격이 대상에게 "무슨 일을 일으키는가"의 단위. <b>데이터만 갖는다.</b>
    /// 행동은 심(<see cref="Effects"/>)이 종류(<see cref="Kind"/>)로 분기한다 — 에셋(UnityEngine.Object)은 심에 들어갈 수 없다.
    /// 심이 읽는 정의는 <see cref="EffectSpecs.Of"/>가 에셋마다 한 번 변환한 <see cref="EffectSpec"/>이다.
    ///
    /// 무기(GunData)·탄약(AmmoModuleSO)·근접(MonsterDataSO.attackEffects)·오라(TowerDataSO)가 <see cref="EffectEntry"/>
    /// 목록을 들고 있다가 발사·공격 시점에 <see cref="EffectSpecs.ToSim"/>으로 변환해 심에 넘긴다.
    ///
    /// 새 종류를 추가하려면: EffectKind에 값 → 이 클래스의 서브클래스(Kind만 돌려주면 된다) → 심 Effects의 분기 →
    /// GameDataImporter의 종류 표. 지속 효과면 <see cref="DurationEffectSO"/>를 상속한다.
    ///
    /// GameDataSO 상속인 이유: 효과도 json(GameData)이 소유하는 데이터다 — 임포터가
    /// id("Effect:이름")로 찾아 갱신하고, 탄약·총의 attackEffects가 id로 참조한다.
    /// </summary>
    public abstract class EffectSO : GameDataSO
    {
        public abstract EffectKind Kind { get; }

        /// <summary>심 정의로 변환 — <see cref="EffectSpecs"/>만 부른다(캐시가 참조 동일성을 보장한다).</summary>
        internal virtual EffectSpec BuildSpec() => new EffectSpec(SpecId, Kind);

        /// <summary>손으로 만든 에셋은 id가 비어 있을 수 있다 — 그때는 에셋 이름.</summary>
        protected string SpecId => string.IsNullOrEmpty(Id) ? name : Id;
    }
}
