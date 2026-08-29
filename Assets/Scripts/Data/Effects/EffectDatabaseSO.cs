using UnityEngine;

namespace CoreDawn.Data
{
    /// <summary>
    /// 프로젝트의 모든 효과 SO 레지스트리 — Recipe/Item/BuildingDatabaseSO와 같은 패턴.
    /// 에디터 스캐너(Editor/BuildingDatabaseScanner)가 EffectSO 에셋을 만들거나 지울 때마다
    /// 자동으로 이 목록을 갱신한다.
    ///
    /// 존재 이유: 런타임 부착 코드(BattleManager의 플레이어 근접 등)는 인스펙터 배선이
    /// 불가능해 효과 에셋을 코드에서 집어야 하는데, 개별 에셋을 Resources에 흩어 두는 대신
    /// DB 하나만 Resources에 두고 조회한다 — 효과 에셋들은 Data/Effects에 산다.
    /// </summary>
    [CreateAssetMenu(fileName = "EffectDatabase", menuName = "Combat/Effect Database")]
    public class EffectDatabaseSO : ScriptableObject
    {
        [Tooltip("자동 수집됨 — 직접 편집하지 말 것 (Tools/Factory/Rebuild Data Databases로 재수집)")]
        public EffectSO[] effects;

        /// <summary>Resources의 기본 데이터베이스. 씬 연결 없이도 어디서든 접근 가능.</summary>
        public static EffectDatabaseSO LoadDefault()
            => Resources.Load<EffectDatabaseSO>("EffectDatabase");

        /// <summary>id("Effect:이름")로 효과 조회 — 없으면 null.</summary>
        public EffectSO FindById(string id)
        {
            if (effects == null || string.IsNullOrEmpty(id)) return null;
            foreach (var e in effects)
                if (e != null && e.Id == id) return e;
            return null;
        }

        /// <summary>해당 채널(클래스)의 첫 효과 — 공용 에셋(기본 피해 등)을 타입으로 집을 때.</summary>
        public T FindFirst<T>() where T : EffectSO
        {
            if (effects == null) return null;
            foreach (var e in effects)
                if (e is T typed) return typed;
            return null;
        }
    }
}
