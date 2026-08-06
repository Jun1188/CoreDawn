using UnityEngine;

public class ProceduralRecoil : MonoBehaviour
{

    [Header("Recoil Settings")]
    public float snappiness = 20f;  // 반동이 튀어오르는 속도 (클수록 탕! 하고 빠름)
    public float returnSpeed = 10f; // 원래 위치로 돌아오는 속도

    private Vector3 currentRotation;
    private Vector3 targetRotation;


    private void Update()
    {
        // 1. targetRotation은 계속해서 0(원래 위치)을 향해 돌아갑니다. (텐션 복구)
        targetRotation = Vector3.Lerp(targetRotation, Vector3.zero, returnSpeed * Time.deltaTime);

        // 2. 실제 회전값은 targetRotation을 부드럽게 따라갑니다. (스프링 효과)
        currentRotation = Vector3.Slerp(currentRotation, targetRotation, snappiness * Time.deltaTime);

        transform.localRotation = Quaternion.Euler(currentRotation);
    }

    // Gun의 ApplyRecoil()에서 이 함수를 호출합니다.
    public void FireRecoil(float recoilX, float recoilY, float recoilZ)
    {

        // 화면이 위로 튀고(X), 좌우로 흔들리며(Y), 총이 비틀림(Z)
        targetRotation += new Vector3(-recoilX, Random.Range(-recoilY, recoilY), Random.Range(-recoilZ, recoilZ));
    }
}