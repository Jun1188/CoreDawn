using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 튜토리얼 완료 조건 한 개(팩 tutorial 섹션의 conditions[]). 판정 코드는 게임(Game/Tutorial/Conditions)에 있고
    /// 여기는 값뿐이다 — 조건 종류(<see cref="Type"/>)마다 실제로 읽는 필드가 다르다(누적형 count · 이동 seconds · 코어 tier · 분류 itemType · 특정 아이템 item).
    /// 안 쓰는 필드는 기본값으로 실려 온다(편집기가 전 필드를 쓴다).
    /// </summary>
    public sealed class TutorialConditionDef
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("count")] public int Count = 1;
        [JsonProperty("seconds")] public float Seconds = 2f;
        [JsonProperty("tier")] public int Tier = 1;
        [JsonProperty("itemType")] public string ItemType;
        [JsonProperty("item")] public string ItemId;

        [JsonIgnore] public ItemDef Item { get; private set; }

        public void Resolve(SimDatabase db, List<string> errors, string owner)
        {
            if (!string.IsNullOrEmpty(ItemId)) Item = db.ResolveItem(ItemId, errors, owner);
        }
    }

    /// <summary>
    /// 안내 카드 한 장 — 팩 tutorial 섹션 한 항목. 순서는 <see cref="Order"/>(동률이면 id 사전순).
    /// id(<c>coredawn:tutorial/…</c>)가 세이브의 완료 키다 — 세이브가 존재하는 키는 바꾸면 안 된다.
    /// 조건이 하나도 없는 스텝은 영영 끝나지 않는다(저작 중 상태).
    /// </summary>
    public sealed class TutorialStepDef : Def
    {
        [JsonProperty("displayName")] public string DisplayName;
        [JsonProperty("description")] public string Description;
        [JsonProperty("order")] public int Order;
        [JsonProperty("tag")] public string Tag = "GUIDE";
        [JsonProperty("body")] public string Body;
        [JsonProperty("keyHints")] public string[] KeyHints;
        [JsonProperty("minSeconds")] public float MinSeconds = 2.5f;
        [JsonProperty("requireInOrder")] public bool RequireInOrder;
        [JsonProperty("conditions")] public List<TutorialConditionDef> Conditions = new List<TutorialConditionDef>();

        public override void Resolve(SimDatabase db, List<string> errors)
        {
            foreach (var c in Conditions)
            {
                if (c == null) { errors.Add($"{Id}: conditions에 null 항목"); continue; }
                if (string.IsNullOrEmpty(c.Type)) errors.Add($"{Id}: 조건에 type이 없습니다");
                c.Resolve(db, errors, Id);
            }
        }
    }
}
