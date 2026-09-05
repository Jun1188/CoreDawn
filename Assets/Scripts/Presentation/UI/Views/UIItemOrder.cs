using System.Collections.Generic;
using System.Linq;
using CoreDawn.Sim;

namespace CoreDawn.UI
{
    /// <summary>
    /// 아이템 목록의 표시 순서 — 티어 → 계통 → 용도 → 이름.
    ///
    /// 목록이 데이터베이스 순서(용도 → 이름)로만 나오면 플레이어에게는 순서가 없어 보인다.
    /// 실제로 물건을 찾을 때 쓰는 기준은 "언제 열리는 것인가(티어)"와 "어느 라인인가(계통)"다.
    ///
    /// 아이템 자체에는 티어가 없다. 그래서 그것을 만드는 레시피의 tier로 역산한다 —
    /// 여러 레시피가 만들면 가장 이른 것, 레시피가 없으면(원광·회수물) 0이다.
    /// 티어를 아이템 데이터로 옮기게 되면 이 역산은 지우고 필드를 바로 읽으면 된다.
    /// </summary>
    public static class UIItemOrder
    {
        static Dictionary<ItemDef, int> _tierCache;
        static SimDatabase _cacheOf;   // 팩이 다시 로드되면(다른 db 인스턴스) 캐시를 버린다

        /// <summary>이 아이템이 열리는 코어 티어. 원광처럼 처음부터 있는 것은 0.</summary>
        public static int TierOf(ItemDef item)
        {
            if (item == null) return int.MaxValue;
            EnsureCache();
            return _tierCache != null && _tierCache.TryGetValue(item, out var t) ? t : 0;
        }

        /// <summary>티어 → 계통 → 용도 → 이름 순으로 정렬한다.</summary>
        public static IEnumerable<ItemDef> Sorted(IEnumerable<ItemDef> items) =>
            items.Where(i => i != null)
                 .OrderBy(TierOf)
                 .ThenBy(i => (int)i.Line)
                 .ThenBy(i => (int)i.Type)
                 .ThenBy(i => i.DisplayName ?? "", System.StringComparer.Ordinal);

        /// <summary>데이터가 다시 임포트되면 버린다 (에디터에서 티어를 고칠 때).</summary>
        public static void Invalidate() { _tierCache = null; _cacheOf = null; }

        /// <summary>
        /// 정의(팩)의 제작기들을 훑는다 — 모든 자동 레시피는 어느 제작 건물(Crafter 정의)의 목록에 들어 있다.
        /// 레시피 자체의 티어와 그것을 돌리는 건물의 티어 중 늦은 쪽이 실제 해금 시점이다.
        /// </summary>
        static void EnsureCache()
        {
            var db = SimHost.Database;
            if (db == null) { _tierCache = null; _cacheOf = null; return; }
            if (_tierCache != null && ReferenceEquals(_cacheOf, db)) return;

            _tierCache = new Dictionary<ItemDef, int>();
            _cacheOf = db;

            foreach (var e in db.Entities.Values)
            {
                var crafter = e.Get<CrafterModuleDef>();
                if (crafter == null || crafter.Manual) continue;

                int buildingTier = e.Get<BuildingModuleDef>()?.RequiredCoreTier ?? 0;

                foreach (var r in crafter.Recipes)
                {
                    if (r?.Outputs == null) continue;
                    int tier = System.Math.Max(r.Tier, buildingTier);

                    foreach (var o in r.Outputs)
                    {
                        if (o?.Item == null) continue;
                        if (!_tierCache.TryGetValue(o.Item, out var cur) || tier < cur)
                            _tierCache[o.Item] = tier;
                    }
                }
            }
        }
    }
}
