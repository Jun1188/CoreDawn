namespace CoreDawn.Sim
{
    /// <summary>
    /// 아이템의 용도 축 — 무엇인가. 획득·소비 경로가 실제로 갈리는 지점에서만 나눈다.
    ///
    /// 정수값을 명시한 이유: 유니티는 enum을 int로 직렬화한다. 기존 에셋의
    /// Ore(0)/Ingot(1)/Component(2)가 각각 Ore/Ingot/Part로 자동 승계되고,
    /// Weapon은 5를 유지해 그대로 남는다.
    /// </summary>
    public enum ItemType
    {
        /// <summary>원광 — 채굴로만 얻는 원시자재. 레시피 산출물이 될 수 없다.</summary>
        Ore = 0,
        /// <summary>소재 — 제련 산출물. 원광 1종 → 소재 1종 대응.</summary>
        Ingot = 1,
        /// <summary>부품 — 제작·조립 중간재.</summary>
        Part = 2,
        /// <summary>수리 부품 — 게이트 납품 전용. 다른 용도가 없다 = 생산 라인의 최종 목적지.</summary>
        RepairPart = 3,
        /// <summary>탄약 — 무기·포탑이 소모한다. 유일하게 소비되어 없어지는 분류.</summary>
        Ammo = 4,
        /// <summary>무기 — 손에 드는 것.</summary>
        Weapon = 5,
        /// <summary>방어구 — 착용 장비. 부위로 쪼개지 않는다.</summary>
        Armor = 6,
        /// <summary>설치물 — 설비·포탑·지뢰.</summary>
        Placeable = 7,
        /// <summary>회수물 — 수집으로만 얻고 제작 불가.</summary>
        Salvage = 8,
    }

    /// <summary>
    /// 아이템의 계통 축 — 어느 생산 라인 소속인가. <see cref="ItemType"/>(용도)과 직교한다.
    /// 둘을 조합하면 Part × Copper(구리 전선), Ammo × Iron(기본 탄약)이 된다.
    ///
    /// UI의 계통색·아이콘 테두리·레이더 광맥 표시가 전부 이 값을 읽는다.
    /// 색 자체는 데이터가 아니다 — 계통→색 매핑은 UI 쪽 한 곳에만 둔다.
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
        /// <summary>괴수 소재 — 둥지·적 개체에서 나온 것.</summary>
        Beast = 4,
    }
}
