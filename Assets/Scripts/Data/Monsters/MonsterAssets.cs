using CoreDawn.Sim;

namespace CoreDawn.Data
{
    /// <summary>몬스터 정의(EntityDef) → 표현 에셋(MonsterDataSO: 프리팹). ItemAssets·BuildingAssets와 같은 자리 — 5a-3 뷰 카탈로그 전까지의 다리.</summary>
    public static class MonsterAssets
    {
        public static MonsterDataSO Of(EntityDef def)
        {
            if (def == null) return null;
            var db = SimHost.Database; var database = MonsterDatabaseSO.LoadDefault();
            if (db == null || database == null || database.monsters == null) return null;
            foreach (var m in database.monsters)
                if (m != null && db.LegacyId(m.Id) == def.Id) return m;
            return null;
        }

        /// <summary>엔티티가 조립된 정의로 찾는다. 코드로 조립한 엔티티(정의 없음)는 null.</summary>
        public static MonsterDataSO OfEntity(Entity entity) => Of(entity?.Def);
    }
}
