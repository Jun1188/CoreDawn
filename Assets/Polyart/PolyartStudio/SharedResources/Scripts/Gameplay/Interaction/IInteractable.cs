using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// [프로젝트 수정] 전역 네임스페이스의 IInteractable이 게임 코드(Runtime/Interactable)와
// 충돌해 Polyart 네임스페이스로 격리 — 사용처(CharacterClickInteraction)도 같은 네임스페이스.
// 에셋 업데이트 시 이 수정이 되돌아오면 다시 감쌀 것.
namespace Polyart
{
    public struct InteractionData
    {
        public RaycastHit hit;
        public GameObject interactor;
    }
    public interface IInteractable
    {
        public void Interact(InteractionData data);
    }

}
