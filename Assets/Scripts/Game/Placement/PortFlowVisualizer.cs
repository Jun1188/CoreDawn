using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Data;

namespace CoreDawn.Placement
{
    /// <summary>
    /// 포트 하나당 조각 3개(짝대기·바닥판·지느러미)를 만들어 "어느 방향으로 물건이 오가는지"를 보여준다.
    /// 파랑 = 입력, 귤색 = 출력.
    ///
    /// 짝대기가 포트 선이고, 바닥판과 지느러미는 거기서 바깥으로 흘러나간다:
    ///   짝대기   — 면에 붙어 **좌우로** 눕는 밝은 막대. 포트의 위치와 폭
    ///   바닥판   — 바닥에 번지는 그라디언트. 흐름 방향
    ///   지느러미 — 흐름 축을 따라 선 수직 판. 위에서 내려다볼 때 납작해 보이지 않게
    ///
    /// 좌표는 **그리드 공간**으로 받는다 — 회전은 <see cref="Building.GetEffectivePorts"/>가
    /// 이미 적용해 두므로 여기서 다시 계산하지 않는다. 그래서 이 오브젝트의 transform은
    /// 항상 회전 없이 풋프린트 원점 칸의 모서리에 놓인다 (건물 뷰에 붙이면 회전이 두 번 먹는다).
    ///
    /// 언제 보여줄지는 <see cref="PortFlowOverlay"/>가 정한다 — 이 클래스는 그리기만 한다.
    /// </summary>
    public class PortFlowVisualizer : MonoBehaviour
    {
        // 타일 1칸 = 1.0 기준

        // 짝대기 — 면에 붙어 좌우로 눕는 밝은 막대. 포트의 정확한 위치와 폭을 긋는다.
        // 두께(바깥 방향)는 얇고, 길이(좌우)는 타일 폭에 가깝다.
        const float BAR_T = 0.10f;   // 바깥 방향 두께
        const float BAR_W = 0.90f;   // 좌우 길이

        // 바닥판·지느러미 — 짝대기에서 바깥으로 흘러나가는 흐름.
        // 길이는 인셋과 맞물린다: 피벗이 면 안쪽 _inset 만큼에 놓이므로 이웃 칸으로 넘어가는 거리는
        // (LEN + BAR_T − _inset) 이다. 0.58 이던 시절엔 0.68타일이 이웃 칸을 덮어, 건물이 붙어 있으면
        // 옆 건물의 포트 표시와 겹쳐 읽을 수 없었다.
        const float LEN = 0.42f;   // 바깥으로 뻗는 길이 (둘 공통)
        const float WID = 0.90f;   // 바닥판 좌우 폭
        const float HGT = 0.36f;   // 지느러미 높이

        const float FLOOR_Y = 0.012f;   // z-파이팅을 피할 만큼만 띄운다
        const float BAR_Y   = 0.02f;    // 바닥판 위에 얹어 막대가 묻히지 않게

        // 감쇠 지수 (셰이더 _Fade). 흐름은 바깥으로 빠르게 사라지고,
        // 짝대기는 거의 평평해야 단단한 슬랩으로 보인다 (0에 가까울수록 균일).
        const float FADE_SOFT = 1.7f;
        const float FADE_BAR  = 0.12f;

        // 세기 — 짝대기는 알파가 1에 닿아 계통색 그대로 꽉 찬 막대가 되고,
        // 번짐은 둘이 겹쳐도 합이 1을 넘지 않게 낮게 잡는다. 넘으면 채널이 물려 흰색으로 날아간다.
        const float POWER_SOFT = 0.55f;
        const float POWER_BAR  = 1.9f;

        // Quad의 로컬 +X가 "면에서 바깥"이 되도록 눕힌다 — 셰이더가 uv.x를 흐름 축으로 읽는다.
        // 부모(포트 피벗)는 +Z가 바깥, +Y가 위, +X가 좌우인 공간이다.
        static readonly Quaternion FlatRot = Quaternion.LookRotation(Vector3.up,   Vector3.right); // 짝대기·바닥판
        static readonly Quaternion FinRot  = Quaternion.LookRotation(Vector3.left, Vector3.up);    // 지느러미

        // 머티리얼 조합은 (입력/출력) × (짝대기/바닥판/지느러미) 6가지뿐이라 공유 인스턴스를 돌려쓴다.
        // MaterialPropertyBlock을 쓰지 않는 이유: URP SRP Batcher가 MPB를 지원하지 않아 배칭이 깨진다.
        // 벨트 100개를 깔면 그 차이가 그대로 드로우콜로 나온다.
        static readonly Dictionary<(Color, float, float, float, bool), Material> _matCache = new();

        // 내장 Quad 메시를 직접 쓴다. GameObject.CreatePrimitive는 콜라이더를 강제로 붙이는데,
        // Destroy는 프레임 끝에야 실행돼 그 프레임의 레이캐스트를 가로막고 에디트 모드에선 아예 안 지워진다.
        // 애초에 안 만드는 쪽이 확실하다 — 이 표시는 조준·클릭·사격에 절대 걸리면 안 된다.
        static Mesh _quadMesh;
        static Mesh QuadMesh
        {
            get
            {
                if (_quadMesh == null) _quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                return _quadMesh;
            }
        }

        Material _source;
        float _cellSize = 1f;
        float _inset;   // 면에서 칸 안쪽으로 물러나는 거리(타일) — 짝대기가 자기 칸 안에 앉는다
        Color _inputColor  = new(0.31f, 0.847f, 0.878f);
        Color _outputColor = new(1f, 0.62f, 0.29f);

        readonly List<GameObject> _pieces = new();

        /// <summary>지금 그려져 있는 포트 개수 — 오버레이가 빈 시각화를 걷어낼 때 쓴다.</summary>
        public int PortCount { get; private set; }

        /// <param name="insetTiles">포트 표시를 면에서 칸 안쪽으로 물리는 거리(타일).
        /// 0이면 짝대기가 면에 딱 붙는다 — 붙어 있는 두 건물의 짝대기가 같은 선 위에 포개진다.</param>
        public static PortFlowVisualizer Create(Transform parent, Material source, float cellSize,
                                                Color inputColor, Color outputColor, float insetTiles = 0f)
        {
            var go = new GameObject("PortFlow");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PortFlowVisualizer>();
            v._source      = source;
            v._cellSize    = cellSize;
            v._inset       = Mathf.Max(0f, insetTiles);
            v._inputColor  = inputColor;
            v._outputColor = outputColor;
            return v;
        }

        /// <summary>
        /// 포트 목록으로 조각을 다시 만든다.
        /// </summary>
        /// <param name="ports">회전이 이미 반영된 포트들 (Building.GetEffectivePorts 또는 SO.GetRotatedPorts).</param>
        /// <param name="originCorner">풋프린트 원점 칸의 왼쪽 아래 모서리 월드 좌표 (지면 높이).</param>
        public void Build(IEnumerable<PortDefinition> ports, Vector3 originCorner)
        {
            Clear();
            transform.SetPositionAndRotation(originCorner, Quaternion.identity);
            if (ports == null || _source == null) return;

            foreach (var p in ports)
            {
                if (p == null) continue;

                var color = p.IsInput ? _inputColor : _outputColor;
                var d     = Dir.ToVec(p.Direction);
                var out3  = new Vector3(d.x, 0f, d.y);   // 그리드 (x,y) → 월드 (x,z)

                // 칸 중앙 → 그 칸의 바깥쪽 면 → 거기서 _inset 만큼 안쪽.
                // 면에 딱 붙이면 붙어 있는 이웃과 짝대기가 같은 선 위에 포개지고, 흐름은 통째로
                // 이웃 칸 위에 그려진다. 자기 칸 안으로 물러나야 "누구 포트인지"가 읽힌다.
                var cellCenter = new Vector3(p.LocalOffset.x + 0.5f, 0f, p.LocalOffset.y + 0.5f) * _cellSize;
                var facePoint  = cellCenter + out3 * ((0.5f - _inset) * _cellSize);

                var pivot = new GameObject($"Port_{p.LocalOffset.x}_{p.LocalOffset.y}_{p.Direction}").transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = facePoint;
                pivot.localRotation = Quaternion.LookRotation(out3, Vector3.up);
                _pieces.Add(pivot.gameObject);

                // 흐름은 짝대기 **바깥쪽에서** 시작한다 — 겹치면 알파가 더해져 그 자리만 흰색으로 날아간다
                AddQuad(pivot, "Floor", LEN, WID, color, FlatRot,
                        new Vector3(0f, FLOOR_Y, (BAR_T + LEN) * 0.5f), POWER_SOFT, FADE_SOFT,
                        vertical: false, additive: true);

                AddQuad(pivot, "Fin", LEN, HGT, color, FinRot,
                        new Vector3(0f, HGT * 0.5f, (BAR_T + LEN) * 0.5f), POWER_SOFT, FADE_SOFT,
                        vertical: true, additive: true);

                // 면에 딱 붙여 바깥으로 두께만큼만 — z는 0(면)에서 시작한다.
                // 알파 합성이라 번짐 위에 얹혀도 색이 섞이지 않고 단단한 막대로 남는다.
                AddQuad(pivot, "Bar", BAR_T, BAR_W, color, FlatRot,
                        new Vector3(0f, BAR_Y, BAR_T * 0.5f), POWER_BAR, FADE_BAR,
                        vertical: false, additive: false);

                PortCount++;
            }
        }

        /// <summary>모양은 그대로 두고 위치만 옮긴다 — 프리뷰가 매 프레임 움직일 때 재생성하지 않기 위함.</summary>
        public void MoveTo(Vector3 originCorner) => transform.position = originCorner;

        public void SetVisible(bool on)
        {
            if (this != null && gameObject.activeSelf != on) gameObject.SetActive(on);
        }

        public void Clear()
        {
            foreach (var go in _pieces) Kill(go);
            _pieces.Clear();
            PortCount = 0;
        }

        /// <summary>
        /// 에디트 모드에서 Destroy는 에러만 내고 아무것도 안 지운다.
        /// 이 표시는 런타임 전용이지만 에디터 프리뷰·검증 스크립트에서도 만들어 보게 되므로 갈라 둔다.
        /// </summary>
        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void AddQuad(Transform parent, string name, float along, float across, Color color,
                     Quaternion localRot, Vector3 localPos, float intensity, float fade,
                     bool vertical, bool additive)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = QuadMesh;   // 콜라이더 없는 순수 렌더 오브젝트

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale    = new Vector3(along * _cellSize, across * _cellSize, 1f);

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.lightProbeUsage   = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.sharedMaterial    = GetMaterial(color, intensity, fade, vertical ? 1f : 0f, additive);
        }

        Material GetMaterial(Color color, float power, float fade, float vertical, bool additive)
        {
            var key = (color, power, fade, vertical, additive);
            if (_matCache.TryGetValue(key, out var m) && m != null) return m;

            m = new Material(_source) { hideFlags = HideFlags.HideAndDontSave };
            m.SetColor("_Color", color);
            m.SetFloat("_Vertical", vertical);
            m.SetFloat("_Power", power);
            m.SetFloat("_Fade", fade);

            // One = 가산(번짐) · OneMinusSrcAlpha = 알파 합성(짝대기)
            m.SetFloat("_DstBlend", additive
                ? (float)UnityEngine.Rendering.BlendMode.One
                : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            // ZWrite가 꺼져 있어 그리는 순서가 거리 정렬에 맡겨진다 —
            // 짝대기가 번짐보다 확실히 나중에 그려지도록 큐를 직접 띄운다.
            m.renderQueue = additive ? 3000 : 3010;

            _matCache[key] = m;
            return m;
        }
    }
}
