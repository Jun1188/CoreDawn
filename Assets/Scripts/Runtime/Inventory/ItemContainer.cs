using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 슬롯 기반 아이템 컨테이너 — 건물 입력/출력 버퍼용 plain C# 클래스.
///
/// 플레이어 Inventory(팀원 작성)와 같은 데이터 모델(ItemStack, SO 참조 비교)을
/// 사용하므로 건물↔플레이어 간 아이템 이동에 변환이 필요 없다.
/// MonoBehaviour가 아니므로 심/뷰 분리·유닛 테스트에 그대로 들고 갈 수 있다.
///
/// 연산은 전량 성공 아니면 실패(all-or-nothing) — stall 판정을 단순하게 유지한다.
/// </summary>
public class ItemContainer
{
    readonly ItemStack[] _slots;
    readonly int _stackCap;   // 0 = ItemStack 기본값(64) 사용. 기계 버퍼는 작게 제한 가능.

    const int DefaultStackSize = 64; // TODO: ItemDataSO.maxStack 도입 시 그쪽을 참조 (팀 합의 대기)

    /// <summary>
    /// true면 같은 아이템은 슬롯 1개까지만 (기계 입력용).
    /// 한 재료가 모든 슬롯을 독점해 다른 재료가 못 들어오는 데드락을 방지한다.
    /// 저장소처럼 같은 아이템이 여러 슬롯을 차지해도 되는 곳은 false(기본).
    /// </summary>
    public bool SingleStackPerType = false;

    /// <summary>
    /// 수용 필터. null = 전부 허용. 어셈블러가 "현재 레시피의 재료만"으로 설정한다.
    /// 거절된 push는 상류에 자연스러운 배압으로 전달된다.
    /// </summary>
    public Func<ItemDataSO, bool> AcceptFilter;

    public ItemContainer(int slotCount, int stackCap = 0)
    {
        _slots    = new ItemStack[Mathf.Max(1, slotCount)];
        _stackCap = stackCap;
    }

    public int SlotCount => _slots.Length;

    int CapOf(ItemStack s) => _stackCap > 0 ? Mathf.Min(_stackCap, s.maxStackSize) : s.maxStackSize;
    int CapOfNew()         => _stackCap > 0 ? _stackCap : DefaultStackSize;

    public bool HasAny
    {
        get
        {
            foreach (var s in _slots)
                if (s != null && s.item != null && s.amount > 0) return true;
            return false;
        }
    }

    public int CountOf(ItemDataSO item)
    {
        int total = 0;
        foreach (var s in _slots)
            if (s != null && s.item == item) total += s.amount;
        return total;
    }

    /// <summary>이 아이템을 몇 개까지 더 받을 수 있는가. 필터·슬롯 규칙 반영.</summary>
    public int RoomFor(ItemDataSO item)
    {
        if (item == null || (AcceptFilter != null && !AcceptFilter(item))) return 0;

        int stackRoom = 0, emptyRoom = 0;
        bool hasStack = false;
        foreach (var s in _slots)
        {
            if (s == null || s.item == null)
                emptyRoom += CapOfNew();
            else if (s.item == item)
            {
                hasStack   = true;
                stackRoom += Mathf.Max(0, CapOf(s) - s.amount);
            }
        }

        if (!SingleStackPerType) return stackRoom + emptyRoom;

        // 종류당 1스택: 기존 스택의 여유분만, 스택이 없으면 빈 슬롯 1개분만
        return hasStack ? stackRoom : (emptyRoom > 0 ? CapOfNew() : 0);
    }

    public bool HasRoomFor(ItemDataSO item, int n = 1) => RoomFor(item) >= n;

    // ───────────────────────────────────────────────────────────
    //  변경 추적 — UI가 "다시 그릴 필요가 있나"를 싸게 판단하는 용도.
    //  내용이 바뀌는 모든 연산에서 증가한다. (인플레이스 amount 수정은
    //  Touch()를 함께 호출할 것 — Inventory 어댑터가 담당)
    // ───────────────────────────────────────────────────────────

    public int Version { get; private set; }

    /// <summary>컨테이너 내용이 바뀔 때마다 발화 — 벨트 Tick이든 UI 드래그드롭이든 모든 변경 경로를 통일해 통지한다.</summary>
    public event Action Changed;

    public void Touch() { Version++; Changed?.Invoke(); }

    // ───────────────────────────────────────────────────────────
    //  위치(슬롯 인덱스) 연산 — 인벤토리 UI(드래그·분할·교환)용.
    //  공장 물류(Tick·벨트)는 위의 위치 무관 API만 사용한다.
    //  규칙(AcceptFilter/SingleStackPerType/스택 캡)은 여기서도 동일하게 지켜진다.
    // ───────────────────────────────────────────────────────────

    /// <summary>슬롯의 스택을 그대로 반환 (없으면 null). 반환된 스택의 amount를 직접 수정했다면 Touch() 필수.</summary>
    public ItemStack PeekAt(int i) => _slots[i];

    /// <summary>슬롯의 스택을 통째로 꺼낸다 (드래그 픽업). 없으면 null.</summary>
    public ItemStack TakeAt(int i)
    {
        var s = _slots[i];
        if (s == null) return null;
        _slots[i] = null;
        Touch();
        return s;
    }

    /// <summary>빈 슬롯에 스택을 놓는다. 필터·종류당 1스택 규칙 위반이거나 슬롯이 차 있으면 false.</summary>
    public bool TryPutAt(int i, ItemStack stack)
    {
        if (stack == null || stack.item == null || stack.amount <= 0) return false;
        if (_slots[i] != null && _slots[i].item != null) return false;
        if (!AllowsPlacement(stack.item, exceptSlot: i)) return false;

        _slots[i] = stack;
        Touch();
        return true;
    }

    /// <summary>슬롯의 스택과 교환한다 (드래그 스왑). 들어갈 스택이 규칙 위반이면 false.</summary>
    public bool TryExchangeAt(int i, ItemStack incoming, out ItemStack previous)
    {
        previous = null;
        if (incoming == null || incoming.item == null) return false;
        if (!AllowsPlacement(incoming.item, exceptSlot: i)) return false;

        previous = _slots[i];
        _slots[i] = incoming;
        Touch();
        return true;
    }

    /// <summary>이 아이템의 새 스택을 슬롯에 둘 수 있는가 (필터 + 종류당 1스택 검사).</summary>
    bool AllowsPlacement(ItemDataSO item, int exceptSlot)
    {
        if (AcceptFilter != null && !AcceptFilter(item)) return false;
        if (!SingleStackPerType) return true;

        for (int j = 0; j < _slots.Length; j++)
            if (j != exceptSlot && _slots[j] != null && _slots[j].item == item)
                return false;
        return true;
    }

    /// <summary>n개 전량 수용 가능할 때만 추가. 기존 스택부터 채우고 빈 슬롯에 새 스택.</summary>
    public bool TryAdd(ItemDataSO item, int n = 1)
    {
        if (item == null || n <= 0 || !HasRoomFor(item, n)) return false;

        foreach (var s in _slots)
        {
            if (n == 0) break;
            if (s == null || s.item != item) continue;
            int add = Mathf.Min(CapOf(s) - s.amount, n);
            s.amount += add;
            n -= add;
        }
        for (int i = 0; i < _slots.Length && n > 0; i++)
        {
            if (_slots[i] != null && _slots[i].item != null) continue;
            int add = Mathf.Min(CapOfNew(), n);
            var stack = new ItemStack(item, add);
            stack.maxStackSize = Mathf.Max(stack.maxStackSize, add); // 상한 오버라이드 시 정합 유지
            _slots[i] = stack;
            n -= add;
            if (SingleStackPerType) break; // 새 스택은 1개까지 (RoomFor 선검사로 n==0 보장)
        }

        // Touch는 반드시 다 넣은 뒤에 — Changed 구독자(핫바 HUD 등)가 변경 전 상태를
        // 다시 그리고 끝나면, 다음 변경까지 화면이 한 박자 낡은 채로 남는다
        Touch();
        return true; // HasRoomFor 선검사로 보장됨
    }

    /// <summary>n개 전량 있을 때만 소비.</summary>
    public bool TryConsume(ItemDataSO item, int n = 1)
    {
        if (item == null || n <= 0 || CountOf(item) < n) return false;

        for (int i = _slots.Length - 1; i >= 0 && n > 0; i--) // 뒤쪽 스택부터 소진
        {
            var s = _slots[i];
            if (s == null || s.item != item) continue;
            int take = Mathf.Min(s.amount, n);
            s.amount -= take;
            n -= take;
            if (s.amount == 0) _slots[i] = null;
        }

        Touch();   // TryAdd와 같은 이유 — 변경이 끝난 뒤에 알린다
        return true;
    }

    /// <summary>아이템 종류별 (item, 총 개수) 목록 — 순회 중 컨테이너 수정에 안전한 사본.</summary>
    public List<(ItemDataSO item, int n)> Snapshot()
    {
        var seen = new Dictionary<ItemDataSO, int>();
        foreach (var s in _slots)
            if (s != null && s.item != null && s.amount > 0)
                seen[s.item] = seen.TryGetValue(s.item, out var c) ? c + s.amount : s.amount;

        var list = new List<(ItemDataSO, int)>(seen.Count);
        foreach (var kv in seen) list.Add((kv.Key, kv.Value));
        return list;
    }
}
