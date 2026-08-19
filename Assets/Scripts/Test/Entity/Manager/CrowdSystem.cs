using System.Collections.Generic;
using UnityEngine;

// SC2식 군중 겹침 해소 — 물리 엔진이 아니라 위치 기반 겹침 해소(positional correction)를
// 중앙에서 한 패스에 돈다. 반발·마찰·속도 적분 없이 "겹쳤으면 겹친 만큼 떼어놓는다"만 남긴 것.
//
// 구 방식(MovementComponent.ApplySeparation)과의 차이:
//   힘(1-dist/r 세기)이 아니라 겹친 양(overlap)을 그대로 되돌린다 → 한 프레임에 딱 붙어 정지
//   개체별 OverlapSphere가 아니라 등록부를 시스템이 한 패스에 처리
//   비대칭 분배 — 움직이는 쪽이 우선권을 갖고 서 있는 쪽이 비켜준다 (대형이 정체를 가르는 그 느낌)
//   이동 속도로 클램프하지 않는다 — 겹침 해소는 이동과 별개 레이어 (꽉 낀 상황도 반드시 풀린다)
//
// 대상은 몬스터끼리만. 플레이어는 몬스터를 밀지 않고(등록부 밖), 몬스터가 플레이어를 미는 건
// 지금처럼 PhysX 접촉(kinematic 콜라이더 vs 플레이어 dynamic RB)에 맡긴다.
public static class CrowdSystem
{
    // 밀림 몫 가중치 — 큰 쪽이 덜 밀린다. 움직이는 쪽 3 : 서 있는 쪽 1이면
    // 이동 중인 개체는 겹침의 1/4만, 정지한 개체가 3/4을 부담해 길을 비켜준다.
    private const float MovingWeight = 3f;
    private const float IdleWeight = 1f;

    // 프레임당 밀림을 이 속도(m/s)로 제한한다. 겹침을 한 프레임에 전부 되돌리면
    // 수십 cm를 즉시 워프해 "뒤로 순간이동"으로 보인다 — 특히 여러 마리가 플레이어
    // 앞에 몰릴 때. 몇 프레임에 걸쳐 풀어도 결과는 같고 눈에는 밀려나는 걸음으로 보인다.
    private const float MaxSeparationSpeed = 4f;

    private static readonly List<Monster> members = new List<Monster>();
    private static readonly List<Vector3> corrections = new List<Vector3>();
    private static CrowdSystemRunner runner;

    // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 static이 플레이를 넘어 살아남는 것 방지
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        members.Clear();
        runner = null;
    }

    // Monster.OnEnable이 호출한다. 첫 등록 때 구동용 러너를 씬에 만든다.
    public static void Register(Monster monster)
    {
        if (monster == null || members.Contains(monster)) return;
        members.Add(monster);

        if (runner == null && Application.isPlaying)
        {
            var go = new GameObject("CrowdSystem (Runtime)");
            go.hideFlags = HideFlags.DontSave;
            runner = go.AddComponent<CrowdSystemRunner>();
        }
    }

    public static void Unregister(Monster monster) => members.Remove(monster);

    /// <summary>
    /// 겹침 해소 한 패스 — 모든 몬스터의 이동(Update)이 끝난 뒤(LateUpdate) 러너가 호출한다.
    /// 보정을 전부 모아 마지막에 한 번 적용해 순회 순서에 따른 편향을 없앤다.
    /// </summary>
    public static void Solve()
    {
        int n = members.Count;
        if (n < 2) return;

        corrections.Clear();
        for (int i = 0; i < n; i++) corrections.Add(Vector3.zero);

        for (int i = 0; i < n; i++)
        {
            var a = members[i];
            if (!IsActive(a)) continue;
            float ra = a.Movement.CrowdRadius;
            if (ra <= 0f) continue;
            Vector3 pa = a.transform.position;

            for (int j = i + 1; j < n; j++)
            {
                var b = members[j];
                if (!IsActive(b)) continue;
                float rb = b.Movement.CrowdRadius;
                if (rb <= 0f) continue;

                Vector3 d = b.transform.position - pa;
                d.y = 0f;
                float radiusSum = ra + rb;
                float sqr = d.sqrMagnitude;
                if (sqr >= radiusSum * radiusSum) continue;

                float dist = Mathf.Sqrt(sqr);
                Vector3 dir;
                if (dist < 0.0001f)
                {
                    // 완전히 겹친 경우 — 인스턴스 ID 기반 고정 방향 (좌우 진동 방지)
                    float angle = (a.GetInstanceID() % 360) * Mathf.Deg2Rad;
                    dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }
                else dir = d / dist;

                float overlap = radiusSum - dist;
                float wa = a.Movement.IsMoving ? MovingWeight : IdleWeight;
                float wb = b.Movement.IsMoving ? MovingWeight : IdleWeight;
                float shareA = wb / (wa + wb); // 상대 가중치만큼 밀린다 — 가중치 큰 쪽이 덜 밀림

                corrections[i] -= dir * (overlap * shareA);
                corrections[j] += dir * (overlap * (1f - shareA));
            }
        }

        float maxStep = MaxSeparationSpeed * Time.deltaTime;
        for (int i = 0; i < n; i++)
        {
            Vector3 c = corrections[i];
            if (c == Vector3.zero) continue;

            float len = c.magnitude;
            if (len > maxStep) c *= maxStep / len;

            var m = members[i];
            Vector3 next = m.transform.position + c;

            // 건물/장애물 셀로는 밀려나지 않는다 (구 코드와 같은 워커빌리티 필터)
            if (GridManager.Instance != null)
            {
                Node node = GridManager.Instance.NodeFromWorldPoint(next);
                if (node == null || !GridManager.Instance.IsWalkable(node)) continue;
            }
            m.transform.position = next;
        }
    }

    // 시체·비활성 개체는 밀지도 밀리지도 않는다
    private static bool IsActive(Monster m)
        => m != null && !m.IsDead && m.gameObject.activeInHierarchy && m.Movement != null;
}

// 구동용 러너 — 첫 몬스터 등록 때 CrowdSystem이 만든다.
// LateUpdate인 이유: 모든 Monster.Update(이동)가 끝난 위치에서 겹침을 풀어야 한다.
public class CrowdSystemRunner : MonoBehaviour
{
    private void LateUpdate() => CrowdSystem.Solve();
}
