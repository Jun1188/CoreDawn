using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 게임 개체의 심(시뮬레이션) 정본 — 몬스터·플레이어·건물·둥지 전부. plain C#, MonoBehaviour 아님.
    ///
    /// 가벼운 컨테이너다: 번호(<see cref="Id"/>)·편·위치·모듈 목록·이벤트뿐이고, 체력·건물·이동·두뇌는
    /// <see cref="EntityModule"/>로 붙는다. "몬스터"나 "플레이어"는 클래스가 아니라 어떤 모듈 묶음을 붙였느냐(아키타입)다.
    ///
    /// 씬 표현(EntityView)은 이 객체를 가리키고 이벤트를 구독해 그릴 뿐, 심은 뷰를 모른다.
    /// 그래서 씬·프레임 없이 생성해 돌릴 수 있고(헤드리스 테스트·서버), 세이브는 이 객체를 직렬화한다.
    /// </summary>
    public sealed class Entity
    {
        public EntityUUID Id { get; }
        public EntityWorld World { get; }

        /// <summary>편 — 적대 판정의 기준. 생성 후 바뀌는 일은 드물지만(포섭 등) 막지는 않는다.</summary>
        public Faction Faction { get; set; }

        Vector3 _position;

        /// <summary>
        /// 월드 위치. 건물은 배치 시 풋프린트 중심으로 확정되고, 이동체는 심 Movement가 매 틱 옮긴다
        /// (과도기에는 뷰가 물리 위치를 돌려준다). 바뀌면 월드의 공간 해시가 따라 움직인다 — 반경 질의의 정본.
        /// </summary>
        public Vector3 Position
        {
            get => _position;
            set
            {
                if (_position == value) return;
                var old = _position;
                _position = value;
                World.OnEntityMoved(this, old, value);
            }
        }

        /// <summary>바라보는 수평 방향(단위 벡터). 이동이 돌리고 뷰가 그대로 그린다. 기본은 +Z.</summary>
        public Vector3 Facing { get; set; } = Vector3.forward;

        /// <summary>월드에서 빠졌는가. 빠진 엔티티를 쥔 참조는 이 플래그로 걸러낸다(큐·캐시에 남은 것들).</summary>
        public bool IsRemoved { get; private set; }

        readonly List<EntityModule> _modules = new List<EntityModule>();

        /// <summary>붙은 순서 그대로 — 피해 인터셉터도 이 순서로 거친다.</summary>
        public IReadOnlyList<EntityModule> Modules => _modules;

        /// <summary>체력 모듈. 없으면 null(때릴 수 없는 개체).</summary>
        public HealthModule Health => Get<HealthModule>();

        /// <summary>살아 있는가 — 월드에 있고, 체력이 있다면 죽지 않았다. 표적 유효성의 심 쪽 기준.</summary>
        public bool IsAlive => !IsRemoved && !(Health != null && Health.IsDead);

        /// <summary>죽는 순간 1회 (Health가 알린다). 월드의 Died보다 먼저 발화한다.</summary>
        public event Action<Entity> Died;

        /// <summary>월드에서 빠지는 순간 1회. 모듈 OnDetach 뒤에 발화한다.</summary>
        public event Action<Entity> Removed;

        internal Entity(EntityWorld world, EntityUUID id, Faction faction, Vector3 position)
        {
            World = world;
            Id = id;
            Faction = faction;
            _position = position;
        }

        /// <summary>모듈 부착. 한 모듈은 한 엔티티에만 붙는다 — 두 번 붙이면 상태가 둘로 갈라진다.</summary>
        public T Add<T>(T module) where T : EntityModule
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (module.Owner != null)
                throw new InvalidOperationException($"{module.GetType().Name} is already attached to {module.Owner.Id}");
            if (IsRemoved)
                throw new InvalidOperationException($"entity {Id} is removed");

            _modules.Add(module);
            module.Owner = this;
            module.OnAttach();
            return module;
        }

        /// <summary>
        /// 종류(또는 인터페이스)로 모듈 조회. 붙은 순서에서 처음 맞는 것. 없으면 null.
        /// where T : class 인 이유 — 인터페이스(IDamageInterceptor 등)로도 찾기 위해서.
        /// </summary>
        public T Get<T>() where T : class
        {
            for (int i = 0; i < _modules.Count; i++)
                if (_modules[i] is T t) return t;
            return null;
        }

        public bool Has<T>() where T : class => Get<T>() != null;

        /// <summary>받는 피해를 인터셉터 모듈에 순서대로 통과시킨다. 0 이하가 되면 그 자리에서 끝.</summary>
        internal float InterceptDamage(float amount, Entity source)
        {
            for (int i = 0; i < _modules.Count && amount > 0f; i++)
                if (_modules[i] is IDamageInterceptor interceptor)
                    amount = interceptor.Intercept(amount, source);
            return amount;
        }

        internal void NotifyDied()
        {
            Died?.Invoke(this);
            World.NotifyDied(this);
        }

        internal void MarkRemoved()
        {
            if (IsRemoved) return;
            IsRemoved = true;
            for (int i = _modules.Count - 1; i >= 0; i--) _modules[i].OnDetach();
            Removed?.Invoke(this);
        }

        public override string ToString() => $"Entity{Id}({Faction})";
    }
}
