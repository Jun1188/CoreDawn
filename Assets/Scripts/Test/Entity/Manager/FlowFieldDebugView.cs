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
/// 다시 그리는 시점은 <b>필드가 다시 계산될 때</b>뿐이다(FlowFieldManager.FieldRebuilt).
/// 필드가 그대로인데 메시를 다시 지을 이유가 없고, 필드가 바뀌면 반드시 다시 지어야 한다.
///
/// 조립은 <b>워커 스레드</b>에서 한다 — 맵 전체면 정점이 백만 단위라 메인에서 돌리면
/// 필드 갱신마다 프레임이 멎는다. 워커는 순수 배열(필드 비용·셀 좌표 공식)만 만지고,
/// 메시 업로드만 메인에서 한다(그것만이 Unity API다).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FlowFieldDebugView : MonoBehaviour
{
    [Header("표시")]
    [Tooltip("켜면 그린다 (Game 뷰·Scene 뷰 공통). 맵 전체를 훑어 메시를 다시 짓는 일이라 " +
             "평소에는 꺼 둔다 — 필요할 때만 켤 것.")]
    [SerializeField] bool show = false;

    [Tooltip("칸 크기 대비 화살표 길이.")]
    [SerializeField, Range(0.2f, 1f)] float arrowScale = 0.65f;

    [Tooltip("지면에서 띄우는 높이(m).")]
    [SerializeField] float lift = 0.15f;

    [Tooltip("전체 투명도.")]
    [SerializeField, Range(0.1f, 1f)] float alpha = 0.9f;

    [Tooltip("목표까지의 비용을 색으로 — 가까울수록 초록, 멀수록 붉게. 끄면 단색.")]
    [SerializeField] bool colorByCost = true;

    [Tooltip("화살촉까지 그린다. 끄면 선분만 남아 정점이 1/3로 준다 — 넓게 볼 때 유리하다.")]
    [SerializeField] bool drawHeads = true;

    [Tooltip("도달할 수 없는 칸(막힘)도 회색 ×로 표시. 맵 전체를 그리므로 켜면 정점이 크게 는다.")]
    [SerializeField] bool showBlocked = false;

    static readonly Color32 GoalColor = new(51, 230, 255, 255);
    static readonly Color32 BlockedColor = new(128, 128, 140, 110);
    static readonly Color32 FlatColor = new(90, 217, 115, 255);
    static readonly Color NearColor = new(0.3f, 0.95f, 0.4f);
    static readonly Color FarColor = new(0.95f, 0.35f, 0.25f);

    Mesh mesh;
    MeshRenderer meshRenderer;
    Material material;
    FlowFieldManager subscribed;

    // 매번 새로 할당하면 맵 전체 규모(수십만 정점)에서 GC가 그대로 히치가 된다 — 재사용한다
    readonly List<Vector3> verts = new();
    readonly List<Color32> colors = new();

    // 선분 인덱스는 언제나 0,1,2,…N-1 — 매번 채울 이유가 없어 한 번 만들어 길이만 맞춰 쓴다
    int[] sequentialIndices = System.Array.Empty<int>();

    Matrix4x4 worldToLocal;   // 셀마다 InverseTransformPoint를 부르지 않으려고 한 번만 잡는다

    System.Threading.Tasks.Task buildTask;
    bool dirty;

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
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;   // 맵 전체는 16bit 인덱스를 훌쩍 넘는다
        GetComponent<MeshFilter>().sharedMesh = mesh;

        dirty = true;   // 이미 계산된 필드가 있으면 첫 프레임에 한 번 그린다
    }

    void OnDisable()
    {
        Unsubscribe();
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

        // 매니저는 씬과 함께 갈릴 수 있다 — 바뀌면 구독을 옮긴다
        if (flow != subscribed)
        {
            Unsubscribe();
            subscribed = flow;
            if (subscribed != null) subscribed.FieldRebuilt += MarkDirty;
            dirty = true;
        }

        bool ready = show && flow != null && grid != null && flow.HasField;
        if (meshRenderer.enabled != ready) meshRenderer.enabled = ready;
        if (!ready) return;

        material.SetFloat("_Alpha", alpha);

        // 워커가 끝났으면 이번 프레임에 올린다 — 메시 업로드는 메인 스레드만 할 수 있다
        if (buildTask != null && buildTask.IsCompleted)
        {
            var failed = buildTask.Exception;
            buildTask = null;

            if (failed != null) Debug.LogException(failed);
            else Upload();
        }

        if (!dirty || buildTask != null) return;
        dirty = false;

        // 워커가 만질 것을 미리 굳혀 둔다: 필드 참조, 격자 정보, 로컬 변환.
        // 이 값들은 메인에서만 바뀌므로 여기서 한 번 읽어 두면 워커는 순수 계산만 한다.
        var field = flow.Field;
        Vector2Int size = grid.gridSize;
        float cell = grid.cellSize;
        Vector3 origin = grid.originPosition;
        worldToLocal = transform.worldToLocalMatrix;

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL 플레이어는 스레드가 없어 Task.Run 작업이 영영 실행되지 않는다 — 동기 조립.
        try { Assemble(field, size, cell, origin); buildTask = System.Threading.Tasks.Task.CompletedTask; }
        catch (System.Exception e) { buildTask = System.Threading.Tasks.Task.FromException(e); }
#else
        buildTask = System.Threading.Tasks.Task.Run(() => Assemble(field, size, cell, origin));
#endif
    }

    /// <summary>워커가 채워 둔 배열을 메시에 올린다 — 메인 스레드 전용.</summary>
    void Upload()
    {
        mesh.Clear();
        if (verts.Count == 0) return;

        if (sequentialIndices.Length < verts.Count)
        {
            sequentialIndices = new int[verts.Count];
            for (int i = 0; i < sequentialIndices.Length; i++) sequentialIndices[i] = i;
        }

        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetIndices(sequentialIndices, 0, verts.Count, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();
    }

    void MarkDirty() => dirty = true;

    void Unsubscribe()
    {
        if (subscribed != null) subscribed.FieldRebuilt -= MarkDirty;
        subscribed = null;
    }

    /// <summary>
    /// 정점·색 배열을 채운다 — <b>워커 스레드에서 돈다</b>. Unity API를 부르지 않으려고
    /// 셀의 월드 좌표도 GridManager를 거치지 않고 같은 공식(원점 + 칸 중앙)으로 직접 만든다.
    /// </summary>
    void Assemble(FlowField field, Vector2Int size, float cell, Vector3 origin)
    {
        verts.Clear();
        colors.Clear();

        float half = cell * arrowScale * 0.5f;
        float head = cell * arrowScale * 0.3f;
        float mid = cell * 0.5f;

        // 색을 상대적으로 매기려면 필드 전체의 비용 폭을 먼저 알아야 한다
        int minCost = int.MaxValue, maxCost = int.MinValue;
        if (colorByCost)
        {
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                    if (field.TryGetCost(new Vector2Int(x, y), out int c))
                    {
                        if (c < minCost) minCost = c;
                        if (c > maxCost) maxCost = c;
                    }
        }

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                var at = new Vector2Int(x, y);
                Vector3 world = new(origin.x + x * cell + mid, origin.y + lift, origin.z + y * cell + mid);
                Vector3 center = worldToLocal.MultiplyPoint3x4(world);

                if (!field.TryGetCost(at, out int cost))
                {
                    if (showBlocked) AddCross(center, cell * 0.25f, BlockedColor);
                    continue;
                }

                if (!field.TryGetNext(at, out Vector2Int nextCell))
                {
                    AddSquare(center, cell * 0.25f, GoalColor);   // 비용은 있는데 다음이 없다 = 목표 칸
                    continue;
                }

                Vector3 dir = new(nextCell.x - x, 0f, nextCell.y - y);
                if (dir.sqrMagnitude < 0.0001f) continue;
                dir.Normalize();

                Color32 color = colorByCost ? CostColor(cost, minCost, maxCost) : FlatColor;
                AddArrow(center - dir * half, center + dir * half, head, color);
            }
        }
    }

    // ── 선분 조립 ────────────────────────────────────────────────

    void AddLine(Vector3 a, Vector3 b, Color32 color)
    {
        verts.Add(a); colors.Add(color);
        verts.Add(b); colors.Add(color);
    }

    void AddArrow(Vector3 from, Vector3 to, float headSize, Color32 color)
    {
        AddLine(from, to, color);
        if (!drawHeads) return;

        Vector3 dir = (to - from).normalized;
        AddLine(to, to + (Quaternion.Euler(0f, 155f, 0f) * dir) * headSize, color);
        AddLine(to, to + (Quaternion.Euler(0f, -155f, 0f) * dir) * headSize, color);
    }

    void AddCross(Vector3 at, float size, Color32 color)
    {
        AddLine(at + new Vector3(-size, 0f, -size), at + new Vector3(size, 0f, size), color);
        AddLine(at + new Vector3(-size, 0f, size), at + new Vector3(size, 0f, -size), color);
    }

    void AddSquare(Vector3 at, float size, Color32 color)
    {
        Vector3 a = at + new Vector3(-size, 0f, -size);
        Vector3 b = at + new Vector3(size, 0f, -size);
        Vector3 c = at + new Vector3(size, 0f, size);
        Vector3 d = at + new Vector3(-size, 0f, size);
        AddLine(a, b, color); AddLine(b, c, color); AddLine(c, d, color); AddLine(d, a, color);
    }

    /// <summary>가까울수록(비용이 낮을수록) 초록, 멀수록 붉게.</summary>
    static Color32 CostColor(int cost, int min, int max)
    {
        if (max <= min) return FlatColor;
        return Color.Lerp(NearColor, FarColor, Mathf.InverseLerp(min, max, cost));
    }
}
