using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>
    /// 심 정의(ItemDef) → 표현 에셋(ItemDataSO: 아이콘·모듈 프리팹). 정의 id로 찾는다 — SO의 옛 id는 LegacyId 규칙으로 맞춘다.
    /// 5a-3의 뷰 카탈로그 전신. 심은 이 클래스를 모른다.
    /// </summary>
    public static class ItemAssets
    {
        static Dictionary<string, ItemDataSO> byDefId;

        public static ItemDataSO Of(ItemDef def)
        {
            if (def == null) return null;
            var db = SimHost.Database;
            if (byDefId == null)
            {
                byDefId = new Dictionary<string, ItemDataSO>();
                var so = ItemDatabaseSO.LoadDefault();
                if (so?.items != null && db != null)
                    foreach (var it in so.items)
                        if (it != null && !string.IsNullOrEmpty(it.Id)) byDefId[db.LegacyId(it.Id)] = it;
            }
            return byDefId.TryGetValue(def.Id, out var found) ? found : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => byDefId = null;
    }
}
