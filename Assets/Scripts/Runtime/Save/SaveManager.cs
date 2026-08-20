using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 세이브/로드의 지휘자. 자기 밑에 어떤 모듈이 있는지는 모른다 —
/// <see cref="ISaveModule"/> 구현체를 리플렉션으로 찾아 등록할 뿐이다.
/// 그래서 새 시스템이 세이브에 참여해도 이 파일은 그대로다.
///
/// 씬에 배치하지 않는다. UIBootstrap과 같은 발상으로 첫 씬 로드 후 스스로 생겨나며
/// DontDestroyOnLoad로 씬 전환을 넘어 살아남는다 — 게임플레이 씬 파일을 건드리지 않기 위해서다.
/// </summary>
[DefaultExecutionOrder(-500)]
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    /// <summary>저장/로드가 끝날 때마다 발화 — 슬롯 목록 UI가 새로고침에 쓴다.</summary>
    public static event Action SlotsChanged;

    readonly List<ISaveModule> _modules = new();

    /// <summary>
    /// 마지막으로 읽어들인 세이브의 모듈 데이터.
    /// 이번 빌드가 모르는 모듈 키를 여기 담아뒀다가 다음 저장 때 그대로 되돌려 쓴다 —
    /// 모듈이 덜 붙은 브랜치에서 열었다 저장해도 남의 데이터를 지우지 않기 위해서다.
    /// </summary>
    Dictionary<string, JToken> _carriedModules = new();

    double _playtime;
    string _currentSlotId;
    int _autoSlotCursor;

    public double Playtime => _playtime;

    /// <summary>지금 플레이 중인 세션이 어느 슬롯에서 왔는가 (없으면 새 게임).</summary>
    public string CurrentSlotId => _currentSlotId;

    SaveSystemConfig Config => SaveSystemConfig.Load();

    // ── 부팅 ──────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("[SaveManager]");
        go.AddComponent<SaveManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        DiscoverModules();

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // 타이틀 화면에 머문 시간은 플레이타임이 아니다
        if (!IsTitleScene(SceneManager.GetActiveScene())) _playtime += Time.unscaledDeltaTime;
    }

    void OnApplicationQuit()
    {
        if (Config.autoSaveOnQuit && !IsTitleScene(SceneManager.GetActiveScene())) AutoSave();
    }

    /// <summary>
    /// ISaveModule 구현체를 전부 찾아 등록한다.
    /// 매개변수 없는 생성자만 있으면 되고, SaveManager에 이름을 적을 필요가 없다.
    /// </summary>
    void DiscoverModules()
    {
        _modules.Clear();

        foreach (var type in typeof(SaveManager).Assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(ISaveModule).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) == null) continue;

            try { Register((ISaveModule)Activator.CreateInstance(type)); }
            catch (Exception e) { Debug.LogError($"[Save] 모듈 '{type.Name}' 생성 실패: {e.Message}"); }
        }

        _modules.Sort((a, b) => a.Order.CompareTo(b.Order));
        Debug.Log($"[Save] 모듈 {_modules.Count}개 등록: {string.Join(", ", _modules.Select(m => m.ModuleId))}");
    }

    /// <summary>수동 등록 — 매개변수 없는 생성자를 쓸 수 없는 모듈용.</summary>
    public void Register(ISaveModule module)
    {
        if (module == null) return;
        if (_modules.Any(m => m.ModuleId == module.ModuleId))
        {
            Debug.LogError($"[Save] 모듈 id 중복: '{module.ModuleId}' — 나중 것은 무시됩니다.");
            return;
        }
        _modules.Add(module);
    }

    // ── 저장 ──────────────────────────────────────────────────────

    /// <summary>현재 상태를 슬롯에 저장한다.</summary>
    public bool Save(string slotId)
    {
        if (string.IsNullOrEmpty(slotId)) { Debug.LogError("[Save] 슬롯 id가 비었습니다."); return false; }

        var file = CaptureToFile(slotId);
        bool ok = SaveStorage.Write(slotId, file, Config.compress);
        if (ok)
        {
            _carriedModules = file.Modules;
            if (!SaveSystemConfig.IsAutoSlot(slotId)) _currentSlotId = slotId;
            Debug.Log($"[Save] 저장 완료 — 슬롯 '{slotId}' (Day {file.Meta.DayNumber}, {file.Meta.PlaytimeText})");
            SlotsChanged?.Invoke();
        }
        return ok;
    }

    /// <summary>
    /// 인게임 아침마다 GameManager가 부르는 자동 저장. 설정에서 꺼져 있으면 아무것도 하지 않는다.
    /// </summary>
    /// <returns>실제로 저장했으면 true.</returns>
    public bool AutoSaveOnDayStart()
    {
        if (!Config.autoSaveOnDayStart) return false;
        if (IsTitleScene(SceneManager.GetActiveScene())) return false;
        return AutoSave();
    }

    /// <summary>
    /// 밤이 시작될 때 GameManager가 부르는 자동 저장.
    ///
    /// 밤은 코어가 부서질 수 있는 유일한 시간이다. 여기서 한 번 저장해 두면 게임오버 화면의
    /// "마지막 지점에서 다시"가 항상 그날 밤의 시작으로 돌아간다 — 아침 저장만 있으면
    /// 밤을 통째로 다시 살아야 하고, 첫날 밤에 죽으면 돌아갈 곳이 아예 없다.
    /// </summary>
    /// <returns>실제로 저장했으면 true.</returns>
    public bool AutoSaveOnNightStart()
    {
        if (!Config.autoSaveOnNightStart) return false;
        if (IsTitleScene(SceneManager.GetActiveScene())) return false;
        return AutoSave();
    }

    /// <summary>자동 저장 슬롯을 순환하며 저장한다.</summary>
    public bool AutoSave()
    {
        // 여기 한 곳이면 아침·밤 자동 저장, 종료 시 저장, 타이틀 복귀 시 저장이 전부 덮인다
        if (ShouldSuppressAutoSave()) return false;

        int count = Mathf.Max(1, Config.autoSlotCount);
        string slotId = SaveSystemConfig.AutoSlotId(_autoSlotCursor % count);
        _autoSlotCursor = (_autoSlotCursor + 1) % count;
        return Save(slotId);
    }

    SaveFile CaptureToFile(string slotId)
    {
        var scene = SceneManager.GetActiveScene();

        // 모르는 모듈 키를 보존하기 위해 이전 데이터 위에 덮어쓴다
        var modules = new Dictionary<string, JToken>(_carriedModules);

        foreach (var m in _modules)
        {
            object captured;
            try { captured = m.Capture(); }
            catch (Exception e)
            {
                Debug.LogError($"[Save] 모듈 '{m.ModuleId}' Capture 실패 — 이전 값을 유지합니다: {e}");
                continue;
            }

            // null = 이 씬에 해당 시스템이 없음. 기존 값을 지우지 않고 그대로 둔다.
            if (captured == null) continue;
            modules[m.ModuleId] = SaveJson.ToToken(captured);
        }

        var cycle = TimeManager.Instance != null ? TimeManager.Instance.Cycle : null;

        return new SaveFile
        {
            SchemaVersion = SaveFile.CurrentSchemaVersion,
            Modules = modules,
            Meta = new SaveMeta
            {
                SlotId = slotId,
                ScenePath = scene.path,
                SceneName = scene.name,
                DayNumber = cycle?.DayNumber ?? 1,
                Phase = (cycle?.Phase ?? DayPhase.Day).ToString(),
                CoreTier = GameManager.Instance != null ? GameManager.Instance.UnlockedTier : 0,
                PlaytimeSeconds = _playtime,
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                AppVersion = Application.version,
            },
        };
    }

    // ── 로드 ──────────────────────────────────────────────────────

    /// <summary>슬롯을 읽어 해당 씬을 열고 상태를 복원한다.</summary>
    public bool Load(string slotId)
    {
        var file = SaveStorage.Read(slotId);
        if (file == null)
        {
            Debug.LogError($"[Save] 슬롯 '{slotId}' 를 읽지 못했습니다.");
            return false;
        }

        if (!SaveMigrations.TryMigrate(file, out string error))
        {
            Debug.LogError($"[Save] 슬롯 '{slotId}' 를 열 수 없습니다 — {error}");
            return false;
        }

        string scene = ResolveScene(file.Meta.ScenePath, file.Meta.SceneName);
        if (scene == null)
        {
            Debug.LogError($"[Save] 세이브가 가리키는 씬을 찾지 못했습니다: '{file.Meta.ScenePath}' — " +
                           "Build Settings에 등록됐는지 확인하세요.");
            return false;
        }

        _playtime = file.Meta.PlaytimeSeconds;
        _currentSlotId = SaveSystemConfig.IsAutoSlot(slotId) ? null : slotId;

        // 씬이 뜨기 전에 켜야 한다 — 각 시스템의 Awake/Start가 이 플래그를 보고 시딩을 건너뛴다
        SaveLoadContext.BeginRestore(file);
        ResetPersistentSingletons();

        Time.timeScale = 1f;   // 일시정지 메뉴에서 불러오는 경로 대비
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
        return true;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        if (SaveLoadContext.Pending == null) return;
        StartCoroutine(RestoreAfterSceneReady());
    }

    /// <summary>
    /// 씬의 Awake/Start가 전부 끝난 다음 프레임에 복원한다.
    /// sceneLoaded 시점에는 아직 Start가 돌지 않아 시스템들이 준비되지 않았다.
    /// </summary>
    IEnumerator RestoreAfterSceneReady()
    {
        yield return null;   // Start 완료
        yield return null;   // Start에서 만들어진 것들(코어 연결 등)까지 정착

        var file = SaveLoadContext.Pending;
        if (file == null) yield break;

        ApplyModules(file);

        _carriedModules = file.Modules;
        SaveLoadContext.Finish();
        Debug.Log($"[Save] 불러오기 완료 — Day {file.Meta.DayNumber}, {file.Meta.PlaytimeText}");
        SlotsChanged?.Invoke();
    }

    void ApplyModules(SaveFile file)
    {
        foreach (var m in _modules)   // Order 순으로 정렬돼 있다
        {
            if (!file.Modules.TryGetValue(m.ModuleId, out var data) || data == null) continue;

            try { m.Restore(data); }
            catch (Exception e)
            {
                // 한 모듈이 실패해도 나머지는 복원한다 — 전부 잃는 것보다 낫다
                Debug.LogError($"[Save] 모듈 '{m.ModuleId}' 복원 실패: {e}");
            }
        }
    }

    /// <summary>
    /// DontDestroyOnLoad 싱글턴은 씬을 다시 열어도 초기화되지 않는다.
    /// 이전 세션의 진행도가 새로 불러온 세이브에 섞이지 않게 여기서 직접 되돌린다.
    /// </summary>
    static void ResetPersistentSingletons()
    {
        if (GameManager.Instance != null) GameManager.Instance.ResetProgress();
    }

    // ── 새 게임 / 타이틀 복귀 ─────────────────────────────────────

    public bool NewGame()
    {
        string scene = ResolveScene(Config.newGameScenePath, SceneNameOf(Config.newGameScenePath));
        if (scene == null)
        {
            Debug.LogError($"[Save] New Game 씬을 찾지 못했습니다: '{Config.newGameScenePath}'");
            return false;
        }

        _playtime = 0;
        _currentSlotId = null;
        _carriedModules = new Dictionary<string, JToken>();
        SaveLoadContext.Finish();       // 복원 아님 — 시딩이 정상 동작해야 한다
        ResetPersistentSingletons();

        Time.timeScale = 1f;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
        return true;
    }

    /// <summary>저장하고 타이틀로 돌아간다. saveFirst=false면 저장 없이 나간다.</summary>
    public bool ReturnToTitle(bool saveFirst = true)
    {
        if (saveFirst && Config.autoSaveOnQuit && !ShouldSuppressAutoSave()
            && !IsTitleScene(SceneManager.GetActiveScene()))
        {
            if (!string.IsNullOrEmpty(_currentSlotId)) Save(_currentSlotId);
            else AutoSave();
        }

        string scene = ResolveScene(Config.titleScenePath, SceneNameOf(Config.titleScenePath));
        if (scene == null)
        {
            Debug.LogError($"[Save] 타이틀 씬을 찾지 못했습니다: '{Config.titleScenePath}'");
            return false;
        }

        SaveLoadContext.Finish();
        ResetPersistentSingletons();

        Time.timeScale = 1f;
        SceneManager.LoadScene(scene, LoadSceneMode.Single);
        return true;
    }

    // ── 슬롯 조회 (UI용) ──────────────────────────────────────────

    public IEnumerable<(string slotId, SaveMeta meta)> ManualSlots()
    {
        for (int i = 0; i < Config.manualSlotCount; i++)
        {
            string id = SaveSystemConfig.ManualSlotId(i);
            yield return (id, SaveStorage.ReadMeta(id));
        }
    }

    public IEnumerable<(string slotId, SaveMeta meta)> AutoSlots()
    {
        for (int i = 0; i < Config.autoSlotCount; i++)
        {
            string id = SaveSystemConfig.AutoSlotId(i);
            yield return (id, SaveStorage.ReadMeta(id));
        }
    }

    /// <summary>수동·자동 슬롯 전체.</summary>
    public IEnumerable<(string slotId, SaveMeta meta)> AllSlots()
        => ManualSlots().Concat(AutoSlots());

    /// <summary>가장 최근에 저장된 슬롯을 연다. 세이브가 하나도 없으면 false.</summary>
    public bool LoadMostRecent()
    {
        string best = null;
        DateTime bestTime = DateTime.MinValue;

        foreach (var (slotId, meta) in AllSlots())
        {
            if (meta == null || meta.IsEmpty) continue;
            if (!DateTime.TryParse(meta.SavedAtUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var t)) continue;
            if (t <= bestTime) continue;

            bestTime = t;
            best = slotId;
        }

        return best != null && Load(best);
    }

    /// <summary>
    /// 가장 최근에 저장된 슬롯의 요약. 세이브가 하나도 없으면 null.
    /// "이어하기"·게임오버 화면이 어느 지점으로 돌아가는지 미리 보여주는 데 쓴다 —
    /// <see cref="LoadMostRecent"/>가 실제로 여는 그 슬롯과 같은 기준으로 고른다.
    /// </summary>
    public SaveMeta LatestMeta()
    {
        SaveMeta best = null;
        DateTime bestTime = DateTime.MinValue;

        foreach (var (_, meta) in AllSlots())
        {
            if (meta == null || meta.IsEmpty) continue;
            if (!DateTime.TryParse(meta.SavedAtUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var t)) continue;
            if (t <= bestTime) continue;

            bestTime = t;
            best = meta;
        }
        return best;
    }

    public bool DeleteSlot(string slotId)
    {
        bool ok = SaveStorage.Delete(slotId);
        if (ok) SlotsChanged?.Invoke();
        return ok;
    }

    // ── 씬 이름 해석 ──────────────────────────────────────────────

    static string SceneNameOf(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        int slash = path.LastIndexOf('/');
        string file = slash >= 0 ? path[(slash + 1)..] : path;
        return file.EndsWith(".unity") ? file[..^6] : file;
    }

    /// <summary>경로 우선, 없으면 이름으로 폴백. 둘 다 빌드에 없으면 null.</summary>
    static string ResolveScene(string path, string name)
    {
        if (!string.IsNullOrEmpty(path) && Application.CanStreamedLevelBeLoaded(path)) return path;
        if (!string.IsNullOrEmpty(name) && Application.CanStreamedLevelBeLoaded(name)) return name;
        return null;
    }

    bool IsTitleScene(Scene scene)
        => scene.path == Config.titleScenePath || scene.name == SceneNameOf(Config.titleScenePath);

    /// <summary>
    /// 지금 상태를 자동으로 저장하면 안 되는가.
    ///
    /// 게임오버 상태를 자동 저장에 남기면 타이틀의 "이어하기"가 죽은 세계로 들어간다 —
    /// 플레이어가 명시적으로 고른 것도 아닌데 되돌릴 수 없는 상태가 기록으로 굳는다.
    /// 복원 중(IsRestoring)에는 아직 절반만 복원된 세계라 저장할 것이 없다.
    /// </summary>
    bool ShouldSuppressAutoSave()
    {
        if (SaveLoadContext.IsRestoring) return true;
        return BattleManager.Instance != null && BattleManager.Instance.IsGameOver;
    }

    // ── 디버그 ────────────────────────────────────────────────────

    /// <summary>
    /// 왕복 검증 — 지금 상태를 캡처(A)하고, 그것을 그대로 복원한 뒤 다시 캡처(B)해서 A와 B를 비교한다.
    /// 두 결과가 같아야 정상이며, 다르다면 그 차이가 곧 "복원되지 않은 필드"다.
    /// 씬 재로드 없이 제자리에서 돌기 때문에 플레이 중 아무 때나 호출할 수 있다.
    /// </summary>
    public bool DebugRoundTripDiff()
    {
        var before = CaptureToFile("__roundtrip");

        bool wasRestoring = SaveLoadContext.IsRestoring;
        SaveLoadContext.BeginRestore(before);
        try { ApplyModules(before); }
        finally { if (!wasRestoring) SaveLoadContext.Finish(); }

        var after = CaptureToFile("__roundtrip");

        int mismatches = 0;
        foreach (var key in before.Modules.Keys.Union(after.Modules.Keys))
        {
            before.Modules.TryGetValue(key, out var a);
            after.Modules.TryGetValue(key, out var b);

            if (JToken.DeepEquals(a, b)) continue;

            mismatches++;
            Debug.LogError($"[Save][왕복] 모듈 '{key}' 불일치\n" +
                           $"── 저장 전 ──\n{Truncate(a)}\n── 복원 후 ──\n{Truncate(b)}");
        }

        if (mismatches == 0) Debug.Log($"[Save][왕복] 통과 — 모듈 {before.Modules.Count}개 전부 일치");
        else Debug.LogError($"[Save][왕복] 실패 — 모듈 {mismatches}개 불일치");

        return mismatches == 0;
    }

    static string Truncate(JToken t, int max = 2000)
    {
        string s = t?.ToString() ?? "(없음)";
        return s.Length <= max ? s : s[..max] + $"\n… (총 {s.Length}자)";
    }

    /// <summary>세이브 폴더 경로 — 콘솔에서 파일을 직접 열어볼 때.</summary>
    public static string SaveFolderPath => SaveStorage.RootDir;
}
