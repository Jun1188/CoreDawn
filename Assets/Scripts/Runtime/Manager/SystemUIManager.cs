using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class SystemUIManager : MonoBehaviour, IInputReceiver
{
    public static SystemUIManager Instance { get; private set; }

    [Header("=== ESC Pause Menu UI ===")]
    public GameObject pausePanel;

    [Header("=== Day / Night HUD ===")]
    public TextMeshProUGUI dayText;          // "Day 1"
    public TextMeshProUGUI timeText;         // "02:15" 또는 "남은 시간 01:30"
    public Image timeIconImage;              // 해/달 아이콘
    public Image timeProgressFill;           // 낮/밤 진행도 게이지 (Image Type: Filled)
    public Sprite morningSprite;             // 낮 아이콘 (해)
    public Sprite nightSprite;               // 밤 아이콘 (달)

    [Header("=== Player Health HUD ===")]
    public Entity playerEntity;                 // 체력 UI 갱신용 (Entity 컴포넌트)
    public Slider healthSlider;              // 체력 슬라이더
    public TextMeshProUGUI healthText;       // "100 / 100"

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (pausePanel != null) pausePanel.SetActive(false);

        UpdateHUD();

        if (InputManager.Instance != null) InputManager.Instance.Register(this);
        else Debug.LogError("[SystemUIManager] 씬에 InputManager가 없습니다.", this);

        if (playerEntity != null)
        {
            // 1. 체력이 변할 때마다 UI 자동 갱신
            playerEntity.OnHealthChanged += (current, max) =>
            {
                if (Instance != null)
                    Instance.UpdateHealthUI(current, max);
            };

            // 2. 게임 시작 시 현재 체력으로 UI 첫 세팅
            if (Instance != null && playerEntity.Health != null)
            {
                Instance.UpdateHealthUI(playerEntity.Health.CurrentHealth, playerEntity.Health.MaxHealth);
            }
        }
    }

    private void Update()
    {
        // ⏰ 시간 및 진행도 실시간 갱신 (매 프레임)
        UpdateTimerUI();
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null) InputManager.Instance.Unregister(this);
    }

    // ====================================================================
    // 🎯 입력 파이프라인 (ESC 일시정지)
    // ====================================================================
    public int Priority => InputPriority.Fallback;
    public bool IsInputActive => isActiveAndEnabled;

    public bool OnInput(in InputEvent e)
    {
        if (e.Phase != InputActionPhase.Performed || e.Id != InputActionId.Cancel) return false;
        TogglePauseMenu();
        return true;
    }

    // ====================================================================
    // ⏰ [시간 UI] 날짜, 아이콘, 타이머 갱신
    // ====================================================================
    public void UpdateHUD()
    {
        if (TimeManager.Instance == null) return;

        bool isNight = TimeManager.Instance.Phase == DayPhase.Night;

        if (dayText != null) dayText.text = $"Day {TimeManager.Instance.DayNumber}";

        if (timeIconImage != null)
        {
            timeIconImage.sprite = isNight ? nightSprite : morningSprite;
        }
    }

    /// <summary>매 프레임 호출되어 남은 시간 텍스트와 게이지를 줄여주는 함수</summary>
    private void UpdateTimerUI()
    {
        if (TimeManager.Instance == null) return;

        float remainingTime = TimeManager.Instance.RemainingPhaseTime; 
        float progressNormalized = TimeManager.Instance.PhaseProgress; 

        // 1. 분:초 ("02:15") 형태로 표시
        if (timeText != null)
        {
            int minutes = Mathf.FloorToInt(remainingTime / 60f);
            int seconds = Mathf.FloorToInt(remainingTime % 60f);
            timeText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }

        // 2. 시간 게이지 갱신 (0.0 ~ 1.0)
        if (timeProgressFill != null)
        {
            timeProgressFill.fillAmount = progressNormalized;
        }
    }

    // ====================================================================
    // ❤️ [체력 UI] 플레이어 체력 변경 시 외부/플레이어 스크립트에서 호출
    // ====================================================================
    public void UpdateHealthUI(float currentHP, float maxHP)
    {
        if (healthSlider != null)
        {
            healthSlider.maxValue = maxHP;
            healthSlider.value = currentHP;
        }

        if (healthText != null)
        {
            healthText.text = $"{Mathf.CeilToInt(currentHP)} / {Mathf.CeilToInt(maxHP)}";
        }
    }

    /// <summary>
    /// ESC 일시정지 토글.
    ///
    /// 새 UITK 메뉴(PauseMenuView)가 씬에 있으면 그쪽을 쓰고, 없으면 기존 uGUI 패널로 넘어간다 —
    /// GameplayHUDView가 구 HUD를 대하는 방식과 같은 공존 규칙이다.
    ///
    /// 구 패널은 9개 씬에 각각 복사돼 있어 지우려면 씬 파일을 전부 건드려야 한다.
    /// 팀의 씬 병합 작업과 충돌하므로 지우지 않고 런타임에 숨기기만 한다.
    /// </summary>
    public void TogglePauseMenu()
    {
        if (PauseMenuView.ExistsInScene())
        {
            HideLegacyPausePanel();

            if (PauseMenuView.IsOpen) PauseMenuView.CloseIfOpen();
            else PauseMenuView.TryOpen();
            return;
        }

        if (pausePanel == null) return;
        pausePanel.SetActive(!pausePanel.activeSelf);
    }

    /// <summary>UITK 메뉴가 있으면 구 패널은 두 번 다시 뜨지 않게 한다.</summary>
    void HideLegacyPausePanel()
    {
        if (pausePanel != null && pausePanel.activeSelf) pausePanel.SetActive(false);
    }

    public void OnClickSave()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.SaveGame();
        Debug.Log($"[시스템] 데이터(Day {TimeManager.Instance.DayNumber}) 저장 완료!");
    }

    public void OnClickLoad()
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.LoadGame();
        Debug.Log($"[시스템] 데이터(Day {TimeManager.Instance.DayNumber}) 불러오기 완료!");
    }
}