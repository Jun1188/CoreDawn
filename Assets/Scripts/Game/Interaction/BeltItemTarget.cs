using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Factory;
using CoreDawn.Inventories;
using CoreDawn.Worlds;
using CoreDawn.Data;

namespace CoreDawn.Interaction
{
    /// <summary>
    /// 벨트 위를 지나가는 아이템을 조준해 낚아채는 상호작용 대상.
    ///
    /// 벨트 아이템에는 콜라이더가 없다 — 그것이 벨트 시각화의 설계 전제다
    /// ("존재가 아니라 그림", <see cref="BeltItemView"/>). 그래서 물리에 묻지 않고
    /// <see cref="BeltItemView.DrawnThisFrame"/>(이번 프레임에 실제로 그린 좌표)에
    /// 레이-구 교차를 직접 돌린다. 곱셈 열 번짜리 계산이라 사거리 안에 수백 개가 있어도
    /// 물리 쿼리 한 번보다 싸고, 아이템마다 GameObject·콜라이더를 만들 이유가 사라진다.
    ///
    /// 아이템마다 인스턴스를 만들지 않는다 — <see cref="PlayerInteractionManager"/>가
    /// 하나만 들고 매 프레임 조준 결과를 갈아 끼운다. 프레임마다 new 하면 그대로 GC다.
    ///
    /// 심 좌표가 아니라 <b>그려진 좌표</b>에 맞추는 것이 핵심이다. 심은 10Hz로만 갱신되고
    /// 화면은 그 사이를 외삽하므로, 심 좌표로 판정하면 조준점 아래 있는 것과 집히는 것이 달라진다.
    /// </summary>
    public sealed class BeltItemTarget : IInteractable
    {
        BeltSegment seg;
        int index = -1;
        ItemDataSO item;

        /// <summary>
        /// 조준선에 가장 먼저 걸리는 벨트 아이템을 이번 프레임의 대상으로 잡는다.
        /// </summary>
        /// <param name="maxDist">여기까지만 본다. 호출자가 물리 히트 거리를 넘겨 주면
        /// 가림 처리가 공짜다 — 벽이 더 가까우면 그 너머 아이템은 사거리 밖이 된다.</param>
        /// <param name="radius">판정 반경(m). 매 호출 받는다 — 플레이 중 인스펙터로 손맛을 맞출 수 있게.</param>
        /// <param name="view">벨트 렌더 뷰. 스스로 찾지 않는다 — 씬 경계를 넘는 배선은
        /// GameBootstrap이 독점한다(주입을 잊으면 조용히 도는 대신 아무것도 집히지 않는다).</param>
        public bool TryAim(BeltItemView view, Ray ray, float maxDist, float radius)
        {
            Clear();
            if (view == null || maxDist <= 0f) return false;

            var drawn = view.DrawnThisFrame;
            float r2 = Mathf.Max(0.01f, radius);
            r2 *= r2;
            float bestT = maxDist;

            for (int i = 0; i < drawn.Count; i++)
            {
                var d = drawn[i];
                if (d.Item == null || d.Seg == null) continue;

                // 레이-구 교차: 중심을 조준선에 투영하고, 남은 수직 거리를 반경과 견준다
                Vector3 to = d.World - ray.origin;
                float t = Vector3.Dot(to, ray.direction);
                if (t < 0f || t >= bestT) continue;          // 등 뒤이거나, 이미 더 가까운 것을 찾았다
                if (to.sqrMagnitude - t * t > r2) continue;

                bestT = t;
                seg   = d.Seg;
                index = d.Index;
                item  = d.Item;
            }

            return item != null;
        }

        public void Clear()
        {
            seg   = null;
            index = -1;
            item  = null;
        }

        public string Prompt => item != null ? $"{DisplayNameOf(item)} 줍기" : null;

        /// <summary>조준한 아이템을 벨트에서 꺼내 가방으로.</summary>
        public void Interact(PlayerController player)
        {
            if (seg == null || item == null) return;

            int at = ResolveIndex();
            if (at < 0) return;

            var holder = PlayerInventoryHolder.Instance;
            if (holder == null) return;

            // 넣을 자리를 먼저 확인하고 꺼낸다 — 꺼낸 뒤 넣기에 실패하면 아이템이 증발한다.
            // 검사 순서는 AddItemToPlayer와 같게 (핫바 → 가방).
            if (!holder.HotbarContainer.HasRoomFor(item) && !holder.MainContainer.HasRoomFor(item))
                return;

            if (seg.TryTakeAt(at, out var taken))
                holder.AddItemToPlayer(taken, 1);

            Clear();   // 이 인덱스는 방금 무효가 됐다 — 다음 프레임 조준이 다시 채운다
        }

        /// <summary>
        /// 조준 당시의 인덱스를 지금 다시 확인한다.
        ///
        /// 조준(Update)과 실행(E 입력) 사이에 심 틱이 돌 수 있다. 출구에 닿은 아이템이
        /// 빠져나가면 <c>_items</c>에서 RemoveAt이 일어나 <b>뒤 아이템의 인덱스가 앞으로 밀린다</b> —
        /// 그대로 쓰면 조준한 것이 아니라 그 뒤 아이템을 꺼내게 된다.
        ///
        /// 그래서 종류가 같은지 확인하고, 어긋났으면 가장 가까운 같은 종류를 찾는다.
        /// 못 찾으면 아무것도 하지 않는다 — 엉뚱한 것을 꺼내느니 한 번 헛치는 편이 낫다.
        /// </summary>
        int ResolveIndex()
        {
            var items = seg.Items;
            if (index >= 0 && index < items.Count && items[index].item == item) return index;

            for (int off = 1; off <= items.Count; off++)
            {
                int lo = index - off, hi = index + off;
                if (lo >= 0 && lo < items.Count && items[lo].item == item) return lo;
                if (hi >= 0 && hi < items.Count && items[hi].item == item) return hi;
            }
            return -1;
        }

        static string DisplayNameOf(ItemDataSO i) =>
            i == null ? "" : string.IsNullOrEmpty(i.displayName) ? i.name : i.displayName;
    }
}
