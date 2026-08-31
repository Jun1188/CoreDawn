using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 스키마 v2의 명시 표 — json "type" 문자열 → 정의 타입. 리플렉션이나 타입명을 json에 쓰지 않는다:
    /// 코드 이름을 바꿔도 데이터가 안 깨지고, 모드가 모르는 type을 쓰면 명확히 실패한다.
    /// 새 모듈은 여기 한 줄 + 정의 클래스 + 런타임 모듈이 전부다.
    /// </summary>
    public static class SimSchema
    {
        public const int Format = 2;

        public static readonly IReadOnlyDictionary<string, Type> EntityModules = new Dictionary<string, Type>
        {
            ["Health"] = typeof(HealthModuleDef),
            ["Effects"] = typeof(EffectsModuleDef),
            ["Building"] = typeof(BuildingModuleDef),
            ["Ports"] = typeof(PortsModuleDef),
            ["Inventory"] = typeof(InventoryModuleDef),
            ["Crafter"] = typeof(CrafterModuleDef),
            ["Conveyor"] = typeof(ConveyorModuleDef),
            ["Extractor"] = typeof(ExtractorModuleDef),
            ["Router"] = typeof(RouterModuleDef),
            ["Core"] = typeof(CoreModuleDef),
            ["NestSpawner"] = typeof(NestSpawnerModuleDef),
            ["ResourceDeposit"] = typeof(ResourceDepositModuleDef),
            ["Loot"] = typeof(LootModuleDef),
            ["Turret"] = typeof(TurretModuleDef),
            ["AmmoConsumer"] = typeof(AmmoConsumerModuleDef),
            ["FixedAmmo"] = typeof(FixedAmmoModuleDef),
            ["AuraEmitter"] = typeof(AuraEmitterModuleDef),
            ["Blocker"] = typeof(BlockerModuleDef),
            ["Trigger"] = typeof(TriggerModuleDef),
            ["DronePort"] = typeof(DronePortModuleDef),
            ["Movement"] = typeof(MovementModuleDef),
            ["Attack"] = typeof(AttackModuleDef),
            ["MonsterBrain"] = typeof(MonsterBrainModuleDef),
        };

        public static readonly IReadOnlyDictionary<string, Type> ItemModules = new Dictionary<string, Type>
        {
            ["Ammo"] = typeof(AmmoModuleDef),
            ["Weapon"] = typeof(WeaponModuleDef),
        };

        /// <summary>정의 타입 → "type" 문자열 (쓰기용 역표).</summary>
        public static string TypeNameOf(Type t)
        {
            foreach (var kv in EntityModules) if (kv.Value == t) return kv.Key;
            foreach (var kv in ItemModules) if (kv.Value == t) return kv.Key;
            return null;
        }

        /// <summary>로더·에디터가 공유하는 직렬화 설정 — 모르는 키는 오류(오타·옛 키를 조용히 삼키지 않는다).</summary>
        public static JsonSerializer CreateSerializer()
        {
            var s = new JsonSerializer
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include,
            };
            s.Converters.Add(new StringEnumConverter());
            s.Converters.Add(new ModuleDefConverter<EntityModuleDef>(EntityModules));
            s.Converters.Add(new ModuleDefConverter<ItemModuleDef>(ItemModules));
            return s;
        }
    }

    /// <summary>"type" 키로 정의 타입을 고르는 컨버터. 읽기: 표에서 타입을 찾아 Populate. 쓰기: "type"을 앞에 붙여 낸다.</summary>
    public sealed class ModuleDefConverter<TBase> : JsonConverter where TBase : ModuleDef
    {
        readonly IReadOnlyDictionary<string, Type> table;
        // 쓰기용 — 이 컨버터가 없는 직렬화기(무한 재귀 방지)
        static readonly JsonSerializer plain = new JsonSerializer { NullValueHandling = NullValueHandling.Ignore, Converters = { new StringEnumConverter() } };

        public ModuleDefConverter(IReadOnlyDictionary<string, Type> table) => this.table = table;

        public override bool CanConvert(Type objectType) => typeof(TBase).IsAssignableFrom(objectType);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var obj = JObject.Load(reader);
            var typeName = (string)obj["type"];
            if (string.IsNullOrEmpty(typeName))
                throw new JsonSerializationException($"모듈에 \"type\"이 없습니다: {obj.ToString(Formatting.None)}");
            if (!table.TryGetValue(typeName, out var type))
                throw new JsonSerializationException($"모르는 모듈 type \"{typeName}\" (허용: {string.Join(", ", table.Keys)})");
            obj.Remove("type");
            var def = (TBase)Activator.CreateInstance(type);
            using (var r = obj.CreateReader()) serializer.Populate(r, def);
            def.TypeName = typeName;
            return def;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var def = (ModuleDef)value;
            var obj = JObject.FromObject(value, plain);
            obj.AddFirst(new JProperty("type", def.TypeName ?? SimSchema.TypeNameOf(value.GetType())));
            obj.WriteTo(writer);
        }
    }
}
