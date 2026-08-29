using UnityEngine;

namespace CoreDawn.Data
{
    /// <summary>
    /// 프로젝트의 모든 몬스터 종류(MonsterDataSO) 레지스트리 — 수동 연결 금지.
    /// GameData 임포터가 monsters 섹션을 들여올 때마다 다시 채운다(Resources/MonsterDatabase, id 순).
    ///
    /// 소비자: 세이브 복원(id → 종류), 종류를 정하지 않은 스폰(구 세이브·비어 있는 웨이브)의 기본값.
    /// 웨이브·둥지는 이 DB를 거치지 않고 MonsterDataSO를 직접 참조한다.
    /// </summary>
    [CreateAssetMenu(fileName = "MonsterDatabase", menuName = "Combat/Monster Database")]
    public class MonsterDatabaseSO : ScriptableObject
    {
        [Tooltip("자동 수집됨 — 직접 편집하지 말 것 (GameData 임포트가 다시 채운다)")]
        public MonsterDataSO[] monsters;

        /// <summary>Resources의 기본 데이터베이스. 씬 연결 없이도 어디서든 접근 가능.</summary>
        public static MonsterDatabaseSO LoadDefault()
            => Resources.Load<MonsterDatabaseSO>("MonsterDatabase");

        /// <summary>id("Monster:Basic")로 조회. 없으면 null.</summary>
        public MonsterDataSO FindById(string id)
        {
            if (monsters == null || string.IsNullOrEmpty(id)) return null;
            foreach (var m in monsters)
                if (m != null && m.Id == id) return m;
            return null;
        }

        /// <summary>
        /// 기본 종류 — 웨이브가 종류를 안 정했거나 세이브에 종류가 없을 때(구 세이브). id 순 첫 항목(Monster:Basic).
        /// 조용한 폴백이 아니라 "종류를 정하지 않은 데이터"를 드러내는 값이다 — 웨이브 편집기가 경고한다.
        /// </summary>
        public MonsterDataSO Default => monsters != null && monsters.Length > 0 ? monsters[0] : null;
    }
}
