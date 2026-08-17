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
    [Tooltip("전달 방식 — Projectile(총알)·Hitscan(즉시 판정, 레이저)·Aura(반경 펄스, 감속 필드).\n" +
             "어느 방식이든 효과는 소비한 탄약/연료(AmmoModuleSO)가 정의한다.")]
    public FireMode fireMode = FireMode.Projectile;

    [Tooltip("탄약 효과 중 피해형(Damage·DoT) 항목에 곱하는 배율 — 감속 같은 부가 효과는 그대로.")]
    public float damageMultiplier = 1f;

    [Tooltip("사거리(타일).")]
    public float range = 8f;

    [Tooltip("최소 사거리 — 이보다 가까운 적은 조준하지 못한다(박격포의 사각). 0 = 제한 없음.")]
    public float minRange;

    [Tooltip("발/초. Aura는 펄스 주기의 역수 (0.2 = 5초마다 펄스).")]
    public float fireRate = 1f;

    [Header("조준 — 탄도(탄속·중력·폭발)는 탄약(AmmoModuleSO)의 성질, 발사기는 각도만 정한다")]
    [Tooltip("총구 위치 오프셋(로컬 높이) — 타워 콜라이더 밖에서 발사되도록.")]
    public float muzzleHeight = 1.2f;

    [Tooltip("중력 있는 탄을 쏠 때 고각 궤적을 고른다 — 박격포는 장애물을 넘겨 쏘고, 직사 발사기는 저각으로 빨리 닿는다.")]
    public bool preferHighArc;

    [Tooltip("포탑 선회 속도(도/초). 0이면 포탑이 없거나 즉시 조준 — 그 경우 조준 대기 없이 바로 쏜다.\n" +
             "연출이 아니라 밸런스 수치다: 느린 포탑은 목표가 바뀔 때마다 실질 연사가 떨어진다.")]
    public float turnSpeed = 180f;

    [Tooltip("조준 완료로 인정하는 좌우 오차(도). 이 안에 들어와야 발사한다.\n" +
             "요(좌우)만 본다 — 피치는 탄종의 중력에 따라 달라져서 발사 전에는 확정할 수 없다.")]
    public float aimTolerance = 3f;

    [Header("보급")]
    [Tooltip("이 포탑이 받을 수 있는 탄약·연료. 비우면 아무것도 소비하지 않는다.\n" +
             "무엇을 먹을 수 있는가가 곧 포탑의 성격이다 — 기본 포탑은 다 받고, 중기관은 고밀도 이상만.")]
    public ItemDataSO[] ammoFilter;

    [Tooltip("무공급 폴백 탄 — 심 없이 씬에 직접 놓인 타워가 무한 사격할 때 가정하는 탄약.\n" +
             "벨트 보급 타워(심 배치)에서는 쓰이지 않는다 — 그쪽은 실제 소비한 탄이 정의한다.")]
    public ItemDataSO defaultAmmo;

    public override IBuildingBehavior CreateBehavior(Building building)
        => new TowerBehavior(building, this);

    /// <summary>발사하지 않는 건물인가 — 오라(감속 필드)는 쏘는 대신 펄스한다.</summary>
    public bool IsPassive => fireMode == FireMode.Aura;
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
    /// 한 발 소비하고 그 탄약의 모듈(명중 효과 + 탄도)을 돌려준다. 탄약이 없으면 false.
    /// 소비한 탄이 발사의 전부를 정의한다 — 유탄을 먹인 박격포는 포물선 폭발탄을 쏜다.
    /// 크기 배율(damageMultiplier)은 씬 쪽(BattleTower)이 발사 스펙에 싣는다.
    /// 소비 후 상류를 깨워, 버퍼가 꽉 차 멈춰 있던 벨트가 다시 흐르게 한다.
    /// IsPassive여도 소비는 막지 않는다 — 감속 필드는 펄스마다 에너지 셀을 연료로 태운다.
    /// </summary>
    public bool TryConsumeRound(out AmmoModuleSO round)
    {
        round = null;

        foreach (var (item, n) in _b.Input.Snapshot())
        {
            if (n <= 0 || !_b.Input.TryConsume(item, 1)) continue;

            // 효과·탄도는 탄약(모듈)이 갖고 포탑은 배율·각도만 갖는다
            round = item.GetModule<AmmoModuleSO>();
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
