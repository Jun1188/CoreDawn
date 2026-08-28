using UnityEngine;

public class pathNode
{

    public Node targetNode;   // 맵의 원본 노드
    public pathNode parent;   // 경로 역추적용 부모 노드
    public int gCost;
    public int hCost;
    public int FCost => gCost + hCost;
    public pathNode(Node node)
    {
        this.targetNode = node;
        this.gCost = int.MaxValue; // 초기 G비용은 무한대
        this.hCost = 0;
        this.parent = null;
    }
}
