using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using CoreDawn.Factory;
using CoreDawn.Placement;
using CoreDawn.Sim;

namespace CoreDawn.Save
{
    /// <summary>
    /// 공장 — 배치된 건물 전부와 컨베이어 위를 흐르는 아이템.
    ///
    /// 저장하지 않는 것: 건물 사이의 연결(BuildingGraph)과 좌표 색인(GridIndex).
    /// 둘 다 건물을 다시 놓으면 배치 과정에서 저절로 재구축되므로 적어둘 이유가 없고,
    /// 적어두면 오히려 실제 배치와 어긋날 여지만 생긴다.
    ///
    /// 복원은 "지우고 다시 짓기"가 아니라 "맞춰 넣기"다 — 이미 자리에 맞는 건물이 있으면 그대로 쓴다.
    /// 그래야 씬에 미리 놓여 있던 코어(CoreBootstrap이 심에 연결해 둔 것)의 껍데기를 잃지 않고,
    /// 같은 코드로 제자리 왕복 검증(DebugRoundTripDiff)도 돌릴 수 있다.
    /// </summary>
    public class FactorySaveModule : ISaveModule
    {
        public string ModuleId => "factory";
        public int Order => 20;

        // ── DTO ──────────────────────────────────────────────────────

        public class Dto
        {
            /// <summary>심 시계. 모든 타이머가 이 값 기준 절대 시각이라 함께 저장해야 한다.</summary>
            [JsonProperty("now")] public float Now;

            [JsonProperty("buildings")] public List<BuildingDto> Buildings = new();
            [JsonProperty("belts")] public List<BeltDto> Belts = new();
        }

        public class BuildingDto
        {
            [JsonProperty("id")] public string DataId;
            [JsonProperty("origin")] public Vector2Int Origin;
            [JsonProperty("rot")] public int RotationSteps;
            [JsonProperty("shape")] public BeltShape Shape;
            /// <summary>역할별 그릇(input·output…). 역할 이름은 InventoryModule이 붙인다 — v3부터(옛 in/out은 SaveMigrations가 옮긴다).</summary>
            [JsonProperty("containers")] public Dictionary<string, SaveContainerDto> Containers = new();

            /// <summary>내구도 — 코어는 수리로 최대치가 올라가므로 SO 값이 아니라 인스턴스 값이다.</summary>
            [JsonProperty("hpMax")] public float HpMax;
            [JsonProperty("hpCur")] public float HpCurrent;

            /// <summary>모듈별 상태(ISaveableModule) — 키는 모듈 타입 이름(…Module 제외, 예: "Turret").</summary>
            [JsonProperty("modules")] public Dictionary<string, JToken> Modules = new();
        }

        public class BeltDto
        {
            /// <summary>세그먼트를 다시 찾기 위한 열쇠 — 출구 벨트의 좌표(세그먼트 안에서 유일하다).</summary>
            [JsonProperty("exit")] public Vector2Int ExitOrigin;

            [JsonProperty("items")] public List<BeltItemDto> Items = new();
        }

        public class BeltItemDto
        {
            [JsonProperty("item")] public string ItemId;

            /// <summary>세그먼트 입구(0)에서 출구(벨트 수)까지의 위치. 아이템이 어디쯤 실려 있는지.</summary>
            [JsonProperty("pos")] public float Position;
        }

        // ── 저장 ──────────────────────────────────────────────────────

        public object Capture()
        {
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null) return null;

            var dto = new Dto { Now = boot.Factory.Now };

            // 좌표순 정렬 — 같은 상태가 항상 같은 파일이 되어야 왕복 비교가 성립한다
            // (그리드가 아니라 뷰 목록을 훑는 이유: 멀티타일 건물은 여러 칸이 같은 Building을 가리킨다)
            foreach (var b in boot.Buildings.Where(b => b != null && !b.IsRemoved)
                                            .OrderBy(b => b.Origin.x).ThenBy(b => b.Origin.y))
            {
                var hp = b.Owner?.Health;   // HP 정본은 심 — 뷰 없는 건물(둥지·헤드리스)도 같은 경로
                dto.Buildings.Add(new BuildingDto
                {
                    DataId = SaveRefs.IdOf(b.Def),
                    Origin = b.Origin,
                    RotationSteps = b.RotationSteps,
                    Shape = b.Shape,
                    Containers = SaveContainers.Capture(b.Owner?.Get<InventoryModule>()),
                    HpMax = hp != null ? hp.MaxHealth : 0f,
                    HpCurrent = hp != null ? hp.CurrentHealth : 0f,
                    Modules = CaptureModules(b.Owner),
                });
            }

            foreach (var seg in boot.Factory.Belts.Segments.Where(s => s.BeltCount > 0)
                                    .OrderBy(s => s.Belts[0].Origin.x).ThenBy(s => s.Belts[0].Origin.y))
            {
                var belt = new BeltDto { ExitOrigin = seg.Belts[0].Origin };
                foreach (var (item, pos) in seg.Items)
                    belt.Items.Add(new BeltItemDto { ItemId = SaveRefs.IdOf(item), Position = pos });
                dto.Belts.Add(belt);
            }

            return dto;
        }

        // ── 복원 ──────────────────────────────────────────────────────

        public void Restore(JToken data)
        {
            var boot = FactoryBootstrap.Instance;
            if (boot == null || boot.Factory == null) return;

            var dto = SaveJson.FromToken<Dto>(data);
            if (dto == null) return;

            // 시계가 먼저다 — 아래에서 되살릴 타이머들이 전부 이 값을 기준으로 다시 예약된다
            SimHost.Sim.RestoreClock(dto.Now);

            var placement = Object.FindFirstObjectByType<PlacementSystem>();

            RemoveMismatched(boot, dto);
            var placed = PlaceOrReuse(boot, dto, placement);
            RestoreBeltItems(boot, dto);

            Debug.Log($"[Save] 공장 복원 — 건물 {placed}/{dto.Buildings.Count}개, 벨트 세그먼트 {dto.Belts.Count}개");
        }

        /// <summary>
        /// 세이브에 없거나 자리는 같지만 다른 건물인 것을 먼저 치운다.
        /// 새 건물을 놓기 전에 해야 칸이 비어 배치가 거절되지 않는다.
        /// </summary>
        static void RemoveMismatched(FactoryBootstrap boot, Dto dto)
        {
            var wanted = new Dictionary<Vector2Int, BuildingDto>();
            foreach (var b in dto.Buildings)
                if (b != null) wanted[b.Origin] = b;

            // 순회 중 제거되므로 사본을 뜬다
            foreach (var existing in boot.Buildings.Where(b => b != null && !b.IsRemoved).ToList())
            {
                if (wanted.TryGetValue(existing.Origin, out var want) &&
                    SaveRefs.Building(want.DataId) == existing.Def &&
                    want.RotationSteps == existing.RotationSteps)
                    continue;   // 그대로 재사용한다

                // 철거 경로(PlacementBridge.Remove)를 쓰지 않는다 — 그쪽은 버퍼 내용물을
                // 월드에 뿌리므로, 복원 중에 부르면 있지도 않던 드롭 아이템이 바닥에 깔린다
                boot.Factory.Remove(existing);
            }
        }

        static int PlaceOrReuse(FactoryBootstrap boot, Dto dto, PlacementSystem placement)
        {
            var byOrigin = boot.Buildings.Where(b => b != null && !b.IsRemoved)
                                         .ToDictionary(b => b.Origin, b => b);
            int count = 0;

            foreach (var want in dto.Buildings)
            {
                if (want == null) continue;

                var def = SaveRefs.Building(want.DataId);
                if (def == null) continue;   // SaveRefs가 이미 경고를 남겼다

                if (!byOrigin.TryGetValue(want.Origin, out var b))
                {
                    if (placement == null)
                    {
                        Debug.LogWarning($"[Save] 씬에 PlacementSystem이 없어 '{want.DataId}' @ {want.Origin} 를 세우지 못했습니다.");
                        continue;
                    }
                    if (!placement.TryPlaceAt(def, want.Origin, want.RotationSteps, out b, out string reason, want.Shape))
                    {
                        Debug.LogWarning($"[Save] '{want.DataId}' @ {want.Origin} 배치 실패: {reason}");
                        continue;
                    }
                }

                SaveContainers.Restore(want.Containers, b.Owner?.Get<InventoryModule>(), want.DataId);

                // 모듈 상태는 그릇 다음이다 — 코어가 보호막을 최대치로 자르는 등
                // 상태에 따라 달라지는 판단이 있고, 그 최대치는 티어(이미 복원됨)에서 나온다
                RestoreModules(b.Owner, want.Modules);

                if (want.HpMax > 0f) b.Owner?.Health?.RestoreState(want.HpMax, want.HpCurrent, isDead: false);

                boot.Factory.MarkDirty(b);
                count++;
            }

            return count;
        }

        /// <summary>
        /// 벨트 위 아이템을 되돌린다. 세그먼트 자체는 건물을 놓는 과정에서 이미 이어 붙여져 있으므로
        /// 여기서는 출구 벨트를 열쇠로 세그먼트를 찾아 내용물만 얹는다.
        /// </summary>
        // ── 모듈 상태 — 엔티티의 ISaveableModule을 전부, 건물 종류를 모르고 ──
        static Dictionary<string, JToken> CaptureModules(CoreDawn.Sim.Entity e)
        {
            var d = new Dictionary<string, JToken>();
            if (e == null) return d;
            foreach (var m in e.Modules)
                if (m is ISaveableModule s) d[ModuleKey(m)] = SaveJson.ToToken(s.CaptureState());
            return d;
        }

        static void RestoreModules(CoreDawn.Sim.Entity e, Dictionary<string, JToken> saved)
        {
            if (e == null || saved == null || saved.Count == 0) return;
            foreach (var m in e.Modules)
                if (m is ISaveableModule s && saved.TryGetValue(ModuleKey(m), out var tok) && tok != null) s.RestoreState(tok);
        }

        static string ModuleKey(CoreDawn.Sim.EntityModule m)
        {
            string n = m.GetType().Name;
            return n.EndsWith("Module") ? n.Substring(0, n.Length - 6) : n;
        }

        static void RestoreBeltItems(FactoryBootstrap boot, Dto dto)
        {
            // 재사용된 벨트에는 이전 아이템이 남아 있다 — 얹기 전에 전부 비운다
            foreach (var seg in boot.Factory.Belts.Segments) seg.ClearItems();

            foreach (var saved in dto.Belts)
            {
                if (saved?.Items == null) continue;

                var exitBelt = boot.Factory.Grid.GetAt(saved.ExitOrigin);
                var seg = exitBelt != null ? boot.Factory.Belts.GetSegment(exitBelt) : null;
                if (seg == null) continue;   // 벨트가 복원되지 못한 경우

                foreach (var it in saved.Items)
                {
                    var item = SaveRefs.Item(it.ItemId);
                    if (item != null) seg.AddItemAt(item, it.Position);
                }

                // 아이템이 실린 세그먼트는 대표(입구) 벨트가 구동한다 — 깨워두지 않으면 멈춘 채로 있는다
                if (seg.HasItems) boot.Factory.MarkDirty(seg.Belts[^1]);
            }
        }
    }
}
