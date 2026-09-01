using System.Collections.Generic;
using EPOOutline;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Interaction;
using CoreDawn.Pings;
using CoreDawn.Placement;
using CoreDawn.Combat;

namespace CoreDawn.Visuals
{
    /// <summary>
    /// 플레이어 반경 안의 몬스터에 아웃라인을 켠다 — 드롭 아이템이 쓰는 것과 같은 EPO(Easy Performant Outline).
    ///
    /// 몬스터 프리팹을 고치지 않는다. 반경에 처음 들어온 몬스터에 <see cref="Outlinable"/>을 붙이고
    /// 스킨드 메시를 대상으로 등록한 뒤, 이후로는 거리 판정으로 컴포넌트를 켜고 끌 뿐이다.
    /// 그리는 쪽(<c>Outliner</c>)은 플레이어 카메라에 이미 있으므로 여기서 할 일이 없다.
    ///
    /// 판정은 0.2초마다 — 아웃라인이 켜지고 꺼지는 경계에서 프레임 단위 정확도가 의미 없고,
    /// 몬스터 수만큼 매 프레임 거리를 재는 것은 낭비다.
    ///
    /// 플레이어 참조는 GameBootstrap이 꽂는다 — 씬 경계를 넘는 탐색은 그 파일이 독점한다.
    /// </summary>
    public class MonsterOutlineProximity : MonoBehaviour
    {
        [Tooltip("이 거리(m) 안의 몬스터에 아웃라인을 켠다.")]
        [SerializeField] float radius = 50f;

        [Tooltip("거리 판정 주기(초).")]
        [SerializeField] float interval = 0.2f;

        [Header("아웃라인 — 기본값은 DroppedItem 프리팹과 같다")]
        [SerializeField] Color color = new(0.9539399f, 0.9996342f, 1.498039f, 1f);
        [SerializeField, Range(0f, 1f)] float dilateShift = 0.5f;
        [SerializeField, Range(0f, 1f)] float blurShift = 0f;

        [Tooltip("반경의 기준점. Combat이 별도 씬으로 올라오므로 GameBootstrap이 플레이어를 꽂는다.")]
        [SerializeField] Transform player;

        readonly Dictionary<MonsterView, Outlinable> outlines = new();
        readonly List<MonsterView> stale = new();
        float nextTick;

        /// <summary>플레이어 루트 주입. 인스펙터 배선이 이미 있으면 덮지 않는다 (PlacementSystem.Inject와 같은 규칙).</summary>
        public void Inject(Transform playerRoot)
        {
            if (player == null) player = playerRoot;
        }

        void Update()
        {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + interval;

            if (player == null) { DisableAll(); return; }

            Vector3 origin = player.position;
            float r2 = radius * radius;

            // 살아 있는 몬스터 — 거리로 켜고 끈다. 죽어가는 것은 끈다(시체에 테두리가 남지 않게)
            var monsters = SimRunner.Monsters.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var m = EntityViewRegistry.ViewOf<MonsterView>(monsters[i]);
                if (m == null) continue;

                bool near = !m.IsDead && (m.transform.position - origin).sqrMagnitude <= r2;

                if (!outlines.TryGetValue(m, out var o))
                {
                    if (!near) continue;              // 멀리 있는 몬스터에 미리 붙여 둘 이유가 없다
                    o = Attach(m);
                    outlines[m] = o;
                }

                if (o != null && o.enabled != near) o.enabled = near;
            }

            // 등록에서 빠진 몬스터(파괴·풀 반납)는 잊는다 — 되살아나면 위 루프가 다시 잡는다
            stale.Clear();
            foreach (var kv in outlines)
                if (kv.Key == null || !kv.Key.isActiveAndEnabled) stale.Add(kv.Key);

            foreach (var m in stale)
            {
                OutlinePool.Return(outlines[m]);
                outlines.Remove(m);
            }
        }

        Outlinable Attach(MonsterView m)
        {
            var o = OutlinePool.Rent(m.gameObject, color, dilateShift, blurShift);   // 풀에서 빌려 몬스터 렌더러를 가리킨다 — 몬스터에 컴포넌트를 붙이지 않는다
            if (o != null) o.enabled = false;   // 켜는 것은 거리 판정이 한다
            return o;
        }

        void OnDisable()
        {
            foreach (var kv in outlines) OutlinePool.Return(kv.Value);
            outlines.Clear();
        }

        void DisableAll()
        {
            foreach (var kv in outlines)
                if (kv.Value != null) kv.Value.enabled = false;
        }
    }
}
