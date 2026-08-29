using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Entities;
using CoreDawn.Factory;
using CoreDawn.Data;

namespace CoreDawn.Placement
{
    /// <summary>
    /// 포트 흐름을 **언제** 보여줄지 정하는 드라이버. 그리기 자체는 <see cref="PortFlowVisualizer"/>가 한다.
    ///
    /// 항상 켜두면 공장이 커졌을 때 화면이 흐름으로 뒤덮인다. 그래서:
    ///   배치 모드(건설·철거) → 시야 안 모든 건물의 **열린 포트** + 프리뷰 건물의 전체 포트
    ///   그 외                → 끔 — 포트 표시는 건설 모드 전용이다
    ///
    /// **열린 포트** = 이웃 칸에 맞물리는 포트가 없는 포트. 지금 무언가를 붙일 수 있는 자리다.
    /// 이미 벨트가 물린 포트는 볼 이유가 없으니 숨긴다 — 공장이 커질수록 표시가 저절로 줄어들고,
    /// 남은 흐름이 전부 "여기에 이을 수 있다"는 뜻이 되어 배치 중 눈이 갈 곳이 명확해진다.
    ///
    /// 갱신은 그리드가 바뀔 때만 한다 (매 프레임 재계산 금지). 프리뷰만 매 프레임 움직이는데,
    /// 이것도 재생성하지 않고 루트 위치만 옮긴다.
    /// </summary>
    public class PortFlowOverlay : MonoBehaviour
    {
        [Header("색")]
        [SerializeField] Color inputColor  = new(0.31f, 0.847f, 0.878f);   // 파랑 = 들어온다
        [SerializeField] Color outputColor = new(1f, 0.62f, 0.29f);        // 귤색 = 나간다

        [Header("모양")]
        [Tooltip("포트 표시를 건물 면에서 칸 안쪽으로 물리는 거리(타일).\n" +
                 "0이면 면에 딱 붙어, 건물이 붙어 있을 때 옆 건물의 표시와 같은 선 위에 포개진다. " +
                 "값이 커질수록 자기 칸 안으로 들어오고 이웃 칸으로 넘어가는 흐름이 짧아진다.")]
        [SerializeField, Range(0f, 0.4f)] float portInsetTiles = 0.15f;

        [Header("범위")]
        [Tooltip("이 반경(타일) 안의 건물만 열린 포트를 그린다.")]
        [SerializeField] float radiusTiles = 30f;
        [Tooltip("기준점이 이만큼(타일) 움직이면 범위를 다시 계산한다.")]
        [SerializeField] float refreshMoveTiles = 6f;

        [Header("셰이더")]
        [Tooltip("비워두면 Shader.Find(\"LevelUp/PortFlow\")로 런타임 생성. " +
                 "빌드에 셰이더를 포함시키려면 이 필드에 머티리얼을 연결하거나 " +
                 "Project Settings > Graphics > Always Included Shaders 에 넣을 것.")]
        [SerializeField] Material flowMaterial;

        float _cellSize = 1f;
        Vector3 _gridOrigin;

        // 건물별 열린 포트 표시 — 그리드가 바뀔 때만 다시 만든다
        readonly Dictionary<BuildingModule, PortFlowVisualizer> _open = new();
        Vector3 _lastCenter;
        bool _hasCenter;

        PortFlowVisualizer _preview;   // 배치 프리뷰 (전체 포트)

        bool _placing;

        public void Configure(float cellSize, Vector3 gridOrigin)
        {
            _cellSize   = cellSize;
            _gridOrigin = gridOrigin;
        }

        Material Source
        {
            get
            {
                if (flowMaterial != null) return flowMaterial;

                var shader = Shader.Find("LevelUp/PortFlow");
                if (shader == null)
                {
                    Debug.LogWarning("[PortFlow] 셰이더 'LevelUp/PortFlow'를 찾지 못했습니다. " +
                                     "빌드에서는 Always Included Shaders에 넣거나 머티리얼을 연결하세요.", this);
                    return null;
                }
                flowMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                return flowMaterial;
            }
        }

        // ───────────────────────── 배치 모드 ─────────────────────────

        public void EnterPlacement()
        {
            _placing = true;
            _hasCenter = false;          // 첫 프레임에 무조건 한 번 계산
        }

        /// <summary>
        /// 프리뷰 위치 갱신 — 매 프레임 호출해도 된다.
        /// 포트 모양이 그대로면 루트만 옮기고, 회전/건물이 바뀌었을 때만 다시 만든다.
        /// </summary>
        /// <param name="ports">프리뷰 건물의 회전 반영 포트.</param>
        /// <param name="origin">풋프린트 원점 칸.</param>
        /// <param name="groundY">지면 높이.</param>
        /// <param name="shapeChanged">회전·건물·벨트 모양이 바뀌었는가.</param>
        public void UpdatePreview(IReadOnlyList<PortDefinition> ports, Vector2Int origin, float groundY,
                                  bool shapeChanged)
        {
            if (!_placing) return;

            Vector3 corner = CornerOf(origin, groundY);

            if (_preview == null)
            {
                _preview = PortFlowVisualizer.Create(transform, Source, _cellSize, inputColor, outputColor, portInsetTiles);
                shapeChanged = true;
            }
            _preview.SetVisible(true);

            if (shapeChanged) _preview.Build(ports, corner);
            else              _preview.MoveTo(corner);

            // 기준점이 충분히 움직였을 때만 주변 열린 포트를 다시 계산한다
            if (!_hasCenter || (corner - _lastCenter).sqrMagnitude > Sq(refreshMoveTiles * _cellSize))
                RefreshOpenPorts(corner);
        }

        /// <summary>지형을 조준하지 못해 프리뷰가 숨겨질 때.</summary>
        public void HidePreview() => _preview?.SetVisible(false);

        /// <summary>배치·철거로 그리드가 바뀌었다 — 열린 포트 판정이 달라진다.</summary>
        public void NotifyGridChanged()
        {
            if (_placing && _hasCenter) RefreshOpenPorts(_lastCenter);
        }

        public void Exit()
        {
            _placing = false;
            if (_preview != null) { Destroy(_preview.gameObject); _preview = null; }
            ClearOpen();
        }

        // ───────────────────────── 열린 포트 ─────────────────────────

        void RefreshOpenPorts(Vector3 center)
        {
            _lastCenter = center;
            _hasCenter  = true;

            var sim = FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.Factory : null;
            if (sim == null) { ClearOpen(); return; }

            float sqRadius = Sq(radiusTiles * _cellSize);
            var live = new HashSet<BuildingModule>();

            var boot = FactoryBootstrap.Instance;
            foreach (var b in sim.Buildings)
            {
                // 포트 표시는 뷰 위치에 눕는다 — 뷰 없는 건물(둥지)은 그릴 것이 없다
                var entity = boot.GetView(b);
                if (entity == null) continue;
                if ((entity.transform.position - center).sqrMagnitude > sqRadius) continue;

                var openPorts = CollectOpenPorts(sim, b);
                if (openPorts.Count == 0) continue;

                live.Add(b);

                if (!_open.TryGetValue(b, out var vis) || vis == null)
                {
                    vis = PortFlowVisualizer.Create(transform, Source, _cellSize, inputColor, outputColor, portInsetTiles);
                    _open[b] = vis;
                }
                vis.SetVisible(true);
                vis.Build(openPorts, CornerOf(b.Origin, GroundYOf(b, entity)));
            }

            // 범위 밖으로 나갔거나 포트가 전부 막힌 건물 정리
            var stale = new List<BuildingModule>();
            foreach (var kv in _open)
                if (!live.Contains(kv.Key)) stale.Add(kv.Key);

            foreach (var b in stale)
            {
                if (_open[b] != null) Destroy(_open[b].gameObject);
                _open.Remove(b);
            }
        }

        /// <summary>
        /// 이웃 칸이 비어 있거나, 있어도 맞물리는 포트가 없으면 열린 포트.
        /// "맞물린다"의 정의(방향 반대 + 입출력 반대)는 BuildingGraph의 연결 규칙과 같다.
        /// </summary>
        static List<PortDefinition> CollectOpenPorts(FactorySystem sim, BuildingModule b)
        {
            var result = new List<PortDefinition>();
            var ports  = b.GetEffectivePorts();
            if (ports == null) return result;

            foreach (var p in ports)
            {
                if (p == null) continue;

                var cell     = b.Origin + p.LocalOffset;
                var neighbor = cell + Dir.ToVec(p.Direction);
                var other    = sim.Grid.GetAt(neighbor);

                // 이웃 칸이 자기 자신 = 안쪽을 향한 포트. 영원히 못 잇는 자리라 표시해도 오해만 준다
                // (BuildingDataSO.OnValidate가 에디터에서 이미 에러로 잡는다)
                if (other == b) continue;

                if (other != null &&
                    other.HasPortAt(neighbor, Dir.Opposite(p.Direction), isInput: !p.IsInput)) continue;

                result.Add(p);
            }
            return result;
        }

        void ClearOpen()
        {
            foreach (var kv in _open)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _open.Clear();
            _hasCenter = false;
        }

        // ───────────────────────── 헬퍼 ─────────────────────────

        /// <summary>풋프린트 원점 칸의 왼쪽 아래 모서리 (시각화 루트가 놓이는 자리).</summary>
        Vector3 CornerOf(Vector2Int cell, float groundY)
        {
            var c = new Vector3(cell.x, 0f, cell.y) * _cellSize + _gridOrigin;
            c.y = groundY;
            return c;
        }

        /// <summary>
        /// 건물이 서 있는 지면 높이. 뷰의 y는 피벗 위치라 그대로 쓰면 안 된다 —
        /// 배치할 때 더해준 만큼(광맥 위 채굴기)을 도로 빼면 지면이 나온다.
        /// 포트 흐름은 지면에 눕는 표시이므로 이 값을 써야 공중에 뜨지 않는다.
        /// </summary>
        float GroundYOf(BuildingModule b, BuildingView view)
        {
            if (view == null) view = FactoryBootstrap.Instance != null
                ? FactoryBootstrap.Instance.GetView(b) : null;
            if (view == null) return _gridOrigin.y;

            return view.transform.position.y - PlacementSystem.SurfaceLift(b.Data, b.Origin);
        }

        static float Sq(float v) => v * v;

        void OnDestroy()
        {
            // 머티리얼은 PortFlowVisualizer가 정적으로 캐시하므로 여기서 지우지 않는다
            _open.Clear();
        }
    }
}
