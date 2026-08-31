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

        static SimDatabase _database;

        /// <summary>팩을 읽어 오는 함수 — 파일·플랫폼을 아는 쪽(PackLoader)이 꽂는다. 심은 경로를 모른다.</summary>
        public static Func<SimDatabase> DatabaseLoader { get; set; }

        /// <summary>
        /// 차폐 판정 — 뷰(PhysX)가 꽂는다(5a 결정: 사격 판정·LOS만 PhysX). 심은 지형·벽을 모른다.
        /// (shooter, target, from, to) → 둘 사이가 가려지지 않았으면 true. 쏘는 쪽·표적 자신의 콜라이더는 제외.
        /// </summary>
        public static Func<Entity, Entity, Vector3, Vector3, bool> LineOfSight { get; set; }

        /// <summary>제공자가 없으면 예외 — 헤드리스 테스트는 직접 넣는다. 조용한 "항상 보임"은 없다.</summary>
        public static bool HasLineOfSight(Entity shooter, Entity target, Vector3 from, Vector3 to)
            => (LineOfSight ?? throw new InvalidOperationException("SimHost.LineOfSight가 없습니다 — 뷰(ProjectileSystem)가 꽂아야 하고, 헤드리스 테스트는 직접 넣는다"))(shooter, target, from, to);

        /// <summary>정의의 정본. 처음 요청될 때 로더로 읽는다. 로더가 없으면(에디터 도구 등) null.</summary>
        public static SimDatabase Database
        {
            get
            {
                if (_database == null && DatabaseLoader != null) _database = DatabaseLoader();
                return _database;
            }
            set => _database = value;
        }

        /// <summary>새 월드로 교체 — 새 게임 시작 등. 옛 월드의 엔티티는 Removed를 받는다.</summary>
        public static void Reset()
        {
            _world?.Clear();
            _world = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _world = null; _database = null; }
    }
}
