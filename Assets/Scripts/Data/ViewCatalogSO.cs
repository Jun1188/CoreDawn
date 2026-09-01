using System;
using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>
    /// 뷰 카탈로그 — 팩 id → 표현 에셋(아이콘·프리팹·연출)의 구운 조회표 (5a-3a).
    ///
    /// 정본은 팩 v2의 view 블록(이름 + guid)이고, 이 에셋은 에디터 베이커
    /// (Tools/Factory/Bake ViewCatalog)가 그 블록을 읽어 직접 참조로 구운 산출물이다 —
    /// 런타임은 GUID로 에셋을 찾을 수 없기 때문이다(광맥 뷰를 씬에 굽는 것과 같은 철학).
    /// 게임 값은 들지 않는다 — 그쪽 정본은 SimDatabase의 Def다. 심은 이 클래스를 모른다.
    ///
    /// 없는 id는 null을 돌려준다 — 무엇이 없어서 무엇이 안 보이는지는 호출부가 제 문맥으로
    /// 경고한다(카탈로그가 대신 소리 내면 한 프레임에 수백 번 울린다).
    /// </summary>
    public sealed class ViewCatalogSO : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;                  // 팩 id (예: coredawn:item/iron_ore, coredawn:entity/miner)
            public Sprite icon;                // 아이템·건물(빌드 메뉴) 아이콘
            public GameObject prefab;          // 건물·몬스터 본체 (벨트는 직선)
            public GameObject curveLPrefab;    // 벨트 커브 — 벨트만
            public GameObject curveRPrefab;
            public GameObject bulletPrefab;    // 탄약 아이템 — 탄 외형(Bullet 컴포넌트 필수)
            public GameObject muzzleFlashPrefab;
            public GameObject hitEffectPrefab;
            public AudioClip[] clips;         // 소리(sounds 섹션) — 변형 묶음, 재생 때 하나를 고른다
        }

        public List<Entry> entries = new();

        [Tooltip("바닥 아이템 공용 프리팹 — 모든 아이템이 같은 프리팹을 쓰고 아이콘만 바꾼다. 팩 view에는 아직 자리가 없어(5a-4) 카탈로그가 든다; 베이커는 이 필드를 건드리지 않는다.")]
        public GameObject droppedItemPrefab;

        Dictionary<string, Entry> byId;

        Entry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, Entry>();
                foreach (var e in entries)
                    if (e != null && !string.IsNullOrEmpty(e.id)) byId[e.id] = e;
            }
            return byId.TryGetValue(id, out var found) ? found : null;
        }

        // ── 정적 창구 — *Assets.Of와 같은 사용감 ─────────────────────

        static ViewCatalogSO instance;

        public static ViewCatalogSO LoadDefault()
            => instance != null ? instance : instance = Resources.Load<ViewCatalogSO>("ViewCatalog");

        public static Entry Of(string id) => LoadDefault() != null ? instance.Find(id) : null;
        public static Entry Of(Def def) => def != null ? Of(def.Id) : null;

        public static Sprite IconOf(Def def) => Of(def)?.icon;
        public static GameObject PrefabOf(Def def) => Of(def)?.prefab;
        /// <summary>소리 id의 클립 묶음. 없으면 null — 호출부(SoundManager)가 한 번 경고한다.</summary>
        public static AudioClip[] ClipsOf(string soundId) => Of(soundId)?.clips;

        /// <summary>벨트 모양별 프리팹 — 직선은 본체(prefab), 커브는 커브 프리팹.</summary>
        public static GameObject BeltPrefabOf(Def def, BeltShape shape)
        {
            var e = Of(def);
            if (e == null) return null;
            return shape switch
            {
                BeltShape.CurveL => e.curveLPrefab,
                BeltShape.CurveR => e.curveRPrefab,
                _ => e.prefab,
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { instance = null; }
    }
}
