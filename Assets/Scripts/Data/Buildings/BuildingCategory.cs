using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Sim;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>
    /// 빌드 메뉴 분류 — BuildingDatabaseSO가 이 순서대로 그룹·정렬한다.
    /// (예전 YAGNI로 제거했던 카테고리의 부활 — 이제 UI 정렬이라는 실소비자가 있다)
    /// </summary>
    public enum BuildingCategory
    {
        Production,   // 생산 — 채굴기, 조립기
        Logistics,    // 물류 — 벨트, 분배기, 합류기
        Storage,      // 저장 — 보관소
        Defense,      // 방어 — 포탑 (밤 웨이브)
    }
}
