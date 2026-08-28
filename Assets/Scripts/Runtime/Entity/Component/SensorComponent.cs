using System.Collections.Generic;
using UnityEngine;
using CoreDawn.Factory;

namespace CoreDawn.Entities
{
    // 감지 — 순수 C# 클래스. OverlapSphere로 대상 레이어의 Entity를 찾는다.
    // 길찾기 최적화의 핵심: 몬스터 하나하나가 플레이어를 스캔하는 대신,
    // 플레이어(1개)가 범위 내 몬스터들을 찾아 콜백(Monster.OnDetectedByPlayer)해준다.
    // 타워(Building)도 같은 컴포넌트로 사거리 내 몬스터를 찾는다.
    [System.Serializable]
    public class SensorComponent
    {
        [SerializeField] private float detectionRange = 10f;
        [Tooltip("감지 대상이 위치한 레이어 이름. Initialize에서 마스크로 변환된다. 플레이어/타워의 감지 대상은 몬스터.")]
        [SerializeField] private string targetLayerName = "Monster";
        [SerializeField] private float scanInterval = 0.2f; // 매 프레임 OverlapSphere 방지

        private EntityView owner;
        private Transform transform;
        private LayerMask targetLayer;
        private float lastScanTime = float.MinValue;

        // GC 방지용 재사용 버퍼 (메인 스레드 전용).
        // static이 아니라 인스턴스 필드다 — 모든 센서(타워 전부 + 플레이어)가 한 배열을 나눠 쓰면
        // 한 스캔이 다른 스캔의 결과 위에 덮어쓸 수 있고(재진입), 디버깅이 불가능한 형태로 어긋난다.
        // 256칸인 이유: 가득 차면 Unity가 조용히 잘라내므로, 잘린 목록에서 고른 "가장 가까운 대상"이
        // 실제로 가장 가깝지 않게 된다 — 밤 웨이브에 몬스터가 몰리는 순간 조준이 튀는 원인이 된다.
        private readonly Collider[] overlapBuffer = new Collider[256];

        public float DetectionRange => detectionRange;
        public float ScanInterval => scanInterval;

        // 런타임 부착 엔티티(EnsurePlayerEntity 등) 전용 — 인스펙터를 못 쓰는 경우 감지 범위 조정
        public void SetDetectionRange(float range) => detectionRange = Mathf.Max(0f, range);
        public void SetTargetLayer(params string[] layerNames) 
        {
            targetLayer = LayerMask.GetMask(layerNames);
        }

        public void Initialize(EntityView owner)
        {
            this.owner = owner;
            transform = owner.transform;

            // 레이어 마스크를 이름으로 해석 — 인스펙터 오설정 방지
            targetLayer = LayerMask.GetMask(targetLayerName);
            if (targetLayer == 0)
                Debug.LogWarning($"[SensorComponent] '{targetLayerName}' 레이어를 찾지 못했습니다. " +
                    $"대상 감지가 동작하지 않습니다. Project Settings > Tags and Layers에서 레이어를 확인하세요.");
        }

        // 스캔 주기가 됐을 때만 true를 반환하며 results를 범위 내 유효 Entity로 채운다.
        // (플레이어가 몬스터 감지/해제 콜백을 쏘는 용도)
        public bool TryScan(List<EntityView> results)
        {
            if (transform == null || Time.time < lastScanTime + scanInterval) return false;
            lastScanTime = Time.time;

            results.Clear();
            int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRange, overlapBuffer, targetLayer);
            for (int i = 0; i < count; i++)
            {
                // 콜라이더가 자식 모델에 붙어 있는 프리팹 구조를 지원하기 위해 부모까지 탐색
                EntityView entity = overlapBuffer[i].GetComponentInParent<EntityView>();
                if (entity == null || entity == owner || !entity.IsValidTarget()) continue;
                if (!results.Contains(entity)) results.Add(entity); // 콜라이더 여러 개인 대상 중복 방지
            }
            return true;
        }

        // 범위 내 가장 가까운 유효 Entity (타워 자동 공격 등 단일 대상용, 즉시 스캔).
        // minRange보다 가까운 대상은 건너뛴다 — 박격포처럼 사각(최소 사거리)이 있는 발사기용.
        //
        // 거리는 반드시 Entity의 위치(= 목표를 계속 유지할지 판정하는 쪽이 쓰는 값)로 잰다.
        // OverlapSphere는 콜라이더의 "부피"가 구에 닿기만 해도 돌려주므로, 반지름만 믿으면
        // 중심이 사거리 밖인 대상까지 잡힌다(몬스터 캡슐 반지름 0.5 → 사거리+0.5까지).
        // 그러면 잡는 쪽과 유지하는 쪽의 기준이 어긋나, 경계에 선 몬스터를 스캔마다
        // 잡았다 버렸다 하며 포탑이 초당 몇 번씩 홱홱 돌아간다.
        public EntityView GetClosestTarget(float rangeOverride = -1f, float minRange = 0f)
        {
            if (transform == null) return null;
            float range = rangeOverride > 0f ? rangeOverride : detectionRange;

            int count = Physics.OverlapSphereNonAlloc(transform.position, range, overlapBuffer, targetLayer);
            EntityView closest = null;
            float minDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                EntityView entity = overlapBuffer[i].GetComponentInParent<EntityView>();
                if (entity == null || entity == owner || !entity.IsValidTarget()) continue;

                float dist = Vector3.Distance(transform.position, entity.GetPosition());
                if (dist < minRange || dist > range) continue;   // 사각 안 / 사거리 밖 — 조준 불가
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closest = entity;
                }
            }
            return closest;
        }
    }
}
