using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FactorySim의 Unity 드라이버 — 심과 씬의 유일한 접점.
/// 씬에 이 컴포넌트 하나만 있으면 공장 시뮬레이션이 돌아간다.
///
/// 역할:
///   1. 심 생성·매 프레임 Advance() 호출
///   2. 심 Building ↔ BuildingEntity(GameObject) 매핑 관리
/// 시뮬레이션 로직은 전부 FactorySim(plain C#)에 있다.
/// </summary>
[RequireComponent(typeof(BeltItemView))]
public class FactoryBootstrap : MonoBehaviour
{
    public static FactoryBootstrap Instance { get; private set; }

    [Tooltip("초당 틱 수. 10이면 0.1초마다 처리.")]
    [SerializeField] float _tps = 10f;

    [Tooltip("프레임 드랍 후 한 프레임에 몰아서 따라잡을 수 있는 최대 틱 수.")]
    [SerializeField] int _maxCatchUpTicks = 5;

    public FactorySim Sim { get; private set; }

    readonly Dictionary<Building, BuildingEntity> _views = new();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        Sim = new FactorySim(_tps, _maxCatchUpTicks);
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        // 벨트 철거로 세그먼트에서 밀려난 아이템 → 월드 드롭 (통지 시점엔 벨트 뷰가 아직 살아있음)
        Sim.Belts.ItemDiscarded += (belt, item) =>
        {
            var view = GetView(belt);
            if (view != null) PlacementBridge.DropAt(item, 1, view.transform.position);
        };

        // 심에서 건물이 사라지면 그 씬 표현도 함께 정리 — 매핑 소유자가 한 곳에서 책임진다.
        // (전투 파괴·철거·테스트의 Sim.Remove 직접 호출까지 전부 이 경로로 모인다)
        Sim.Removed += b =>
        {
            var view = GetView(b);
            _views.Remove(b);
            if (view != null) Destroy(view.gameObject);
        };

        // 벨트 위 아이템 시각화 뷰 — 씬 배선 없이 드라이버가 직접 부착
        if (GetComponent<BeltItemView>() == null) Debug.LogWarning("No Belt Item Renderer");
    }

    void Update() => Sim.Advance(Time.deltaTime);

    // ── Building ↔ View 매핑 (PlacementBridge가 등록/해제)

    public void RegisterView(Building b, BuildingEntity v) => _views[b] = v;
    public void UnregisterView(Building b) => _views.Remove(b);
    public BuildingEntity GetView(Building b) =>
        b != null && _views.TryGetValue(b, out var v) ? v : null;
}
