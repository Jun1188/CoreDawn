using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// SCR-02 전투 HUD — 4단계 이관.
///
/// 팝업이 아니다 — 입력 맵을 Push하지 않고 커서도 풀지 않는다 (배치 HUD와 같은 이유).
/// UIDocument만 들고 매 프레임 값을 읽는다: 시간·나침반·조준·탄약은 어차피
/// 매 프레임 변하는 값이고, 체력·핫바는 이벤트로 받는다.
///
/// 이 컴포넌트가 켜지면 같은 정보를 그리던 uGUI HUD(시간 박스·체력 바·핫바·크로스헤어)를
/// 숨긴다 — 같은 값이 두 번 그려지면 어느 쪽이 진실인지 흔들린다. 문서 SCR-05가 미뤄둔
/// "DAY/NIGHT 스탯 통합"이 여기다.
///
/// 뺀 것: 웨이브 배지·남은 규모 — 웨이브 시스템이 아직 없다.
/// 적 수는 실데이터(BattleManager.Spawner.AliveCount)가 있는 씬에서만 보인다.
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(UIDocument))]
public class GameplayHUDView : MonoBehaviour
{
    [SerializeField] UIDocument document;

    VisualElement root, compass, crosshair, deathOverlay, coreLine, ammoBox, hotbarRow, enemyLine;
    VisualElement hpFill, coreFill;
    Label dayValue, phaseLabel, phaseTime, hpNow, hpMax, corePct, ammoName, ammoNow, ammoCap, enemyCount;
    Label ammoType, ammoReserve;

    PlayerController player;
    Entity playerEntity;
    BuildingEntity core;
    ItemContainer hotbar;
    GameObject legacyCrosshair;   // uGUI InventoryPopup이 닫힐 때마다 도로 켜서, 살아 있는 동안 계속 눌러야 한다

    // ── 나침반 — 15°마다 눈금 하나, 45°마다 방위 라벨. 풀을 만들어 두고 매 프레임 배치만 ──
    const int TickCount = 24;                 // 360 / 15
    readonly VisualElement[] ticks = new VisualElement[TickCount];
    readonly Label[] tickLabels = new Label[TickCount];
    readonly List<TriangleGlyph> markPool = new();
    static readonly string[] Cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    Camera cam;

    int shownHotbarIndex = -1;

    void Awake()
    {
        if (document == null) document = GetComponent<UIDocument>();
    }

    void OnEnable()
    {
        var r = document != null ? document.rootVisualElement : null;
        if (r == null)
        {
            Debug.LogError("[GameplayHUDView] UIDocument.rootVisualElement가 null입니다. " +
                           "Source Asset(GameplayHUD.uxml)과 Panel Settings를 확인하세요.", this);
            enabled = false;
            return;
        }

        root       = r.Q("hud");
        compass    = r.Q("compass");
        crosshair  = r.Q("crosshair");
        deathOverlay = r.Q("death-overlay");
        coreLine   = r.Q("core-line");
        ammoBox    = r.Q("ammo");
        hotbarRow  = r.Q("hotbar");
        enemyLine  = r.Q("enemy-line");
        hpFill     = r.Q("hp-fill");
        coreFill   = r.Q("core-fill");

        dayValue   = r.Q<Label>("day-value");
        phaseLabel = r.Q<Label>("phase-label");
        phaseTime  = r.Q<Label>("phase-time");
        hpNow      = r.Q<Label>("hp-now");
        hpMax      = r.Q<Label>("hp-max");
        corePct    = r.Q<Label>("core-pct");
        ammoName   = r.Q<Label>("ammo-name");
        ammoNow    = r.Q<Label>("ammo-now");
        ammoCap    = r.Q<Label>("ammo-cap");
        ammoType   = r.Q<Label>("ammo-type");
        ammoReserve = r.Q<Label>("ammo-reserve");
        enemyCount = r.Q<Label>("enemy-count");

        root.pickingMode = PickingMode.Ignore;   // HUD는 클릭을 먹으면 안 된다 — 월드로 통과

        BuildCompassScale();

        if (enemyLine != null && enemyLine.Q<MonsterGlyph>() == null)
        {
            var glyph = new MonsterGlyph();
            glyph.AddToClassList("combat-enemyline__glyph");
            enemyLine.Insert(0, glyph);
        }

        Show(deathOverlay, false);
        BindPlayerIfNeeded();

        hotbar = PlayerInventoryHolder.Instance != null ? PlayerInventoryHolder.Instance.HotbarContainer : null;
        if (hotbar != null) hotbar.Changed += RebuildHotbar;
        shownHotbarIndex = -1;
        RebuildHotbar();

        ShowLegacyHud(false);
    }

    void OnDisable()
    {
        if (playerEntity != null) playerEntity.OnHealthChanged -= OnPlayerHp;
        playerEntity = null;
        player = null;
        if (core != null) { core.OnHealthChanged -= OnCoreHp; core = null; }
        if (hotbar != null) hotbar.Changed -= RebuildHotbar;

        ShowLegacyHud(true);
    }

    void Update()
    {
        BindPlayerIfNeeded();
        UpdateTime();
        UpdateCompass();
        UpdateAmmo();
        UpdateEnemies();
        BindCoreIfNeeded();

        // 창이 열려 있으면 커서가 조작 중 — 그 지점은 조준이 아니다 (포트 흐름과 같은 규칙)
        Show(crosshair, !UIPopup.AnyOpen);

        // uGUI 크로스헤어는 InventoryPopup이 닫힐 때마다(첫 프레임의 CloseScreen 포함)
        // 도로 켠다 — HUD가 살아 있는 동안은 UITK 크로스헤어가 유일한 조준점이어야 한다
        if (legacyCrosshair != null && legacyCrosshair.activeSelf) legacyCrosshair.SetActive(false);

        // 핫바 선택 칸 — 변경 이벤트가 없어 스탬프 비교로만
        int active = HotbarController.Instance != null ? HotbarController.Instance.CurrentHotbarIndex : -1;
        if (active != shownHotbarIndex) RebuildHotbar();
    }

    // ───────────────────── 시간 ─────────────────────

    void UpdateTime()
    {
        var tm = TimeManager.Instance;
        if (tm == null || tm.Cycle == null)
        {
            dayValue.text = "—";
            phaseTime.text = "—";
            return;
        }

        dayValue.text = tm.DayNumber.ToString();

        bool night = tm.Phase == DayPhase.Night;
        phaseLabel.text = night ? "NIGHT ENDS" : "NIGHT IN";

        float rem = Mathf.Max(0f, tm.RemainingPhaseTime);
        phaseTime.text = $"{(int)(rem / 60f):00}:{(int)(rem % 60f):00}";
        // 밤에는 시계가 위협의 잔여 시간이다 — 괴수색으로
        phaseTime.style.color = night ? UIFlowColors.Of(ItemLine.Beast) : StyleKeyword.Null;
    }

    // ───────────────────── 나침반 ─────────────────────

    void BuildCompassScale()
    {
        if (compass == null) return;

        for (int i = 0; i < TickCount; i++)
        {
            if (ticks[i] != null) continue;

            bool cardinal = i % 3 == 0;   // 45°마다 (45 / 15 = 3)

            var tick = new VisualElement { pickingMode = PickingMode.Ignore };
            tick.AddToClassList("ui-compass__tick");
            tick.style.height = cardinal ? 12f : 7.5f;
            compass.Add(tick);
            ticks[i] = tick;

            var label = new Label(cardinal ? Cardinals[i / 3] : (i * 15).ToString())
                { pickingMode = PickingMode.Ignore };
            label.AddToClassList("ui-compass__label");
            if (cardinal) label.AddToClassList("ui-compass__label--card");
            compass.Add(label);
            tickLabels[i] = label;
        }
    }

    void UpdateCompass()
    {
        if (compass == null || player == null) return;

        float compW = compass.resolvedStyle.width;
        float hudW  = root.resolvedStyle.width;
        if (compW <= 0f || hudW <= 0f) return;

        // 띠가 화면과 같은 배율로 움직인다 — 눈금이 그 방위의 실제 월드 지점 위에 얹힌다.
        // 카메라 수직 FOV → 수평 반각의 tan. ADS로 줌하면 나침반도 함께 좁아진다 (의도)
        if (cam == null) cam = Camera.main;
        float vFov = cam != null ? cam.fieldOfView : 60f;
        float aspect = cam != null ? cam.aspect : 16f / 9f;
        float halfTan = Mathf.Tan(0.5f * vFov * Mathf.Deg2Rad) * aspect;

        // 요(yaw)는 플레이어 루트에 있다 — 카메라에는 피치만 얹힌다
        float yaw = player.transform.eulerAngles.y;

        for (int i = 0; i < TickCount; i++)
        {
            float x = StripOffset(Mathf.DeltaAngle(yaw, i * 15f), halfTan, hudW);
            PlaceOnStrip(ticks[i], x, compW);
            PlaceOnStrip(tickLabels[i], x, compW);
        }

        // ── 마커: 초록 = 코어 방향(길 잃음 방지), 붉음 = 살아있는 괴수 방향 ──
        int used = 0;

        if (core != null)
            PlaceMark(used++, StripOffset(BearingTo(core.transform.position), halfTan, hudW));

        var spawner = BattleManager.Instance != null ? BattleManager.Instance.Spawner : null;
        if (spawner != null)
        {
            const int MaxThreatMarks = 12;   // 그 이상은 뭉개져 방향을 못 읽는다
            foreach (var m in spawner.Monsters)
            {
                if (used >= 1 + MaxThreatMarks) break;
                if (m == null || m.IsDead) continue;
                PlaceMark(used++, StripOffset(BearingTo(m.transform.position), halfTan, hudW), threat: true);
            }
        }

        for (int i = used; i < markPool.Count; i++)
            markPool[i].style.display = DisplayStyle.None;
    }

    /// <summary>각도차 → 띠 중앙 기준 픽셀 오프셋 (화면 투영과 같은 tan 매핑). 뒤쪽(±90° 밖)은 NaN.</summary>
    static float StripOffset(float delta, float halfTan, float hudWidth)
    {
        if (Mathf.Abs(delta) >= 89f) return float.NaN;   // 뒤쪽 — tan이 뒤집히기 전에 자른다
        return Mathf.Tan(delta * Mathf.Deg2Rad) / halfTan * (hudWidth * 0.5f);
    }

    float BearingTo(Vector3 worldPos)
    {
        Vector3 d = worldPos - player.transform.position;
        float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        return Mathf.DeltaAngle(player.transform.eulerAngles.y, bearing);
    }

    void PlaceMark(int index, float xOffset, bool threat = false)
    {
        while (markPool.Count <= index)
        {
            var t = new TriangleGlyph();
            t.AddToClassList("ui-compass__mark");
            compass.Add(t);
            markPool.Add(t);
        }

        var mark = markPool[index];
        mark.RemoveFromClassList("combat-mark--core");
        mark.RemoveFromClassList("combat-mark--threat");
        mark.AddToClassList(threat ? "combat-mark--threat" : "combat-mark--core");
        PlaceOnStrip(mark, xOffset, compass.resolvedStyle.width);
    }

    /// <summary>띠 중앙 기준 픽셀 오프셋을 위치·투명도로 옮긴다. 범위 밖·뒤쪽(NaN)은 숨긴다.
    /// 원본의 mask-image(양끝 페이드)가 USS에 없어 투명도를 직접 준다.</summary>
    static void PlaceOnStrip(VisualElement e, float xOffset, float compassWidth)
    {
        if (e == null) return;

        float half = compassWidth * 0.5f;
        if (float.IsNaN(xOffset) || Mathf.Abs(xOffset) > half)
        {
            e.style.display = DisplayStyle.None;
            return;
        }

        e.style.display = DisplayStyle.Flex;
        e.style.left = half + xOffset;
        e.style.opacity = Mathf.Clamp01((1f - Mathf.Abs(xOffset) / half) / 0.24f);   // 양끝 12% 페이드
    }

    // ───────────────────── 체력 · 코어 ─────────────────────

    /// <summary>
    /// 플레이어 엔티티는 <b>BattleManager가 런타임에 부착</b>한다(씬 에셋에는 없다).
    /// HUD가 먼저 켜지면 OnEnable 시점엔 아직 컴포넌트가 없어, 한 번만 찾으면 체력이
    /// 영영 갱신되지 않는다 — 붙을 때까지 매 프레임 다시 찾는다(코어와 같은 규칙).
    /// </summary>
    void BindPlayerIfNeeded()
    {
        if (playerEntity != null) return;

        if (player == null)
            player = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        // PlayerController에는 호환용 Entity가 함께 있을 수 있으므로 실제 전투 Player를 명시한다.
        playerEntity = player != null ? player.GetComponent<Player>() : null;
        if (playerEntity == null) return;

        playerEntity.OnHealthChanged += OnPlayerHp;
        OnPlayerHp(playerEntity.Health.CurrentHealth, playerEntity.Health.MaxHealth);
    }

    void OnPlayerHp(float current, float max)
    {
        hpNow.text = Mathf.CeilToInt(current).ToString();
        hpMax.text = $"/ {Mathf.CeilToInt(max)}";
        Fill(hpFill, max > 0f ? current / max : 0f);
        Show(deathOverlay, current <= 0f);
    }
    public void RefreshPlayerHp()
    {
        if (playerEntity == null)
        {
            BindPlayerIfNeeded();
        }

        if (playerEntity == null)
            return;

        OnPlayerHp(
            playerEntity.Health.CurrentHealth,
            playerEntity.Health.MaxHealth
        );
    }
    public void HideDeathOverlay()
    {
        Show(deathOverlay, false);
    }
    /// <summary>
    /// 코어는 나중에 생길 수도, 부서질 수도 있다 — 붙을 때까지 매 프레임 다시 찾는다.
    /// <b>없다고 줄을 숨기지는 않는다</b>: 코어가 부서진 순간은 체력바가 사라질 때가 아니라
    /// 0%가 되어야 하는 때다. 사라지면 무슨 일이 일어났는지가 화면에서 지워진다.
    /// </summary>
    void BindCoreIfNeeded()
    {
        if (core != null) return;

        foreach (var e in BuildingEntity.All)
            if (e != null && e.IsCore) { core = e; break; }

        if (core == null) { OnCoreHp(0f, 1f); return; }

        core.OnHealthChanged += OnCoreHp;
        OnCoreHp(core.Health.CurrentHealth, core.Health.MaxHealth);
    }

    void OnCoreHp(float current, float max)
    {
        float t = max > 0f ? current / max : 0f;
        corePct.text = $"{Mathf.RoundToInt(t * 100f)}%";
        Fill(coreFill, t);
    }

    // ───────────────────── 탄약 ─────────────────────

    void UpdateAmmo()
    {
        var weapon = player != null && player.weaponManager != null ? player.weaponManager.CurrentWeapon : null;
        bool has = weapon != null && weapon.gunData != null;

        Show(ammoBox, has);
        if (!has) return;

        // 근접무기(무한 탄약)는 셀 탄이 없다 — 탄창 칸을 ∞로 접어 장전 수/최대치를 지운다.
        bool unlimited = weapon.gunData.unlimitedAmmo;

        ammoName.text = (weapon.gunData.displayName ?? "").ToUpperInvariant();
        ammoNow.text = unlimited ? "∞" : weapon.CurrentAmmo.ToString();
        ammoCap.text = unlimited ? "" : $" / {weapon.gunData.magSize}";
        ammoNow.EnableInClassList("combat-ammo__now--reloading", weapon.IsReloading);

        // 탄종 + 인벤토리 예비탄 (실소비). 인벤토리 없는 씬은 ReserveAmmo -1 = 무한 보급.
        var item = weapon.CurrentAmmoItem;
        ammoType.text = unlimited ? "근접" : item != null ? item.displayName : "";
        int reserve = weapon.ReserveAmmo;
        ammoReserve.text = unlimited ? ""              // 장전 수 칸이 이미 ∞다 — 두 번 말하지 않는다
                         : weapon.IsReloading ? "장전 중"
                         : reserve < 0 ? "∞"
                         : $"× {reserve}";
    }

    // ───────────────────── 적 수 ─────────────────────

    void UpdateEnemies()
    {
        var spawner = BattleManager.Instance != null ? BattleManager.Instance.Spawner : null;
        Show(enemyLine, spawner != null);
        if (spawner != null) enemyCount.text = spawner.AliveCount.ToString();
    }

    // ───────────────────── 핫바 ─────────────────────

    void RebuildHotbar()
    {
        if (hotbarRow == null || hotbar == null) return;

        hotbarRow.Clear();
        shownHotbarIndex = HotbarController.Instance != null ? HotbarController.Instance.CurrentHotbarIndex : -1;

        for (int i = 0; i < hotbar.SlotCount; i++)
        {
            var stack = hotbar.PeekAt(i);
            bool empty = stack == null || stack.item == null || stack.amount <= 0;

            var slot = new VisualElement { pickingMode = PickingMode.Ignore };
            slot.AddToClassList("ui-slot");
            if (empty) slot.AddToClassList("ui-slot--empty");
            if (i == shownHotbarIndex) slot.AddToClassList("ui-slot--active");
            if (i == hotbar.SlotCount - 1) slot.AddToClassList("ui-slot--last");

            var key = new Label((i + 1).ToString()) { pickingMode = PickingMode.Ignore };
            key.AddToClassList("ui-slot__key");
            slot.Add(key);

            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ui-slot__icon");
            if (!empty) UIItemIcon.Apply(icon, stack.item);
            slot.Add(icon);

            if (!empty)
            {
                var n = new Label(stack.amount.ToString()) { pickingMode = PickingMode.Ignore };
                n.AddToClassList("ui-slot__n");
                slot.Add(n);
            }

            hotbarRow.Add(slot);
        }
    }

    // ───────────────── uGUI HUD 숨김 — 같은 값이 두 번 그려지면 안 된다 ─────────────────

    void ShowLegacyHud(bool show)
    {
        var sys = FindFirstObjectByType<SystemUIManager>(FindObjectsInactive.Include);
        if (sys != null)
        {
            ToggleAncestor(sys.dayText != null ? sys.dayText.transform : null, "TimeHUD_Panel", show);
            ToggleAncestor(sys.healthSlider != null ? sys.healthSlider.transform : null, "PlayerHealth_Panel", show);
        }

        if (HotbarUI.Instance != null) HotbarUI.Instance.gameObject.SetActive(show);

        var pc = player != null ? player : FindFirstObjectByType<PlayerController>();
        legacyCrosshair = pc != null ? pc.crosshairUI : null;
        if (legacyCrosshair != null) legacyCrosshair.SetActive(show);
    }

    /// <summary>이름이 맞는 조상을 찾아 켜고 끈다. 못 찾으면 아무것도 안 한다 — 씬마다 구조가 다를 수 있다.</summary>
    static void ToggleAncestor(Transform from, string name, bool active)
    {
        for (var t = from; t != null; t = t.parent)
            if (t.name == name) { t.gameObject.SetActive(active); return; }
    }

    // ───────────────────── 잡동사니 ─────────────────────

    static void Fill(VisualElement fill, float normalized)
    {
        if (fill != null) fill.style.width = Length.Percent(Mathf.Clamp01(normalized) * 100f);
    }

    static void Show(VisualElement e, bool on)
    {
        if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
