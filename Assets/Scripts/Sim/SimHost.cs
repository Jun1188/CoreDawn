using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// "지금의 월드" — 과도기 정적 접근점. 씬 오브젝트(뷰)가 Awake에서 심 엔티티를 만들어 붙이는 동안만 필요하다.
    /// 5단계에서 WorldRunner(심 호스트)가 월드를 소유하면 이 접근점은 그 인스턴스를 가리키거나 사라진다.
    ///
    /// 씬을 넘어 살아남는 이유: 엔티티의 생사는 소유자(FactorySystem·뷰)가 책임지고, 씬이 내려가면
    /// 소유자들이 OnDestroy에서 자기 것을 뺀다. 도메인 리로드를 끈 환경에서 플레이를 넘어 남는 것만 막는다.
    /// </summary>
    public static class SimHost
    {
        static EntityWorld _world;

        public static EntityWorld World => _world ??= new EntityWorld();

        /// <summary>새 월드로 교체 — 새 게임 시작 등. 옛 월드의 엔티티는 Removed를 받는다.</summary>
        public static void Reset()
        {
            _world?.Clear();
            _world = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _world = null;
    }
}
