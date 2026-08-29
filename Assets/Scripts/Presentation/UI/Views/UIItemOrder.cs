using System.Collections.Generic;
using System.Linq;
using CoreDawn.Factory;
using CoreDawn.Data;

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
        static Dictionary<ItemDataSO, int> _tierCache;

        /// <summary>이 아이템이 열리는 코어 티어. 원광처럼 처음부터 있는 것은 0.</summary>
        public static int TierOf(ItemDataSO item)
        {
            if (item == null) return int.MaxValue;
            EnsureCache();
            return _tierCache.TryGetValue(item, out var t) ? t : 0;
        }

        /// <summary>티어 → 계통 → 용도 → 이름 순으로 정렬한다.</summary>
        public static IEnumerable<ItemDataSO> Sorted(IEnumerable<ItemDataSO> items) =>
            items.Where(i => i != null)
                 .OrderBy(TierOf)
                 .ThenBy(i => (int)i.line)
                 .ThenBy(i => (int)i.type)
                 .ThenBy(NameOf, System.StringComparer.Ordinal);

        static string NameOf(ItemDataSO i) =>
            string.IsNullOrEmpty(i.displayName) ? i.name : i.displayName;

        /// <summary>데이터가 다시 임포트되면 버린다 (에디터에서 티어를 고칠 때).</summary>
        public static void Invalidate() => _tierCache = null;

        /// <summary>
        /// 레시피는 Resources에 없으므로 BuildingDatabase의 조립기들을 통해 모은다 —
        /// 모든 레시피는 어느 조립기의 availableRecipes에 들어 있다.
        /// </summary>
        static void EnsureCache()
        {
            if (_tierCache != null) return;
            _tierCache = new Dictionary<ItemDataSO, int>();

            var db = BuildingDatabaseSO.LoadDefault();
            if (db == null || db.buildings == null) return;

            foreach (var b in db.buildings)
            {
                if (b is not AssemblerDataSO asm || asm.availableRecipes == null) continue;

                foreach (var r in asm.availableRecipes)
                {
                    if (r == null || r.outputs == null) continue;

                    // 레시피 자체의 티어와 그것을 돌리는 건물의 티어 중 늦은 쪽이 실제 해금 시점이다
                    int tier = System.Math.Max(r.tier, b.requiredCoreTier);

                    foreach (var o in r.outputs)
                    {
                        if (o.item == null) continue;
                        if (!_tierCache.TryGetValue(o.item, out var cur) || tier < cur)
                            _tierCache[o.item] = tier;
                    }
                }
            }
        }
    }
}
