using System;
using UnityEngine;

/// <summary>
/// 방어 타워 — 벨트로 탄약을 보급받아 밤 웨이브를 막는다.
///
/// 피해를 배율로 둔 이유: 탄약이 강해지면 그 탄을 쓰는 모든 포탑이 함께 강해져야 한다.
/// 포탑마다 고정 피해를 박아두면 새 탄약을 추가할 때마다 모든 포탑 수치를 다시 만져야 한다.
/// 1발의 명중 효과(피해 포함)는 탄약의 <see cref="AmmoModuleSO.attackEffects"/>가 갖는다.
/// </summary>
[CreateAssetMenu(fileName = "NewTower", menuName = "Factory/Buildings/Tower")]
public class TowerDataSO : BuildingDataSO
{
    [Header("전투")]
    [Tooltip("탄약 효과들의 크기(value)에 곱하는 배율. 0 = 발사하지 않음(인펜스·감속 필드).")]
    public float damageMultiplier = 1f;

    [Tooltip("사거리(타일).")]
    public float range = 8f;

    [Tooltip("발/초.")]
    public float fireRate = 1f;

    [Tooltip("발사 없이 타워 자신이 거는 효과(펄스형 오라) — 감속 필드(배율 0)가 fireRate 주기마다\n" +
             "범위 내 몬스터에게 적용한다. 발사 타워의 명중 효과는 탄약(AmmoModuleSO)이 정의한다.")]
    public EffectEntry[] auraEffects;

    [Header("보급")]
    [Tooltip("이 포탑이 받을 수 있는 탄약·연료. 비우면 아무것도 소비하지 않는다.\n" +
             "무엇을 먹을 수 있는가가 곧 포탑의 성격이다 — 기본 포탑은 다 받고, 중기관은 고밀도 이상만.")]
    public ItemDataSO[] ammoFilter;

    public override IBuildingBehavior CreateBehavior(Building building)
        => new TowerBehavior(building, this);

    /// <summary>공격하지 않는 건물인가 (인펜스·감속 필드처럼 피해가 0인 것).</summary>
    public bool IsPassive => damageMultiplier <= 0f;
}

// ─── 행동 ──────────────────────────────────────────────────────

/// <summary>
/// 타워의 <b>보급</b> 담당 — 심(플레인 C#) 쪽 절반이다.
///
/// 조준·발사는 씬 쪽 BattleTower가 한다. 심은 씬 좌표도 몬스터도 모르는 헤드리스
/// 시뮬레이션이라(테스트가 씬 없이 돌아간다) 여기서 타깃을 찾을 수 없고,
/// 사거리 탐색·총알 발사는 이미 BattleTower/SensorComponent에 있는 것을 재사용하는 편이 낫다.
///
/// 그래서 역할을 이렇게 나눈다:
///   심(여기)      — 무엇을 받을지(AcceptFilter), 한 발 소비, 막힌 상류 깨우기
///   씬(BattleTower) — 언제 누구를 쏠지, 총알 생성
///
/// 포탑이 입력 포트를 갖는다는 게 핵심이다. 벨트를 연결하면 탄약이 자동 보급되고,
/// Building.Input 버퍼가 그대로 탄창이 된다 — 공장 배관이 그대로 보급선이 된다.
/// </summary>
public class TowerBehavior : IBuildingBehavior
{
    readonly Building _b;
    readonly TowerDataSO _data;

    public TowerBehavior(Building b, TowerDataSO data)
    {
        _b = b;
        _data = data;

        // 조립기가 현재 레시피의 재료만 받는 것과 같은 구조.
        // 거절된 push는 상류에 자연스러운 배압으로 전달된다.
        _b.Input.AcceptFilter = Accepts;
    }

    public TowerDataSO Data => _data;

    bool Accepts(ItemDataSO item)
    {
        if (item == null) return false;
        var filter = _data.ammoFilter;
        if (filter == null || filter.Length == 0) return false;   // 비우면 아무것도 안 받는다
        return Array.IndexOf(filter, item) >= 0;
    }

    /// <summary>장전된 탄약이 하나라도 있는가.</summary>
    public bool HasAmmo => _b.Input.HasAny;

    /// <summary>
    /// 한 발 소비하고 그 탄약의 명중 효과 목록을 돌려준다. 탄약이 없으면 false.
    /// 크기 배율(damageMultiplier)은 씬 쪽(BattleTower)이 발사 스펙에 싣는다.
    /// 소비 후 상류를 깨워, 버퍼가 꽉 차 멈춰 있던 벨트가 다시 흐르게 한다.
    /// IsPassive여도 소비는 막지 않는다 — 감속 필드는 펄스마다 에너지 셀을 연료로 태운다.
    /// </summary>
    public bool TryConsumeRound(out EffectEntry[] effects)
    {
        effects = null;

        foreach (var (item, n) in _b.Input.Snapshot())
        {
            if (n <= 0 || !_b.Input.TryConsume(item, 1)) continue;

            // 명중 효과는 탄약(모듈)이 갖고 포탑은 배율만 갖는다
            effects = item.GetModule<AmmoModuleSO>()?.attackEffects;
            _b.NotifyUpstream();
            return true;
        }
        return false;
    }

    // 발사 판정은 씬(Update)이 주도하므로 심 틱에서 할 일이 없다.
    // 탄약이 들어오면 Input.Changed → MarkDirty로 깨어나지만, 여기서 소비하지는 않는다.
    public void Tick(float dt) { }

    public void OnAfterPlaced() { }
}
