using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Entities
{
    /// <summary>
    /// 몬스터 뷰 풀 — 정의 id 별로 "완성된 뷰 한 벌"(루트 + 캡슐·Rigidbody + glb 모델·클립 + 체력바)을 보관한다.
    /// 밤 버스트는 한 프레임에 열 마리 안팎을 조립하는데, 스킨 메시 Instantiate 와 월드 캔버스 생성이 그 스파이크의 몸통이다.
    /// 심이 엔티티를 지우면(<see cref="MonsterView.OnEntityRemoved"/>) 뷰는 부서지는 대신 여기로 돌아오고,
    /// 다음 스폰(<see cref="Combat.MonsterSpawner.AttachView"/>)이 같은 정의면 꺼내 쓴다.
    ///
    /// 되돌리는 상태: 위치·회전·부모(여기서), 심 참조·두뇌 구독(<see cref="MonsterView.OnEntityDetached"/>),
    /// 사망 플래그·클립·가라앉기 오프셋(<see cref="MonsterVisualController"/> OnEnable), 체력바(뷰의 OnHealthChanged 릴레이에
    /// 묶여 있어 새 엔티티가 붙으면 자동). 정의 id 가 다르면 섞이지 않으므로 모델·콜라이더·자세는 그대로 맞다.
    ///
    /// 씬과 함께 사라진다 — 풀 루트는 씬 오브젝트라 씬 전환에 부서지고, 남은 참조는 꺼낼 때 null 검사로 걸러진다.
    /// </summary>
    public static class MonsterViewPool
    {
        static readonly Dictionary<string, Stack<MonsterView>> free = new Dictionary<string, Stack<MonsterView>>();
        static Transform root;

        /// <summary>쓸 수 있는 뷰가 있으면 꺼내 세우고, 없으면 새로 조립한다.</summary>
        public static MonsterView Rent(EntityDef def, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            if (free.TryGetValue(def.Id, out var stack))
            {
                while (stack.Count > 0)
                {
                    var view = stack.Pop();
                    if (view == null) continue;   // 씬 전환으로 부서진 것
                    view.transform.SetParent(parent, false);
                    view.transform.SetPositionAndRotation(position, rotation);
                    view.gameObject.SetActive(true);   // MonsterVisualController.OnEnable 이 연출 상태를 되돌린다
                    return view;
                }
            }
            var go = MonsterAssembler.Build(def, position, rotation, parent);
            var fresh = go.GetComponent<MonsterView>();
            fresh.PoolKey = def.Id;
            return fresh;
        }

        /// <summary>심 엔티티가 떨어진 뷰를 거둔다 — 비활성으로 풀 루트 아래에 둔다. 엔티티가 아직 붙어 있으면 거부(순서 오류).</summary>
        public static void Return(MonsterView view)
        {
            if (view == null) return;
            if (view.Entity != null)
            {
                Debug.LogError($"[MonsterViewPool] '{view.name}': 엔티티가 붙은 채로 반납됐습니다 — 먼저 Detach 해야 합니다.", view);
                return;
            }
            if (string.IsNullOrEmpty(view.PoolKey))
            {
                Object.Destroy(view.gameObject);   // 풀을 거치지 않고 만들어진 뷰 — 보관할 열쇠가 없다
                return;
            }
            view.gameObject.SetActive(false);
            view.transform.SetParent(Root, false);
            if (!free.TryGetValue(view.PoolKey, out var stack)) free[view.PoolKey] = stack = new Stack<MonsterView>();
            stack.Push(view);
        }

        /// <summary>보관 중인 뷰 수(정의별 합) — 검증·디버그용.</summary>
        public static int FreeCount
        {
            get
            {
                int n = 0;
                foreach (var s in free.Values) foreach (var v in s) if (v != null) n++;
                return n;
            }
        }

        static Transform Root
        {
            get
            {
                if (root == null)
                {
                    var go = new GameObject("MonsterViewPool") { hideFlags = HideFlags.DontSave };
                    root = go.transform;
                }
                return root;
            }
        }

        // 도메인 리로드를 끈 환경(Enter Play Mode Options)에서 static이 플레이를 넘어 살아남는 것 방지
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { free.Clear(); root = null; }
    }
}
