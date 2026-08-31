using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoreDawn.Save
{
    /// <summary>
    /// 세이브 파일의 루트. 모듈 데이터는 이름표 붙은 JToken으로만 들고 있다 —
    /// SaveManager는 각 모듈이 안에 무엇을 넣었는지 알 필요가 없고,
    /// 모르는 모듈 키도 원본 그대로 보존했다가 다시 써준다.
    /// (팀원 A가 만든 세이브를 모듈이 덜 붙은 B 브랜치에서 열었다 저장해도 A의 데이터가 날아가지 않는다)
    /// </summary>
    public class SaveFile
    {
        /// <summary>현재 스키마 버전. 구조를 바꿀 때마다 올리고 <see cref="SaveMigrations"/>에 단계를 추가한다.</summary>
        // v1 최초 · v2 팩 id + 역할 키 그릇(containers) + 핫바 병합 — 단계는 SaveMigrations.Steps
        public const int CurrentSchemaVersion = 4;

        [JsonProperty("schemaVersion")]
        public int SchemaVersion = CurrentSchemaVersion;

        [JsonProperty("meta")]
        public SaveMeta Meta = new();

        [JsonProperty("modules")]
        public Dictionary<string, JToken> Modules = new();
    }

    /// <summary>
    /// 슬롯 목록 화면이 파일 본체를 열지 않고도 읽을 수 있게 따로 빼둔 요약.
    /// 같은 내용이 본체(<see cref="SaveFile.Meta"/>)에도 들어가므로 meta.json이 깨져도 복구된다.
    /// </summary>
    public class SaveMeta
    {
        /// <summary>저장 당시의 씬 경로 (예: "Assets/Scenes/Test/MainScene.unity"). 로드 시 이 씬을 연다.</summary>
        [JsonProperty("scenePath")]
        public string ScenePath = "";

        /// <summary>씬 이름만 — 경로가 바뀐 씬을 이름으로라도 찾기 위한 폴백.</summary>
        [JsonProperty("sceneName")]
        public string SceneName = "";

        [JsonProperty("slotId")]
        public string SlotId = "";

        [JsonProperty("dayNumber")]
        public int DayNumber = 1;

        [JsonProperty("phase")]
        public string Phase = "Day";

        [JsonProperty("coreTier")]
        public int CoreTier;

        [JsonProperty("playtimeSeconds")]
        public double PlaytimeSeconds;

        [JsonProperty("savedAtUtc")]
        public string SavedAtUtc = "";

        [JsonProperty("appVersion")]
        public string AppVersion = "";

        [JsonIgnore]
        public bool IsEmpty => string.IsNullOrEmpty(SavedAtUtc);

        /// <summary>슬롯 목록에 그대로 쓸 수 있는 현지 시각 문자열. 파싱 실패 시 원문 반환.</summary>
        [JsonIgnore]
        public string SavedAtLocalText =>
            DateTime.TryParse(SavedAtUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : SavedAtUtc;

        /// <summary>"02:41:07" 형태의 플레이타임.</summary>
        [JsonIgnore]
        public string PlaytimeText
        {
            get
            {
                var t = TimeSpan.FromSeconds(Math.Max(0, PlaytimeSeconds));
                return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
            }
        }
    }
}
