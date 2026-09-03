using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using fNbt;
using Newtonsoft.Json;
using UnityEngine;
using CoreDawn.Save;

namespace CoreDawn.Tests
{
    /// <summary>
    /// 세이브 NBT 층 특성화 테스트 — 씬·플레이모드 없이 돈다.
    /// 검증 대상: DTO↔NBT 왕복(모든 필드 종류, null 생략, JsonIgnore, 빈 목록, 사전, 태그 통과) · 없는 태그는 초기값 ·
    /// 숫자 폭 관용 · JToken 다리(Sim ISaveableModule 용) · SaveStorage 파일 왕복(압축/비압축, 스키마 v6) · 옛 JSON 슬롯 거절.
    /// 실행: eval `CoreDawn.Tests.SaveNbtTests.RunAll(out var r)`.
    /// </summary>
    public static class SaveNbtTests
    {
        static readonly List<(string name, bool pass, string detail)> _results = new();
        static readonly List<string> _fails = new();
        const string Slot = "__nbt_test";

        enum Kind { A, B, C }

        class Child
        {
            [JsonProperty("n")] public string Name;
            public int Amount;
        }

        /// <summary>Sim 의 ISaveableModule 상태 꼴 — 스칼라·문자열·목록·중첩만(좌표 없음), 기본 Newtonsoft 로 읽는다.</summary>
        class BridgeDto
        {
            [JsonProperty("readyAt")] public float ReadyAt;
            [JsonProperty("crafting")] public bool Crafting;
            [JsonProperty("recipe")] public string RecipeId;
            [JsonProperty("next")] public int Next;
            [JsonProperty("passed")] public List<string> Passed = new();
            [JsonProperty("blocked")] public List<Kind> Blocked = new();
            [JsonProperty("filters")] public List<Child> Filters = new();
        }

        class Dto
        {
            [JsonProperty("i")] public int I = 1;
            public uint U;
            public long L;
            public float F;
            public double D;
            public bool B;
            public string S;
            public string Null;
            public Vector3 V3;
            public Vector2Int V2i;
            public Quaternion Q = Quaternion.identity;
            public Kind K = Kind.A;
            public List<string> Names = new();
            public List<Vector3> Pts = new();
            public List<Child> Kids = new();
            public Dictionary<string, Child> Map = new();
            public Dictionary<string, NbtCompound> Raw = new();
            public int[] Ints;
            public byte[] Bytes;
            public Child Nested;
            [JsonIgnore] public int Ignored = 7;
            public const int C = 3;
        }

        public static bool RunAll(out string report)
        {
            _results.Clear();
            Run("1. DTO 왕복 — 모든 필드 종류가 값 그대로 돌아온다", S1_RoundTrip);
            Run("2. null·JsonIgnore·const 는 쓰지 않고, 없는 태그는 초기값", S2_Omissions);
            Run("3. 숫자 폭 — 어느 숫자 태그든 필드 타입으로 맞춘다", S3_NumericWidth);
            Run("4. JToken 다리 — Sim 모듈 상태가 Newtonsoft 로 읽힌다", S4_JsonBridge);
            Run("5. 파일 왕복 — SaveStorage 압축/비압축, 스키마 v6, 모듈 보존", S5_File);
            Run("6. 옛 JSON 슬롯은 열리지 않는다", S6_LegacyRejected);
            Run("7. DeepEquals — 순서 무시, 값 차이 감지", S7_DeepEquals);

            int passed = _results.Count(r => r.pass);
            var sb = new System.Text.StringBuilder();
            foreach (var r in _results) sb.AppendLine($"  {(r.pass ? "PASS" : "FAIL")}  {r.name}{(r.pass ? "" : "\n" + r.detail)}");
            report = $"[SaveNbtTests] {passed}/{_results.Count} 통과\n" + sb;
            return passed == _results.Count;
        }

        static void Run(string name, Action scenario)
        {
            _fails.Clear();
            try { scenario(); }
            catch (Exception e) { _fails.Add("예외 발생:\n" + e); }
            finally { SaveStorage.Delete(Slot); }
            _results.Add((name, _fails.Count == 0, string.Join("\n", _fails)));
        }

        static void Expect(bool condition, string message) { if (!condition) _fails.Add(message); }

        static Dto Sample()
        {
            var d = new Dto
            {
                I = -42, U = 0xF0000001u, L = 1L << 40, F = 1.5f, D = Math.PI, B = true, S = "한글 ok",
                V3 = new Vector3(1, -2, 3.25f), V2i = new Vector2Int(7, -8), Q = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), K = Kind.C,
                Names = new List<string> { "a", "b" },
                Pts = new List<Vector3> { Vector3.one, Vector3.up },
                Kids = new List<Child> { new() { Name = "k1", Amount = 1 }, new() { Name = "k2", Amount = 2 } },
                Ints = new[] { 1, 2, 3 }, Bytes = new byte[] { 9, 8 },
                Nested = new Child { Name = "nest", Amount = 5 },
            };
            d.Map["x"] = new Child { Name = "mx", Amount = 10 };
            d.Raw["turret"] = new NbtCompound("turret") { new NbtFloat("readyAt", 12.5f), new NbtFloat("yaw", 90f) };
            return d;
        }

        static void S1_RoundTrip()
        {
            var a = Sample();
            var tag = SaveNbt.ToTag(a, "dto");
            var b = SaveNbt.FromTag<Dto>(tag);
            Expect(b != null, "역직렬화 null");
            if (b == null) return;
            Expect(b.I == a.I && b.U == a.U && b.L == a.L && b.F == a.F && b.D == a.D && b.B == a.B && b.S == a.S, $"스칼라 불일치: {b.I} {b.U} {b.L} {b.F} {b.D} {b.B} {b.S}");
            Expect(b.V3 == a.V3 && b.V2i == a.V2i && b.Q.x == a.Q.x && b.Q.y == a.Q.y && b.Q.z == a.Q.z && b.Q.w == a.Q.w, $"좌표 불일치: {b.V3} {b.V2i} {b.Q}");   // Quaternion == 는 내적 근사라 성분으로
            Expect(b.K == Kind.C, $"enum 불일치: {b.K}");
            Expect(b.Names.SequenceEqual(a.Names), "문자열 목록 불일치");
            Expect(b.Pts.SequenceEqual(a.Pts), "좌표 목록 불일치");
            Expect(b.Kids.Count == 2 && b.Kids[1].Name == "k2" && b.Kids[1].Amount == 2, "중첩 목록 불일치");
            Expect(b.Map.Count == 1 && b.Map["x"].Name == "mx" && b.Map["x"].Amount == 10, "사전 불일치");
            Expect(b.Raw.Count == 1 && b.Raw["turret"]["yaw"] is NbtFloat y && y.Value == 90f, "태그 통과 불일치");
            Expect(b.Ints.SequenceEqual(a.Ints) && b.Bytes.SequenceEqual(a.Bytes), "배열 불일치");
            Expect(b.Nested != null && b.Nested.Name == "nest" && b.Nested.Amount == 5, "중첩 객체 불일치");
            // 태그 꼴 — JSON 시절 규칙
            Expect(tag["i"] is NbtInt, "JsonProperty 이름 'i' 로 저장");
            Expect(tag["U"] is NbtLong ul && ul.Value == 0xF0000001L, "uint → Long(부호 없이)");
            Expect(tag["B"] is NbtByte bb && bb.Value == 1, "bool → Byte 1");
            Expect(tag["V3"] is NbtList vl && vl.ListType == NbtTagType.Float && vl.Count == 3, "Vector3 → Float×3");
            Expect(tag["V2i"] is NbtIntArray, "Vector2Int → IntArray");
            Expect(tag["K"] is NbtString ks && ks.Value == "C", "enum → 이름 문자열");
            Expect(tag["Map"] is NbtCompound, "사전 → Compound");
        }

        static void S2_Omissions()
        {
            var tag = SaveNbt.ToTag(new Dto { S = null, Names = null, Ints = null }, "dto");
            Expect(tag["Null"] == null && tag["S"] == null && tag["Names"] == null && tag["Ints"] == null, "null 필드는 태그 없음");
            Expect(tag["Ignored"] == null, "JsonIgnore 필드는 태그 없음");
            Expect(tag["C"] == null, "const 는 태그 없음");
            Expect(tag["Kids"] is NbtList kl && kl.Count == 0 && kl.ListType == NbtTagType.Compound, "빈 목록은 원소 타입이 정해진 빈 List");
            Expect(tag["Pts"] is NbtList pl && pl.ListType == NbtTagType.List && tag["Names"] == null, "빈 좌표 목록은 List 타입");
            Expect(new fNbt.NbtFile(new NbtCompound("r") { (NbtTag)tag.Clone() }).SaveToBuffer(NbtCompression.None).Length > 0, "빈 목록이 있어도 파일로 써진다");
            var b = SaveNbt.FromTag<Dto>(new NbtCompound("dto") { new NbtString("S", "only") });
            Expect(b.I == 1 && b.K == Kind.A && b.Ignored == 7 && b.S == "only" && b.Names != null && b.Names.Count == 0, "없는 태그는 초기값 유지");
            Expect(SaveNbt.FromTag<Dto>(null) == null, "null 태그 → null");
            var unknown = new NbtCompound("dto") { new NbtInt("i", 3), new NbtString("noSuchField", "x") };
            Expect(SaveNbt.FromTag<Dto>(unknown).I == 3, "모르는 태그는 무시");
        }

        static void S3_NumericWidth()
        {
            var tag = new NbtCompound("dto")
            {
                new NbtInt("F", 5), new NbtDouble("i", 7.9), new NbtByte("B", 1), new NbtLong("U", unchecked((long)0xF0000001u)),
                new NbtFloat("D", 2.5f), new NbtInt("K", 1),
            };
            var b = SaveNbt.FromTag<Dto>(tag);
            Expect(b.F == 5f, $"Int → float: {b.F}");
            Expect(b.I == 7, $"Double → int(절사): {b.I}");
            Expect(b.B, "Byte 1 → true");
            Expect(b.U == 0xF0000001u, $"Long → uint: {b.U}");
            Expect(b.D == 2.5, $"Float → double: {b.D}");
            Expect(b.K == Kind.B, $"정수 → enum: {b.K}");
        }

        static void S4_JsonBridge()
        {
            var src = new BridgeDto { ReadyAt = 12.5f, Crafting = true, RecipeId = "coredawn:recipe/iron_plate", Next = 2,
                Passed = new List<string> { "a", "b" }, Blocked = new List<Kind> { Kind.B, Kind.C },
                Filters = new List<Child> { new() { Name = "f1", Amount = 3 } } };
            var tag = SaveNbt.ToTag(src, "state");
            var j = SaveNbt.ToJson(tag);
            Expect(j is Newtonsoft.Json.Linq.JObject, "compound → JObject");
            var back = j.ToObject<BridgeDto>();   // Sim 모듈과 같은 경로: state?.ToObject<SaveState>()
            Expect(back.ReadyAt == 12.5f && back.Crafting && back.RecipeId == src.RecipeId && back.Next == 2, $"스칼라: {back.ReadyAt} {back.Crafting} {back.RecipeId} {back.Next}");
            Expect(back.Passed.SequenceEqual(src.Passed), "문자열 목록");
            Expect(back.Blocked.SequenceEqual(src.Blocked), "enum 목록(이름 문자열 → enum)");
            Expect(back.Filters.Count == 1 && back.Filters[0].Name == "f1" && back.Filters[0].Amount == 3, "중첩 목록");
            var empty = SaveNbt.ToJson(SaveNbt.ToTag(new BridgeDto(), "state")).ToObject<BridgeDto>();
            Expect(empty.Passed.Count == 0 && !empty.Crafting && empty.RecipeId == null, "빈 상태도 읽힌다");
            Expect(SaveNbt.ToJson(new NbtIntArray("a", new[] { 1, 2 })).ToObject<int[]>().SequenceEqual(new[] { 1, 2 }), "IntArray → JArray");
        }

        static void S5_File()
        {
            foreach (bool compress in new[] { true, false })
            {
                var file = new SaveFile
                {
                    Meta = new SaveMeta { SlotId = Slot, ScenePath = "Assets/Scenes/World.unity", SceneName = "World", DayNumber = 3, Phase = "Night", CoreTier = 2, PlaytimeSeconds = 123.5, SavedAtUtc = "2026-09-03T00:00:00Z", AppVersion = "t" },
                };
                file.Modules["factory"] = SaveNbt.ToTag(Sample());
                file.Modules["time"] = new NbtCompound { new NbtInt("day", 3) };
                Expect(SaveStorage.Write(Slot, file, compress), $"쓰기 실패 (compress={compress})");
                var read = SaveStorage.Read(Slot);
                Expect(read != null, $"읽기 실패 (compress={compress})");
                if (read == null) continue;
                Expect(read.SchemaVersion == SaveFile.CurrentSchemaVersion && read.SchemaVersion == 6, $"스키마 v6: {read.SchemaVersion}");
                Expect(read.Meta.DayNumber == 3 && read.Meta.Phase == "Night" && read.Meta.PlaytimeSeconds == 123.5 && read.Meta.SceneName == "World", "meta 왕복");
                Expect(read.Modules.Count == 2 && read.Modules.ContainsKey("factory") && read.Modules.ContainsKey("time"), "모듈 키 보존");
                Expect(SaveNbt.DeepEquals(read.Modules["factory"], file.Modules["factory"]), "factory 모듈 값 보존");
                Expect(SaveNbt.DeepEquals(read.Modules["time"], file.Modules["time"]), "time 모듈 값 보존");
                Expect(SaveStorage.ReadMeta(Slot)?.DayNumber == 3, "meta.json 요약");
                // 다시 쓰면 백업이 생긴다
                Expect(SaveStorage.Write(Slot, file, compress), "두 번째 쓰기");
                string dir = SaveStorage.SlotDir(Slot);
                Expect(File.Exists(Path.Combine(dir, compress ? "save.bak.nbt.gz" : "save.bak.nbt")), "백업 파일");
                Expect(!File.Exists(Path.Combine(dir, "save.tmp")), "임시 파일 정리");
                Expect(SaveMigrations.TryMigrate(read, out var err), $"v6 마이그레이션 통과: {err}");
                SaveStorage.Delete(Slot);
            }
        }

        static void S6_LegacyRejected()
        {
            string dir = SaveStorage.SlotDir(Slot);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "save.json.gz"), "{}");
            File.WriteAllText(Path.Combine(dir, "meta.json"), "{\"slotId\":\"" + Slot + "\",\"savedAtUtc\":\"2026-01-01T00:00:00Z\",\"sceneName\":\"World\"}");
            Expect(SaveStorage.Read(Slot) == null, "옛 JSON 본체는 null");
            var old = new SaveFile { SchemaVersion = 5 };
            Expect(!SaveMigrations.TryMigrate(old, out var err) && err.Contains("v5"), $"v5 는 거절: {err}");
            var future = new SaveFile { SchemaVersion = SaveFile.CurrentSchemaVersion + 1 };
            Expect(!SaveMigrations.TryMigrate(future, out _), "미래 버전은 거절");
        }

        static void S7_DeepEquals()
        {
            var a = new NbtCompound("a") { new NbtInt("x", 1), new NbtList("l", NbtTagType.Float) { new NbtFloat(1f), new NbtFloat(2f) } };
            var b = new NbtCompound("b") { new NbtList("l", NbtTagType.Float) { new NbtFloat(1f), new NbtFloat(2f) }, new NbtInt("x", 1) };
            Expect(SaveNbt.DeepEquals(a, b), "키 순서·이름 무시");
            ((NbtFloat)((NbtList)b["l"])[1]).Value = 3f;
            Expect(!SaveNbt.DeepEquals(a, b), "값 차이 감지");
            Expect(!SaveNbt.DeepEquals(a, new NbtCompound("c") { new NbtInt("x", 1) }), "키 수 차이 감지");
        }
    }
}
