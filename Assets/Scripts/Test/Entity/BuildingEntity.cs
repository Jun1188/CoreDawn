using System;
using System.Collections.Generic;
using UnityEngine;

// 건물 엔티티 — 심 건물의 "씬 위 껍데기(View)". 데이터 원본은 팩토리 심의 Building이다.
//
// 역할 분담 (합치지 않고 나눈다 — MonoBehaviour 제약 때문에 상속으로는 못 합침):
//   Building (순수 C#, Runtime/Factory) = 데이터 원본. Data/Origin/회전/버퍼/연결/행동/IsRemoved.
//   BuildingEntity (여기)               = 씬 표현 + 전투. HP·피격·사망, 코어 여부, 상호작용 창구,
//                                         그리고 심 데이터는 아래 위임 프로퍼티로만 노출한다.
// 그래서 소비자는 view.Sim.Data처럼 심 내부로 두 단계 들어가지 말고 view.Data를 쓰면 된다.
//
// 생명주기는 양방향으로 맞춘다 (한쪽만 사라진 유령 방지):
//   심 제거 → FactorySim.Removed → FactoryBootstrap이 이 GameObject를 파괴
//   뷰 파괴 → OnDestroy에서 심도 제거 (씬 언로드/종료 중에는 건너뜀)
//
// 이름 주의: 심의 plain C# Building과 헷갈리지 않게 Entity 접미사를 붙였다 (2026-07-31 이전 이름: Entities.Building).
// 코어처럼 심 없이 씬에 직접 배치하는 건물은 Sim이 null이어도 된다.
public class BuildingEntity : Entity, IInteractable
{
    [Header("Building Settings")]
    [Tooltip("맵 중앙의 코어인지 여부. 플로우필드에서 타워보다 우선하는 최종 목표가 된다.")]
    [SerializeField] private bool isCore;

    // 이 엔티티가 대변하는 팩토리 심 건물(plain C#). PlacementBridge가 배치 시 연결한다.
    public Building Sim { get; set; }

    public bool IsCore => isCore;
    public override bool IsDead => base.IsDead || (Sim != null && Sim.IsRemoved);

    // ── 심 데이터 위임 — 소비자가 Sim 내부로 두 단계 들어가지 않게 하는 창구.
    //    심이 없는 씬 직접 배치 건물(코어 등)에서도 안전하게 null/기본값을 돌려준다.

    /// <summary>이 건물의 설계도(SO). 심이 없으면 null.</summary>
    public BuildingDataSO Data => Sim?.Data;

    /// <summary>점유 풋프린트의 왼쪽 아래 셀. 심이 없으면 기본값.</summary>
    public Vector2Int Origin => Sim != null ? Sim.Origin : default;

    /// <summary>회전이 반영된 점유 크기(타일). 심이 없으면 1x1.</summary>
    public Vector2Int Size =>
        Sim != null ? Sim.Data.GetRotatedSize(Sim.RotationSteps) : Vector2Int.one;

    /// <summary>살아 있는 심에 연결돼 있는가 (철거·파괴된 심은 false).</summary>
    public bool HasSim => Sim != null && !Sim.IsRemoved;

    // ── 플레이어 상호작용(E) — 행동이 IInteractiveBehavior를 구현한 건물만 반응 (opt-in)
    public string Prompt => Sim?.Behavior is IInteractiveBehavior i ? i.InteractPrompt : null;

    public void Interact(PlayerController player)
    {
        if (Sim?.Behavior is IInteractiveBehavior i) i.Interact(player);
    }

    // 살아있는 건물 레지스트리 — 플로우필드 목표 수집과 몬스터의 사거리 검색용
    private static readonly List<BuildingEntity> all = new List<BuildingEntity>();
    public static IReadOnlyList<BuildingEntity> All => all;

    // 코어 파괴 = 게임오버 조건. BattleManager가 구독한다.
    public static event Action<BuildingEntity> CoreDestroyed;

    private void OnEnable()
    {
        all.Add(this);
        // 건물 배치/파괴는 몬스터 경로에 영향을 주므로 플로우필드 갱신 예약
        if (FlowFieldManager.Instance != null) FlowFieldManager.Instance.MarkDirty();
    }

    private void OnDisable()
    {
        all.Remove(this);
        if (FlowFieldManager.Instance != null) FlowFieldManager.Instance.MarkDirty();
    }

    // 게임 종료/씬 언로드 중에는 정리에 손대지 않는다 — 그 시점의 Sim.Remove는
    // 벨트 아이템 드롭 같은 새 오브젝트 생성을 유발해 에러가 난다.
    private static bool quitting;
    private void OnApplicationQuit() => quitting = true;

    // 뷰가 다른 경로로 파괴돼도(부모 파괴·직접 Destroy) 심이 그리드를 계속 점유하지 않게.
    // 정상 경로(철거·전투 파괴)는 이미 심이 먼저 지워져 있어 여기서는 아무 일도 하지 않는다.
    private void OnDestroy()
    {
        if (quitting || !gameObject.scene.isLoaded) return;
        if (Sim == null || Sim.IsRemoved) return;

        var boot = FactoryBootstrap.Instance;
        if (boot != null && boot.Sim != null) boot.Sim.Remove(Sim);
    }

    // HP 0 → 몬스터의 사망 연출 지연 없이 즉시 소멸
    protected override void HandleDeath()
    {
        if (isCore) CoreDestroyed?.Invoke(this); // 소멸 전에 게임오버 통지

        if (Sim != null && !Sim.IsRemoved)
        {
            PlacementBridge.Remove(Sim); // 심 제거 + GridIndex 해제 + 뷰(GO) 파괴 일괄 처리
        }
        else
        {
            // 심 연결이 없는 건물(코어 등 씬 직접 배치)은 뷰만 정리
            Destroy(gameObject);
        }
    }

    // 심 건물(POCO) → 건물 엔티티 (구 BuildingDamageable.GetOrAttach).
    // PlacementBridge가 배치 시 모든 뷰 GO에 이 컴포넌트를 붙이고 매핑을 등록한다.
    public static BuildingEntity GetOrAttach(Building sim)
    {
        if (sim == null || sim.IsRemoved) return null;

        var boot = FactoryBootstrap.Instance;
        if (boot == null) return null;

        var entity = boot.GetView(sim);
        if (entity == null) return null; // 뷰 없는 심 전용 건물(테스트 등)은 공격 대상이 될 수 없음

        if (entity.Sim == null) entity.Sim = sim;
        return entity;
    }

    // 사거리 내 가장 가까운 살아있는 건물 — 몬스터(FlowFieldState)의 도착/공격 판정용.
    // 멀티타일 건물을 고려해 콜라이더 표면 거리(DistanceTo)를 사용한다.
    public static BuildingEntity FindClosestInRange(Vector3 from, float range)
    {
        BuildingEntity closest = null;
        float minDistance = float.MaxValue;
        foreach (var building in all)
        {
            if (!building.IsValidTarget()) continue;
            float dist = building.DistanceTo(from);
            if (dist <= range && dist < minDistance)
            {
                minDistance = dist;
                closest = building;
            }
        }
        return closest;
    }
}
