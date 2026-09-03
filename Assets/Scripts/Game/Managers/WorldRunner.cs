using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Managers
{
    /// <summary>
    /// 심 호스트 — 프레임 dt를 누적해 <see cref="SimHost.Sim"/>을 고정 틱(20Hz)으로 정수 번 <see cref="SimWorld.Step"/> 한다.
    /// 따라잡기 상한(<see cref="MaxCatchUpTicks"/>)을 넘긴 빚은 버린다(저사양에서 "틱 몰아치기 → 프레임 더 느려짐" 나선 방지).
    /// 남은 시간은 <see cref="SimWorld.FrameAlpha"/>로 뷰가 보간한다. 일시정지(timeScale 0)는 dt가 0이라 저절로 멈춘다.
    ///
    /// 시스템은 각자 생성자에서 SimWorld에 등록한다(SimRunner의 전투 시스템들, FactoryBootstrap의 공장) — 이 러너는 순서를 모른다.
    /// 씬마다 하나(DontSave, 씬과 함께 사라짐) — 필요한 쪽이 <see cref="Ensure"/>로 세운다.
    /// </summary>
    public sealed class WorldRunner : MonoBehaviour
    {
        public const int MaxCatchUpTicks = 5;

        static WorldRunner instance;
        float acc;

        public static WorldRunner Ensure()
        {
            if (instance != null || !Application.isPlaying) return instance;
            var go = new GameObject("WorldRunner");
            go.hideFlags = HideFlags.DontSave;
            instance = go.AddComponent<WorldRunner>();
            return instance;
        }

        void Update()
        {
            var sim = SimHost.Sim;
            acc += Time.deltaTime;
            int steps = 0;
            while (acc >= SimWorld.TickDt && steps < MaxCatchUpTicks)
            {
                sim.Step();
                acc -= SimWorld.TickDt;
                steps++;
            }
            if (acc > SimWorld.TickDt) acc = SimWorld.TickDt;   // 한도를 넘긴 빚은 버린다 — 다음 프레임 1틱분만 남긴다
            sim.FrameAlpha = acc / SimWorld.TickDt;
        }

        // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 static이 플레이를 넘어 살아남는 것 방지
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;
    }
}
