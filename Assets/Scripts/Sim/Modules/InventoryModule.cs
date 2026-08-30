using System.Collections.Generic;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 역할별 아이템 그릇 — 플레이어(main·hotbar)·저장고·기계(input·output)가 같은 모듈을 쓴다.
    /// 어떤 역할이 몇 칸인지는 정의(<see cref="InventoryModuleDef"/>)가 정하고, 내용물은 이 모듈(엔티티당 하나)의 것이다.
    /// 없는 역할은 null — "핫바가 없는 저장고"는 정상이다.
    /// </summary>
    public sealed class InventoryModule : EntityModule
    {
        public const string RoleInput = "input", RoleOutput = "output", RoleMain = "main", RoleHotbar = "hotbar";

        public InventoryModuleDef Def { get; }
        readonly Dictionary<string, ItemContainer> _byRole = new Dictionary<string, ItemContainer>();

        public InventoryModule(InventoryModuleDef def)
        {
            Def = def;
            if (def == null) return;
            Add(RoleInput,  def.Input,  def.StackCap);
            Add(RoleOutput, def.Output, def.StackCap);
            Add(RoleMain,   def.Main,   def.StackCap);
            Add(RoleHotbar, def.Hotbar, def.StackCap);
        }

        void Add(string role, int slots, int stackCap)
        {
            if (slots > 0) _byRole[role] = new ItemContainer(slots, stackCap);
        }

        /// <summary>벨트·상류가 넣는 곳(기계·저장고·포탑 탄약함).</summary>
        public ItemContainer Input  => Get(RoleInput);
        /// <summary>하류로 내보내는 곳.</summary>
        public ItemContainer Output => Get(RoleOutput);
        /// <summary>플레이어 가방.</summary>
        public ItemContainer Main   => Get(RoleMain);
        /// <summary>플레이어 핫바(장착 슬롯).</summary>
        public ItemContainer Hotbar => Get(RoleHotbar);

        public ItemContainer Get(string role) => _byRole.TryGetValue(role, out var c) ? c : null;

        public IReadOnlyDictionary<string, ItemContainer> All => _byRole;

        /// <summary>모든 그릇을 합친 보유량 — 비용 지불·튜토리얼 집계처럼 "어디에 있든" 세는 질문용.</summary>
        public int CountOf(ItemDef item)
        {
            int n = 0;
            foreach (var c in _byRole.Values) n += c.CountOf(item);
            return n;
        }
    }
}
