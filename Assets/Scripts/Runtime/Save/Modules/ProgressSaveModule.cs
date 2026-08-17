using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 게임 수준 진행도 — 코어 티어와 게임오버 여부.
///
/// 티어는 건물·레시피 해금의 기준이라 공장을 복원하기 전에 제자리를 찾아야 한다(Order 10).
/// 코어의 최대 보호막처럼 티어에서 파생되는 값은 저장하지 않는다.
/// </summary>
public class ProgressSaveModule : ISaveModule
{
    public string ModuleId => "progress";
    public int Order => 10;

    public class Dto
    {
        [JsonProperty("coreTier")] public int CoreTier;
        [JsonProperty("gameOver")] public bool IsGameOver;
    }

    public object Capture()
    {
        if (GameManager.Instance == null) return null;

        return new Dto
        {
            CoreTier = GameManager.Instance.UnlockedTier,
            IsGameOver = BattleManager.Instance != null && BattleManager.Instance.IsGameOver,
        };
    }

    public void Restore(JToken data)
    {
        var dto = SaveJson.FromToken<Dto>(data);
        if (dto == null) return;

        if (GameManager.Instance != null) GameManager.Instance.RestoreTier(dto.CoreTier);
        if (BattleManager.Instance != null) BattleManager.Instance.RestoreGameOver(dto.IsGameOver);
    }
}
