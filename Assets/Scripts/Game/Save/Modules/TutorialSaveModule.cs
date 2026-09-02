using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CoreDawn.Tutorial;

namespace CoreDawn.Save
{
    /// <summary>
    /// 튜토리얼 진행도. SaveManager는 손대지 않는다 — 리플렉션이 이 클래스를 찾아 자동 등록한다.
    ///
    /// 완료한 스텝의 팩 id(coredawn:tutorial/…)만 저장한다. 순서·문구가 바뀌어도(스텝을 새로
    /// 끼워 넣어도) 이미 끝낸 것은 끝낸 채로 남고, 새 스텝만 안내된다.
    ///
    /// 관측 카운터(채굴량·설치 수)는 저장하지 않는다. 불러온 직후 세계 상태에서 다시 읽히므로
    /// 중복 저장이고, 기준점은 TutorialManager가 복원 후 새로 잡는다.
    /// </summary>
    public class TutorialSaveModule : ISaveModule
    {
        public string ModuleId => "tutorial";

        /// <summary>시간(0) 다음, 진행도(10) 앞. 코어 티어보다 먼저 서서 순서에 의존하지 않게 둔다.</summary>
        public int Order => 5;

        public class Dto
        {
            [JsonProperty("done")] public List<string> CompletedIds;
            [JsonProperty("skipped")] public bool Skipped;
        }

        public object Capture()
        {
            var t = TutorialManager.Instance;
            if (t == null) return null;   // 튜토리얼이 없는 씬 — 남의 데이터를 지우지 않는다

            return new Dto
            {
                CompletedIds = t.CaptureCompleted(),
                Skipped = t.Skipped,
            };
        }

        public void Restore(JToken data)
        {
            var t = TutorialManager.Instance;
            if (t == null) return;

            var dto = SaveJson.FromToken<Dto>(data);
            if (dto == null) return;

            t.RestoreProgress(dto.CompletedIds, dto.Skipped);
        }
    }
}
