using UnityEngine;

public class Node
{
    public bool walkable;           // 장애물이 없어 지나갈 수 있는지 여부
    public Vector3 worldPosition;   // 유니티 월드 상의 실제 위치 좌표 (GridSystem.GridToWorldCenter)
    public Vector2Int gridCoord;    // 기존의 gridX, gridY를 Vector2Int 하나로 통합
    public Node(bool _walkable, Vector3 _worldPos, Vector2Int _gridCoord)
    {
        walkable = _walkable;
        worldPosition = _worldPos;
        gridCoord = _gridCoord;
    }
}
