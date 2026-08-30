using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 정의의 공통 머리. <b>id는 json에 없다</b> — 로더가 위치(팩:섹션/키, 예 coredawn:item/iron_plate)에서 파생해 넣는다.
    /// 정의는 로드 뒤 불변이고 정의당 하나라, "같은 정의인가"는 참조 동일성으로 판정한다.
    /// </summary>
    public abstract class Def
    {
        [JsonIgnore] public string Id { get; internal set; }

        [JsonProperty("displayName")] public string DisplayName;
        [JsonProperty("description")] public string Description;

        /// <summary>로드 뒤 id 문자열을 정의 참조로 잇는다. 모르는 id는 errors에 적고 null로 둔다.</summary>
        public virtual void Resolve(SimDatabase db, List<string> errors) { }

        public override string ToString() => Id;
    }

    /// <summary>격자 크기·좌표 — json {"x":1,"y":1}. UnityEngine.Vector2Int는 직렬화 형태가 안정적이지 않아 심 정의는 이걸 쓴다.</summary>
    public struct Vec2i
    {
        [JsonProperty("x")] public int x;
        [JsonProperty("y")] public int y;
        public Vec2i(int x, int y) { this.x = x; this.y = y; }
        public override string ToString() => $"({x},{y})";
    }

    /// <summary>아이템 + 수량 — 레시피 입출력·건설 비용·드롭·티어 요구.</summary>
    public sealed class ItemAmount
    {
        [JsonProperty("item")] public string ItemId;
        [JsonProperty("amount")] public int Amount = 1;

        [JsonIgnore] public ItemDef Item { get; private set; }

        public ItemAmount() { }
        /// <summary>코드에서 만든(이미 해석된) 수량 — 테스트·런타임 조립용. json 로드는 기본 생성자 + Resolve.</summary>
        public ItemAmount(ItemDef item, int amount) { Item = item; ItemId = item?.Id; Amount = amount; }

        public void Resolve(SimDatabase db, List<string> errors, string owner) => Item = db.ResolveItem(ItemId, errors, owner);
        public override string ToString() => $"{ItemId} x{Amount}";
    }

    /// <summary>
    /// 효과 적용 한 건 — 무엇을(effect) 얼마나(value) 얼마 동안(duration). 빠진 값(0)은 정의 기본값을 쓴다.
    /// 공격·탄·오라·웨이브 버프의 내용은 이 목록이 전부다. 레벨 개념은 없다.
    /// </summary>
    public sealed class EffectUse
    {
        [JsonProperty("effect")] public string EffectId;
        [JsonProperty("value")] public float Value;
        [JsonProperty("duration")] public float Duration;
        [JsonProperty("tickInterval")] public float TickInterval;

        [JsonIgnore] public EffectSpec Spec { get; private set; }

        public void Resolve(SimDatabase db, List<string> errors, string owner) => Spec = db.ResolveEffect(EffectId, errors, owner);

        public Effect ToEffect() => new Effect(Spec, Value, Duration, TickInterval);

        public static Effect[] ToEffects(List<EffectUse> uses)
        {
            if (uses == null || uses.Count == 0) return System.Array.Empty<Effect>();
            var result = new List<Effect>(uses.Count);
            foreach (var u in uses)
                if (u.Spec != null) result.Add(u.ToEffect());
            return result.ToArray();
        }
    }
}
