using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플로우필드 시각화 — 칸마다 "여기서 다음에 어디로 가는가"를 화살표로 그린다.
///
/// 몬스터가 왜 그쪽으로 가는지(또는 왜 안 가는지)는 필드를 눈으로 봐야 알 수 있다.
/// 목표 시드가 잘못 깔렸는지, 건물이 길을 막아 우회가 생겼는지, 아예 도달 불가 지대인지가
/// 화살표의 흐름과 색으로 바로 드러난다.
///
/// <b>선분 메시 한 덩이</b>로 그린다 — 기즈모로 칸마다 선을 그으면 드로우콜이 칸 수만큼 늘고
/// Scene 뷰에서만 보인다. 통짜 메시는 드로우콜 하나로 끝나고 Game 뷰에도 그대로 나오므로
/// 플레이하면서 흐름을 볼 수 있다. 색은 칸마다 다르니 머티리얼이 아니라 정점 색으로 싣는다.
///
/// 갱신은 필요할 때만 — 기준점이 반 칸 넘게 움직였거나 필드가 다시 계산됐을 때.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FlowFieldDebugView : MonoBehaviour
{
    [Header("표시")]
    [Tooltip("끄면 아무것도 그리지 않는다 (Game 뷰·Scene 뷰 공통).")]
    [SerializeField] bool show = true;

    [Tooltip("기준점에서 이 반경(칸)만 그린다.")]
    [SerializeField, Range(3, 80)] int radiusCells = 25;

    [Tooltip("비워 두면 플레이어를 따라간다. 플레이어도 없으면 이 오브젝트 위치.")]
    [SerializeField] Transform focus;

    [Header("모양")]
    [Tooltip("칸 크기 대비 화살표 길이.")]
    [SerializeField, Range(0.2f, 1f)] float arrowScale = 0.65f;

    [Tooltip("지면에서 띄우는 높이(m).")]
    [SerializeField] float lift = 0.15f;

    [Tooltip("전체 투명도.")]
    [SerializeField, Range(0.1f, 1f)] float alpha = 0.9f;

    [Tooltip("목표까지의 비용을 색으로 — 가까울수록 초록, 멀수록 붉게. 끄면 단색.")]
    [SerializeField] bool colorByCost = true;

    [Tooltip("도달할 수 없는 칸(막힘)을 회색 ×로 표시.")]
    [SerializeField] bool showBlocked = true;

    static readonly Color GoalColor = new(0.2f, 0.9f, 1f);
    static readonly Color BlockedColor = new(0.5f, 0.5f, 0.55f, 0.5f);
    static readonly Color FlatColor = new(0.35f, 0.85f, 0.45f);
    static readonly Color NearColor = new(0.3f, 0.95f, 0.4f);
    static readonly Color FarColor = new(0.95f, 0.35f, 0.25f);

    Mesh mesh;
    MeshRenderer meshRenderer;
    Material material;

    readonly List<Vector3> verts = new();
    readonly List<Color> colors = new();
    readonly List<int> indices = new();

    Vector2Int lastCenter = new(int.MinValue, int.MinValue);
    float nextRefresh;

    void OnEnable()
    {
        meshRenderer = GetComponent<MeshRenderer>();

        var shader = Shader.Find("LevelUp/DebugVertexColor");
        if (shader == null)
        {
            Debug.LogWarning("[FlowFieldDebug] 'LevelUp/DebugVertexColor' 셰이더를 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        mesh = new Mesh { name = "FlowFieldDebug", hideFlags = HideFlags.HideAndDontSave };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;   // 반경 80이면 정점이 16bit를 넘는다
        GetComponent<MeshFilter>().sharedMesh = mesh;

        lastCenter = new Vector2Int(int.MinValue, int.MinValue);
    }

    void OnDisable()
    {
        if (mesh != null) DestroyImmediate(mesh);
        if (material != null) DestroyImmediate(material);
        mesh = null;
        material = null;
    }

    void LateUpdate()
    {
        if (meshRenderer == null) return;

        var flow = FlowFieldManager.Instance;
        var grid = GridManager.Instance;
        bool ready = show && flow != null && grid != null && flow.HasField;

        if (meshRenderer.enabled != ready) meshRenderer.enabled = ready;
        if (!ready) return;

        material.SetFloat("_Alpha", alpha);

        // 필드는 몇 초마다 다시 계산되고 기준점도 움직인다 — 둘 중 하나라도 바뀌면 다시 짓는다.
        // 매 프레임 짓지 않는 이유는 단순하다: 칸 수천 개를 매 프레임 순회할 이유가 없다.
        var node = grid.NodeFromWorldPoint(FocusPosition());
        if (node == null) return;

        bool moved = node.gridCoord != lastCenter;
        bool due = Time.unscaledTime >= nextRefresh;
        if (!moved && !due) return;

        lastCenter = node.gridCoord;
        nextRefresh = Time.unscaledTime + 0.5f;
        Rebuild(flow, grid, node.gridCoord);
    }

    void Rebuild(FlowFieldManager flow, GridManager grid, Vector2Int mid)
    {
        verts.Clear();
        colors.Clear();
        indices.Clear();

        float cell = grid.cellSize;
        float half = cell * arrowScale * 0.5f;
        float head = cell * arrowScale * 0.3f;

        // 색을 상대적으로 매기려면 보이는 범위의 비용 폭을 먼저 알아야 한다
        int minCost = int.MaxValue, maxCost = int.MinValue;
        if (colorByCost)
        {
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
                for (int dy = -radiusCells; dy <= radiusCells; dy++)
                    if (flow.TryGetCost(new Vector2Int(mid.x + dx, mid.y + dy), out int c))
                    {
                        if (c < minCost) minCost = c;
                        if (c > maxCost) maxCost = c;
                    }
        }

        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        {
            for (int dy = -radiusCells; dy <= radiusCells; dy++)
            {
                var at = new Vector2Int(mid.x + dx, mid.y + dy);
                var node = grid.GetNode(at);
                if (node == null) continue;

                // 메시는 이 오브젝트의 로컬 공간이다 — 월드 좌표를 그대로 넣으면 트랜스폼만큼 밀린다
                Vector3 center = transform.InverseTransformPoint(node.worldPosition + Vector3.up * lift);

                if (!flow.TryGetCost(at, out int cost))
                {
                    if (showBlocked) AddCross(center, cell * 0.25f, BlockedColor);
                    continue;
                }

                if (!flow.TryGetNextCell(at, out Vector2Int nextCell))
                {
                    AddSquare(center, cell * 0.25f, GoalColor);   // 비용은 있는데 다음이 없다 = 목표 칸
                    continue;
                }

                var nextNode = grid.GetNode(nextCell);
                if (nextNode == null) continue;

                Vector3 dir = nextNode.worldPosition - node.worldPosition;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) continue;
                dir.Normalize();

                Color color = colorByCost ? CostColor(cost, minCost, maxCost) : FlatColor;
                AddArrow(center - dir * half, center + dir * half, head, color);
            }
        }

        mesh.Clear();
        if (verts.Count == 0) return;

        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetIndices(indices, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();
    }

    // ── 선분 조립 ────────────────────────────────────────────────

    void AddLine(Vector3 a, Vector3 b, Color color)
    {
        indices.Add(verts.Count);
        indices.Add(verts.Count + 1);
        verts.Add(a); colors.Add(color);
        verts.Add(b); colors.Add(color);
    }

    void AddArrow(Vector3 from, Vector3 to, float headSize, Color color)
    {
        AddLine(from, to, color);

        Vector3 dir = (to - from).normalized;
        AddLine(to, to + (Quaternion.Euler(0f, 155f, 0f) * dir) * headSize, color);
        AddLine(to, to + (Quaternion.Euler(0f, -155f, 0f) * dir) * headSize, color);
    }

    void AddCross(Vector3 at, float size, Color color)
    {
        AddLine(at + new Vector3(-size, 0f, -size), at + new Vector3(size, 0f, size), color);
        AddLine(at + new Vector3(-size, 0f, size), at + new Vector3(size, 0f, -size), color);
    }

    void AddSquare(Vector3 at, float size, Color color)
    {
        Vector3 a = at + new Vector3(-size, 0f, -size);
        Vector3 b = at + new Vector3(size, 0f, -size);
        Vector3 c = at + new Vector3(size, 0f, size);
        Vector3 d = at + new Vector3(-size, 0f, size);
        AddLine(a, b, color); AddLine(b, c, color); AddLine(c, d, color); AddLine(d, a, color);
    }

    Vector3 FocusPosition()
    {
        if (focus != null) return focus.position;

        var player = FindFirstObjectByType<PlayerController>();
        return player != null ? player.transform.position : transform.position;
    }

    /// <summary>가까울수록(비용이 낮을수록) 초록, 멀수록 붉게.</summary>
    static Color CostColor(int cost, int min, int max)
    {
        if (max <= min) return FlatColor;
        return Color.Lerp(NearColor, FarColor, Mathf.InverseLerp(min, max, cost));
    }
}
