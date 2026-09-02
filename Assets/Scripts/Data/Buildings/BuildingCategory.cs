using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace CoreDawn.Data
{
    /// <summary>
    /// 빌드 메뉴 분류 — BuildMenuView가 이 순서대로 그룹·정렬한다(정의의 Building.category 문자열을 이 enum으로 읽는다).
    /// (예전 YAGNI로 제거했던 카테고리의 부활 — 이제 UI 정렬이라는 실소비자가 있다)
    /// </summary>
    public enum BuildingCategory
    {
        // 값을 못 박는다 — SO 에셋이 정수로 직렬화한다. Storage(2)는 물류로 합쳐 삭제(2026-09-01, 사용자 지시)
        Production = 0,   // 생산 — 채굴기, 조립기
        Logistics  = 1,   // 물류 — 벨트, 분배기, 합류기, 보관소
        Defense    = 3,   // 방어 — 포탑 (밤 웨이브)
    }
}
