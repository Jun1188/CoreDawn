using System;
using UnityEngine;

namespace CoreDawn.Factory
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

    [CreateAssetMenu(fileName = "NewItem", menuName = "Factory/Item")]
    public class ItemDataSO : GameDataSO
    {
        [Obsolete("표시 이름은 GameDataSO.displayName, 식별은 GameDataSO.Id를 쓸 것. " +
                  "이 프로퍼티는 에셋 파일명(Object.name)으로의 fallback이라 표시용으로 부적합하다.")]
        public string Name => base.name;

        [Tooltip("용도 축 — 무엇인가. 분류·UI용이며, 코드 판정은 모듈 존재(GetModule)로 한다.")]
        public ItemType type;

        [Tooltip("계통 축 — 어느 생산 라인 소속인가. UI 계통색의 근거.")]
        public ItemLine line;

        [Tooltip("한 슬롯에 쌓이는 최대 개수. 무기·설치물처럼 낱개로 다루는 것은 1. " +
                 "건물 버퍼 상한(BuildingDataSO.bufferStackCap)과 만나면 더 작은 쪽이 이긴다.")]
        [Min(1)] public int maxStack = 64;

        [Tooltip("분배기 필터처럼 아이템을 고르는 목록에서 숨긴다 — 근접 무기의 내부 탄약(플라즈마 아크)처럼 " +
                 "플레이어가 손에 쥘 일이 없는 항목용. 건물의 hideFromBuildMenu와 같은 역할.")]
        public bool hideFromMenu;

        [Tooltip("역할 모듈 — 탄약(AmmoModuleSO)·무기(WeaponModuleSO) 같은 전용 데이터를 " +
                 "상속 대신 조합으로 단다. 아이템 에셋의 서브에셋으로 저장되며 임포터가 관리한다.")]
        [SerializeField] private System.Collections.Generic.List<ItemModuleSO> modules = new();

        /// <summary>해당 역할 모듈을 돌려준다 — 없으면 null. "탄약인가?"의 정의는 타입 검사가 아니라 이것이다.</summary>
        public T GetModule<T>() where T : ItemModuleSO
        {
            foreach (var m in modules)
                if (m is T typed) return typed;
            return null;
        }

        public bool TryGetModule<T>(out T module) where T : ItemModuleSO
        {
            module = GetModule<T>();
            return module != null;
        }

    #if UNITY_EDITOR
        /// <summary>임포터·마이그레이션 전용 — 런타임 코드는 GetModule만 쓸 것.</summary>
        public System.Collections.Generic.List<ItemModuleSO> EditorModules => modules;
    #endif
    }
}
