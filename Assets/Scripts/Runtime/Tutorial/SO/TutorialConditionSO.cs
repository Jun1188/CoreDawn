using System;
using UnityEngine;

/// <summary>
/// 인스펙터의 "조건 추가 ▾" 메뉴에 보일 이름. 없으면 클래스 이름이 그대로 뜬다.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TutorialConditionMenuAttribute : Attribute
{
    public readonly string Label;
    public TutorialConditionMenuAttribute(string label) => Label = label;
}

/// <summary>
/// 튜토리얼 완료 조건 모듈 — 스텝 에셋의 <b>서브에셋</b>으로 저장된다 (ItemModuleSO와 같은 패턴).
///
/// 스텝은 조건 목록을 갖고, 전부 충족해야 끝난다. 디자이너는 새 스텝을 만들 때 이 모듈들을
/// 인스펙터에서 골라 조합한다 — 조건 종류를 더하는 것만 프로그래머의 일이다(파일 하나).
///
/// 구 구조(enum + 판정 표)와 다른 점: 조건의 파라미터와 판정 코드가 <b>같은 클래스</b>에 있다.
/// 인스펙터에는 그 조건에 실제로 쓰이는 필드만 보이고, 무의미한 파라미터 조합을 저작할 수 없다.
///
/// 조건이 하나도 없는 스텝은 영영 끝나지 않는다 — 저작 중인 스텝을 막아 두는 상태다.
/// </summary>
public abstract class TutorialConditionSO : ScriptableObject
{
    /// <summary>
    /// 충족했는가. <paramref name="baseline"/>은 이 스텝이 화면에 뜬 순간의 <see cref="CounterOf"/> 값이고,
    /// 아직 뜬 적 없는 스텝은 0이다 — 절대형 조건은 무시하면 된다.
    /// </summary>
    public abstract bool Evaluate(TutorialObserver world, int baseline);

    /// <summary>누적형 조건의 기준점으로 쓸 현재 값. 절대형은 0 (기준점이 무의미하다).</summary>
    public virtual int CounterOf(TutorialObserver world) => 0;

    /// <summary>인스펙터 머리줄 한 줄 요약 — 디자이너가 조건 목록을 펼치지 않고도 읽게.</summary>
    public abstract string Summary { get; }
}

/// <summary>
/// 누적형 조건의 공통 뼈대 — "이 안내가 뜬 뒤로 <see cref="count"/>번 더".
///
/// 서브클래스는 단조 증가 카운터 하나(<see cref="Counter"/>)만 정의한다. 기준점을 빼는 판정식은
/// 여기 한 곳에 봉인돼 있어(sealed) 카운터와 판정이 어긋날 방법이 없다 — 구 구조에서
/// CounterOf와 Evaluate 두 switch가 각자 트리거를 나열하던 함정을 타입으로 막는다.
/// </summary>
public abstract class CumulativeConditionSO : TutorialConditionSO
{
    [Tooltip("이 안내가 뜬 뒤로 몇 번 더 해야 하는가.")]
    [Min(1)] public int count = 1;

    /// <summary>관측기에서 읽을 단조 증가 카운터.</summary>
    protected abstract int Counter(TutorialObserver world);

    /// <summary>요약에 쓸 동사구 — "자원 캐기", "건물 설치" 처럼.</summary>
    protected abstract string Verb { get; }

    public sealed override int CounterOf(TutorialObserver world) => Counter(world);

    public sealed override bool Evaluate(TutorialObserver world, int baseline)
        => Counter(world) - baseline >= Mathf.Max(1, count);

    public override string Summary => $"{Verb} ×{Mathf.Max(1, count)}";
}
