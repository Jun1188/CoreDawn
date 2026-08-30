using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>
    /// 심 정의(EntityDef) → 표현 에셋(BuildingDataSO: 프리팹·아이콘·커브 메시). 정의 id로 찾는다 — SO의 옛 id는 LegacyId 규칙으로 맞춘다.
    /// 5a-3의 뷰 카탈로그 전신. 심은 이 클래스를 모른다.
    /// </summary>
    public static class BuildingAssets
    {
        static Dictionary<string, BuildingDataSO> byDefId;

        public static BuildingDataSO Of(EntityDef def)
        {
            if (def == null) return null;
            var db = SimHost.Database;
            if (byDefId == null)
            {
                byDefId = new Dictionary<string, BuildingDataSO>();
                var so = BuildingDatabaseSO.LoadDefault();
                if (so?.buildings != null && db != null)
                    foreach (var b in so.buildings)
                        if (b != null && !string.IsNullOrEmpty(b.Id)) byDefId[db.LegacyId(b.Id)] = b;
            }
            return byDefId.TryGetValue(def.Id, out var found) ? found : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => byDefId = null;
    }
}
