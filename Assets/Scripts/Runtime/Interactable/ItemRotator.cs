using UnityEngine;

// 💡 마인크래프트처럼 아이템이 제자리에서 빙글빙글 돌게 만드는 컴포넌트
// (구 DroppedItem.cs 내 정의 — 공용 드롭 프리팹에 붙이기 위해 파일 분리)
public class ItemRotator : MonoBehaviour
{
    void Update()
    {
        transform.Rotate(90f * Time.deltaTime * Vector3.up, Space.World);
    }
}
