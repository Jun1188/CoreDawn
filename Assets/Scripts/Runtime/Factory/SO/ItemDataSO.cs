using System;
using UnityEngine;

/// <summary>
/// 아이템의 용도 축 — 무엇으로 쓰는가. 획득/소비 경로가 갈리는 지점에서만 나눈다.
/// </summary>
public enum ItemType
{
    /// <summary>원광 — 채굴기·수동 채굴로만 획득. 레시피 산출물이 될 수 없다.</summary>
    Ore,
    /// <summary>소재 — 제련로 산출물. 원광 1종 → 소재 1종 대응.</summary>
    Ingot,
    /// <summary>부품 — 다른 레시피의 재료로 들어가는 중간재.</summary>
    Part,
    /// <summary>수리 부품 — 코어 납품 전용. 다른 용도가 없다 = 생산 라인의 최종 목적지.</summary>
    RepairPart,
    /// <summary>탄약 — 소비되어 사라진다. 플레이어 무기와 포탑이 공유.</summary>
    Ammo,
    /// <summary>무기 — 장착해서 쓰는 것.</summary>
    Weapon,
    /// <summary>방어구 — 착용해서 방어력·이동성을 올리는 것. 부위로 쪼개지 않는다.</summary>
    Armor,
    /// <summary>설치물 — 월드에 배치되는 것.</summary>
    Placeable,
    /// <summary>회수물 — 제작 불가. 수집·전투로만 얻는다.</summary>
    Salvage,
}

/// <summary>
/// 아이템의 계통 축 — 어느 생산 라인에 속하는가. <see cref="ItemType"/>(용도)과 직교한다.
/// 예: 철광석·철판·선체 패널은 용도가 Ore/Part/RepairPart로 다르지만 계통은 셋 다 Iron이다.
///
/// UI 계통색의 근거가 되는 데이터다 (UI 디자인시스템 레퍼런스 §01).
/// 이름은 레퍼런스·테크트리 문서의 어휘를 그대로 쓴다 — 문서에서 "iron"으로 읽은 것이
/// 코드에서도 Iron이어야 옮길 때 실수가 없다.
/// 색 자체는 데이터가 아니다. 계통→색 매핑은 UIItemPalette 한 곳에만 둔다.
/// </summary>
public enum ItemLine
{
    /// <summary>미지정. 계통 강조 없이 중립으로 표시된다.</summary>
    None = 0,
    /// <summary>구조 계통 — 철광석·철판·선체 패널·물리 탄약.</summary>
    Iron = 1,
    /// <summary>전자 계통 — 구리·회로·제어 유닛.</summary>
    Copper = 2,
    /// <summary>동력 계통 — 크리스탈·에너지 셀·동력 모듈.</summary>
    Crystal = 3,
    /// <summary>괴수 소재 — 둥지·적대 요소에서 나온 것.</summary>
    Beast = 4,
}

[CreateAssetMenu(fileName = "NewItem", menuName = "Factory/Item")]
public class ItemDataSO : GameDataSO
{
    [Obsolete("표시 이름은 GameDataSO.displayName, 식별은 GameDataSO.Id를 쓸 것. " +
              "이 프로퍼티는 에셋 파일명(Object.name)으로의 fallback이라 표시용으로 부적합하다.")]
    public string Name => base.name;

    [Tooltip("용도 축 — 무엇으로 쓰는가.")]
    public ItemType type;

    [Tooltip("계통 축 — 어느 생산 라인 소속인가. UI 계통색의 근거.")]
    public ItemLine line;
}
