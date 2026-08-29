using System;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{

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
        // ── 전환기 브리지 — 정의의 정본은 팩(json)이다. SO는 아이콘·프리팹·저작 참조(표현 몫)만 남는다 ──
        ItemDef _def;

        /// <summary>이 에셋이 가리키는 심 정의(같은 id). 팩이 없으면 null.</summary>
        public ItemDef Def
        {
            get
            {
                if (_def == null) { var db = SimHost.Database; if (db != null) _def = db.Item(db.LegacyId(Id)); }
                return _def;
            }
        }

        // 암시 변환: 인벤토리·공장은 ItemDef를, UI·저작은 아직 SO를 든다. SO 퇴역(5a-3) 때 함께 사라진다.
        public static implicit operator ItemDef(ItemDataSO so) => so != null ? so.Def : null;
        public static implicit operator ItemDataSO(ItemDef def) => ItemAssets.Of(def);

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
