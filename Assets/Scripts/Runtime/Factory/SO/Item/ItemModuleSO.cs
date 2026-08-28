using UnityEngine;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 아이템 역할 모듈의 베이스 — 아이템의 전용 데이터를 상속 대신 **조합**으로 단다.
    ///
    /// 구 구조(ItemDataSO ← AmmoItemSO/WeaponItemSO)의 문제: 역할이 늘 때마다 서브클래스가
    /// 늘고, 한 아이템이 두 역할(예: 탄약이자 투척 무기)을 가질 수 없으며, 타입 승격이
    /// 필요할 때 에셋을 같은 id로 재생성해야 했다(참조 복구 의존).
    ///
    /// 지금 구조: 모든 아이템은 평평한 ItemDataSO 하나이고, 역할은 modules 목록의
    /// 모듈(AmmoModuleSO·WeaponModuleSO…)이 정의한다. 판정도 타입 검사 대신
    /// <c>item.GetModule&lt;T&gt;()</c>. 모듈은 아이템 에셋의 **서브에셋**으로 저장된다
    /// (파일 하나 = 아이템 + 그 모듈들 — 임포터가 관리).
    /// </summary>
    public abstract class ItemModuleSO : ScriptableObject
    {
    }
}
