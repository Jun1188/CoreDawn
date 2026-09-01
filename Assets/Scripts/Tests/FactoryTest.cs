using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using CoreDawn.Entities;
using CoreDawn.Factory;
using CoreDawn.Inventories;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Tests
{
    /// <summary>
    /// 수동 플레이 테스트용 유틸: 건물 클릭 → 버퍼 상태 표시, 연결/벨트 아이템 gizmo.
    /// FactoryBootstrap(드라이버)이 있는 씬에서 사용.
    /// </summary>
    public class FactoryTest : MonoBehaviour
    {
        [Header("ScriptableObjects — Inspector에서 연결")]
        public string ironOreId;

        private Camera mainCamera;
        private string currentBuildingInfo = "";

        FactorySystem Factory => FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.Factory : null;

        void Start()
        {
            mainCamera = Camera.main;

        }

        void Update()
        {
            // 헤드리스(배치모드)에는 입력 장치가 없어 Mouse.current가 null이다 — 프레임마다 NRE 방지
            if (Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
                DetectAndDisplayBuilding();
        }

        private void DetectAndDisplayBuilding()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.value);
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var view = hit.collider.GetComponentInParent<BuildingView>();
                if (view != null && view.Building != null)
                    PrintBuildingData(view.Building);
            }
        }

        private void PrintBuildingData(BuildingModule building)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine("[ 건물 정보 ]");
            sb.AppendLine($"- 이름 : {building.DisplayName}");
            sb.AppendLine($"- 정의 : {building.Def.Id}");
            sb.AppendLine($"- 위치 : {building.Origin}");
            sb.AppendLine($"- 회전 : {building.RotationSteps}단계 ({building.RotationSteps * 90}도)");
            sb.AppendLine();

            sb.AppendLine("[ 버퍼 상태 ]");
            AppendContainer(sb, "입력 버퍼", building.Input);
            sb.AppendLine();
            AppendContainer(sb, "출력 버퍼", building.Output);
            sb.AppendLine("--------------------------------------------------");

            currentBuildingInfo = sb.ToString();
        }

        private static void AppendContainer(StringBuilder sb, string label, ItemContainer c)
        {
            sb.AppendLine($"- {label} ({c.SlotCount}칸)");
            var entries = c.Snapshot();
            if (entries.Count == 0) { sb.AppendLine("  * (비어 있음)"); return; }
            foreach (var (item, n) in entries)
                sb.AppendLine($"  * {item.DisplayName} : {n}개");
        }

        void OnGUI()
        {
            // 건물을 클릭하기 전에는 내용이 없다 — 그때 그리면 빈 반투명 검정 박스가 게임 화면을 가린다.
            if (string.IsNullOrWhiteSpace(currentBuildingInfo)) return;

            GUI.TextArea(new Rect(20, 200, 400, 300), currentBuildingInfo);
        }

        // ─── Gizmo — 심 상태 시각화 (뷰 transform 경유) ─────────────

        static Transform ViewOf(BuildingModule b)
        {
            var view = FactoryBootstrap.Instance != null ? FactoryBootstrap.Instance.GetView(b) : null;
            return view != null ? view.transform : null;
        }

        static Vector3 SegmentPosToWorld(BeltSegment seg, float pos)
        {
            int n = seg.Belts.Count;

            // Belts[i] 중심의 pos = (n-1-i) + 0.5.  pos→연속 인덱스 u로 역변환:
            //   u = (n - 0.5) - pos   (pos 클수록 u 작음 = 출구 쪽 index 0)
            float u = (n - 0.5f) - pos;

            Transform T(int i) => ViewOf(seg.Belts[i]);

            // 입구 바깥(파랑 = Belts[^1] 뒤로 외삽)
            if (u >= n - 1)
            {
                var last = T(n - 1); if (last == null) return default;
                Vector3 c = last.position;
                Vector3 dir = (n > 1 && T(n - 2) != null)
                    ? (T(n - 2).position - c).normalized   // 입구→출구 진행 방향
                    : ExitDir(seg.Belts[n - 1]);
                return c + dir * (u - (n - 1)) * -1f;      // 입구 뒤쪽
            }

            // 출구 바깥(빨강 = Belts[0] 앞으로 외삽)
            if (u <= 0f)
            {
                var first = T(0); if (first == null) return default;
                Vector3 c = first.position;
                Vector3 dir = (n > 1 && T(1) != null)
                    ? (c - T(1).position).normalized       // 진행 방향(출구 쪽)
                    : ExitDir(seg.Belts[0]);
                return c + dir * (-u);
            }

            // 중간: 인접 벨트 중심 보간
            int j = Mathf.FloorToInt(u);
            float frac = u - j;
            var a = T(j); var b = T(j + 1);
            if (a == null || b == null) return default;
            return Vector3.Lerp(a.position, b.position, frac);
        }

        static Vector3 ExitDir(BuildingModule b)
        {
            var self = ViewOf(b);
            if (self == null) return Vector3.forward;
            if (b.OutputConnections.Count > 0)
            {
                var to = ViewOf(b.OutputConnections[0].To);
                if (to != null) return (to.position - self.position).normalized;
            }
            return self.forward;
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying || Factory == null) return;

            // 1. 모든 건물 연결선 시각화 (초록)
            Gizmos.color = Color.green;
            foreach (var view in FindObjectsByType<BuildingView>(FindObjectsSortMode.None))
            {
                var b = view.Building;
                if (b == null || b.OutputConnections == null) continue;

                Vector3 startPos = view.transform.position + Vector3.up * 0.5f;
                foreach (var conn in b.OutputConnections)
                {
                    var toT = ViewOf(conn.To);
                    if (toT == null) continue;

                    Vector3 endPos = toT.position + Vector3.up * 0.5f;
                    Gizmos.DrawLine(startPos, endPos);

                    // 흐름 방향 안내용 작은 구체
                    Vector3 dir = (endPos - startPos).normalized;
                    Gizmos.DrawSphere(endPos - dir * 0.3f, 0.1f);
                }
            }

            // 2. 벨트 세그먼트의 아이템 실시간 위치 (노랑) + 입구/출구 마커
            foreach (var seg in Factory.Belts.Segments)
            {
                int n = seg.Belts.Count;
                if (n == 0) continue;

                var exitT  = ViewOf(seg.Belts[0]);
                var entryT = ViewOf(seg.Belts[n - 1]);
                if (exitT == null || entryT == null) continue; // 철거 직후 과도기

                Gizmos.color = Color.red;  Gizmos.DrawSphere(exitT.position  + Vector3.up, 0.25f); // 출구
                Gizmos.color = Color.blue; Gizmos.DrawSphere(entryT.position + Vector3.up, 0.25f); // 입구
                Gizmos.color = Color.yellow;

                if (!seg.HasItems) continue;
                foreach (var (item, pos) in seg.Items)
                {
                    Vector3 wp = SegmentPosToWorld(seg, pos);
                    wp.y += 0.6f;
                    Gizmos.DrawSphere(wp, 0.15f);
                }
            }
        }
    }
}
