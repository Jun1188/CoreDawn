using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 제작 — 플레이어의 수제작과 조립기가 <b>같은</b> 모듈을 쓴다(구 InventoryPanelView.CraftOnce + AssemblerBehavior의 제작 로직).
    ///
    /// 두 가지 구동 방식이 있고 정의(<see cref="CrafterModuleDef.Manual"/>)가 고른다:
    /// <list type="bullet">
    /// <item><b>수제작</b>(manual): 플레이어가 누르고 있는 동안 <see cref="Hold"/>로 진행하고, 재료는 소지품에서 뒤 칸(가방)부터 빼고
    ///   결과는 같은 그릇에 넣는다(앞 칸=핫바부터). 안 들어가는 몫은 <see cref="Overflow"/>로 소유자(바닥에 떨어뜨림)에게 넘긴다.</item>
    /// <item><b>자동</b>(assembler): 심 시계(now)로 <see cref="Step"/>을 밟는다 — 재료가 모이면 소비하고 완료 시각을 잡고, 완료되면
    ///   출력 그릇에 넣는다. 출력이 막히면 완료를 보류(stall)한다. 언제 깨울지는 반환값으로 소유자(공장)에게 돌려준다.</item>
    /// </list>
    /// 그릇은 같은 엔티티의 <see cref="InventoryModule"/> — 수제작은 main, 자동은 input·output.
    /// 레시피 목록이 비어 있으면 팩의 모든 레시피가 후보다(해금 판정은 게임의 몫).
    /// </summary>
    public sealed class CrafterModule : EntityModule, ISteppable, ISaveableModule
    {
        public CrafterModuleDef Def { get; }

        public CrafterModule(CrafterModuleDef def) { Def = def ?? throw new ArgumentNullException(nameof(def)); }

        // ── 그릇 ────────────────────────────────────────────────────
        // 같은 엔티티의 InventoryModule에서 읽는다. 수제작은 소지품(main) 하나 — 핫바는 그 앞 칸일 뿐이다,
        // 자동은 input에서 빼고 output에 넣는다. 한 번 찾으면 굳힌다 — 정의가 만든 모듈은 엔티티가 사는 동안 바뀌지 않는다.
        static readonly ItemContainer[] None = System.Array.Empty<ItemContainer>();
        ItemContainer[] _inputs, _outputs;

        void Resolve()
        {
            if (_inputs != null) return;
            var inv = Owner?.Get<InventoryModule>();
            if (inv == null) return;
            if (Def.Manual) { _inputs = NonNull(inv.Main); _outputs = NonNull(inv.Main); }   // 넣기는 앞(핫바)부터, 빼기는 뒤(가방)부터 — 그릇의 규칙
            else            { _inputs = NonNull(inv.Input);            _outputs = NonNull(inv.Output); }
        }

        static ItemContainer[] NonNull(params ItemContainer[] cs)
        {
            int n = 0; foreach (var c in cs) if (c != null) n++;
            var r = new ItemContainer[n]; n = 0; foreach (var c in cs) if (c != null) r[n++] = c;
            return r;
        }

        /// <summary>재료를 빼는 그릇들(앞에서부터). 수제작: main. 자동: input.</summary>
        public IReadOnlyList<ItemContainer> Inputs { get { Resolve(); return _inputs ?? None; } }
        /// <summary>결과를 넣는 그릇들(앞에서부터). 수제작: main. 자동: output.</summary>
        public IReadOnlyList<ItemContainer> Outputs { get { Resolve(); return _outputs ?? None; } }

        // ── 레시피 ──────────────────────────────────────────────────
        static List<RecipeDef> _allRecipes; static SimDatabase _allRecipesOf;

        /// <summary>이 제작기가 만들 수 있는 레시피 — 정의에 적힌 것, 비어 있으면 팩 전체.</summary>
        public IReadOnlyList<RecipeDef> Recipes
        {
            get
            {
                if (Def.Recipes.Count > 0) return Def.Recipes;
                var db = SimHost.Database;
                if (db == null) return Array.Empty<RecipeDef>();
                if (_allRecipes == null || !ReferenceEquals(_allRecipesOf, db))
                {
                    _allRecipes = new List<RecipeDef>(db.Recipes.Values);
                    _allRecipesOf = db;
                }
                return _allRecipes;
            }
        }

        public bool Accepts(RecipeDef r) => r != null && (Def.Recipes.Count == 0 || Def.Recipes.Contains(r));

        /// <summary>회당 시간 — 정의의 speed 배율이 곱해진다.</summary>
        public float SecondsOf(RecipeDef r) => r.Seconds / Math.Max(0.01f, Def.Speed);

        // ── 수량 ────────────────────────────────────────────────────
        public int CountOf(ItemDef item)
        {
            int n = 0;
            foreach (var c in Inputs) n += c.CountOf(item);
            return n;
        }

        public bool HasIngredients(RecipeDef r)
        {
            if (r == null) return false;
            foreach (var i in r.Inputs)
                if (i.Item == null || CountOf(i.Item) < i.Amount) return false;
            return true;
        }

        /// <summary>결과가 전부 들어갈 자리가 있는가 — 자동 제작은 이것이 거짓이면 시작·완료를 보류한다.</summary>
        public bool CanStoreOutputs(RecipeDef r)
        {
            if (r == null) return false;
            foreach (var o in r.Outputs)
            {
                int room = 0;
                foreach (var c in Outputs) room += c.RoomFor(o.Item);
                if (room < o.Amount) return false;
            }
            return true;
        }

        void Consume(ItemDef item, int n)
        {
            foreach (var c in Inputs)
            {
                if (n <= 0) return;
                int take = Math.Min(n, c.CountOf(item));
                if (take > 0 && c.TryConsume(item, take)) n -= take;
            }
        }

        /// <summary>그릇들에 순서대로 넣고 못 넣은 개수를 돌려준다.</summary>
        int Deliver(ItemDef item, int n)
        {
            foreach (var c in Outputs)
            {
                if (n <= 0) break;
                int add = Math.Min(n, c.RoomFor(item));
                if (add > 0 && c.TryAdd(item, add)) n -= add;
            }
            return n;
        }

        /// <summary>제작 1회가 끝났다(수제작·자동 모두). 튜토리얼처럼 "무엇을 만들었나"를 세는 쪽이 듣는다.</summary>
        public event Action<RecipeDef> Crafted;
        /// <summary>결과가 그릇에 안 들어갔다 — 소유자가 바닥에 떨어뜨린다. 자동 제작은 자리를 먼저 확인하므로 나오지 않는다.</summary>
        public event Action<ItemDef, int> Overflow;
        /// <summary>자동 제작의 결과를 출력 그릇에 넣은 직후 — 공장이 하류로 밀어낼(Flush) 기회.</summary>
        public event Action Delivered;

        // ── 수제작 ──────────────────────────────────────────────────
        /// <summary>누르고 있는 현재 1회분의 경과(초). 손을 떼면 0.</summary>
        public float ManualProgress { get; private set; }

        /// <summary>재료를 빼고 결과를 넣는 1회. 재료가 모자라면 false.</summary>
        public bool CraftOnce(RecipeDef r)
        {
            if (!HasIngredients(r)) return false;
            foreach (var i in r.Inputs) Consume(i.Item, i.Amount);
            foreach (var o in r.Outputs)
            {
                if (o.Item == null || o.Amount <= 0) continue;
                int left = Deliver(o.Item, o.Amount);
                if (left > 0) Overflow?.Invoke(o.Item, left);   // 소비는 이미 일어났으므로 잃게 두면 안 된다
            }
            Crafted?.Invoke(r);
            return true;
        }

        /// <summary>누르고 있는 동안 dt만큼 진행. 1회가 완성된 호출에서 true. 재료가 모자라면 진행이 0으로 돌아간다.</summary>
        public bool Hold(RecipeDef r, float dt)
        {
            if (r == null || !HasIngredients(r)) { ManualProgress = 0f; return false; }
            ManualProgress += Math.Max(0f, dt);
            if (ManualProgress < SecondsOf(r)) return false;
            ManualProgress = 0f;   // 계속 누르고 있으면 다음 1회가 바로 시작된다
            return CraftOnce(r);
        }

        /// <summary>손을 뗐다 — 소비 전이므로 잃는 것 없이 그 자리에서 멈춘다.</summary>
        public void Release() => ManualProgress = 0f;

        // ── 자동 ────────────────────────────────────────────────────
        /// <summary>다음 조합이 따를 레시피 (UI에서 교체 가능).</summary>
        public RecipeDef Recipe { get; private set; }
        /// <summary>진행 중인 1회가 따르는 레시피 — 교체돼도 이 1회는 이것으로 끝난다.</summary>
        public RecipeDef CraftingRecipe { get; private set; }
        public float ReadyAt { get; private set; } = -1f;
        public bool Crafting { get; private set; }
        public bool Paused { get; private set; }
        float _pausedRemaining;   // 중지 시점의 잔여 시간 — 진행률은 보존된다

        /// <summary>입력 슬롯 수(첫 입력 그릇) — 재료 종류가 이보다 많은 레시피는 영구 stall이라 거절한다.</summary>
        public int InputSlotCount => Inputs.Count > 0 ? Inputs[0].SlotCount : 0;

        public bool IsIngredient(ItemDef item)
        {
            if (Recipe == null || item == null) return false;
            foreach (var i in Recipe.Inputs)
                if (i.Item == item) return true;
            return false;
        }

        /// <summary>레시피 교체. 슬롯이 모자라면 false. 진행 중인 1회는 취소한다 — 재료는 완료 순간에만 소비하므로 잃는 것이 없다.</summary>
        public bool SetRecipe(RecipeDef r)
        {
            if (r != null && r.Inputs != null && r.Inputs.Count > InputSlotCount) return false;
            if (r != Recipe) { Crafting = false; ReadyAt = -1f; }
            Recipe = r;
            return true;
        }

        public void SetPaused(bool paused, float now)
        {
            if (Paused == paused) return;
            Paused = paused;
            if (!Crafting) return;
            if (paused) _pausedRemaining = Math.Max(0f, ReadyAt - now);
            else        ReadyAt = now + _pausedRemaining;
        }

        public float RemainingTime(float now) =>
            !Crafting ? 0f : Paused ? _pausedRemaining : Math.Max(0f, ReadyAt - now);

        public float Progress(float now)
        {
            if (!Crafting || CraftingRecipe == null) return 0f;
            float total = SecondsOf(CraftingRecipe);
            if (total <= 0f) return 0f;
            return Math.Clamp(1f - RemainingTime(now) / total, 0f, 1f);
        }

        public MachineState State(float now)
        {
            if (Paused || Recipe == null) return MachineState.Stopped;
            if (Crafting)
                return now < ReadyAt || CanStoreOutputs(CraftingRecipe) ? MachineState.Running : MachineState.OutputBlocked;
            if (!HasIngredients(Recipe)) return MachineState.WaitingInput;
            if (!CanStoreOutputs(Recipe)) return MachineState.OutputBlocked;
            return MachineState.Running;
        }

        /// <summary>
        /// 자동 제작 한 걸음(공장 틱). 완료 시각이 됐으면 재료를 소비하고 출력에 넣고, 잔여물을 밀어내고, 재료가 있으면 다음 1회의 타이머를 시작한다.
        /// </summary>
        /// <param name="now">심 시계.</param>
        /// <param name="inputsFreed">입력 그릇에 자리가 생겼다 — 소유자가 막혀 있던 상류를 깨운다.</param>
        /// <returns>다음 1회의 완료까지 시간(초). 시작하지 않았으면 0.</returns>
        public float Step(float now, out bool inputsFreed)
        {
            inputsFreed = false;
            if (Recipe == null || Paused) return 0f;   // 중지 — 진행률·버퍼 보존
            // 규칙(손 제작과 같다): 재료가 있는 동안 타이머가 돌고, 완료 순간에 소비·산출한다.
            // 중간에 재료를 빼면 타이머는 초기화된다 — 소비된 것이 없으니 잃는 것도 없다. (2026-09-01 사용자 지시로 "소비 → 타이머"에서 되돌림)
            if (Crafting)
            {
                if (!HasIngredients(CraftingRecipe)) { Crafting = false; ReadyAt = -1f; }   // 재료가 빠졌다 → 초기화
                else
                {
                    if (now < ReadyAt) return 0f;                      // 이른 기상 (재료 도착 등) → 완료 시각에 다시 깨어남
                    if (!CanStoreOutputs(CraftingRecipe)) return 0f;   // 출력 막힘 → 완료 보류 (stall), 재료는 그대로
                    foreach (var i in CraftingRecipe.Inputs) Consume(i.Item, i.Amount);   // 완료 순간에 소비
                    inputsFreed = true;
                    foreach (var o in CraftingRecipe.Outputs) Deliver(o.Item, o.Amount);  // 자리를 확인했으므로 남지 않는다
                    Crafting = false;
                    var done = CraftingRecipe;
                    Delivered?.Invoke();
                    Crafted?.Invoke(done);
                }
            }
            if (EvictForeignInputs()) inputsFreed = true;
            if (!HasIngredients(Recipe) || !CanStoreOutputs(Recipe)) return 0f;
            Crafting = true;
            CraftingRecipe = Recipe;
            float seconds = SecondsOf(Recipe);
            ReadyAt = now + seconds;
            return seconds;
        }

        /// <summary>현재 레시피에 안 쓰는 입력 잔여물을 출력으로 밀어낸다(레시피 교체 뒤). 옮긴 것이 있으면 true.</summary>
        bool EvictForeignInputs()
        {
            if (Recipe == null || Inputs.Count == 0) return false;
            bool moved = false;
            var input = Inputs[0];
            foreach (var (item, n) in input.Snapshot())
            {
                if (IsIngredient(item)) continue;
                int left = n;
                foreach (var c in Outputs)
                {
                    int move = Math.Min(left, c.RoomFor(item));
                    if (move <= 0) continue;
                    input.TryConsume(item, move);
                    c.TryAdd(item, move);
                    left -= move; moved = true;
                }
            }
            return moved;
        }

        // ── 공통 틱(ISteppable): 시작했으면 완료까지, 진행 중 이른 기상이면 남은 시간(힙 중복 예약은 싸다).
        // 재료·출력 대기(0)는 그릇 변화(Changed·벨트 입고)가 깨운다. 입력이 줄면 소유자가 그릇 변화로 보고 상류를 깨운다.
        float ISteppable.Step(float now, float dt)
        {
            float wakeIn = Step(now, out _);
            if (wakeIn > 0f) return wakeIn;
            return Crafting && !Paused && ReadyAt > now ? ReadyAt - now : 0f;
        }

        // ── 세이브 (게임의 세이브 모듈이 id로 바꿔 싣는다) ──────────
        public struct Snapshot
        {
            public RecipeDef Recipe, CraftingRecipe;
            public float ReadyAt, PausedRemaining;
            public bool Crafting, Paused;
        }

        public Snapshot Capture() => new Snapshot
        {
            Recipe = Recipe, CraftingRecipe = CraftingRecipe, ReadyAt = ReadyAt,
            PausedRemaining = _pausedRemaining, Crafting = Crafting, Paused = Paused,
        };

        /// <summary>저장 당시 유효했던 레시피를 그대로 되돌린다 — 해금 검사는 거치지 않는다(티어도 함께 복원된다).</summary>
        public void Restore(Snapshot s)
        {
            Recipe = s.Recipe;
            CraftingRecipe = s.CraftingRecipe;
            ReadyAt = s.ReadyAt;
            Crafting = s.Crafting && s.CraftingRecipe != null;
            _pausedRemaining = s.PausedRemaining;
            Paused = s.Paused;
        }

        // ── 세이브(ISaveableModule) — 키는 옛 AssemblerBehavior 저장과 같다. 레시피는 id로.
        public sealed class SaveState
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
            var s = Capture();
            return new SaveState
            {
                RecipeId = s.Recipe?.Id, CraftingRecipeId = s.CraftingRecipe?.Id,
                ReadyAt = s.ReadyAt, Crafting = s.Crafting,
                PausedRemaining = s.PausedRemaining, Paused = s.Paused,
            };
        }

        /// <summary>기상 예약은 여기서 하지 않는다 — 복원자가 MarkDirty를 걸면 공통 틱(ISteppable)이 남은 시간으로 다시 예약한다.</summary>
        public void RestoreState(JToken state)
        {
            var s = state?.ToObject<SaveState>();
            if (s == null) return;
            Restore(new Snapshot
            {
                Recipe = FindRecipe(s.RecipeId), CraftingRecipe = FindRecipe(s.CraftingRecipeId),
                ReadyAt = s.ReadyAt, Crafting = s.Crafting,
                PausedRemaining = s.PausedRemaining, Paused = s.Paused,
            });
        }

        static RecipeDef FindRecipe(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var def = SimHost.Database?.Recipe(id);
            if (def == null) UnityEngine.Debug.LogWarning($"[Crafter] 세이브의 레시피 id \"{id}\"가 팩에 없습니다 — 그 항목은 건너뜁니다.");
            return def;
        }
    }
}
