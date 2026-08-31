using System.Collections.Generic;
using UnityEngine;

namespace CoreDawn.Sim
{
    /// <summary>
    /// 정의의 크기·포트를 회전해 읽는다 — 옛 BuildingDataSO.GetRotatedSize/GetRotatedPorts의 정의판.
    /// 정의는 불변·공유이므로 회전 4벌을 정의별로 한 번만 만들어 캐시한다
    /// (매 조회마다 새 배열을 할당하던 방식 + 재앵커링 누락 버그를 대체했던 SO 캐시와 같은 규칙).
    /// PortDefinition·Direction은 아직 Data에 있다 — 5a-2f에서 공장과 함께 Sim으로 옮긴다.
    /// </summary>
    public static class BuildingPorts
    {
        static Dictionary<EntityDef, PortDefinition[][]> cache = new();

        /// <summary>정의의 점유 크기(칸). 0 이하는 1로 본다.</summary>
        public static Vector2Int SizeOf(BuildingModuleDef building)
            => building != null ? new Vector2Int(Mathf.Max(1, building.Size.x), Mathf.Max(1, building.Size.y)) : Vector2Int.one;

        /// <summary>회전이 반영된 점유 크기(칸).</summary>
        public static Vector2Int RotatedSize(BuildingModuleDef building, int cwSteps)
        {
            var size = SizeOf(building);
            return cwSteps % 2 == 0 ? size : new Vector2Int(size.y, size.x);
        }

        public static Vector2Int RotatedSize(EntityDef def, int cwSteps)
            => RotatedSize(def?.Get<BuildingModuleDef>(), cwSteps);

        /// <summary>회전이 반영된 포트 목록. 포트 정의가 없으면 null.</summary>
        public static PortDefinition[] Rotated(EntityDef def, int cwSteps)
        {
            if (def == null) return null;
            int steps = (cwSteps % 4 + 4) % 4;
            if (!cache.TryGetValue(def, out var table)) cache[def] = table = Build(def);
            return table[steps];
        }

        static PortDefinition[][] Build(EntityDef def)
        {
            var table = new PortDefinition[4][];
            table[0] = Convert(def.Get<PortsModuleDef>());
            if (table[0] == null) return table;   // 포트 없는 건물 — 네 방향 모두 null
            var building = def.Get<BuildingModuleDef>();
            for (int s = 1; s < 4; s++)
            {
                int prevWidth = RotatedSize(building, s - 1).x;   // 이번 스텝 회전 전의 가로 크기
                var prev = table[s - 1];
                var next = new PortDefinition[prev.Length];
                for (int i = 0; i < prev.Length; i++)
                    next[i] = new PortDefinition
                    {
                        IsInput     = prev[i].IsInput,
                        Direction   = Dir.RotateCW(prev[i].Direction),
                        LocalOffset = Dir.RotateCellCW(prev[i].LocalOffset, prevWidth),
                    };
                table[s] = next;
            }
            return table;
        }

        /// <summary>json 포트 정의 → 공장의 포트 타입. 비어 있으면 null.</summary>
        public static PortDefinition[] Convert(PortsModuleDef ports)
        {
            if (ports == null || ports.Ports == null || ports.Ports.Count == 0) return null;
            var result = new PortDefinition[ports.Ports.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Convert(ports.Ports[i]);
            return result;
        }

        public static PortDefinition Convert(PortDef p) => new PortDefinition
        {
            IsInput     = p.IsInput,
            Direction   = ParseDirection(p.Dir),
            LocalOffset = new Vector2Int(p.X, p.Y),
        };

        public static Direction ParseDirection(string dir)
            => System.Enum.TryParse<Direction>(dir, true, out var d) ? d : Direction.North;

        // 팩을 다시 읽으면 정의 객체가 바뀐다 — 옛 정의를 키로 쥔 캐시는 버린다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => cache = new Dictionary<EntityDef, PortDefinition[][]>();
    }
}
