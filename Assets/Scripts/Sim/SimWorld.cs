using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 심 루트 — 엔티티 등록부(<see cref="Entities"/>) + 고정 시계(<see cref="Now"/>, 20Hz) + 시스템 실행 순서.
    /// 프레임을 모른다: 호스트(WorldRunner)가 프레임 dt를 누적해 정수 번 <see cref="Step"/>을 부르고,
    /// 테스트는 씬 없이 <see cref="Step(int)"/>으로 돌린다. 시계는 틱 수(<see cref="TickCount"/>)가 정본이라
    /// 누적 오차가 없고 세이브는 정수 하나로 복원한다.
    ///
    /// 뷰 보간: 스텝 시작에 모든 엔티티가 이전 위치를 남기고(<see cref="Entity.BeginStep"/>), 호스트가
    /// <see cref="FrameAlpha"/>(마지막 스텝 이후 흐른 프레임 시간 / 틱 간격)를 매 프레임 쓴다.
    /// </summary>
    public sealed class SimWorld
    {
        public const int TicksPerSecond = 20;
        public const float TickDt = 1f / TicksPerSecond;

        public EntityWorld Entities { get; }

        /// <summary>지금까지 돈 스텝 수 — 시계의 정본.</summary>
        public long TickCount { get; private set; }

        /// <summary>심 시간(초) = 스텝 수 × 틱 간격. 쿨다운·예약·진행도는 전부 이 값 기준.</summary>
        public float Now => (float)(TickCount * (double)TickDt);

        /// <summary>마지막 스텝 이후 흐른 프레임 시간을 틱 간격으로 나눈 값(0~1). 호스트가 쓰고 뷰가 보간에 읽는다. 헤드리스는 0.</summary>
        public float FrameAlpha { get; set; }

        readonly List<(int order, ISimSystem system)> _systems = new();
        readonly List<ISimSystem> _stepBuffer = new();

        public SimWorld(EntityWorld entities = null)
        {
            Entities = entities ?? new EntityWorld();
        }

        /// <summary>시스템 등록 — 같은 order 안에서는 등록 순. 이미 있으면 무시.</summary>
        public void AddSystem(ISimSystem system, int order)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            for (int i = 0; i < _systems.Count; i++) if (ReferenceEquals(_systems[i].system, system)) return;
            int at = _systems.Count;
            while (at > 0 && _systems[at - 1].order > order) at--;
            _systems.Insert(at, (order, system));
        }

        public void RemoveSystem(ISimSystem system)
        {
            for (int i = 0; i < _systems.Count; i++)
                if (ReferenceEquals(_systems[i].system, system)) { _systems.RemoveAt(i); return; }
        }

        public int SystemCount => _systems.Count;

        /// <summary>한 스텝 — 보간 기준점 갱신 → 시계 한 틱 → 시스템 순서대로 Tick(TickDt). 스텝 중 등록·해제가 있어도 안전(스냅샷).</summary>
        public void Step()
        {
            foreach (var e in Entities.All) e.BeginStep();
            TickCount++;
            _stepBuffer.Clear();
            for (int i = 0; i < _systems.Count; i++) _stepBuffer.Add(_systems[i].system);
            for (int i = 0; i < _stepBuffer.Count; i++) _stepBuffer[i].Tick(TickDt);
        }

        public void Step(int count)
        {
            for (int i = 0; i < count; i++) Step();
        }

        /// <summary>세이브 복원 전용 — 시계를 저장 시점으로. 예약(절대 시각)을 되살리기 전에 부를 것.</summary>
        public void RestoreClock(long tickCount) => TickCount = Math.Max(0, tickCount);

        /// <summary>초 단위 저장값으로 복원(가장 가까운 틱).</summary>
        public void RestoreClock(float seconds) => RestoreClock((long)Math.Round(seconds / TickDt));

        /// <summary>전부 비움 — 새 게임. 엔티티는 Removed를 받고 시계는 0으로. 시스템 등록은 유지한다.</summary>
        public void Clear()
        {
            Entities.Clear();
            TickCount = 0;
            FrameAlpha = 0f;
        }
    }
}
