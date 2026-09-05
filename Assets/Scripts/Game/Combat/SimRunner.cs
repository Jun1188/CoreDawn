using UnityEngine;
using CoreDawn.DayTime;
using CoreDawn.Managers;
using CoreDawn.Navigation;
using CoreDawn.Sim;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 전투 심 시스템들의 과도기 접근점 — 몬스터·효과·플레이어·웨이브 시스템을 처음 요청될 때 만들어
    /// <see cref="SimHost.Sim"/>에 등록한다(순서는 SimOrder, 시스템이 생성자에서 스스로 등록). 돌리는 것은 WorldRunner(고정 20Hz).
    /// 정적인 이유: 씬 어디서든(스포너·타워·뷰) 같은 심을 가리켜야 하고, 아직 씬 부트스트랩이 심을 소유하지 않는다.
    /// </summary>
    public static class SimRunner
    {
        static MonsterSystem monsters;
        static EffectSystem effects;
        static PlayerSystem players;
        static WaveSystem waves;

        /// <summary>몬스터 시스템 — 두뇌·이동·군중·스폰. 시계는 SimWorld.Now.</summary>
        public static MonsterSystem Monsters
        {
            get
            {
                if (monsters == null)
                {
                    monsters = new MonsterSystem(SimHost.Sim, new SceneNavigation());
                    // 둥지 교전 규칙(DayOnly)이 보는 낮/밤 — 주야 매니저가 없는 테스트 씬은 항상 낮
                    monsters.IsDay = () => TimeManager.Instance == null || TimeManager.Instance.Phase == DayPhase.Day;
                    monsters.PlayerEntity = players?.Entity;   // 플레이어가 먼저 만들어졌으면 이어 준다
                }
                WorldRunner.Ensure();
                return monsters;
            }
        }

        /// <summary>효과 시스템 — 월드 전체(몬스터·건물·플레이어)의 지속 효과 틱.</summary>
        public static EffectSystem Effects
        {
            get
            {
                if (effects == null) effects = new EffectSystem(SimHost.Sim);
                WorldRunner.Ensure();
                return effects;
            }
        }

        /// <summary>밤 웨이브 시스템 — 팩 wave 규칙이 있을 때만(없으면 null: 밤 웨이브 없는 씬).</summary>
        public static WaveSystem Waves
        {
            get
            {
                if (waves == null)
                {
                    var rule = SimHost.Database?.Wave;
                    if (rule == null) return null;
                    waves = new WaveSystem(SimHost.Sim, Monsters, rule);
                }
                WorldRunner.Ensure();
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
                    players = new PlayerSystem(SimHost.Sim);
                    players.Spawned += e => Monsters.PlayerEntity = e;
                    players.Despawned += e => { if (monsters != null && ReferenceEquals(monsters.PlayerEntity, e)) monsters.PlayerEntity = null; };
                }
                WorldRunner.Ensure();
                return players;
            }
        }

        /// <summary>
        /// 전투 시스템을 전부 버린다 — 심은 씬 하나의 것이라 씬 전환 게이트(BootScene)가 <see cref="SimHost.Reset"/> 앞에 부른다.
        /// 다음 접근에서 새 SimWorld 에 다시 만들어진다.
        /// </summary>
        public static void Reset()
        {
            waves?.Dispose();
            waves = null;
            monsters?.Dispose();
            monsters = null;
            effects?.Dispose();
            effects = null;
            players?.Dispose();
            players = null;
        }

        // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 static이 플레이를 넘어 살아남는 것 방지
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reset();
    }
}
