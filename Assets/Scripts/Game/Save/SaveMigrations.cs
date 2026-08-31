using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Sim;

namespace CoreDawn.Save
{
    /// <summary>
    /// 구버전 세이브를 현재 스키마로 끌어올린다.
    ///
    /// 개발 중 데이터 구조가 바뀌는 것은 정상이다. 이 장치가 없으면 구조를 바꿀 때마다
    /// 팀원들이 갖고 있던 세이브가 전부 못 쓰게 되고, 결국 "세이브 지우고 다시 하세요"가 반복된다.
    ///
    /// 쓰는 법: <see cref="SaveFile.CurrentSchemaVersion"/>을 올리고, 여기 Steps에
    /// "이전 버전 → 다음 버전" 변환 하나를 추가한다. 변환은 JSON 트리를 직접 만지므로
    /// 옛 DTO 클래스를 남겨둘 필요가 없다.
    ///
    /// 읽는 쪽(DTO·SaveRefs)에는 옛 키·옛 id를 받아 주는 폴백을 두지 않는다 — 변환은 전부 여기서, 한 번, 소리 내어 한다.
    /// 변환할 수 없는 세이브는 조용히 반쯤 열리는 대신 로드가 실패한다(SaveManager가 오류를 남긴다).
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>key = 이 단계가 적용되는 버전. 실행하면 key+1 버전이 된다.</summary>
        static readonly Dictionary<int, Action<SaveFile>> Steps = new()
        {
            // v1 → v2 (2026-08-30, 5a-1c·5a-2d): 팀의 다른 작업이 시작되기 전이라 중간 버전(v2·v3)을 두지 않고 한 단계로 합쳤다.
            //   ① 정의 id — 옛 SO id("Item:IronOre"·"Building:Belt"·"Recipe:Recipe_IronPlate")를 팩 id("coredawn:item/iron_ore")로.
            //      몬스터(combat.monsters[].data)는 아직 SO(MonsterDatabaseSO)로 복원하므로 손대지 않는다 — 5a-3에서 함께 옮긴다.
            //   ② 그릇 — 건물 in/out, 플레이어 hotbar/main → 역할 키 사전 containers{}. 역할 이름표는 InventoryModule이 붙인다.
            //   ③ 핫바 병합 — 플레이어 containers.hotbar + containers.main → main 하나(핫바 칸이 앞, 옛 가방 칸은 인덱스가 핫바 칸 수만큼 뒤로).
            //   ④ 광맥 매장량 삭제 — world.nodes[]의 stock·nextAt을 뗀다(누적 채굴량만 남긴다).
            { 1, f => { MigrateIdsToPack(f); MigrateContainersToRoles(f); MergeHotbarIntoMain(f); DropDepositStock(f); DropLegacyWeaponAmmo(f); } },
        };

        /// <summary>
        /// 세이브를 현재 버전까지 순차 변환한다.
        /// 미래 버전(이 빌드보다 새 세이브)은 변환할 수 없으므로 false를 반환한다.
        /// </summary>
        public static bool TryMigrate(SaveFile file, out string error)
        {
            error = null;
            if (file == null) { error = "세이브가 비어 있습니다."; return false; }
            if (file.SchemaVersion > SaveFile.CurrentSchemaVersion)
            {
                error = $"이 세이브는 더 새로운 버전(v{file.SchemaVersion})입니다 — " +
                        $"현재 빌드는 v{SaveFile.CurrentSchemaVersion}까지 읽을 수 있습니다.";
                return false;
            }
            while (file.SchemaVersion < SaveFile.CurrentSchemaVersion)
            {
                if (!Steps.TryGetValue(file.SchemaVersion, out var step))
                {
                    error = $"v{file.SchemaVersion} → v{file.SchemaVersion + 1} 변환 단계가 없습니다.";
                    return false;
                }
                int from = file.SchemaVersion;
                try
                {
                    step(file);
                }
                catch (Exception e)
                {
                    error = $"v{from} → v{from + 1} 변환 중 오류: {e.Message}";
                    return false;
                }
                file.SchemaVersion++;
                Debug.Log($"[Save] 세이브 마이그레이션 v{from} → v{file.SchemaVersion}");
            }
            return true;
        }

        // ── ① 정의 id ────────────────────────────────────────────────

        // 세이브 안에서 정의 id가 사는 자리 — 모듈별 JSON 키. 여기 없는 자리는 변환되지 않으므로 새 id 자리를 만들면 여기도 적는다.
        //   factory.buildings[].id · .in/.out.slots[].item · .behavior.{recipe,craftingRecipe,target,passed[],filters[].items[]}
        //   factory.belts[].items[].item · player.{hotbar,main}.slots[].item · world.drops[].item
        static void MigrateIdsToPack(SaveFile f)
        {
            var db = SimHost.Database ?? throw new InvalidOperationException("팩 정의(SimHost.Database)가 없어 id를 변환할 수 없습니다.");
            int converted = 0;

            void Convert(JToken parent, string key)
            {
                if (parent is not JObject o || o[key]?.Type != JTokenType.String) return;
                string old = (string)o[key], now = db.LegacyId(old);
                if (now != old) { o[key] = now; converted++; }
            }
            void ConvertArray(JToken arr)
            {
                if (arr is not JArray a) return;
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Type != JTokenType.String) continue;
                    string old = (string)a[i], now = db.LegacyId(old);
                    if (now != old) { a[i] = now; converted++; }
                }
            }
            void ConvertContainer(JToken c)
            {
                if (c is JObject o) foreach (var s in Arr(o["slots"])) Convert(s, "item");
            }

            if (Module(f, "factory") is JObject factory)
            {
                foreach (var b in Arr(factory["buildings"]))
                {
                    Convert(b, "id");
                    ConvertContainer(b["in"]);
                    ConvertContainer(b["out"]);
                    if (b["behavior"] is JObject behavior)
                    {
                        Convert(behavior, "recipe");            // 조립기
                        Convert(behavior, "craftingRecipe");
                        Convert(behavior, "target");            // 채굴기
                        ConvertArray(behavior["passed"]);       // 분배기
                        foreach (var filter in Arr(behavior["filters"])) ConvertArray(filter["items"]);
                    }
                }
                foreach (var belt in Arr(factory["belts"]))
                    foreach (var it in Arr(belt["items"])) Convert(it, "item");
            }
            if (Module(f, "player") is JObject player)
            {
                ConvertContainer(player["hotbar"]);
                ConvertContainer(player["main"]);
            }
            if (Module(f, "world") is JObject world)
                foreach (var d in Arr(world["drops"])) Convert(d, "item");

            Debug.Log($"[Save] v1 → v2 ①: 정의 id {converted}개를 팩 id로 바꿨습니다.");
        }

        // ── ② 역할 키 그릇 ────────────────────────────────────────────

        static void MigrateContainersToRoles(SaveFile f)
        {
            int moved = 0;
            void Move(JToken owner, string oldKey, string role)
            {
                if (owner is not JObject o) return;
                var c = o[oldKey];
                if (c == null) return;
                o.Remove(oldKey);
                if (c.Type == JTokenType.Null) return;
                if (o["containers"] is not JObject bag) o["containers"] = bag = new JObject();
                bag[role] = c;
                moved++;
            }

            if (Module(f, "factory") is JObject factory)
                foreach (var b in Arr(factory["buildings"]))
                {
                    Move(b, "in",  InventoryModule.RoleInput);
                    Move(b, "out", InventoryModule.RoleOutput);
                }
            if (Module(f, "player") is JObject player)
            {
                Move(player, "hotbar", "hotbar");   // 임시 역할 — 바로 아래 ③이 main으로 합친다
                Move(player, "main",   InventoryModule.RoleMain);
            }

            Debug.Log($"[Save] v1 → v2 ②: 그릇 {moved}개를 역할 키(containers)로 옮겼습니다.");
        }

        // ── ③ 핫바 병합 ───────────────────────────────────────────────

        static void MergeHotbarIntoMain(SaveFile f)
        {
            if (Module(f, "player") is not JObject player || player["containers"] is not JObject bag) return;
            var hot = bag["hotbar"] as JObject;
            var main = bag["main"] as JObject;
            bag.Remove("hotbar");
            if (hot == null && main == null) return;

            int hotSlots = (int?)hot?["slotCount"] ?? 0;
            int mainSlots = (int?)main?["slotCount"] ?? 0;
            var merged = new JObject { ["slotCount"] = hotSlots + mainSlots };
            var slots = new JArray();
            int hotItems = 0, mainItems = 0;
            foreach (var s in Arr(hot?["slots"])) { slots.Add(s.DeepClone()); hotItems++; }   // 핫바 칸은 그대로 앞에
            foreach (var s in Arr(main?["slots"]))                                             // 옛 가방 칸은 핫바 칸 수만큼 뒤로
            {
                var c = (JObject)s.DeepClone();
                c["i"] = ((int?)c["i"] ?? 0) + hotSlots;
                slots.Add(c); mainItems++;
            }
            merged["slots"] = slots;
            bag[InventoryModule.RoleMain] = merged;

            Debug.Log($"[Save] v1 → v2 ③: 핫바 {hotSlots}칸({hotItems}종) + 가방 {mainSlots}칸({mainItems}종) → main {hotSlots + mainSlots}칸으로 합쳤습니다.");
        }

        // ── ④ 광맥 매장량 삭제 ───────────────────────────────────────

        static void DropDepositStock(SaveFile f)
        {
            int dropped = 0;
            if (Module(f, "world") is JObject world)
                foreach (var n in Arr(world["nodes"]))
                {
                    if (n is not JObject o) continue;
                    if (o.Remove("stock")) dropped++;
                    if (o.Remove("nextAt")) dropped++;
                }
            Debug.Log($"[Save] v1 → v2 ④: 광맥 매장량 키 {dropped}개를 뗐습니다(광맥은 바닥나지 않는다).");
        }

        // ── ⑤ 무기 탄창 — 인스펙터 배열 순서의 탄수 목록 삭제 ──────────────

        static void DropLegacyWeaponAmmo(SaveFile f)
        {
            if (Module(f, "player") is JObject player && player.Remove("ammo"))
                Debug.Log("[Save] v1 → v2 ⑤: 옛 무기 탄수(ammo — WeaponManager 배열 순서)를 뗐습니다. 탄창은 총 id로 다시 저장됩니다(weapons) — 옛 값은 순서 기반이라 옮길 수 없다.");
        }

        // ── 공통 ──────────────────────────────────────────

        static JToken Module(SaveFile f, string id) => f.Modules != null && f.Modules.TryGetValue(id, out var m) ? m : null;

        static IEnumerable<JToken> Arr(JToken t) => t is JArray a ? a : Array.Empty<JToken>();
    }
}
