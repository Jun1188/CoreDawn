using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 몬스터 시스템 — 몬스터 엔티티의 생성·소멸과 틱 순서(두뇌 → 이동 → 군중 겹침 해소)를 소유한다.
    /// FactorySystem이 건물에 대해 하는 역할을 몬스터에 대해 한다. 구동은 뷰 쪽 러너(MonsterSystemHost)가 매 프레임 Tick.
    ///
    /// 시계(<see cref="Now"/>)는 dt 누적 — 두뇌의 쿨다운·타임아웃이 Time.time 대신 이것을 본다.
    /// 고정 틱·월드 시계 통합은 5단계.
    /// </summary>
    public sealed class MonsterSystem
    {
        public readonly EntityWorld World;

        /// <summary>길찾기 창구 — 이동·두뇌가 이것만 본다. 뷰 쪽 어댑터(SceneNavigation)를 러너가 꽂는다.</summary>
        public INavigation Nav { get; private set; }

        /// <summary>시스템 시계(초). 두뇌·전투의 쿨다운·타임아웃 기준.</summary>
        public float Now { get; private set; }

        /// <summary>플레이어 엔티티 — 보스가 "누가 때렸는지 모를 때" 찾는 대상. 플레이어 뷰가 심 엔티티를 붙일 때 넣는다.</summary>
        public Entity PlayerEntity { get; set; }

        /// <summary>낮인가 — 둥지 교전 규칙(DayOnly)이 본다. 러너가 TimeManager로 꽂는다. 기본 = 항상 낮.</summary>
        public Func<bool> IsDay = () => true;

        readonly List<Entity> _monsters = new List<Entity>();
        readonly List<Entity> _tickBuffer = new List<Entity>();

        /// <summary>살아 있는(제거되지 않은) 몬스터 엔티티. 순회 중 제거 금지.</summary>
        public IReadOnlyList<Entity> Monsters => _monsters;

        public event Action<Entity> Spawned;

        public MonsterSystem(EntityWorld world, INavigation nav)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Nav = nav;
            World.Removed += OnEntityRemoved;
        }

        /// <summary>월드 구독 해제 — 러너가 사라질 때. 안 부르면 죽은 시스템이 다음 씬의 제거 통지를 받는다.</summary>
        public void Dispose() => World.Removed -= OnEntityRemoved;

        public void SetNavigation(INavigation nav)
        {
            Nav = nav;
            foreach (var e in _monsters) e.Get<Movement>()?.SetNavigation(nav);
        }

        /// <summary>
        /// 몬스터를 세운다 — 엔티티 + Health + Movement + Attack + MonsterBrain. 뷰(프리팹)는 호출자가 따로 만들어 붙인다.
        /// </summary>
        public Entity Spawn(in MonsterSpec spec, Vector3 position, Vector3 facing)
        {
            var e = World.Create(Faction.Monster, position);
            if (facing.sqrMagnitude > 0.0001f) { facing.y = 0f; e.Facing = facing.normalized; }

            e.Add(new Health(spec.MaxHp));
            e.Add(new Effects());   // 받는 배율·지속 효과 — Movement보다 먼저(속도 배율을 읽는다)
            e.Add(new Movement(spec, Nav));
            e.Add(new Attack(spec.AttackRange, spec.AttackCooldown, spec.AttackEffects));
            e.Add(new MonsterBrain(this, spec));

            _monsters.Add(e);
            Spawned?.Invoke(e);
            return e;
        }

        /// <summary>월드에서 뺀다. 뷰는 Entity.Removed를 받아 스스로 사라진다.</summary>
        public void Despawn(Entity e)
        {
            if (e == null || e.IsRemoved) return;
            World.Remove(e);
        }

        void OnEntityRemoved(Entity e) => _monsters.Remove(e);

        /// <summary>한 틱 — 두뇌(상태기) → 이동 → 군중 겹침 해소. 순회 중 제거(복귀 후 소멸)가 있어 스냅샷을 돈다.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            Now += dt;

            _tickBuffer.Clear();
            _tickBuffer.AddRange(_monsters);
            for (int i = 0; i < _tickBuffer.Count; i++)
            {
                var e = _tickBuffer[i];
                if (e.IsRemoved) continue;
                e.Get<MonsterBrain>()?.Tick(dt);
                if (e.IsRemoved) continue;   // 두뇌가 소멸시켰을 수 있다(복귀 도착)
                e.Get<Movement>()?.Tick(dt);
            }

            SolveCrowd(dt);
        }

        // ── 군중 겹침 해소 (구 CrowdSystem) ───────────────────────────
        // SC2식 — 물리 엔진이 아니라 위치 기반 겹침 해소(positional correction)를 중앙에서 한 패스에 돈다.
        // 힘이 아니라 겹친 양(overlap)을 그대로 되돌린다 → 한 프레임에 딱 붙어 정지. 비대칭 분배 — 움직이는 쪽이
        // 우선권을 갖고 서 있는 쪽이 비켜준다. 이동 속도로 클램프하지 않는다 — 겹침 해소는 이동과 별개 레이어.
        // 대상은 몬스터끼리만. 플레이어는 밀지 않고, 몬스터가 플레이어를 미는 건 PhysX 접촉(뷰)에 맡긴다.

        const float MovingWeight = 3f;
        const float IdleWeight = 1f;

        // 프레임당 밀림을 이 속도(m/s)로 제한 — 겹침을 한 프레임에 전부 되돌리면 수십 cm를 즉시 워프해 "뒤로 순간이동"으로 보인다
        const float MaxSeparationSpeed = 4f;

        readonly List<Vector3> _corrections = new List<Vector3>();

        void SolveCrowd(float dt)
        {
            var members = _tickBuffer;   // 이번 틱 스냅샷
            int n = members.Count;
            if (n < 2) return;

            _corrections.Clear();
            for (int i = 0; i < n; i++) _corrections.Add(Vector3.zero);

            for (int i = 0; i < n; i++)
            {
                var a = members[i];
                var ma = CrowdMember(a);
                if (ma == null) continue;
                float ra = ma.CrowdRadius;
                Vector3 pa = a.Position;

                for (int j = i + 1; j < n; j++)
                {
                    var b = members[j];
                    var mb = CrowdMember(b);
                    if (mb == null) continue;
                    float rb = mb.CrowdRadius;

                    Vector3 d = b.Position - pa;
                    d.y = 0f;
                    float radiusSum = ra + rb;
                    float sqr = d.sqrMagnitude;
                    if (sqr >= radiusSum * radiusSum) continue;

                    float dist = Mathf.Sqrt(sqr);
                    Vector3 dir;
                    if (dist < 0.0001f)
                    {
                        // 완전히 겹친 경우 — 번호 기반 고정 방향 (좌우 진동 방지)
                        float angle = ((a.Id.GetHashCode() & 0x7fffffff) % 360) * Mathf.Deg2Rad;   // 엔티티마다 고정된 각 — 같은 자리에 겹친 둘이 매 틱 다른 방향으로 흔들리지 않게
                        dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    }
                    else dir = d / dist;

                    float overlap = radiusSum - dist;
                    float wa = ma.IsMoving ? MovingWeight : IdleWeight;
                    float wb = mb.IsMoving ? MovingWeight : IdleWeight;
                    float shareA = wb / (wa + wb); // 상대 가중치만큼 밀린다 — 가중치 큰 쪽이 덜 밀림

                    _corrections[i] -= dir * (overlap * shareA);
                    _corrections[j] += dir * (overlap * (1f - shareA));
                }
            }

            float maxStep = MaxSeparationSpeed * dt;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = _corrections[i];
                if (c == Vector3.zero) continue;

                float len = c.magnitude;
                if (len > maxStep) c *= maxStep / len;

                var m = members[i];
                Vector3 next = m.Position + c;

                // 건물/장애물 셀로는 밀려나지 않는다
                if (Nav != null && !Nav.IsWalkable(next)) continue;
                m.Position = next;
            }
        }

        // 시체·제거된 개체·반지름 0은 밀지도 밀리지도 않는다
        static Movement CrowdMember(Entity e)
        {
            if (e == null || !e.IsAlive) return null;
            var m = e.Get<Movement>();
            return m != null && m.CrowdRadius > 0f ? m : null;
        }
    }
}
