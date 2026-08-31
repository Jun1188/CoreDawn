using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CoreDawn.Sim
{
    // ================================================================
    //  BuildingGraph.cs
    //  건물 간 상호작용의 핵심 — 연결 관리 (plain C#, FactorySystem 소유)
    //
    //  포함:
    //    BuildingConnection — 두 건물 간의 단방향 연결
    //    BuildingGraph      — 포트 매칭으로 연결 자동 생성/해제
    //
    //  관련 (별도 파일):
    //    ItemContainer — 건물 입출력 버퍼 (슬롯 기반)
    //    Building      — 심 엔티티
    //    GridIndex     — 좌표 → BuildingModule O(1) 조회 (FactorySystem.cs)
    // ================================================================

    /// <summary>두 Building 간의 단방향 아이템 연결.</summary>
    public class BuildingConnection
    {
        public BuildingModule       From, To;
        public PortDefinition FromPort, ToPort;
    }

    /// <summary>
    /// 건물 간 연결을 방향 그래프(인접 리스트)로 관리.
    ///
    /// [포트 매칭 알고리즘 — OnPlaced에서 1회 실행]
    ///
    ///   새 건물 A의 출력 포트 P:
    ///     ① P의 그리드 좌표   = A.Origin + P.LocalOffset
    ///     ② 이웃 셀 좌표      = ①의 좌표 + P.Direction 방향으로 1칸
    ///     ③ 이웃 건물 B       = Grid.GetAt(②)   — O(1)
    ///     ④ B의 입력 포트 중
    ///          Direction == Opposite(P.Direction) 인 포트 → 연결!
    ///
    ///   제거 시 해당 건물의 모든 연결을 양방향으로 제거.
    /// </summary>
    public class BuildingGraph
    {
        readonly FactorySystem _sim;

        // 인접 리스트 — 나가는/들어오는 연결 분리 저장
        readonly Dictionary<BuildingModule, List<BuildingConnection>> _out = new();
        readonly Dictionary<BuildingModule, List<BuildingConnection>> _in  = new();

        public BuildingGraph(FactorySystem sim) => _sim = sim;

        // ── FactorySim이 호출하는 진입점

        /// <summary>건물이 GridIndex에 등록된 직후 호출.</summary>
        public void OnPlaced(BuildingModule b)
        {
            _out[b] = new List<BuildingConnection>();
            _in[b]  = new List<BuildingConnection>();
            FindAndRegister(b);            // 포트 매칭
            b.OnAfterConnected();          // 행동 초기화 (Miner가 자원 조회 등)
        }

        /// <summary>건물 제거 시 호출. GridIndex에서 빠지기 전에 호출해야 한다.</summary>
        public void OnRemoved(BuildingModule b)
        {
            foreach (var c in _out[b])
            {
                _in[c.To].Remove(c);
                c.To.InputConnections.Remove(c);
            }
            foreach (var c in _in[b])
            {
                _out[c.From].Remove(c);
                c.From.OutputConnections.Remove(c);
            }
            _out.Remove(b);
            _in.Remove(b);

            _sim.Belts.OnBuildingRemoved(b);
        }

        // ── 포트 매칭 (핵심 알고리즘)

        void FindAndRegister(BuildingModule nb)
        {
            var ports = nb.GetEffectivePorts();
            if (ports == null) return;

            foreach (var port in ports)
            {
                // ① 이 포트의 그리드 좌표
                var portCell     = nb.Origin + port.LocalOffset;
                // ② 이웃 셀
                var neighborCell = portCell + Dir.ToVec(port.Direction);
                var neighbor     = _sim.Grid.GetAt(neighborCell);
                if (neighbor == null) continue;

                var opp    = Dir.Opposite(port.Direction);
                var nPorts = neighbor.GetEffectivePorts();
                if (nPorts == null) continue;

                foreach (var np in nPorts)
                {
                    // 방향 반대 + 입출력 반대여야 연결 가능
                    if (np.Direction != opp || np.IsInput == port.IsInput) continue;

                    // 그리고 그 포트가 **맞닿은 칸에** 있어야 한다.
                    // 이 검사가 없으면 2×2 저장소처럼 면마다 칸이 여럿인 건물에서,
                    // 실제로는 반대편 칸에 있는 포트와 연결돼 버린다 —
                    // 화면(포트 흐름 표시)은 칸까지 보고 그리므로 보이는 것과 실제가 어긋난다.
                    if (neighbor.Origin + np.LocalOffset != neighborCell) continue;

                    // 연결 방향: 출력 → 입력
                    var conn = !port.IsInput
                        ? new BuildingConnection { From = nb, FromPort = port, To = neighbor, ToPort = np }
                        : new BuildingConnection { From = neighbor, FromPort = np, To = nb, ToPort = port };

                    // 중복 방지
                    if (_out[conn.From].Any(c => c.To == conn.To && c.FromPort == conn.FromPort))
                        break;

                    RegisterConn(conn);
                    break; // 포트당 연결 1개
                }
            }
        }

        void RegisterConn(BuildingConnection c)
        {
            _out[c.From].Add(c); _in[c.To].Add(c);
            c.From.OutputConnections.Add(c);
            c.To.InputConnections.Add(c);
            _sim.Belts.OnNewConnection(c);

            // 새 연결 = 상태 변화. 출력이 막혀 정지(stall)해 있던 건물이
            // 새로 생긴 하류로 밀어낼 수 있도록 양단을 깨운다.
            // (이게 없으면 "마이너 먼저 설치 → 버퍼 가득 → 벨트 연결" 시 영구 정지)
            _sim.MarkDirty(c.From);
            _sim.MarkDirty(c.To);
        }
    }
}
