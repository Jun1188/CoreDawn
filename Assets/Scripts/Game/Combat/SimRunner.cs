using UnityEngine;
using CoreDawn.DayTime;
using CoreDawn.Navigation;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 심 시스템들의 과도기 접근점 + 구동기 — 몬스터·효과·플레이어 시스템을 한 러너가 매 프레임 같은 순서로 돌린다.
    /// (구 MonsterSystemHost.) 5단계 WorldRunner의 전신: 그때 고정 틱·SimHost.World 통합·씬 생명주기가 여기로 온다.
    /// 정적인 이유: 씬 어디서든(스포너·타워·뷰) 같은 심을 가리켜야 하고, 아직 씬 부트스트랩이 심을 소유하지 않는다.
    /// </summary>
    public static class SimRunner
    {
        static MonsterSystem monsters;
        static EffectSystem effects;
        static PlayerSystem players;
        static SimRunnerBehaviour runner;

        /// <summary>몬스터 시스템 — 두뇌·이동·군중·스폰과 심 시계(Now).</summary>
        public static MonsterSystem Monsters
        {
            get
            {
                if (monsters == null)
                {
                    monsters = new MonsterSystem(SimHost.World, new SceneNavigation());
                    // 둥지 교전 규칙(DayOnly)이 보는 낮/밤 — 주야 매니저가 없는 테스트 씬은 항상 낮
                    monsters.IsDay = () => TimeManager.Instance == null || TimeManager.Instance.Phase == DayPhase.Day;
                    monsters.PlayerEntity = players?.Entity;   // 플레이어가 먼저 만들어졌으면 이어 준다
                }
                EnsureRunner();
                return monsters;
            }
        }

        /// <summary>효과 시스템 — 월드 전체(몬스터·건물·플레이어)의 지속 효과 틱.</summary>
        public static EffectSystem Effects
        {
            get
            {
                if (effects == null) effects = new EffectSystem(SimHost.World);
                EnsureRunner();
                return effects;
            }
        }

        static WaveSystem waves;

        /// <summary>밤 웨이브 시스템 — 팩 wave 규칙이 있을 때만(없으면 null: 밤 웨이브 없는 씬).</summary>
        public static WaveSystem Waves
        {
            get
            {
                if (waves == null)
                {
                    var rule = SimHost.Database?.Wave;
                    if (rule == null) return null;
                    waves = new WaveSystem(SimHost.World, Monsters, rule);
                }
                EnsureRunner();
                return waves;
            }
        }

        /// <summary>플레이어 시스템 — 플레이어 엔티티의 생성 주체. 몬스터 두뇌가 보는 PlayerEntity를 여기서 이어 준다.</summary>
        public static PlayerSystem Players
        {
            get
            {
                if (players == null)
                {
                    players = new PlayerSystem(SimHost.World);
                    players.Spawned += e => Monsters.PlayerEntity = e;
                    players.Despawned += e => { if (monsters != null && ReferenceEquals(monsters.PlayerEntity, e)) monsters.PlayerEntity = null; };
                }
                EnsureRunner();
                return players;
            }
        }

        static void EnsureRunner()
        {
            if (runner != null || !Application.isPlaying) return;
            var go = new GameObject("SimRunner (Runtime)");
            go.hideFlags = HideFlags.DontSave;
            runner = go.AddComponent<SimRunnerBehaviour>();
        }

        // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 static이 플레이를 넘어 살아남는 것 방지
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            monsters?.Dispose();
            monsters = null;
            effects?.Dispose();
            effects = null;
            players?.Dispose();
            players = null;
            runner = null;
        }
    }

    /// <summary>매 프레임 심을 한 틱 돌리는 러너 — 효과(배율) → 몬스터(이동·공격) → 플레이어(무기) 순.</summary>
    public class SimRunnerBehaviour : MonoBehaviour
    {
        void Update()
        {
            float dt = Time.deltaTime;
            SimRunner.Effects.Tick(dt);   // 효과가 먼저 — 이번 틱의 속도 배율이 이번 틱의 이동에 쓰이게
            SimRunner.Monsters.Tick(dt);
            SimRunner.Players.Tick(dt);   // 플레이어 시계 — 무기 재장전·연사 간격
            SimRunner.Waves?.Tick(dt);    // 밤 웨이브 — 버스트·진입로 무리·종료 판정
        }
    }
}
