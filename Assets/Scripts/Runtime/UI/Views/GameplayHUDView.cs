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
    Label ammoType, ammoReserve, interactPrompt;
    HoldRing holdRing;

    PlayerController player;
    PlayerInteractionManager interaction;
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

    // ── 매 프레임 같은 값을 다시 그리지 않으려는 스탬프 ──
    // Label.text는 값이 같으면 무시하지만, 그 값을 만드는 ToString()·문자열 보간은 이미
    // 일어난 뒤다. 프로파일러에서 HUD가 GC 1위였던 것이 대부분 여기서 나왔다.
    int shownDay = int.MinValue;
    int shownPhaseValue = int.MinValue;   // 남은 초, 또는 수량 밤의 남은 적 수
    bool shownPhaseIsCount, shownNight;
    int shownEnemies = int.MinValue;

    Gun shownGun;
    int shownAmmo = int.MinValue, shownReserve = int.MinValue;
    bool shownReloading;

    // 나침반 마커가 지금 무슨 역할인지 (-1 미정 / 0 코어 / 1 위협) — markPool과 나란히
    readonly List<int> markRoles = new();

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
        interactPrompt = r.Q<Label>("interact-prompt");

        root.pickingMode = PickingMode.Ignore;   // HUD는 클릭을 먹으면 안 된다 — 월드로 통과

        // 홀드 진행 링 — 크로스헤어를 감싸는 자리. UXML에 두지 않고 코드로 붙이는 이유는
        // 배치 HUD(BuildModeHUDView)와 같다: 호(arc)는 Painter2D가 그리므로 어차피 코드가 만든다
        holdRing = new HoldRing();
        holdRing.AddToClassList("combat-holdring");
        holdRing.Accent = ManualHoldAccent;   // 첫 표시 전 기본값 — 주인에 따라 매 프레임 갱신된다
        root.Add(holdRing);
        Show(holdRing, false);

        BuildCompassScale();

        if (enemyLine != null && enemyLine.Q<MonsterGlyph>() == null)
        {
            var glyph = new MonsterGlyph();
            glyph.AddToClassList("combat-enemyline__glyph");
            enemyLine.Insert(0, glyph);
        }

        // 스탬프 초기화 — 다시 켜졌을 때 옛 값 때문에 첫 갱신을 건너뛰지 않게
        shownDay = shownPhaseValue = shownEnemies = int.MinValue;
        shownAmmo = shownReserve = int.MinValue;
        shownGun = null;
        shownReloading = false;
        shownNight = false;
        if (phaseTime != null) phaseTime.style.color = StyleKeyword.Null;

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
        UpdateInteractPrompt();
        UpdateCenterRing();

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
            shownDay = shownPhaseValue = int.MinValue;
            return;
        }

        if (shownDay != tm.DayNumber)
        {
            shownDay = tm.DayNumber;
            dayValue.text = shownDay.ToString();
        }

        bool night = tm.Phase == DayPhase.Night;
        bool quantityNight = tm.TryGetNightWaveStatus(out int remainingEnemies, out _);
        phaseLabel.text = quantityNight ? "ENEMIES LEFT" : night ? "NIGHT ENDS" : "NIGHT IN";

        // 시계는 1초에 한 번만 달라진다 — 그 초에만 문자열을 만든다
        int value = quantityNight ? remainingEnemies
                                  : Mathf.FloorToInt(Mathf.Max(0f, tm.RemainingPhaseTime));
        if (value != shownPhaseValue || quantityNight != shownPhaseIsCount)
        {
            shownPhaseValue = value;
            shownPhaseIsCount = quantityNight;
            phaseTime.text = quantityNight ? value.ToString()
                                           : $"{value / 60:00}:{value % 60:00}";
        }

        // 밤에는 시계가 위협의 잔여 시간이다 — 괴수색으로
        if (night != shownNight)
        {
            shownNight = night;
            phaseTime.style.color = night ? UIFlowColors.Of(ItemLine.Beast) : StyleKeyword.Null;
        }
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
            tick.style.left = 0f;      // 가로 이동은 translate가 맡는다 (PlaceOnStrip 참고)
            compass.Add(tick);
            ticks[i] = tick;

            var label = new Label(cardinal ? Cardinals[i / 3] : (i * 15).ToString())
                { pickingMode = PickingMode.Ignore };
            label.AddToClassList("ui-compass__label");
            if (cardinal) label.AddToClassList("ui-compass__label--card");
            label.style.left = 0f;
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
            PlaceOnStrip(ticks[i], x, compW, centered: false);
            PlaceOnStrip(tickLabels[i], x, compW, centered: true);
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
            t.style.left = 0f;   // 가로 이동은 translate가 맡는다
            compass.Add(t);
            markPool.Add(t);
            markRoles.Add(-1);
        }

        // 클래스 교체는 역할이 바뀔 때만 한다. 매 프레임 뗐다 붙이면 클래스 리스트 변경이
        // 서브트리 재스타일을 부르고, TriangleGlyph는 Painter2D로 그리는 요소라 메시까지
        // 다시 만든다 — 프로파일러의 RenderTreeManager·MeshGenerationDeferrer 할당이 이것이었다.
        int role = threat ? 1 : 0;
        var mark = markPool[index];
        if (markRoles[index] != role)
        {
            markRoles[index] = role;
            mark.EnableInClassList("combat-mark--threat", threat);
            mark.EnableInClassList("combat-mark--core", !threat);
        }

        PlaceOnStrip(mark, xOffset, compass.resolvedStyle.width, centered: true);
    }

    /// <summary>
    /// 띠 중앙 기준 픽셀 오프셋을 위치·투명도로 옮긴다. 범위 밖·뒤쪽(NaN)은 숨긴다.
    /// 원본의 mask-image(양끝 페이드)가 USS에 없어 투명도를 직접 준다.
    ///
    /// <b>left가 아니라 translate로 옮긴다</b>: left는 레이아웃 속성이라 매 프레임 쓰면
    /// 나침반이 프레임마다 다시 배치된다(눈금·라벨 48개 + 마커). translate는 트랜스폼만
    /// 건드려 배치를 건드리지 않는다. 대신 USS의 <c>translate: -50% 0</c>(라벨·마커의
    /// 중앙 정렬)을 덮으므로, <paramref name="centered"/>면 그 몫을 픽셀로 직접 뺀다.
    /// </summary>
    static void PlaceOnStrip(VisualElement e, float xOffset, float compassWidth, bool centered)
    {
        if (e == null) return;

        float half = compassWidth * 0.5f;
        if (float.IsNaN(xOffset) || Mathf.Abs(xOffset) > half)
        {
            e.style.display = DisplayStyle.None;
            return;
        }

        float x = half + xOffset;
        if (centered)
        {
            float w = e.resolvedStyle.width;   // 마지막 배치 결과 — 읽어도 배치를 부르지 않는다
            if (!float.IsNaN(w)) x -= w * 0.5f;
        }

        e.style.display = DisplayStyle.Flex;
        e.style.translate = new Translate(x, 0f);
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
        if (!has) { shownGun = null; return; }

        // 근접무기(무한 탄약)는 셀 탄이 없다 — 탄창 칸을 ∞로 접어 장전 수/최대치를 지운다.
        bool unlimited = weapon.gunData.unlimitedAmmo;

        // 무기에 딸린 값은 무기가 바뀔 때만 다시 만든다 (ToUpperInvariant가 매번 새 문자열이었다)
        if (shownGun != weapon)
        {
            shownGun = weapon;
            shownAmmo = shownReserve = int.MinValue;
            ammoName.text = (weapon.gunData.displayName ?? "").ToUpperInvariant();
            ammoCap.text = unlimited ? "" : $" / {weapon.gunData.magSize}";
        }

        int ammo = weapon.CurrentAmmo;
        if (shownAmmo != ammo)
        {
            shownAmmo = ammo;
            ammoNow.text = unlimited ? "∞" : ammo.ToString();
        }
        ammoNow.EnableInClassList("combat-ammo__now--reloading", weapon.IsReloading);

        // 탄종 + 인벤토리 예비탄 (실소비). 인벤토리 없는 씬은 ReserveAmmo -1 = 무한 보급.
        var item = weapon.CurrentAmmoItem;
        ammoType.text = unlimited ? "근접" : item != null ? item.displayName : "";

        int reserve = weapon.ReserveAmmo;
        if (shownReserve != reserve || shownReloading != weapon.IsReloading)
        {
            shownReserve = reserve;
            shownReloading = weapon.IsReloading;
            ammoReserve.text = unlimited ? ""              // 장전 수 칸이 이미 ∞다 — 두 번 말하지 않는다
                             : weapon.IsReloading ? "장전 중"
                             : reserve < 0 ? "∞"
                             : $"× {reserve}";
        }
    }

    // ───────────────────── 상호작용 프롬프트 ─────────────────────

    /// <summary>
    /// uGUI 프롬프트 텍스트가 사라진 이후 표시는 HUD 몫이다 — 판정(Current)은 그대로
    /// PlayerInteractionManager가 소유하고, 여기는 읽어서 그리기만 한다.
    /// 크로스헤어와 같은 규칙으로 창이 열려 있으면 숨긴다 — 그 지점은 조준이 아니다.
    /// </summary>
    void UpdateInteractPrompt()
    {
        if (interactPrompt == null) return;

        if (interaction == null)
        {
            interaction = FindFirstObjectByType<PlayerInteractionManager>(FindObjectsInactive.Include);
            if (interaction == null) { Show(interactPrompt, false); return; }
        }

        string prompt = interaction.Current?.Prompt;
        bool show = !string.IsNullOrEmpty(prompt) && !UIPopup.AnyOpen;
        Show(interactPrompt, show);
        if (show) interactPrompt.text = $"[E] {prompt}";
    }

    // ───────────────────── 크로스헤어 진행 링 ─────────────────────

    /// <summary>손 채굴처럼 누르고 있어야 하는 상호작용의 진행도. 캐는 일은 파괴가 아니라 산출이라 --out 색.</summary>
    static readonly Color ManualHoldAccent = UIFlowColors.Out;

    /// <summary>재장전 진행도. 탄이 밖에서 안으로 들어오는 일이라 --copper(In) 색 — 채굴과 방향이 반대다.</summary>
    static readonly Color ReloadAccent = UIFlowColors.In;

    /// <summary>
    /// 크로스헤어를 감싸는 진행 링. 지금 진행 중인 것이 있을 때만 뜬다.
    ///
    /// 배치 HUD의 철거 링과 달리 화면 아래 카드가 아니라 조준점에 있다. 손으로 캐는 동안에도,
    /// 장전이 끝나기를 기다리는 동안에도 눈은 화면 중앙에 붙어 있으니, 진행도가 구석에 있으면 보이지 않는다.
    ///
    /// 링은 하나뿐이라 주인을 하나만 고른다 — <b>손 채굴이 우선</b>이다. 그쪽은 플레이어가 손가락으로
    /// 직접 굴리고 있는 진행이라, 멈추면 곧바로 "왜 안 되지"가 되지만 장전은 놔둬도 알아서 끝난다.
    /// 색으로 어느 쪽인지 구분된다(채굴 주황 / 장전 청록).
    ///
    /// 링이 뜨는 동안에는 프롬프트를 아래로 밀어 둔다 — 기본 위치(중심에서 24px)가
    /// 링 반지름(34.5px) 안이라 글자와 호가 겹친다.
    /// </summary>
    void UpdateCenterRing()
    {
        if (holdRing == null) return;

        var hold = interaction != null ? interaction.HoldTarget : null;

        var weapon = player != null && player.weaponManager != null ? player.weaponManager.CurrentWeapon : null;
        bool reloading = hold == null && weapon != null && weapon.IsReloading;

        bool show = (hold != null || reloading) && !UIPopup.AnyOpen;
        Show(holdRing, show);
        ToggleClass(interactPrompt, "combat-interact--held", show);
        if (!show) return;

        if (hold != null)
        {
            holdRing.Accent   = ManualHoldAccent;
            holdRing.Progress = interaction.HoldProgress;
            holdRing.Text     = hold.HoldLabel ?? "";
        }
        else
        {
            holdRing.Accent   = ReloadAccent;
            holdRing.Progress = weapon.ReloadProgress;
            holdRing.Text     = "장전";
        }
    }

    static void ToggleClass(VisualElement e, string className, bool on)
    {
        if (e == null) return;
        if (on) e.AddToClassList(className);
        else e.RemoveFromClassList(className);
    }

    // ───────────────────── 적 수 ─────────────────────

    void UpdateEnemies()
    {
        var spawner = BattleManager.Instance != null ? BattleManager.Instance.Spawner : null;
        Show(enemyLine, spawner != null);
        if (spawner == null) return;

        int alive = spawner.AliveCount;
        if (shownEnemies == alive) return;
        shownEnemies = alive;
        enemyCount.text = alive.ToString();
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

            if (!empty && stack.amount > 1)   // 1개는 숫자 없이 아이콘만
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
        // 시간·체력 uGUI 패널은 SystemUIManager와 함께 삭제됐다 — 남은 잔재만 처리한다
        if (HotbarUI.Instance != null) HotbarUI.Instance.gameObject.SetActive(show);

        var pc = player != null ? player : FindFirstObjectByType<PlayerController>();
        legacyCrosshair = pc != null ? pc.crosshairUI : null;
        if (legacyCrosshair != null) legacyCrosshair.SetActive(show);
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
