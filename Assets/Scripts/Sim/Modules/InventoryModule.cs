using System.Collections.Generic;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 역할별 아이템 그릇 — 플레이어(main·hotbar)·저장고·기계(input·output)가 같은 모듈을 쓴다.
    /// 어떤 역할이 몇 칸인지는 정의(<see cref="InventoryModuleDef"/>)가 정하고, 내용물은 이 모듈(엔티티당 하나)의 것이다.
    /// 없는 역할은 null — "핫바가 없는 저장고"는 정상이다.
    ///
    /// 역할은 정의의 네 필드로 닫힌 집합이라 런타임은 타입이 있는 프로퍼티다. 역할 이름(문자열)은 경계(세이브 파일)에만 나가고,
    /// 그 이름표는 <see cref="Roles"/>·<see cref="ByRole"/> 한 곳에서만 붙는다 — 세이브 모듈이 역할 목록을 따로 적지 않는다.
    /// </summary>
    public sealed class InventoryModule : EntityModule
    {
        public const string RoleInput = "input", RoleOutput = "output", RoleMain = "main", RoleHotbar = "hotbar";

        public InventoryModuleDef Def { get; }

        /// <summary>벨트·상류가 넣는 곳(기계·저장고·포탑 탄약함).</summary>
        public ItemContainer Input  { get; }
        /// <summary>하류로 내보내는 곳.</summary>
        public ItemContainer Output { get; }
        /// <summary>플레이어 가방.</summary>
        public ItemContainer Main   { get; }
        /// <summary>플레이어 핫바(장착 슬롯).</summary>
        public ItemContainer Hotbar { get; }

        readonly (string role, ItemContainer container)[] _roles;

        public InventoryModule(InventoryModuleDef def)
        {
            Def = def;
            int cap = def?.StackCap ?? 0;
            Input  = Make(def?.Input  ?? 0, cap);
            Output = Make(def?.Output ?? 0, cap);
            Main   = Make(def?.Main   ?? 0, cap);
            Hotbar = Make(def?.Hotbar ?? 0, cap);
            var list = new List<(string, ItemContainer)>(4);
            if (Input  != null) list.Add((RoleInput,  Input));
            if (Output != null) list.Add((RoleOutput, Output));
            if (Main   != null) list.Add((RoleMain,   Main));
            if (Hotbar != null) list.Add((RoleHotbar, Hotbar));
            _roles = list.ToArray();
        }

        static ItemContainer Make(int slots, int stackCap) => slots > 0 ? new ItemContainer(slots, stackCap) : null;

        /// <summary>있는 그릇들을 역할 이름과 함께 — 세이브·집계처럼 "전부"를 훑는 쪽이 쓴다.</summary>
        public IReadOnlyList<(string role, ItemContainer container)> Roles => _roles;

        /// <summary>역할 이름으로 그릇을 찾는다(세이브 복원 등 경계용). 없는 역할·모르는 이름은 null.</summary>
        public ItemContainer ByRole(string role) => role switch
        {
            RoleInput  => Input,
            RoleOutput => Output,
            RoleMain   => Main,
            RoleHotbar => Hotbar,
            _          => null,
        };

        /// <summary>모든 그릇을 합친 보유량 — 비용 지불·튜토리얼 집계처럼 "어디에 있든" 세는 질문용.</summary>
        public int CountOf(ItemDef item)
        {
            int n = 0;
            foreach (var (_, c) in _roles) n += c.CountOf(item);
            return n;
        }
    }
}
