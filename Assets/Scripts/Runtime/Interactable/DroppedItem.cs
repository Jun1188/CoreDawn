using UnityEngine;
using UnityEngine.Pool;

public class DroppedItem : Interactable
{
    public ItemDataSO item;
    public int amount;

    /// <summary>핑 이름 — "철판 ×3". 프롬프트("xN 줍기")는 동사가 붙어 있어 알림용 이름으로는 맞지 않는다.</summary>
    public override string PingLabel
    {
        get
        {
            if (item == null) return name;
            string itemName = string.IsNullOrEmpty(item.displayName) ? item.name : item.displayName;
            return amount > 1 ? $"{itemName} ×{amount}" : itemName;
        }
    }

    [Tooltip("아이템 아이콘을 표시할 렌더러 — 공용 프리팹에서 연결 (폴백 조립 시 런타임 주입)")]
    [SerializeField] private SpriteRenderer visual;

    // ── 풀 ──────────────────────────────────────────────────────
    // 드롭은 채굴·철거 환급·루팅으로 끊임없이 나고 사라진다. 매번 Instantiate/Destroy를 하면
    // 그만큼 GC 쓰레기가 쌓이므로 인스턴스를 돌려 쓴다 (총알 풀과 같은 UnityEngine.Pool).
    // 활성이든 대기든 전부 DroppedItemPool 아래 모은다 — 하이라키에 흩어지지 않는다.
    const string PooledName = "DroppedItem (Pooled)";
    static ObjectPool<DroppedItem> pool;
    static Transform poolRoot;

    /// <summary>
    /// 풀 — 루트가 없으면(첫 사용, 또는 씬 전환으로 파괴됨) 함께 새로 만든다.
    /// 씬을 넘어 살리지 않는 이유: 바닥 아이템은 그 씬의 물건이라 다음 씬까지 따라가면 안 된다.
    /// 그래서 루트의 생사가 곧 풀의 생사이고, 죽은 인스턴스 참조가 남을 일도 없다.
    /// </summary>
    static ObjectPool<DroppedItem> Pool
    {
        get
        {
            if (poolRoot != null) return pool;

            poolRoot = new GameObject("DroppedItemPool").transform;
            pool = new ObjectPool<DroppedItem>(
                createFunc: CreateInstance,
                actionOnGet: d => d.gameObject.SetActive(true),
                actionOnRelease: d => d.gameObject.SetActive(false),
                actionOnDestroy: d => { if (d != null) Destroy(d.gameObject); },
                collectionCheck: true,   // 같은 인스턴스를 두 번 반환하면 즉시 드러나게
                defaultCapacity: 20,
                maxSize: 200);
            return pool;
        }
    }

    static Transform PoolRoot()
    {
        _ = Pool;            // 루트 생성은 풀 초기화와 한 몸이다
        return poolRoot;
    }

    // 도메인 리로드를 끈 환경에서 static이 플레이를 넘어 살아남는 것 방지
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        pool = null;
        poolRoot = null;
    }

    Rigidbody body;

    // 프리팹이 비활성으로 저장돼 있으면 Awake가 늦게 오므로 첫 접근에 잡는다
    Rigidbody Body => body != null ? body : (body = GetComponent<Rigidbody>());

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
        DroppedItem dropped = Rent(position);
        if (dropped == null) return null;

        dropped.Setup(item, amount);
        if (dropped.Body != null) dropped.Body.AddForce(throwDirection * 3.5f, ForceMode.Impulse);
        return dropped;
    }

    /// <summary>풀에서 하나 꺼내(없으면 새로 만들어) 지정 위치에 세운다.</summary>
    static DroppedItem Rent(Vector3 position)
    {
        var d = Pool.Get();
        if (d == null) return null;

        // 지난번에 남의 부모(Spawned 루트 등)로 옮겨졌을 수 있다 — 다시 풀 아래로
        d.transform.SetParent(PoolRoot(), false);
        d.transform.SetPositionAndRotation(position, Quaternion.identity);
        d.ResetPhysics();
        return d;
    }

    static DroppedItem CreateInstance()
    {
        var db = ItemDatabaseSO.LoadDefault();
        var prefab = db != null ? db.droppedItemPrefab : null;
        var created = prefab != null ? Instantiate(prefab) : BuildFallback(Vector3.zero);
        created.name = PooledName;
        created.transform.SetParent(poolRoot, false);
        return created;
    }

    /// <summary>
    /// 풀로 돌려보낸다 — 파괴 대신. 남은 관성이 다음 사용에 새어 나가지 않게 물리도 멈춘다.
    /// 이미 반환된 인스턴스는 무시한다 — 중복 반환은 같은 것을 두 번 꺼내 쓰게 만든다
    /// (풀의 collectionCheck에도 걸리지만, 조용히 넘기는 편이 호출부에 안전하다).
    /// </summary>
    public void Release()
    {
        if (!gameObject.activeSelf) return;

        item = null;
        amount = 0;
        promptMessage = null;
        // 이름은 되돌린다 — WorldPopulator가 시작 아이템에 "StartItem_*"를 붙이는데,
        // 그대로 풀에 들어가면 다음 배치에서 스폰 마커로 오인된다.
        gameObject.name = PooledName;

        ResetPhysics();
        transform.SetParent(PoolRoot(), false);
        Pool.Release(this);   // 비활성화는 actionOnRelease가 한다
    }

    void ResetPhysics()
    {
        if (Body == null) return;
        Body.linearVelocity = Vector3.zero;
        Body.angularVelocity = Vector3.zero;
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
        if (amount + other.amount > item.maxStack) return;

        amount += other.amount;
        other.amount = 0;                                      // 상대의 후속 병합/줍기 차단
        other.Release();
        Setup(item, amount);                                   // 프롬프트("xN 줍기") 갱신
    }

    // 줍기 — 조준 후 E (유일한 줍기 경로)
    public override void OnInteract(PlayerController player)
    {
        if (item == null || amount <= 0) return;

        // 핫바 -> 가방 순으로 자동 적재[cite: 24]
        bool success = PlayerInventoryHolder.Instance != null &&
                    PlayerInventoryHolder.Instance.AddItemToPlayer(item, amount);

        if (success)
        {
            // Release가 amount·프롬프트를 즉시 비우고 오브젝트를 끈다 — 연타로 E가 한 번 더
            // 들어와도 위의 amount 가드에 걸려 같은 더미를 두 번 먹지 않는다.
            // 적재로 컨테이너가 Changed를 쏘면 HUD·장착은 스스로 따라온다.
            Release();
        }
        else
        {
            Debug.LogWarning("[가방 가득 참] 인벤토리에 빈 공간이 없습니다!");
        }
    }

    // 줍기는 E(OnInteract) 전용 — 플레이어 접촉 자동 줍기는 제거됨.
    // 트리거 센서는 스택 병합에만 쓴다.
    private void OnTriggerEnter(Collider other)
    {
        // 마크식 스택 병합 — 같은 아이템끼리 센서에 닿으면 하나로
        TryMergeWith(other.GetComponentInParent<DroppedItem>());
    }
}
