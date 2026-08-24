using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 벨트 위 아이템 시각화 — 심 데이터(BeltSegment.Items)를 매 프레임 그리는 immediate-mode 뷰.
/// FactoryBootstrap이 자기 GO에 자동 부착한다 (씬 배선 불필요).
///
/// 원칙:
///  - 벨트 아이템은 "존재"가 아니라 "그림" — 물리·콜라이더·상호작용 없음 (성능 핵심)
///  - 아이템 정체성 추적 없음 — 매 프레임 풀에서 꺼내 통째로 다시 배치 (동기화 버그 원천 차단)
///  - 규모가 수천 개로 커지면 같은 위치 계산 위에 Graphics.RenderMeshInstanced로 교체 (v2)
///
/// FBX 전환 예정: 비주얼 적용은 ApplyVisual() 한 곳 — 스프라이트 → 메시로 바꿀 때 여기만 수정.
/// </summary>
public class BeltItemView : MonoBehaviour
{
    [Tooltip("벨트 표면 위 아이템 표시 높이")]
    [SerializeField] private float itemHeight = 1.5f;

    [Tooltip("아이템 표시 크기 배율")]
    [SerializeField] private float itemScale = 0.5f;

    private readonly List<SpriteRenderer> pool = new();
    private int used;

    /// <summary>이번 프레임에 그린 아이템 하나 — 조준(BeltItemTarget)이 이 좌표에 맞춘다.</summary>
    public readonly struct Drawn
    {
        public readonly BeltSegment Seg;
        public readonly int Index;          // 세그먼트 안 인덱스 (0 = 출구 쪽)
        public readonly Vector3 World;      // 외삽까지 반영된 실제 표시 좌표
        public readonly ItemDataSO Item;

        public Drawn(BeltSegment seg, int index, Vector3 world, ItemDataSO item)
        { Seg = seg; Index = index; World = world; Item = item; }
    }

    private readonly List<Drawn> drawn = new();

    /// <summary>
    /// 직전 LateUpdate가 그린 목록. 벨트 아이템은 콜라이더가 없어 조준이 이것을 훑는다 —
    /// 심 좌표(틱 10Hz)가 아니라 <b>화면에 실제로 보이는 좌표</b>여야 조준점 아래 있는 것이 집힌다.
    ///
    /// Update에서 읽으면 한 프레임 묵은 값이다(이 뷰는 LateUpdate에 돈다). 60fps·2타일/초면
    /// 어긋남이 0.03타일 남짓이라 조준 반경보다 한참 작다 — 손맛에 잡히지 않는다.
    /// </summary>
    public IReadOnlyList<Drawn> DrawnThisFrame => drawn;

    private PlacementSystem placement;   // 셀 크기의 소유자 — 늦게 생길 수 있어 찾을 때까지 재시도
    private float halfCell = 0.5f;

    private void LateUpdate()   // 심 Advance(Update) 이후의 최신 상태를 그린다
    {
        // 타일 중심(건물 뷰 위치)은 셀 크기를 따라 배치되는데 에지 오프셋만 고정이면
        // 아이템이 타일마다 압축된 경로를 돌다 다음 타일로 점프한다 — 프레임당 한 번 갱신.
        if (placement == null) placement = FindFirstObjectByType<PlacementSystem>();
        halfCell = placement != null ? placement.CellSize * 0.5f : 0.5f;

        used = 0;
        drawn.Clear();
        var boot = FactoryBootstrap.Instance;
        if (boot != null && boot.Sim != null)
        {
            foreach (var seg in boot.Sim.Belts.Segments)
                DrawSegment(seg, boot);
        }

        // 이번 프레임에 안 쓴 슬롯은 끈다
        for (int i = used; i < pool.Count; i++)
            if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
    }

    private void DrawSegment(BeltSegment seg, FactoryBootstrap boot)
    {
        int n = seg.BeltCount;
        if (n == 0) return;

        // 심은 틱(기본 10Hz) 단위로만 pos를 갱신한다 — 그대로 그리면 계단식.
        // 마지막 틱 이후 흐른 시간만큼 외삽하되, 틱과 같은 규칙(간격·출구·역주행)로 클램프해
        // 다음 틱 결과와 어긋나지 않게 한다.
        float extra = seg.SpeedTilesPerSec * boot.Sim.TickLeftover;

        float prevVisual = float.MaxValue;   // index 0 = 출구 쪽 — 앞 아이템의 시각 위치
        var items = seg.Items;
        for (int i = 0; i < items.Count; i++)   // 인덱스를 남겨야 조준이 그 아이템을 꺼낼 수 있다
        {
            var (item, pos) = items[i];
            if (item == null) continue;

            float vpos = pos + extra;
            vpos = Mathf.Min(vpos, prevVisual - BeltSegment.Spacing);   // 앞 아이템과 간격 유지
            vpos = Mathf.Min(vpos, n - 0.01f);                          // 출구 대기 클램프
            vpos = Mathf.Max(vpos, pos);                                // 역주행 방지
            prevVisual = vpos;

            if (!TryGetWorldPos(seg, vpos, boot, out var world)) continue;

            Vector3 at = world + Vector3.up * itemHeight;

            var slot = Rent();
            ApplyVisual(slot, item);
            slot.transform.position = at;

            drawn.Add(new Drawn(seg, i, at, item));
        }
    }

    /// <summary>
    /// 세그먼트 진행도(pos: 0=입구, n=출구)를 월드 좌표로.
    /// 아이템이 올라탄 벨트 타일의 **실제 포트 방향**(심 데이터, 회전·커브 오버라이드 반영)으로
    /// 진입 에지 → 중심 → 진출 에지 경로를 만든다:
    ///   - 직선 벨트: 세 점이 일직선 → 정확한 선형 이동
    ///   - 커브 벨트: 2차 베지어 → 부드러운 90° 호
    /// 이웃 타일 추정이 없으므로 싱글 벨트·세그먼트 양끝 커브에서도 정확하다.
    /// </summary>
    private bool TryGetWorldPos(BeltSegment seg, float pos, FactoryBootstrap boot, out Vector3 world)
    {
        world = default;
        int n = seg.BeltCount;

        float s = Mathf.Clamp(pos, 0f, n - 0.0001f);
        int j = Mathf.Min((int)s, n - 1);   // 입구 기준 타일 인덱스
        float t = s - j;                     // 타일 내 진행도 0..1

        var belt = seg.Belts[n - 1 - j];     // Belts는 출구=0 순서
        var view = boot.GetView(belt);
        if (view == null) return false;
        Vector3 center = view.transform.position;

        // 포트 → 타일 에지 (그리드 y = 월드 z). 에지 = 중심에서 반 셀 — 셀 크기를 따라간다.
        Vector3 entry = center, exit = center;
        foreach (var p in belt.GetEffectivePorts())
        {
            var v = Dir.ToVec(p.Direction);
            var edge = center + new Vector3(v.x, 0f, v.y) * halfCell;
            if (p.IsInput) entry = edge;
            else exit = edge;
        }

        float u = 1f - t;
        world = u * u * entry + 2f * u * t * center + t * t * exit;
        return true;
    }

    // ───────────────────── 풀 / 비주얼 ─────────────────────

    private SpriteRenderer Rent()
    {
        if (used < pool.Count)
        {
            var r = pool[used++];
            if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
            return r;
        }

        var go = new GameObject("BeltItem(Pooled)");   // 물리·콜라이더 없음 — 순수 그리기용
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * itemScale;
        var sr = go.AddComponent<SpriteRenderer>();
        pool.Add(sr);
        used++;
        return sr;
    }

    /// <summary>
    /// 아이템 → 비주얼 적용. ★ FBX(메시) 전환 시 이 메서드만 수정:
    /// ItemDataSO에 메시 필드를 추가하고, 슬롯을 MeshFilter/MeshRenderer로 바꾸면 된다.
    /// </summary>
    private static void ApplyVisual(SpriteRenderer slot, ItemDataSO item)
    {
        if (slot.sprite != item.icon) slot.sprite = item.icon;
    }
}
