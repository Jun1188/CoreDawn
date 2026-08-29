using UnityEngine;
using CoreDawn.DayTime;
using CoreDawn.Navigation;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    /// <summary>
    /// "지금의 몬스터 시스템" — 과도기 정적 접근점 + 구동 러너. SimHost.World와 같은 자리.
    ///
    /// 씬 배선을 요구하지 않는다: 첫 접근(스폰)에서 시스템과 러너가 스스로 선다. 몬스터가 없는 씬에는 서지도 않는다.
    /// BattleManager가 있든 없든 몬스터는 돈다 — 구 Monster.Update(뷰 자체 틱)와 같은 독립성을 유지한다.
    /// 5단계에서 WorldRunner가 시스템을 소유하면 이 접근점은 그 인스턴스를 가리키거나 사라진다.
    /// </summary>
    public static class MonsterSystemHost
    {
        static MonsterSystem system;
        static EffectSystem effects;
        static MonsterSystemRunner runner;

        public static MonsterSystem System
        {
            get
            {
                if (system == null)
                {
                    system = new MonsterSystem(SimHost.World, new SceneNavigation());
                    // 둥지 교전 규칙(DayOnly)이 보는 낮/밤 — 주야 매니저가 없는 테스트 씬은 항상 낮
                    system.IsDay = () => TimeManager.Instance == null || TimeManager.Instance.Phase == DayPhase.Day;
                }
                EnsureRunner();
                return system;
            }
        }

        /// <summary>효과 시스템 — 월드 전체(몬스터·건물·플레이어)의 지속 효과 틱. 몬스터 시스템과 같은 러너가 돌린다.</summary>
        public static EffectSystem Effects
        {
            get
            {
                if (effects == null) effects = new EffectSystem(SimHost.World);
                EnsureRunner();
                return effects;
            }
        }

        static void EnsureRunner()
        {
            if (runner != null || !Application.isPlaying) return;
            var go = new GameObject("MonsterSystem (Runtime)");
            go.hideFlags = HideFlags.DontSave;
            runner = go.AddComponent<MonsterSystemRunner>();
        }

        // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 static이 플레이를 넘어 살아남는 것 방지
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            system?.Dispose();
            system = null;
            effects?.Dispose();
            effects = null;
            runner = null;
        }
    }

    /// <summary>구동용 러너 — Update에서 두뇌→이동→군중을 한 틱 돌린다. 뷰는 LateUpdate에서 결과를 그린다.</summary>
    public class MonsterSystemRunner : MonoBehaviour
    {
        void Update()
        {
            float dt = Time.deltaTime;
            MonsterSystemHost.Effects.Tick(dt);   // 효과가 먼저 — 이번 틱의 속도 배율이 이번 틱의 이동에 쓰이게
            MonsterSystemHost.System.Tick(dt);
        }
    }
}
