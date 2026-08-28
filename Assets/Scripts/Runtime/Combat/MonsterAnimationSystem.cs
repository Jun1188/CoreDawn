using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.UI;

namespace CoreDawn.Combat
{
    /// <summary>
    /// 몬스터 애니메이션의 <b>중앙 구동부 겸 LOD</b>. <see cref="CrowdSystem"/>과 같은 구조다 —
    /// 정적 등록부 + 첫 등록 때 자동 생성되는 러너, 개체별 Update() 대신 한 패스로 처리.
    ///
    /// 왜 필요한가: 스킨드 메시 + Animator는 몬스터 하나당 결코 싸지 않다. 밤 웨이브에 수십~수백
    /// 마리가 깔리면 대부분은 화면 밖이거나 멀리 있어 매 프레임 본을 갱신할 이유가 없다.
    /// 여기서 거리·가시성으로 3단계를 나눠, 가까운 몇 마리만 제값을 치르게 한다:
    ///
    ///   Full     — 그대로 Animator가 매 프레임 돈다. 그림자 켬.
    ///   Reduced  — Animator를 끄고 <c>Animator.Update(누적dt)</c>를 저빈도로 직접 부른다.
    ///              프레임마다 갱신 대상이 흩어지도록 위상을 어긋내 스파이크를 막는다. 그림자 끔.
    ///   Culled   — 아무것도 하지 않는다.
    ///
    /// <c>animator.enabled = false</c>여도 파라미터 설정은 그대로 먹고, 트리거는 큐에 남았다가
    /// 다음 수동 Update에서 소비된다. 그래서 저빈도 티어에서도 공격·피격 모션을 놓치지 않는다.
    /// </summary>
    public static class MonsterAnimationSystem
    {
        public enum Tier { Full, Reduced, Culled }

        /// <summary>이 거리 안(월드 단위)은 제값을 치를 후보다.</summary>
        private const float NearDistance = 20f;

        /// <summary>이 거리 밖은 화면 안이어도 갱신을 멈춘다.</summary>
        private const float FarDistance = 45f;

        /// <summary>동시에 Full 티어로 돌 수 있는 최대 마리 수 — 가까운 순으로 배분한다.</summary>
        private const int FullRateBudget = 25;

        /// <summary>Reduced 티어의 목표 갱신 주기(초). 12Hz면 걷는 다리가 끊겨 보이지 않는다.</summary>
        private const float ReducedInterval = 1f / 12f;

        /// <summary>티어 재판정 주기(초). 매 프레임 다시 볼 이유가 없다.</summary>
        private const float TierRefreshInterval = 0.25f;

        private class Member
        {
            public MonsterVisualController Visual;
            public Tier Tier = Tier.Culled;
            public float Accumulated;   // 마지막 갱신 이후 누적 시간
            public float Phase;         // 개체별 위상 — 같은 프레임에 몰리지 않게 한다
        }

        private static readonly List<Member> members = new List<Member>();
        private static readonly Dictionary<MonsterVisualController, Member> lookup
            = new Dictionary<MonsterVisualController, Member>();

        private static MonsterAnimationRunner runner;
        private static float nextTierRefresh;

        // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 static이 플레이를 넘어 살아남는 것 방지 —
        // CrowdSystem과 같은 처리.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            members.Clear();
            lookup.Clear();
            runner = null;
            nextTierRefresh = 0f;
        }

        public static void Register(MonsterVisualController visual)
        {
            if (visual == null || lookup.ContainsKey(visual)) return;

            var member = new Member
            {
                Visual = visual,
                // 첫 갱신 시점을 흩어 놓는다. 웨이브가 한꺼번에 스폰돼도 같은 프레임에 몰리지 않는다.
                Phase = Random.value * ReducedInterval,
            };
            members.Add(member);
            lookup.Add(visual, member);

            // 새로 들어온 개체는 다음 판정까지 Full로 둔다 — 스폰 순간 굳어 보이는 것보다 낫다
            Apply(member, Tier.Full);

            if (runner == null && Application.isPlaying)
            {
                var go = new GameObject("MonsterAnimationSystem (Runtime)");
                go.hideFlags = HideFlags.DontSave;
                runner = go.AddComponent<MonsterAnimationRunner>();
            }
        }

        public static void Unregister(MonsterVisualController visual)
        {
            if (visual == null || !lookup.TryGetValue(visual, out Member member)) return;
            lookup.Remove(visual);
            members.Remove(member);
        }

        /// <summary>러너가 매 프레임 부른다.</summary>
        public static void Tick(float deltaTime)
        {
            int count = members.Count;
            if (count == 0) return;

            if (Time.time >= nextTierRefresh)
            {
                nextTierRefresh = Time.time + TierRefreshInterval;
                RefreshTiers();
            }

            for (int i = 0; i < count; i++)
            {
                Member m = members[i];
                if (m.Visual == null) continue;

                switch (m.Tier)
                {
                    case Tier.Full:
                        // Animator는 스스로 돈다 — 연출 로직만 밀어준다
                        m.Visual.VisualTick(deltaTime);
                        break;

                    case Tier.Reduced:
                        m.Accumulated += deltaTime;
                        if (m.Accumulated + m.Phase < ReducedInterval) break;

                        float step = m.Accumulated;
                        m.Accumulated = 0f;
                        m.Phase = 0f;
                        m.Visual.VisualTick(step);
                        if (m.Visual.Animator != null) m.Visual.Animator.Update(step);
                        break;

                    case Tier.Culled:
                        break;
                }
            }
        }

        /// <summary>
        /// 거리·가시성으로 티어를 다시 정한다. 예산을 넘으면 <b>가까운 순</b>으로 Full을 배분한다.
        /// 배열 정렬 대신 거리 임계값을 이분 탐색하듯 좁히지 않고, 그냥 한 번 훑어 예산 경계 거리를
        /// 구한다 — 몬스터 수백 마리에 0.25초마다 한 번이면 이 정도가 가장 읽기 쉽다.
        /// </summary>
        private static void RefreshTiers()
        {
            Camera cam = Viewer;
            if (cam == null)
            {
                // 기준 카메라가 사라졌다(씬 전환·플레이어 사망 등). 그냥 return하면 이미 Reduced/Culled로
                // 내려간 개체가 Animator가 꺼진 채 영영 얼어붙는다 — 판단 근거가 없을 때는 전부 살린다.
                for (int i = 0; i < members.Count; i++)
                    if (members[i].Visual != null && members[i].Tier != Tier.Full)
                        Apply(members[i], Tier.Full);
                return;
            }
            Vector3 eye = cam.transform.position;

            // 1) 후보(가까움 + 화면 안)의 거리를 모아 예산 경계를 찾는다
            candidates.Clear();
            for (int i = 0; i < members.Count; i++)
            {
                Member m = members[i];
                if (m.Visual == null) continue;

                float sqr = (m.Visual.transform.position - eye).sqrMagnitude;
                if (sqr <= NearDistance * NearDistance && IsVisible(m.Visual))
                    candidates.Add(sqr);
            }

            float budgetCutoff = float.PositiveInfinity;
            if (candidates.Count > FullRateBudget)
            {
                candidates.Sort();
                budgetCutoff = candidates[FullRateBudget - 1];
            }

            // 2) 티어 확정
            for (int i = 0; i < members.Count; i++)
            {
                Member m = members[i];
                if (m.Visual == null) continue;

                float sqr = (m.Visual.transform.position - eye).sqrMagnitude;
                bool visible = IsVisible(m.Visual);

                Tier tier;
                if (!visible || sqr > FarDistance * FarDistance) tier = Tier.Culled;
                else if (sqr <= NearDistance * NearDistance && sqr <= budgetCutoff) tier = Tier.Full;
                else tier = Tier.Reduced;

                if (tier != m.Tier) Apply(m, tier);
            }
        }

        private static readonly List<float> candidates = new List<float>();

        /// <summary>렌더러 하나라도 지난 프레임에 그려졌는가 — 엔진의 컬링 결과를 그대로 빌린다.</summary>
        private static bool IsVisible(MonsterVisualController visual)
        {
            var renderers = visual.Renderers;
            if (renderers == null || renderers.Length == 0) return true; // 판단 근거가 없으면 살려둔다
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].isVisible) return true;
            return false;
        }

        private static void Apply(Member member, Tier tier)
        {
            Tier previous = member.Tier;
            member.Tier = tier;
            member.Accumulated = 0f;

            var visual = member.Visual;
            var animator = visual.Animator;

            if (animator != null)
            {
                // Full일 때만 Animator가 스스로 돈다. 나머지는 우리가 직접 Update를 부르거나 아예 멈춘다.
                animator.enabled = tier == Tier.Full;
                if (tier == Tier.Full)
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }

            // 멀리 있는 몬스터의 그림자는 화면에서 몇 픽셀도 차지하지 않으면서 스킨된 메시를
            // 그림자 패스에서 한 번 더 그리게 만든다 — 물량에서 가장 먼저 버릴 것.
            var shadow = tier == Tier.Full
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            var renderers = visual.Renderers;
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null) continue;
                    r.shadowCastingMode = shadow;
                    if (r is SkinnedMeshRenderer smr)
                        smr.quality = tier == Tier.Full ? SkinQuality.Auto : SkinQuality.Bone2;
                }
            }

            // 컬링에서 돌아오면 이동 기준점을 다시 잡아야 한다 — 그동안의 이동이 한 번에
            // 속도로 환산되면 서 있는 몬스터가 달리는 모션을 낸다.
            if (previous == Tier.Culled && tier != Tier.Culled)
                visual.ResumeFromCulled();
        }

        /// <summary>
        /// 지금 티어 분포 — 프로파일링·디버그용. 몬스터가 굳어 보인다는 제보가 오면
        /// 제일 먼저 이걸 찍어 보면 된다(거리 상수가 맵 규모에 안 맞는 경우가 대부분이다).
        /// </summary>
        public static string Describe()
        {
            int full = 0, reduced = 0, culled = 0;
            for (int i = 0; i < members.Count; i++)
            {
                switch (members[i].Tier)
                {
                    case Tier.Full: full++; break;
                    case Tier.Reduced: reduced++; break;
                    default: culled++; break;
                }
            }
            return $"몬스터 {members.Count}마리 — Full {full} / Reduced {reduced} / Culled {culled}";
        }

        /// <summary>
        /// 기준 카메라. <b>캐시하지 않는다.</b>
        ///
        /// 처음엔 "죽었을 때만 다시 찾는" 캐시를 뒀는데, 그러면 카메라가 파괴되지 않고 꺼지거나
        /// 다른 카메라로 <c>Camera.main</c>이 갈릴 때(컷신 카메라 등) 낡은 카메라를 계속 붙들어
        /// 엉뚱한 위치에서 거리를 잰다. 실제로 재현했다.
        ///
        /// <c>Camera.main</c>은 Unity 2020부터 엔진이 캐시하므로 매번 태그를 훑지 않는다. 게다가
        /// 여기는 초당 4번(TierRefreshInterval)만 도는 자리다 — 아낄 것이 없는 곳에서 아끼려다
        /// 버그를 산 셈이라, 캐시를 걷어냈다.
        /// </summary>
        private static Camera Viewer => Camera.main;
    }

    /// <summary>
    /// 구동용 러너 — 첫 등록 때 <see cref="MonsterAnimationSystem"/>이 만든다.
    /// LateUpdate인 이유: 모든 Monster.Update(이동)와 CrowdSystem의 겹침 해소가 끝난 위치에서
    /// 속도를 재야 실제 변위가 나온다.
    /// </summary>
    public class MonsterAnimationRunner : MonoBehaviour
    {
        private void LateUpdate() => MonsterAnimationSystem.Tick(Time.deltaTime);
    }
}
