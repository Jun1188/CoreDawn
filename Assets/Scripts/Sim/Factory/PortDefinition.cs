using System;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 건물의 입출력 연결점.
    /// BuildingDataSO.ports[] 배열에 Inspector로 설정.
    ///
    /// 예 — Miner (1×1, 오른쪽 출력):
    ///   ports[0]: IsInput=false, LocalOffset=(0,0), Direction=East
    ///
    /// 예 — Belt (1×1, 왼쪽 입력→오른쪽 출력):
    ///   ports[0]: IsInput=true,  LocalOffset=(0,0), Direction=West
    ///   ports[1]: IsInput=false, LocalOffset=(0,0), Direction=East
    ///
    /// 예 — Assembler 2×1 (왼쪽 두 입력, 오른쪽 출력):
    ///   ports[0]: IsInput=true,  LocalOffset=(0,0), Direction=West
    ///   ports[1]: IsInput=true,  LocalOffset=(0,1), Direction=West
    ///   ports[2]: IsInput=false, LocalOffset=(1,0), Direction=East
    /// </summary>
    [Serializable]
    public class PortDefinition
    {
        public Vector2Int LocalOffset;    // 건물 Origin 기준 상대 그리드 좌표
        public Direction  Direction;      // 포트가 향하는 방향 (아이템 흐름 방향)
        public bool       IsInput;        // true = 수신 포트,  false = 배출 포트

        // 아이템 필터링은 포트가 아니라 수신자의 ItemContainer.AcceptFilter가 담당한다
        // (예: 어셈블러 입력 = 현재 레시피의 재료만). 포트 필터를 두면 레시피와
        // 이중 장부가 되어 어긋날 수 있어 제거했다.
    }
}
