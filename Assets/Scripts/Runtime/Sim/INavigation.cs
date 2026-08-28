using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 심이 길찾기에 묻는 것 전부 — 통행 가능·지형 배율·지면 높이·플로우필드 방향·돌파 대상·A* 경로.
    ///
    /// 인터페이스인 이유: 지금 길찾기는 씬 싱글턴(GridManager·FlowFieldManager·PathRequest·GroundSampler)이라
    /// 심 모듈이 그걸 직접 부르면 MonoBehaviour에 다시 묶인다. 어댑터(SceneNavigation)가 그 뒤에 서고,
    /// 5단계에서 길찾기가 심 내부로 들어오면 구현만 바뀐다. 헤드리스 테스트는 가짜 구현을 꽂는다.
    ///
    /// 경로 질의가 콜백인 이유: 계산이 워커에서 돌아 답이 다음 프레임 이후에 온다. 답이 올 즈음 묻는 쪽이
    /// 다른 상태일 수 있다 — 그 확인은 묻는 쪽(두뇌)의 몫이다.
    /// </summary>
    public interface INavigation
    {
        /// <summary>격자가 준비됐는가. 아니면 경로 질의는 즉시 null을 돌려준다.</summary>
        bool IsReady { get; }

        /// <summary>이 자리를 걸어서 지날 수 있는가(건물·절벽 포함). 격자가 없으면 true.</summary>
        bool IsWalkable(Vector3 world);

        /// <summary>이 자리의 지형 이동 속도 배율(강 0.5 등). 격자가 없으면 1.</summary>
        float TerrainSpeedAt(Vector3 world);

        /// <summary>이 자리(XZ)의 지면 높이(Y).</summary>
        float GroundHeightAt(Vector3 world);

        bool HasFlowField { get; }

        /// <summary>플로우필드가 가리키는 다음 방향. 필드 없음·목표 도달·맵 밖이면 zero.</summary>
        Vector3 FlowDirectionAt(Vector3 world);

        /// <summary>진격 경로 위에 서 있는 건물의 엔티티 — 지금 부숴야 앞으로 갈 수 있는 것. 없으면 null.</summary>
        Entity FindBreachTarget(Vector3 from, float range);

        /// <summary>A* 경로(월드 좌표 웨이포인트). 못 찾으면 null, 이미 도착이면 빈 목록.</summary>
        void FindPath(Vector3 from, Vector3 to, bool ignoreBuildings, Action<List<Vector3>> onDone);

        /// <summary>길이 막혔을 때 부수면 열리는 건물의 엔티티. 지형이 막은 것이면 null.</summary>
        void FindBlockingBuilding(Vector3 from, Vector3 to, Action<Entity> onDone);
    }
}
