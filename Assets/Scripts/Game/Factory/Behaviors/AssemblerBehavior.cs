using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CoreDawn.FPS;
using CoreDawn.Save;
using CoreDawn.UI;
using CoreDawn.Data;
using CoreDawn.Sim;

namespace CoreDawn.Factory
{
    /// <summary>
    /// 조립기·제련로 — 공장과 <see cref="CrafterModule"/>(심의 제작 로직) 사이의 어댑터.
    ///
    /// 제작 자체(재료 소비·완료 시각·출력 보류·레시피 교체)는 모듈이 하고, 이 행동은 공장에 속한 일만 한다:
    /// 입력 버퍼의 정책(현재 레시피의 재료만·종류당 1스택), 틱에서 모듈을 한 걸음 밟고 완료 시각에 깨우기 예약,
    /// 재료를 소비했으면 상류(벨트)를 깨우고, 결과물을 하류로 밀어내기(Flush), 그리고 해금(게임 규칙) 검사.
    ///
    /// stall 정책은 모듈의 것이다: 결과물 자리가 없으면 시작하지 않고, 완료 시점에 자리가 없으면 보류한다(유실 없음).
    /// 하류가 소비하면 NotifyUpstream으로 깨어나 재개한다.
    /// </summary>
    public class AssemblerBehavior : IBuildingBehavior, IInteractiveBehavior, ISaveableBehavior
    {
        readonly BuildingModule _b;
        readonly CrafterModule _crafter;

        public AssemblerBehavior(BuildingModule b, CrafterModule crafter)
        {
            _b = b;
            _crafter = crafter;
            // 한 재료가 입력 슬롯 전부를 독점해 다른 재료가 못 들어오는 데드락 방지
            _b.Input.SingleStackPerType = true;
            // 입력 버퍼는 현재 레시피의 재료만 받는다 (포트 필터 AcceptedTypes 대체)
            _b.Input.AcceptFilter = _crafter.IsIngredient;
            // 플레이어가 설비 창(SCR-09)에서 손으로 넣고 빼는 경로는 벨트를 거치지 않아
            // 아무도 깨워 주지 않는다. 설비가 자고 있는 이유는 둘인데 둘 다 손으로 풀 수 있다:
            //   재료 부족 — 손으로 재료를 넣어도 다음 벨트 입고 때까지 그대로 서 있다
            //   출력 막힘 — 손으로 완성품을 빼내도 하류가 소비할 때까지 그대로 서 있다
            // (보관소·코어가 같은 이유로 이미 쓰고 있는 방식)
            _b.Input.Changed  += Wake;
            _b.Output.Changed += Wake;
            // 완성품을 출력에 넣는 즉시 하류로 밀어낸다 — 그래야 같은 틱에 다음 1회가 시작될 자리가 난다
            _crafter.Delivered += _b.FlushOutputs;
            SetRecipe(_crafter.Recipes.FirstOrDefault());
        }

        /// <summary>
        /// 버퍼가 바뀌었으니 다음 틱에 다시 판단하게 한다.
        ///
        /// 심 자신의 조작(재료 소비·완성품 적재)도 이걸 거쳐 한 틱 더 돌지만, 그 틱은
        /// 어차피 해야 할 재평가다. 아무것도 움직이지 않는 상태에서는 Changed가 아예 발화하지
        /// 않으므로(FlushOutputs는 밀어내기에 성공했을 때만 TryConsume) 헛도는 일은 없다.
        /// </summary>
        void Wake() => _b.Factory.MarkDirty(_b);

        // ── 설비 UI(SCR-09)가 읽는 표면 ──────────────────────────
        public BuildingModule Building => _b;
        public CrafterModule Crafter => _crafter;
        public CrafterModuleDef Def => _crafter.Def;
        public RecipeDef CurrentRecipe => _crafter.Recipe;

        public bool Paused => _crafter.Paused;

        public void SetPaused(bool paused)
        {
            if (_crafter.Paused == paused) return;
            var sim = _b.Factory;
            _crafter.SetPaused(paused, sim.Now);
            if (paused) return;
            if (_crafter.Crafting) sim.ScheduleWake(_b, _crafter.RemainingTime(sim.Now));
            sim.MarkDirty(_b);
        }

        public float Progress => _crafter.Progress(_b.Factory.Now);
        public float RemainingTime => _crafter.RemainingTime(_b.Factory.Now);
        public MachineState State => _crafter.State(_b.Factory.Now);

        public string InteractPrompt => $"{_b.DisplayName} 열기";
        public void Interact(PlayerController player) => MachinePanelView.TryOpen(this);

        public void SetRecipe(RecipeDef r)
        {
            if (r != null && r.Inputs != null && r.Inputs.Count > _crafter.InputSlotCount)
            {
                Debug.LogWarning($"[Assembler] 레시피 '{r.DisplayName}'의 재료 종류({r.Inputs.Count})가 " +
                                 $"입력 슬롯({_crafter.InputSlotCount})보다 많아 거부됨");
                return;
            }
            if (r != null && !RecipeDatabaseSO.IsUnlocked(r))
            {
                Debug.LogWarning($"[Assembler] 레시피 '{r.DisplayName}'는 아직 해금되지 않음 (요구 Tier {r.Tier})");
                return;
            }
            if (r == _crafter.Recipe) return;
            // 진행 중인 1회는 취소하지 않는다 — 이미 소비된 재료는 옛 레시피의 완성품이 되어 출구로 나간다.
            // 건물의 물건은 건물의 출구로만 나간다 (가방 순간이동 없음). 새 레시피에 안 쓰는 입력 잔여물은 틱이 출구로 밀어낸다.
            if (_crafter.SetRecipe(r)) _b.Factory.MarkDirty(_b);
        }

        /// <summary>현재 해금된 레시피만 — 레시피 선택 UI가 이걸로 목록을 채우므로 게이팅이 자동 반영된다.</summary>
        public IEnumerable<RecipeDef> GetUnlockedRecipes() =>
            _crafter.Recipes.Where(r => RecipeDatabaseSO.IsUnlocked(r));

        public void OnAfterPlaced() { }

        public void Tick(float dt)
        {
            var sim = _b.Factory;
            _b.FlushOutputs();                                   // 밀려 있던 출력부터
            float wakeIn = _crafter.Step(sim.Now, out bool inputsFreed);
            if (inputsFreed) _b.NotifyUpstream();               // 입력 버퍼에 자리 생김 → 막혀 있던 상류 깨움
            if (wakeIn > 0f) sim.ScheduleWake(_b, wakeIn);      // 완료 시각에 다시 깨어난다
        }

        // ── 세이브 ────────────────────────────────────────────────────
        // 키는 옛 저장 파일과 같다 — 모듈로 옮겼어도 세이브 형식은 그대로.
        public class SaveState
        {
            [JsonProperty("recipe")] public string RecipeId;
            [JsonProperty("craftingRecipe")] public string CraftingRecipeId;
            [JsonProperty("readyAt")] public float ReadyAt;
            [JsonProperty("crafting")] public bool Crafting;
            [JsonProperty("pausedRemaining")] public float PausedRemaining;
            [JsonProperty("paused")] public bool Paused;
        }

        public object CaptureState()
        {
            var s = _crafter.Capture();
            return new SaveState
            {
                RecipeId = SaveRefs.IdOf(s.Recipe),
                CraftingRecipeId = SaveRefs.IdOf(s.CraftingRecipe),
                ReadyAt = s.ReadyAt,
                Crafting = s.Crafting,
                PausedRemaining = s.PausedRemaining,
                Paused = s.Paused,
            };
        }

        public void RestoreState(JToken state)
        {
            var s = SaveJson.FromToken<SaveState>(state);
            if (s == null) return;
            // SetRecipe를 거치지 않는다 — 그쪽은 티어 해금을 검사해서 거절할 수 있는데,
            // 저장된 레시피는 저장 당시 이미 유효했던 것이고 티어도 함께 복원된다.
            _crafter.Restore(new CrafterModule.Snapshot
            {
                Recipe = SaveRefs.Recipe(s.RecipeId),
                CraftingRecipe = SaveRefs.Recipe(s.CraftingRecipeId),
                ReadyAt = s.ReadyAt,
                Crafting = s.Crafting,
                PausedRemaining = s.PausedRemaining,
                Paused = s.Paused,
            });
            var sim = _b.Factory;
            if (_crafter.Crafting && !_crafter.Paused)
                sim.ScheduleWake(_b, Mathf.Max(0f, _crafter.ReadyAt - sim.Now));
            else
                sim.MarkDirty(_b);
        }
    }
}
