using UnityEngine;

public class DroppedItem : Interactable
{
    public ItemDataSO item;
    public int amount;

    [Tooltip("아이템 아이콘을 표시할 렌더러 — 공용 프리팹에서 연결 (폴백 조립 시 런타임 주입)")]
    [SerializeField] private SpriteRenderer visual;

    public void Setup(ItemDataSO itemData, int count)
    {
        item = itemData;
        amount = count;

        // 조준했을 때 화면에 뜰 메시지 세팅
        promptMessage = $"{item.name} x{amount} 줍기";

        // 마크식 정형화 — 모든 아이템이 같은 프리팹, 아이콘만 교체
        if (visual != null) visual.sprite = itemData.icon;
    }

    /// <summary>
    /// 월드 드롭 아이템 스폰 — 핫바 Q드롭, 인벤 캐리지 드롭, (예정) 몬스터 루팅 공용.
    /// 마크처럼 정형화: ItemDatabase의 공용 프리팹 하나를 모든 아이템이 쓴다 (아이콘만 교체).
    /// 프리팹 미지정 시 코드 조립 폴백 — 테스트 씬 안전.
    /// </summary>
    public static DroppedItem Spawn(ItemDataSO item, int amount, Vector3 position, Vector3 throwDirection)
    {
        var db = ItemDatabaseSO.LoadDefault();
        var prefab = db != null ? db.droppedItemPrefab : null;

        DroppedItem dropped = prefab != null
            ? Instantiate(prefab, position, Quaternion.identity)
            : BuildFallback(position);

        dropped.Setup(item, amount);

        var rb = dropped.GetComponent<Rigidbody>();
        if (rb != null) rb.AddForce(throwDirection * 3.5f, ForceMode.Impulse);
        return dropped;
    }

    /// <summary>공용 프리팹이 없을 때의 코드 조립 (구 방식). 프리팹과 같은 구조를 만든다.</summary>
    static DroppedItem BuildFallback(Vector3 position)
    {
        // 1. 루트 오브젝트 + 레이어
        GameObject dropObj = new("Dropped(Fallback)");
        dropObj.transform.position = position;

        int layer = LayerMask.NameToLayer("Interactable");
        if (layer != -1) dropObj.layer = layer;
        else Debug.LogWarning("[레이어 경고] 'Interactable' 레이어가 없습니다. Tags and Layers 설정 확인!");

        // 2. 물리
        Rigidbody rb = dropObj.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        // 3. 콜라이더 2개 — 바닥 충돌용 고체 + 플레이어 획득 감지용 센서
        BoxCollider solidCol = dropObj.AddComponent<BoxCollider>();
        solidCol.size = new Vector3(0.3f, 0.3f, 0.3f);
        solidCol.isTrigger = false;

        BoxCollider triggerCol = dropObj.AddComponent<BoxCollider>();
        triggerCol.size = new Vector3(1.5f, 1.5f, 1.5f);
        triggerCol.isTrigger = true;

        // 4. 비주얼 자식 (둥둥 떠서 도는 아이콘)
        GameObject visualObj = new("Visual");
        visualObj.transform.SetParent(dropObj.transform);
        visualObj.transform.localPosition = Vector3.zero;
        visualObj.layer = dropObj.layer;

        var sr = visualObj.AddComponent<SpriteRenderer>();
        visualObj.AddComponent<ItemRotator>();

        var dropped = dropObj.AddComponent<DroppedItem>();
        dropped.visual = sr;
        return dropped;
    }

    /// <summary>
    /// 같은 아이템 스택 병합. 양쪽 모두 서로의 OnTriggerEnter를 받으므로
    /// InstanceID가 작은 쪽만 수행해 중복 병합을 막는다. 합계가 스택 상한을 넘으면 각자 유지.
    /// </summary>
    private void TryMergeWith(DroppedItem other)
    {
        if (other == null || other == this) return;
        if (item == null || other.item != item) return;
        if (amount <= 0 || other.amount <= 0) return;          // 이미 병합/줍기로 소멸 예정인 상대
        if (GetInstanceID() > other.GetInstanceID()) return;   // 한쪽만 수행
        if (amount + other.amount > new ItemStack(item, 1).maxStackSize) return;

        amount += other.amount;
        other.amount = 0;                                      // 상대의 후속 병합/줍기 차단
        Destroy(other.gameObject);
        Setup(item, amount);                                   // 프롬프트("xN 줍기") 갱신
    }

    // [방법 1] 조준 후 직접 상호작용 키를 눌러서 줍기
    public override void OnInteract(PlayerController player)
    {
        if (item == null || amount <= 0) return;

        // 플레이어 가방 백엔드에 아이템 주워담기 시도
        bool success = player.playerInventory.AddItem(item, amount);

        if (success)
        {
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.RefreshAllGameUIs(player.playerInventory);
            }
            Destroy(gameObject); // 바닥에 있던 오브젝트 삭제
        }
        else
        {
            Debug.LogWarning("[가방 가득 참] 인벤토리에 빈 공간이 없습니다!");
        }
    }

    // [방법 2] 발로 밟거나 근처 센서 구역에 들어가면 자동으로 줍기 (자석 루팅)
    private void OnTriggerEnter(Collider other)
    {
        // 마크식 스택 병합 — 같은 아이템끼리 센서에 닿으면 하나로 (플레이어 판정보다 먼저)
        TryMergeWith(other.GetComponentInParent<DroppedItem>());

        // 충돌한 대상이 플레이어인지 태그 검사
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null && player.playerInventory != null)
            {
                // 백엔드 가방에 아이템 데이터 추가 시도
                bool isSuccess = player.playerInventory.AddItem(this.item, this.amount);

                if (isSuccess)
                {
                    if (InventoryManager.Instance != null)
                    {
                        Debug.Log($"[루팅 성공] {item.name} {amount}개를 자동으로 주웠습니다.");
                        // 핫바를 포함한 모든 인벤토리 UI 실시간 새로고침 강제 호출!
                        InventoryManager.Instance.RefreshAllGameUIs(player.playerInventory);
                    }

                    // 바닥 오브젝트 깔끔하게 파괴
                    Destroy(gameObject);
                }
            }
        }
    }
}
